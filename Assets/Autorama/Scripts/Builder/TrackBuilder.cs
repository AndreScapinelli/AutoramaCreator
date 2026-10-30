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
        };

        public string TrackName { get; set; } = "";
        public Pose2 Origin { get; private set; }
        public readonly List<TrackPiece> pieces = new List<TrackPiece>();
        public List<Segment> Segments { get; private set; } = new List<Segment>();
        public TrackAnalysis Info { get; private set; }
        public int Selected { get; private set; } = -1;      // -1 = encaixa no fim da pista
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public event Action Changed;
        public event Action<Vector2> Shifted;     // a pista foi empurrada para dentro da mesa (a câmera acompanha)

        readonly List<TrackPieceView> views = new List<TrackPieceView>();
        readonly Stack<string> undo = new Stack<string>(), redo = new Stack<string>();
        GameObject generated;              // raiz das peças geradas (fora da cena salva)
        Transform arrow, hoverCell;
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
            arrow = hoverCell = null;
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

        public int InsertIndex => Selected < 0 ? pieces.Count : Selected + 1;
        public Pose2 InsertPose => Selected < 0 ? Segments[Segments.Count - 1].b : Segments[Selected].b;
        public int InsertLevel => Selected < 0 ? Segments[Segments.Count - 1].zOut : Segments[Selected].zOut;

        public void Add(PaletteEntry e) => Commit(() =>
        {
            int at = InsertIndex;
            pieces.Insert(at, new TrackPiece(e.def.Id, e.dir, e.rise));
            if (Selected >= 0) Selected = at;     // continua encaixando em sequência
        });

        public void RemoveSelected()
        {
            if (Selected <= 0) return;            // a largada não sai
            Commit(() => { pieces.RemoveAt(Selected); Selected--; });
        }

        public void RemoveLast()
        {
            if (pieces.Count < 2) return;
            Commit(() => { pieces.RemoveAt(pieces.Count - 1); if (Selected >= pieces.Count) Selected = -1; });
        }

        public void FlipSelected()
        {
            if (Selected <= 0 || !pieces[Selected].Def.IsCurve) return;
            Commit(() => pieces[Selected].dir *= -1);
        }

        public void SetRise(int rise)
        {
            if (Selected <= 0) return;
            Commit(() => pieces[Selected].rise = rise);
        }

        // Gira a largada 90° em volta do centro dela; a pista inteira vai junto.
        public void RotateStart() => Commit(() =>
        {
            Vector2 mid = Origin.pos + TrackMath.Dir(Origin.heading);
            float h = Origin.heading + 90f;
            Origin = TrackMath.SnapStart(new Pose2(mid - TrackMath.Dir(h), h));
        });

        // Arrastar a largada: BeginEdit uma vez no começo, MoveStart a cada passo.
        public void BeginEdit() { undo.Push(Snapshot()); redo.Clear(); }
        public void EndEdit() { if (GrowBoard()) Rebuild(); }     // arrastando, a mesa espera: cresce quando solta
        public void MoveStart(Vector2 pos)
        {
            var o = TrackMath.SnapStart(new Pose2(pos, Origin.heading));
            if (o.pos == Origin.pos) return;
            Origin = o;
            Rebuild();
        }

        public void Select(int i) { Selected = (i == Selected) ? -1 : i; Rebuild(); }

        public void NewTrack() => Commit(() =>
        {
            pieces.Clear();
            pieces.Add(new TrackPiece(PieceCatalog.Start.Id));
            TrackName = "";
            Selected = -1;
        });

        public void Load(TrackSave s) => Commit(() => { Apply(s); Selected = -1; });

        public TrackSave ToSave() => TrackSave.From(TrackName, boardSize, cellCm, Origin, pieces);

        // ---------- Desfazer ----------

        string Snapshot() => ToSave().ToJson(false);

        void Commit(Action change)
        {
            undo.Push(Snapshot());
            if (undo.Count > 300) TrimUndo();
            redo.Clear();
            change();
            GrowBoard();
            Rebuild();
        }

        // A mesa cresce com a pista: se a pista passa da borda, a mesa ganha células (com 1 de folga). Se passou da
        // esquerda ou de baixo, a pista inteira é empurrada para dentro (células inteiras: a largada continua encaixada)
        // e avisa quem mostra (Shifted), para a câmera acompanhar. A mesa nunca encolhe sozinha.
        const int BoardMargin = 1, BoardMax = 120;
        bool GrowBoard()
        {
            if (pieces.Count == 0) return false;
            var b = TrackLayout.Analyze(Origin, TrackLayout.Build(Origin, pieces)).bounds;
            int dx = b.xMin < -0.01f ? Mathf.CeilToInt(-b.xMin) + BoardMargin : 0;
            int dz = b.yMin < -0.01f ? Mathf.CeilToInt(-b.yMin) + BoardMargin : 0;
            int w = boardSize.x + dx, h = boardSize.y + dz;
            if (b.xMax + dx > w + 0.01f) w = Mathf.CeilToInt(b.xMax + dx) + BoardMargin;
            if (b.yMax + dz > h + 0.01f) h = Mathf.CeilToInt(b.yMax + dz) + BoardMargin;
            w = Mathf.Min(w, BoardMax); h = Mathf.Min(h, BoardMax);
            if (dx == 0 && dz == 0 && w == boardSize.x && h == boardSize.y) return false;
            if (dx != 0 || dz != 0)
            {
                Origin = new Pose2(Origin.pos + new Vector2(dx, dz), Origin.heading);
                Shifted?.Invoke(new Vector2(dx, dz));
            }
            boardSize = new Vector2Int(w, h);
            return true;
        }

        public void Undo() { if (undo.Count == 0) return; redo.Push(Snapshot()); Apply(TrackSave.FromJson(undo.Pop())); Rebuild(); }
        public void Redo() { if (redo.Count == 0) return; undo.Push(Snapshot()); Apply(TrackSave.FromJson(redo.Pop())); Rebuild(); }

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
            boardSize = new Vector2Int(Mathf.Clamp(s.mesa.w, 8, 120), Mathf.Clamp(s.mesa.h, 8, 120));
            cellCm = s.escalaCm > 0 ? s.escalaCm : 15f;
            TrackName = s.nome ?? "";
            if (Selected >= pieces.Count) Selected = -1;
        }

        // ---------- Reconstrução ----------

        public void Rebuild()
        {
            if (theme == null || pieces.Count == 0) return;
            if (Selected >= pieces.Count) Selected = -1;
            Segments = TrackLayout.Build(Origin, pieces);
            Info = TrackLayout.Analyze(Origin, Segments);

            views.RemoveAll(v => v == null);
            while (views.Count < Segments.Count)
            {
                var go = new GameObject("Peça", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(TrackPieceView)) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(Root, false);
                views.Add(go.GetComponent<TrackPieceView>());
            }
            while (views.Count > Segments.Count) { TrackPieceView.Kill(views[views.Count - 1].gameObject); views.RemoveAt(views.Count - 1); }

            TrackLayout.ComputeBanking(Segments, Info.closed);
            for (int i = 0; i < Segments.Count; i++)
            {
                var s = Segments[i];
                views[i].index = i;
                views[i].name = $"{i + 1:00} {s.piece.Describe()}";
                views[i].Build(s, theme);
                // Na corrida, sem destaques de edição.
                views[i].SetHighlight(RaceMode ? null : i == Selected ? theme.selected : Info.overlaps.Contains(i) ? theme.overlap : null);
            }

            UpdateArrow();
            if (arrow) arrow.gameObject.SetActive(!RaceMode);
            if (ghostEntry.HasValue && !RaceMode) ShowGhost(ghostEntry.Value); else if (ghost) ghost.gameObject.SetActive(false);
            if (RaceMode) SetHoverCell(null);
            if (board) board.Build(boardSize);
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

        // Completa a pista da ponta até a largada (uma ação só: Ctrl+Z desfaz tudo). Devolve a mensagem para o jogador.
        public string AutoClose()
        {
            if (Info == null) return "";
            if (Info.closed) return "A pista já está fechada.";
            if (pieces.Count < 2) return "Coloque algumas peças antes: o fechamento parte da ponta da pista.";
            // Segue o estilo da pista: se ela usa mais curvas inclinadas, fecha com inclinadas.
            int banked = 0, plain = 0;
            foreach (var p in pieces) { var d = p.Def; if (d.IsCurve) { if (d.Banked) banked++; else plain++; } }
            var r = TrackCloser.Close(Origin, pieces, banked > plain);
            if (!r.ok) return r.message;
            Commit(() => { pieces.AddRange(r.pieces); Selected = -1; });
            return r.message;
        }

        // ---------- Modo corrida ----------

        // Na corrida: sem seta, sem fantasma, sem seleção, e o rascunho do construtor não é tocado.
        public bool RaceMode { get; set; }

        // Mostra uma pista sem passar pelo desfazer (troca de pista na corrida, volta ao construtor).
        public void ShowTrack(TrackSave s)
        {
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

        // Seta amarela no ponto onde a próxima peça vai encaixar.
        void UpdateArrow()
        {
            if (!arrow) { arrow = Quad("Seta de inserção", theme.arrow, 0f); arrow.localScale = new Vector3(1.28f, 1.28f, 1f); }
            var p = InsertPose;
            // A imagem aponta para +X e o centro dela fica 0,25 célula à frente da linha.
            var c = p.pos + TrackMath.Dir(p.heading) * 0.25f;
            arrow.localPosition = new Vector3(c.x, InsertLevel * TrackMath.LevelHeight + 0.03f, c.y);
            arrow.localRotation = Quaternion.Euler(0f, -p.heading, 0f) * Quaternion.Euler(90f, 0f, 0f);
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
            var seg = new Segment
            {
                index = -1, piece = new TrackPiece(e.def.Id, e.dir, e.rise), def = e.def, a = InsertPose,
                zIn = zIn, zOut = Mathf.Clamp(zIn + e.rise, 0, TrackMath.MaxLevel),
            };
            seg.b = TrackMath.PoseAt(seg.a, seg.def, e.dir, 1f);
            ghost.gameObject.SetActive(true);
            ghost.Build(seg, theme, theme.selected);
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
            hoverCell.localPosition = new Vector3(cell.Value.x + 0.5f, 0.002f, cell.Value.y + 0.5f);
        }

        public int IndexOf(Collider c)
        {
            var v = c ? c.GetComponent<TrackPieceView>() : null;
            return v ? v.index : -1;
        }
    }
}
