using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Autorama
{
    // Uma entrada da paleta: a peça, o sentido, a rampa e a tecla de atalho (as mesmas do HTML).
    public struct PaletteEntry
    {
        public PieceDef def; public int dir, rise; public Key key; public string label, sub, icon;
        public PaletteEntry(PieceDef def, int dir, int rise, Key key, string label, string sub, string icon)
        { this.def = def; this.dir = dir; this.rise = rise; this.key = key; this.label = label; this.sub = sub; this.icon = icon; }
    }

    // O construtor: guarda a lista de peças e recalcula tudo a partir da largada a cada mudança.
    // Roda também fora do Play: no Editor ele só desenha o rascunho salvo (as peças geradas não entram na cena).
    [ExecuteAlways]
    public class TrackBuilder : MonoBehaviour
    {
        public TrackTheme theme;
        public GridBoard board;
        public Vector2Int boardSize = new Vector2Int(32, 20);
        public float cellCm = 15f;

        public static readonly PaletteEntry[] Palette =
        {
            new PaletteEntry(PieceCatalog.Short,  0,  0, Key.Digit1, "Reta curta", "2 células", "icone-reta-curta"),
            new PaletteEntry(PieceCatalog.Long,   0,  0, Key.Digit2, "Reta comprida", "4 células", "icone-reta-comprida"),
            new PaletteEntry(PieceCatalog.Tiny,   0,  0, Key.T,      "Reta curtinha", "1 célula", "icone-reta-curtinha"),
            new PaletteEntry(PieceCatalog.Cross,  0,  0, Key.X,      "Cruzamento", "4 células", "icone-cruzamento"),
            new PaletteEntry(PieceCatalog.CrossS, 0,  0, Key.B,      "Cruzamento curto", "2 células", "icone-cruzamento-curto"),
            new PaletteEntry(PieceCatalog.Curve, -1,  0, Key.Digit3, "Curva longa", "90° esquerda", "icone-curva-longa-esquerda"),
            new PaletteEntry(PieceCatalog.Curve,  1,  0, Key.Digit4, "Curva longa", "90° direita", "icone-curva-longa-direita"),
            new PaletteEntry(PieceCatalog.Half,  -1,  0, Key.Digit5, "Meia curva", "45° esquerda", "icone-meia-curva-esquerda"),
            new PaletteEntry(PieceCatalog.Half,   1,  0, Key.Digit6, "Meia curva", "45° direita", "icone-meia-curva-direita"),
            new PaletteEntry(PieceCatalog.CurveB,-1,  0, Key.Digit7, "Curva longa inclinada", "90° esquerda", "icone-curva-longa-inclinada-esquerda"),
            new PaletteEntry(PieceCatalog.CurveB, 1,  0, Key.Digit8, "Curva longa inclinada", "90° direita", "icone-curva-longa-inclinada-direita"),
            new PaletteEntry(PieceCatalog.HalfB, -1,  0, Key.Digit9, "Curva curta inclinada", "45° esquerda", "icone-curva-curta-inclinada-esquerda"),
            new PaletteEntry(PieceCatalog.HalfB,  1,  0, Key.Digit0, "Curva curta inclinada", "45° direita", "icone-curva-curta-inclinada-direita"),
            new PaletteEntry(PieceCatalog.Long,   0,  1, Key.U,      "Rampa", "sobe 1 nível", "icone-rampa-sobe"),
            new PaletteEntry(PieceCatalog.Long,   0, -1, Key.D,      "Rampa", "desce 1 nível", "icone-rampa-desce"),
            new PaletteEntry(PieceCatalog.Loop,  -1,  0, Key.K,      "Loop", "sai à esquerda", "icone-loop-esquerda"),
            new PaletteEntry(PieceCatalog.Loop,   1,  0, Key.L,      "Loop", "sai à direita", "icone-loop-direita"),
            new PaletteEntry(PieceCatalog.Jump,   0,  0, Key.J,      "Rampa de salto", "4 células", "icone-salto"),
        };

        public string TrackName { get; set; } = "";
        // 2 ou 4 carros. A geometria em células é a mesma; na pista de 4, a raiz das peças e a mesa ficam com escala 2
        // (1 célula = 2 unidades do mundo). Converta com CellToWorld/WorldToCell (seção 25 do guia).
        public int Faixas { get; private set; } = 2;
        public float Scale => TrackMath.Scale(Faixas);
        public Vector3 CellToWorld(Vector3 cell) => transform.position + cell * Scale;
        public Vector3 WorldToCell(Vector3 world) => (world - transform.position) / Scale;
        public Pose2 Origin { get; private set; }
        public int StartZ { get; private set; }               // nível da largada (0 = chão)
        public readonly List<TrackPiece> pieces = new List<TrackPiece>();
        public List<Segment> Segments { get; private set; } = new List<Segment>();
        public TrackAnalysis Info { get; private set; }
        public int Selected { get; private set; } = -1;      // -1 = encaixa no fim da pista
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public event Action Changed;
        public event Action<Vector2> Shifted;     // a pista foi empurrada para dentro da mesa (a câmera acompanha; em unidades do mundo)

        readonly List<TrackPieceView> views = new List<TrackPieceView>();
        readonly Stack<string> undo = new Stack<string>(), redo = new Stack<string>();
        GameObject generated;              // raiz das peças geradas (fora da cena salva)
        Transform arrow, arrow2, hoverCell;
        TrackPieceView ghost;

        void OnEnable() => LoadDraftAndBuild();

        // Garantia para o Editor: se ainda não desenhou (ex.: o tema chegou depois do OnEnable), desenha agora.
        void Update()
        {
            if (pieces.Count == 0 && theme != null) LoadDraftAndBuild();
        }

        void LoadDraftAndBuild()
        {
            if (theme == null) return;
            var draft = TrackLibrary.LoadDraft();
            try { Apply(draft ?? TrackSave.Oval()); }
            catch (Exception e) { Debug.LogWarning("Rascunho inválido, abrindo o exemplo: " + e.Message); Apply(TrackSave.Oval()); }
            GrowBoard();
            Rebuild();
        }

        // As peças geradas ficam numa raiz própria, que não é salva na cena e some junto com o construtor.
        void OnDisable()
        {
            TrackPieceView.Kill(generated);
            generated = null;
            views.Clear();
            arrow = arrow2 = hoverCell = null;
            ghost = null;
        }

        Transform Root
        {
            get
            {
                if (generated == null)
                {
                    generated = new GameObject("Pista (gerada)") { hideFlags = HideFlags.DontSave };
                    generated.transform.SetPositionAndRotation(transform.position, transform.rotation);
                }
                return generated.transform;
            }
        }

        // ---------- Ações ----------

        // A frente termina em FrontEnd - 1; dali até o fim da lista ficam as peças de trás (Tail), que encostam na largada.
        public int Tail { get; private set; }
        public int FrontEnd => pieces.Count - Tail;

        public int InsertIndex => Selected < 0 ? FrontEnd : Selected + 1;
        public Pose2 InsertPose => Back ? BackPose : FrontPose;
        public int InsertLevel => Back ? StartZ : Selected < 0 ? Segments[FrontEnd - 1].zOut : Segments[Selected].zOut;

        // Ponta de trás: a entrada da peça mais afastada atrás da largada (ou da própria largada), apontando para fora.
        public Pose2 BackPose
        {
            get
            {
                var c = Tail > 0 ? Segments[FrontEnd].a : Origin;
                return new Pose2(c.pos, c.heading + 180f);
            }
        }
        public Pose2 FrontPose => Selected < 0 ? Segments[FrontEnd - 1].b : Segments[Selected].b;

        // Ponta onde as peças encaixam: frente (fim da pista, ou depois da selecionada) ou trás (atrás da largada).
        public bool Back { get; private set; }
        public void SetBack(bool back) { Back = back; Rebuild(); }
        public event Action<string> Notice;

        public void Add(PaletteEntry e)
        {
            if (Back) { AddBack(e); return; }
            Commit(() =>
            {
                int at = InsertIndex;
                if (at > FrontEnd) Tail++;                // inserida entre as peças de trás
                pieces.Insert(at, new TrackPiece(e.def.Id, e.dir, e.rise));
                if (Selected >= 0) Selected = at;         // continua encaixando em sequência
            });
        }

        // Atrás da largada: a peça encosta na ponta de trás (a largada não sai do lugar) e vira a nova ponta.
        // Na lista ela entra logo antes das que já estavam atrás, porque a volta continua sendo largada, frente, trás.
        void AddBack(PaletteEntry e)
        {
            if (e.rise != 0) { Notice?.Invoke(NoRampBehind); return; }
            Commit(() =>
            {
                int at = FrontEnd;
                pieces.Insert(at, new TrackPiece(e.def.Id, e.dir, 0));
                Tail++;
                if (Selected >= at) Selected++;
            });
        }

        const string NoRampBehind = "Rampa não encaixa atrás da largada: ela fica no chão.";

        public void RemoveSelected()
        {
            if (Selected <= 0) return;            // a largada não sai
            Commit(() => { if (Selected >= FrontEnd) Tail--; pieces.RemoveAt(Selected); Selected--; });
        }

        public void RemoveLast()
        {
            // Tira a última peça da ponta ativa (na de trás, a mais afastada da largada).
            int at = Back && Tail > 0 ? FrontEnd : FrontEnd - 1;
            if (at < 1) return;
            Commit(() =>
            {
                if (at >= FrontEnd) Tail--;
                pieces.RemoveAt(at);
                if (Selected >= 0) Selected = Selected == at ? -1 : Selected > at ? Selected - 1 : Selected;
            });
        }

        public void FlipSelected()
        {
            if (Selected <= 0 || !pieces[Selected].Def.Sided) return;
            Commit(() => pieces[Selected].dir *= -1);
        }

        public void SetRise(int rise)
        {
            if (Selected <= 0) return;
            if (rise != 0 && pieces[Selected].Def.GroundOnly) { Notice?.Invoke("Loop e rampa de salto não sobem nem descem de nível."); return; }
            if (rise != 0 && Selected >= FrontEnd) { Notice?.Invoke(NoRampBehind); return; }
            Commit(() => pieces[Selected].rise = rise);
        }

        // Sobe ou desce a largada um nível; a pista inteira vai junto (as rampas sobem e descem a partir dela).
        public void MoveStartLevel(int d)
        {
            int z = Mathf.Clamp(StartZ + d, 0, TrackMath.MaxLevel);
            if (z != StartZ) Commit(() => StartZ = z);
        }

        // Gira a largada 90° em volta do centro dela; a pista inteira vai junto.
        public void RotateStart() => Commit(() =>
        {
            Vector2 mid = Origin.pos + TrackMath.Dir(Origin.heading);
            float h = Origin.heading + 90f;
            Origin = TrackMath.SnapStart(new Pose2(mid - TrackMath.Dir(h), h));
        });

        // Arrastar a largada: BeginEdit uma vez no começo, MoveStart a cada passo.
        public void BeginEdit() { CancelAutoClose(); undo.Push(Snapshot()); redo.Clear(); }
        public void EndEdit() { if (GrowBoard()) Rebuild(); }     // arrastando, a mesa espera: cresce quando solta
        public void MoveStart(Vector2 pos)
        {
            var o = TrackMath.SnapStart(new Pose2(pos, Origin.heading));
            if (o.pos == Origin.pos || !Fits(o)) return;      // a largada não leva a pista para fora da mesa
            Origin = o;
            Rebuild();
        }

        public void Select(int i) { Selected = (i == Selected) ? -1 : i; Rebuild(); }

        // Nova pista vazia de 2 ou 4 carros (sem argumento, mantém o tipo atual).
        public void NewTrack(int faixas = 0) => Commit(() =>
        {
            if (faixas != 0) Faixas = TrackMath.Faixas(faixas);
            pieces.Clear();
            pieces.Add(new TrackPiece(PieceCatalog.Start.Id));
            Tail = 0;
            StartZ = 0;
            TrackName = "";
            Selected = -1;
        });

        public void Load(TrackSave s) => Commit(() => { Apply(s); Selected = -1; });

        public TrackSave ToSave() => TrackSave.From(TrackName, boardSize, cellCm, Origin, pieces, Tail, Faixas, StartZ);

        // Adapta a pista aberta para 2, 4 ou 6 carros (seção 32 do guia). Sem perda: as peças, a forma em células, a
        // largada e a mesa são as mesmas; só mudam a largura da pista, o número de trilhos e a escala no mundo.
        // Entra no desfazer. O nome ganha o tipo ("Oval (6 carros)"), para salvar a adaptada não sobrescrever a original.
        public void AdaptTo(int faixas)
        {
            faixas = TrackMath.Faixas(faixas);
            if (faixas == Faixas) return;
            Commit(() => { Faixas = faixas; TrackName = RenameForKind(TrackName, faixas); });
        }

        // "Oval" → "Oval (6 carros)"; "Oval (6 carros)" → "Oval (2 carros)". Nome vazio continua vazio.
        public static string RenameForKind(string name, int faixas)
        {
            if (string.IsNullOrWhiteSpace(name)) return name ?? "";
            name = System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s*\((2|4|6|8) carros\)$", "");
            return $"{name} ({faixas} carros)";
        }

        // ---------- Desfazer ----------

        string Snapshot() => ToSave().ToJson(false);

        void Commit(Action change)
        {
            CancelAutoClose();
            undo.Push(Snapshot());
            if (undo.Count > 300) TrimUndo();
            redo.Clear();
            bool fitted = Fits();
            change();
            // Passou do tamanho máximo da mesa: desfaz e avisa. (Pista antiga que já não cabia continua editável.)
            if (fitted && !Fits()) { Apply(TrackSave.FromJson(undo.Pop())); Notice?.Invoke(TooBig); Rebuild(); return; }
            GrowBoard();
            SettleTail();
            Rebuild();
        }

        // Pista fechada é a mesma volta de qualquer jeito: já não há ponta de trás, então as peças voltam a contar a partir da largada.
        void SettleTail()
        {
            if (Tail > 0 && TrackLayout.Analyze(Origin, TrackLayout.Build(Origin, pieces, Tail, StartZ), Tail).closed) Tail = 0;
        }

        // A mesa cresce com a pista: se a pista passa da borda, a mesa ganha células (com 1 de folga). Se passou da
        // esquerda ou de baixo, a pista inteira é empurrada para dentro (células inteiras: a largada continua encaixada)
        // e avisa quem mostra (Shifted), para a câmera acompanhar. A mesa nunca encolhe sozinha.
        const int BoardMargin = 1, BoardMax = 120;
        const string TooBig = "Não cabe: a mesa vai até 120 × 120 células.";

        // Tamanho que a mesa precisa ter para a pista (sem o limite) e quanto a pista é empurrada para dentro.
        void Needed(Pose2 origin, out int dx, out int dz, out int w, out int h)
        {
            var b = TrackLayout.Analyze(origin, TrackLayout.Build(origin, pieces, Tail, StartZ), Tail).bounds;
            dx = b.xMin < -0.01f ? Mathf.CeilToInt(-b.xMin) + BoardMargin : 0;
            dz = b.yMin < -0.01f ? Mathf.CeilToInt(-b.yMin) + BoardMargin : 0;
            w = boardSize.x + dx; h = boardSize.y + dz;
            if (b.xMax + dx > w + 0.01f) w = Mathf.CeilToInt(b.xMax + dx) + BoardMargin;
            if (b.yMax + dz > h + 0.01f) h = Mathf.CeilToInt(b.yMax + dz) + BoardMargin;
        }

        bool Fits(Pose2 origin) { if (pieces.Count == 0) return true; Needed(origin, out _, out _, out int w, out int h); return w <= BoardMax && h <= BoardMax; }
        bool Fits() => Fits(Origin);

        bool GrowBoard()
        {
            if (pieces.Count == 0) return false;
            Needed(Origin, out int dx, out int dz, out int w, out int h);
            w = Mathf.Min(w, BoardMax); h = Mathf.Min(h, BoardMax);
            if (dx == 0 && dz == 0 && w == boardSize.x && h == boardSize.y) return false;
            if (dx != 0 || dz != 0)
            {
                Origin = new Pose2(Origin.pos + new Vector2(dx, dz), Origin.heading);
                Shifted?.Invoke(new Vector2(dx, dz) * Scale);     // no mundo
            }
            boardSize = new Vector2Int(w, h);
            return true;
        }

        public void Undo() { if (undo.Count == 0) return; CancelAutoClose(); redo.Push(Snapshot()); Apply(TrackSave.FromJson(undo.Pop())); Rebuild(); }
        public void Redo() { if (redo.Count == 0) return; CancelAutoClose(); undo.Push(Snapshot()); Apply(TrackSave.FromJson(redo.Pop())); Rebuild(); }

        void TrimUndo()
        {
            var keep = undo.ToArray();                     // do mais novo para o mais velho
            undo.Clear();
            for (int i = Mathf.Min(keep.Length, 200) - 1; i >= 0; i--) undo.Push(keep[i]);
        }

        void Apply(TrackSave s)
        {
            var list = s.Pieces();
            pieces.Clear();
            pieces.AddRange(list);
            Origin = s.Origin;
            StartZ = s.StartZ;
            Tail = s.Tail;
            if (Tail > 0 && TrackLayout.Analyze(Origin, TrackLayout.Build(Origin, pieces, Tail, StartZ), Tail).closed) Tail = 0;
            boardSize = new Vector2Int(Mathf.Clamp(s.mesa.w, 8, 120), Mathf.Clamp(s.mesa.h, 8, 120));
            cellCm = s.escalaCm > 0 ? s.escalaCm : 15f;
            Faixas = s.Faixas;
            TrackName = s.nome ?? "";
            if (Selected >= pieces.Count) Selected = -1;
        }

        // ---------- Reconstrução ----------

        public void Rebuild()
        {
            if (theme == null || pieces.Count == 0) return;
            if (Selected >= pieces.Count) Selected = -1;
            Segments = TrackLayout.Build(Origin, pieces, Tail, StartZ);
            Info = TrackLayout.Analyze(Origin, Segments, Tail, Faixas);
            Root.localScale = Vector3.one * Scale;

            views.RemoveAll(v => v == null);
            while (views.Count < Segments.Count)
            {
                var go = new GameObject("Peça", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(TrackPieceView)) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(Root, false);
                views.Add(go.GetComponent<TrackPieceView>());
            }
            while (views.Count > Segments.Count) { TrackPieceView.Kill(views[views.Count - 1].gameObject); views.RemoveAt(views.Count - 1); }

            TrackLayout.ComputeBanking(Segments, Info.closed, Tail);
            for (int i = 0; i < Segments.Count; i++)
            {
                var s = Segments[i];
                views[i].index = i;
                views[i].name = $"{i + 1:00} {s.piece.Describe()}";
                views[i].Build(s, theme, null, Faixas);
                // Na corrida, sem destaques de edição.
                views[i].SetHighlight(RaceMode ? null : i == Selected ? theme.selected : Info.overlaps.Contains(i) ? theme.overlap : null);
            }

            UpdateArrow();
            if (arrow) arrow.gameObject.SetActive(!RaceMode);
            if (arrow2) arrow2.gameObject.SetActive(!RaceMode);
            if (ghostEntry.HasValue && !RaceMode) ShowGhost(ghostEntry.Value); else if (ghost) ghost.gameObject.SetActive(false);
            if (RaceMode) SetHoverCell(null);
            if (board) board.Build(boardSize, Scale, theme.Board(Faixas));
            // Só salva o rascunho no Play e fora da corrida: no Editor a pista é só mostrada,
            // e na corrida a pista em uso pode ser outra (escolhida na lista).
            if (Application.isPlaying && !RaceMode)
            {
                try { TrackLibrary.SaveDraft(ToSave()); }
                catch (Exception e) { Debug.LogWarning("Não deu para salvar o rascunho: " + e.Message); }
            }
            Changed?.Invoke();
        }

        // ---------- Fechar o circuito sozinho ----------

        // A busca anda aos poucos (BuilderPanel chama PumpAutoClose a cada quadro) e pode ser cancelada.
        public TrackCloser.Job CloseJob { get; private set; }
        public bool Closing => CloseJob != null;

        // Começa a fechar a pista da ponta até a largada. Devolve a mensagem se não há o que buscar, ou null se começou.
        public string BeginAutoClose()
        {
            if (Info == null) return "";
            if (Closing) return null;
            if (Info.closed) return "A pista já está fechada.";
            if (pieces.Count < 2) return "Coloque algumas peças antes: o fechamento parte da ponta da pista.";
            // Segue o estilo da pista: se ela usa mais curvas inclinadas, fecha com inclinadas.
            int banked = 0, plain = 0;
            foreach (var p in pieces) { var d = p.Def; if (d.IsCurve) { if (d.Banked) banked++; else plain++; } }
            // Fecha da ponta da frente até a ponta de trás (a largada, se não há peças atrás dela).
            int fe = FrontEnd;
            CloseJob = TrackCloser.Start(Origin, pieces.GetRange(0, fe), pieces.GetRange(fe, Tail), banked > plain, StartZ);
            return null;
        }

        public void CancelAutoClose()
        {
            if (CloseJob == null) return;
            CloseJob.Cancel();
            CloseJob = null;
            Notice?.Invoke("Busca cancelada.");
        }

        // Trabalha na busca por até budgetMs. Quando acaba, entra na pista (uma ação só: Ctrl+Z desfaz tudo)
        // e avisa o jogador. Devolve true no quadro em que terminou.
        public bool PumpAutoClose(long budgetMs)
        {
            if (CloseJob == null) return false;
            var job = CloseJob;
            try { if (job.Pump(budgetMs)) return false; }
            catch (Exception e)
            {
                CloseJob = null;
                Debug.LogError("Fechar circuito falhou: " + e);
                Notice?.Invoke("Não deu para fechar o circuito.");
                return true;
            }
            CloseJob = null;
            var r = job.Result;
            if (r.ok)
            {
                int fe = FrontEnd;
                Commit(() => { pieces.InsertRange(fe, r.pieces); Selected = -1; });
            }
            Notice?.Invoke(r.message);
            return true;
        }

        // De uma vez só (scripts e testes): busca inteira sem devolver o controle. Devolve a mensagem para o jogador.
        public string AutoClose()
        {
            string msg = BeginAutoClose();
            if (msg != null) return msg;
            var job = CloseJob;
            job.Pump(long.MaxValue);
            string result = null;
            void Keep(string m) => result = m;
            Notice += Keep;
            PumpAutoClose(0);
            Notice -= Keep;
            return result;
        }

        // ---------- Modo corrida ----------

        // Na corrida: sem seta, sem fantasma, sem seleção, e o rascunho do construtor não é tocado.
        public bool RaceMode { get; set; }

        // Mostra uma pista sem passar pelo desfazer (troca de pista na corrida, volta ao construtor).
        public void ShowTrack(TrackSave s)
        {
            CancelAutoClose();
            Apply(s);
            GrowBoard();
            Selected = -1;
            Rebuild();
        }

        Transform Quad(string name, Material m, float y)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            TrackPieceView.Kill(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(Root, false);
            return go.transform;
        }

        // Seta amarela no ponto onde a próxima peça vai encaixar; uma menor marca a outra ponta (frente × trás).
        void UpdateArrow()
        {
            if (!arrow) { arrow = Quad("Seta de inserção", theme.arrow, 0f); arrow.localScale = new Vector3(1.28f, 1.28f, 1f); }
            if (!arrow2) { arrow2 = Quad("Seta da outra ponta", theme.arrow, 0f); arrow2.localScale = new Vector3(0.8f, 0.8f, 1f); }
            PlaceArrow(arrow, InsertPose, InsertLevel);
            var tip = Segments[FrontEnd - 1];
            PlaceArrow(arrow2, Back ? tip.b : BackPose, Back ? tip.zOut : 0);
        }

        static void PlaceArrow(Transform t, Pose2 p, int level)
        {
            // A imagem aponta para +X e o centro dela fica 0,25 célula à frente da linha.
            var c = p.pos + TrackMath.Dir(p.heading) * 0.25f;
            t.localPosition = new Vector3(c.x, level * TrackMath.LevelHeight + 0.03f, c.y);
            t.localRotation = Quaternion.Euler(0f, -p.heading, 0f) * Quaternion.Euler(90f, 0f, 0f);
        }

        // ---------- Peça fantasma e célula sob o mouse ----------

        PaletteEntry? ghostEntry;

        // Mostra em amarelo transparente onde a peça da paleta vai encaixar.
        public void ShowGhost(PaletteEntry e)
        {
            ghostEntry = e;
            if (Segments.Count == 0) return;
            if (!ghost)
            {
                var go = new GameObject("Peça fantasma", typeof(MeshFilter), typeof(MeshRenderer), typeof(TrackPieceView)) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(Root, false);
                go.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                ghost = go.GetComponent<TrackPieceView>();
            }
            int zIn = InsertLevel;
            int gdir = Back && !e.def.IsLoop ? -e.dir : e.dir, grise = Back ? 0 : e.rise;     // de ré a curva espelha; o loop, não
            var seg = new Segment
            {
                index = -1, piece = new TrackPiece(e.def.Id, gdir, grise), def = e.def, a = InsertPose,
                zIn = zIn, zOut = Mathf.Clamp(zIn + grise, 0, TrackMath.MaxLevel),
            };
            seg.b = TrackMath.PoseAt(seg.a, seg.def, gdir, 1f);
            ghost.gameObject.SetActive(true);
            ghost.Build(seg, theme, theme.selected, Faixas);
        }

        public void HideGhost()
        {
            ghostEntry = null;
            if (ghost) ghost.gameObject.SetActive(false);
        }

        // Destaca a célula da mesa sob o mouse (null esconde).
        public void SetHoverCell(Vector2Int? cell)
        {
            if (!cell.HasValue) { if (hoverCell) hoverCell.gameObject.SetActive(false); return; }
            if (!hoverCell) hoverCell = Quad("Célula sob o mouse", theme.selected, 0f);
            hoverCell.gameObject.SetActive(true);
            hoverCell.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // O tampo da mesa fica Thickness abaixo do nível 0 (a base da pista tem espessura).
            hoverCell.localPosition = new Vector3(cell.Value.x + 0.5f, (-TrackPieceView.Thickness + 0.004f) / Scale, cell.Value.y + 0.5f);
        }

        public int IndexOf(Collider c)
        {
            var v = c ? c.GetComponent<TrackPieceView>() : null;
            return v ? v.index : -1;
        }
    }
}
