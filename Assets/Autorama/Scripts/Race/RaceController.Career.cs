using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Carreira (seções 68 e 69 do guia): telas próprias por cima da mesa, fora do painel lateral.
    // Save → Mapa 3D (a maquete da região: uma pista com um pino por prova e o carrinho que anda até a escolhida)
    // → Cartão da prova → Corrida (objetivos ao vivo, Esc pausa) → Resultado → Mapa.
    // O jogador é sempre o carro 0; os adversários são a IA do nó.
    public partial class RaceController
    {
        enum KScreen { Saves, Map, Race, Result }
        KScreen kScreen;
        int kSlot = 1, kRegion, kSel;
        CareerSave kSave;
        bool kTurbo, kSaved, kCard, kPause, kTiltWas;
        float kCarS, kDoneT;
        int kFrame;
        int kGot, kNew;
        ResultadoCorrida kRes;

        VisualElement kLayer, kSaves, kMap, kCardBox, kObjBox, kPauseBox, kResBox;
        Label kRegionLabel, kStarsLabel;
        readonly List<Button> kPinLabels = new List<Button>();
        readonly List<GameObject> kPins = new List<GameObject>();
        readonly List<float> kPinS = new List<float>();
        GameObject kPinRoot;

        RegionDef KRegion => Career.Def.regioes[Mathf.Clamp(kRegion, 0, Career.Def.regioes.Length - 1)];
        NodeDef KNode => kSel >= 0 && kSel < KRegion.nos.Length ? KRegion.nos[kSel] : null;
        // Na carreira, o HUD da corrida (placar, controles, minimapa, câmera) só aparece na prova.
        bool RaceUI => !panel.CareerMode || kScreen == KScreen.Race || kScreen == KScreen.Result;
        // A simulação só anda na prova (e no resultado, com os carros terminando); no mapa o carrinho é posto à mão.
        bool CareerSteps => !panel.CareerMode || (kScreen == KScreen.Race && !kPause) || kScreen == KScreen.Result;

        // ---------- montagem ----------

        void BuildCareerUI()
        {
            kLayer = El("k-layer", panel.Root);
            kLayer.pickingMode = PickingMode.Ignore;
            kLayer.style.display = DisplayStyle.None;

            // Save
            kSaves = El("k-full k-dim", kLayer);
            var sc = El("k-panel k-saves", kSaves);
            Text("CARREIRA", "start-title", sc);
            Text("Da garagem ao campeonato nacional.", "start-sub", sc);
            El("k-save-row", sc).name = "rows";
            Btn("Voltar ao menu (Esc)", CareerExit, "btn start-btn", sc);

            // Mapa: barra de cima, cartão da prova e os rótulos dos pinos (criados com o mapa)
            kMap = El("k-full", kLayer);
            kMap.pickingMode = PickingMode.Ignore;
            var top = El("k-top", kMap);
            Btn("◀ Saves", () => CareerScreen(KScreen.Saves), "btn", top);
            var reg = El("k-top-mid", top);
            Btn("◀", () => CareerRegion(-1), "btn icon-btn", reg);
            kRegionLabel = Text("", "k-region", reg);
            Btn("▶", () => CareerRegion(1), "btn icon-btn", reg);
            kStarsLabel = Text("", "k-stars", top);
            kCardBox = El("k-panel k-card", kMap);
            Text("← → escolhe a prova · Enter abre e corre · Esc volta", "k-hint", kMap).pickingMode = PickingMode.Ignore;

            // Corrida: objetivos ao vivo e a pausa do Esc
            kObjBox = El("k-obj", kLayer);
            kObjBox.pickingMode = PickingMode.Ignore;
            kPauseBox = El("k-full k-dim", kLayer);
            var pc = El("k-panel k-small", kPauseBox);
            Text("PAUSA", "k-title", pc);
            Btn("Continuar (Esc)", () => CareerPause(false), "btn btn--primary k-wide", pc);
            Btn("Reiniciar", () => { CareerPause(false); CareerRun(); }, "btn k-wide", pc);
            Btn("Voltar ao mapa", () => { CareerPause(false); CareerScreen(KScreen.Map); }, "btn k-wide", pc);

            // Resultado
            kResBox = El("k-full k-dim", kLayer);
            El("k-panel k-result", kResBox).name = "res";

            TopDownCamera.Moved += CareerPlacePins;
        }

        void CareerDestroy() => TopDownCamera.Moved -= CareerPlacePins;

        // Entrar na aba (pelo menu inicial).
        void CareerEnterMode()
        {
            kTiltWas = cam.Tilted;
            panel.HideChrome(true);
            kLayer.style.display = DisplayStyle.Flex;
            kSave ??= Career.Load(kSlot);
            CareerScreen(KScreen.Saves);
        }

        void CareerLeave()
        {
            if (kLayer == null || kLayer.style.display == DisplayStyle.None) return;
            kLayer.style.display = DisplayStyle.None;
            kPause = false;
            ClearPins();
            cam.SetTilted(kTiltWas);
            panel.HideChrome(false);
        }

        void CareerExit()
        {
            panel.SetMode(Mode.Build);
            panel.ShowMenu(true);
        }

        void CareerScreen(KScreen s)
        {
            kScreen = s;
            kCard = false;
            kPause = false;
            kSaves.style.display = s == KScreen.Saves ? DisplayStyle.Flex : DisplayStyle.None;
            kMap.style.display = s == KScreen.Map ? DisplayStyle.Flex : DisplayStyle.None;
            kObjBox.style.display = s == KScreen.Race ? DisplayStyle.Flex : DisplayStyle.None;
            kPauseBox.style.display = DisplayStyle.None;
            kResBox.style.display = s == KScreen.Result ? DisplayStyle.Flex : DisplayStyle.None;
            topBox.style.display = RaceUI ? DisplayStyle.Flex : DisplayStyle.None;
            if (s == KScreen.Saves) { ClearPins(); DrawSaves(); ShowMapTrack(); }
            else if (s == KScreen.Map) ShowMapTrack();
            DrawPadHeads();
            scoreDirty = true;
        }

        // ---------- save ----------

        void DrawSaves()
        {
            var rows = kSaves.Q("rows");
            rows.Clear();
            for (int i = 1; i <= Career.Slots; i++)
            {
                int slot = i;
                var sv = Career.Load(slot);
                int stars = 0, total = 0, last = 0;
                for (int r = 0; r < Career.Def.regioes.Length; r++)
                {
                    stars += Career.RegionStars(sv, Career.Def.regioes[r]);
                    total += Career.Def.regioes[r].nos.Length * 3;
                    if (Career.RegionOpen(sv, r)) last = r;
                }
                var b = new Button(() => { kSlot = slot; kSave = Career.Load(slot); kRegion = last; kSel = FirstOpen(); CareerScreen(KScreen.Map); });
                b.AddToClassList("k-save");
                Text($"Carreira {slot}", "k-save-title", b).pickingMode = PickingMode.Ignore;
                Text(sv.nos.Count == 0 ? "Nova carreira" : Career.Def.regioes[last].nome, "k-save-sub", b).pickingMode = PickingMode.Ignore;
                Text($"★ {stars} / {total}", "k-save-stars", b).pickingMode = PickingMode.Ignore;
                rows.Add(b);
            }
        }

        // ---------- mapa ----------

        int FirstOpen()
        {
            var R = KRegion;
            int first = -1;
            for (int i = 0; i < R.nos.Length; i++)
            {
                if (!Career.NodeOpen(kSave, R, R.nos[i])) continue;
                if (first < 0) first = i;
                if (Career.Stars(kSave, R.nos[i].id) < 3) return i;
            }
            return Mathf.Max(0, first);
        }

        void CareerRegion(int d)
        {
            int n = Career.Def.regioes.Length, r = kRegion + d;
            if (r < 0 || r >= n) { panel.Toast("As outras regiões ainda não estão prontas."); return; }
            if (!Career.RegionOpen(kSave, r)) { panel.Toast("Seja campeão desta região para abrir a próxima."); return; }
            kRegion = r;
            kSel = FirstOpen();
            kCard = false;
            ShowMapTrack();
        }

        // A maquete: a pista "mapa-<região>" na mesa, câmera parada de cima, o carrinho 0 sozinho e um pino por prova.
        void ShowMapTrack()
        {
            var t = Career.Track("mapa-" + KRegion.id);
            sim.set = new RaceSettings { laps = 1, turbo = false };
            sim.cutoff = null;
            for (int k = 0; k < RaceSim.MaxCars; k++) { sim.present[k] = k == 0; sim.aiSkill[k] = 0; sim.setup[k] = default; }
            if (t != null) builder.ShowTrack(t);
            sim.Reset();
            cam.SetRaceViewMode(TopDownCamera.RaceViewMode.Atual);
            cam.SetTilted(true);                            // maquete vista inclinada
            cam.SetDistanceScale(1f);                       // sem o zoom da corrida (Opções)
            panel.FrameTrack();
            kFrame = 5;                                     // enquadra de novo nos próximos quadros: a mesa se monta depois
            if (kScreen != KScreen.Map) return;
            BuildPins();
            kCarS = kSel < kPinS.Count ? kPinS[kSel] : 0f;
            DrawMapUI();
        }

        void ClearPins()
        {
            if (kPinRoot != null) Destroy(kPinRoot);
            kPinRoot = null;
            kPins.Clear(); kPinS.Clear();
            foreach (var l in kPinLabels) l.RemoveFromHierarchy();
            kPinLabels.Clear();
        }

        void BuildPins()
        {
            ClearPins();
            if (!sim.Ready) return;
            var R = KRegion;
            var path = sim.PathOf(0);
            kPinRoot = new GameObject("Mapa da carreira");
            for (int i = 0; i < R.nos.Length; i++)
            {
                int idx = i;
                float s = path.Total * (i + 0.5f) / R.nos.Length;
                kPinS.Add(s);
                var pt = path.At(s);
                var side = TrackMath.Dir(pt.heading + 90f) * 1.1f;     // ao lado da pista, por dentro da volta
                bool big = R.nos[i].tipo == "rival" || R.nos[i].tipo == "campeonato";
                var pin = new GameObject("Prova " + R.nos[i].id);
                pin.transform.SetParent(kPinRoot.transform, false);
                pin.transform.position = new Vector3(pt.pos.x + side.x, pt.y, pt.pos.y + side.y);
                Part(PrimitiveType.Cylinder, pin.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.08f, 0.25f, 0.08f));
                Part(PrimitiveType.Cylinder, pin.transform, new Vector3(0f, 0.52f, 0f), big ? new Vector3(0.75f, 0.04f, 0.75f) : new Vector3(0.5f, 0.04f, 0.5f));
                kPins.Add(pin);
                var lb = new Button(() => CareerPick(idx));
                lb.AddToClassList("k-pin");
                kLayer.Add(lb);
                kPinLabels.Add(lb);
            }
            ColorPins();
        }

        void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale)
        {
            var g = GameObject.CreatePrimitive(type);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().material = new Material(builder.theme.support);
        }

        static readonly Color PinDone = new Color(0.24f, 0.75f, 0.45f), PinGold = new Color(0.96f, 0.76f, 0.1f),
            PinOpen = new Color(0.92f, 0.94f, 0.9f), PinLocked = new Color(0.32f, 0.36f, 0.34f), PinRival = new Color(0.94f, 0.29f, 0.25f);

        void ColorPins()
        {
            var R = KRegion;
            for (int i = 0; i < kPins.Count; i++)
            {
                var n = R.nos[i];
                bool open = Career.NodeOpen(kSave, R, n);
                int st = Career.Stars(kSave, n.id);
                var c = !open ? PinLocked : st == 3 ? PinGold : st > 0 ? PinDone : n.tipo == "rival" ? PinRival : PinOpen;
                foreach (var r in kPins[i].GetComponentsInChildren<Renderer>()) r.material.color = c;
                kPinLabels[i].text = $"{(open ? "" : "🔒 ")}{n.nome}\n<size=85%>{Stars(st)}</size>";
                kPinLabels[i].EnableInClassList("k-pin--on", i == kSel);
                kPinLabels[i].EnableInClassList("k-pin--locked", !open);
            }
        }

        // Clicar num pino: escolhe a prova e abre o cartão; clicar de novo na mesma, com o cartão aberto, corre.
        void CareerPick(int i)
        {
            var R = KRegion;
            if (i < 0 || i >= R.nos.Length) return;
            if (!Career.NodeOpen(kSave, R, R.nos[i]))
            {
                panel.Toast(R.nos[i].tipo == "rival" ? $"Faça {R.estrelasRival} estrelas na região para abrir." : "Termine a prova anterior para abrir esta.");
                return;
            }
            if (i == kSel && kCard) { CareerRun(); return; }
            kSel = i;
            kCard = true;
            DrawMapUI();
        }

        // Anda para a prova aberta anterior ou seguinte (setas).
        void CareerStep(int d)
        {
            var R = KRegion;
            for (int i = kSel + d; i >= 0 && i < R.nos.Length; i += d)
                if (Career.NodeOpen(kSave, R, R.nos[i])) { kSel = i; DrawMapUI(); return; }
        }

        void DrawMapUI()
        {
            var R = KRegion;
            kRegionLabel.text = $"{R.nome} · {R.faixas} faixas";
            kStarsLabel.text = $"★ {Career.RegionStars(kSave, R)} / {R.nos.Length * 3}";
            ColorPins();
            kCardBox.Clear();
            var n = KNode;
            kCardBox.style.display = kCard && n != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!kCard || n == null) return;
            var t = Career.Track(n.pista);
            if (t != null) kCardBox.Add(new TrackThumb(t));
            Text($"{kSel + 1} · {n.nome}", "k-title", kCardBox);
            Text($"{TipoNome(n.tipo)} · {n.voltas} voltas" + (n.tipo == "relogio" ? " · sem turbo" : $" · contra {(n.adversarios == 1 ? "1 piloto" : $"{n.adversarios} pilotos")} IA {n.ia}"), "k-sub", kCardBox);
            if (n.tipo == "rival") Text($"{n.rival} · melhor de {n.baterias}, trocando de trilho" + (string.IsNullOrEmpty(n.rivalFrase) ? "" : $"\n“{n.rivalFrase}”"), "k-rival", kCardBox);
            if (n.regras != null) foreach (var g in n.regras) Text("⚠ " + Career.RegraTexto(n, g), "k-rule", kCardBox);
            for (int i = 0; i < n.estrelas.Length; i++) Text($"{new string('★', i + 1)}  {Career.Texto(n.estrelas[i])}", "k-goal", kCardBox);
            var ns = Career.Of(kSave, n.id);
            Text(ns == null ? "Ainda não correu aqui." : $"Seu melhor: {Stars(ns.estrelas)}" + (ns.melhorPos > 0 && n.tipo != "relogio" ? $" · {ns.melhorPos}º" : "") + (ns.melhorVolta >= 0f ? $" · volta {Fmt(ns.melhorVolta)}" : ""), "k-sub", kCardBox);
            var row = El("row", kCardBox);
            Btn("Correr (Enter)", CareerRun, "btn btn--primary", row);
            Btn("Fechar (Esc)", () => { kCard = false; DrawMapUI(); }, "btn", row);
        }

        // Rótulos sobre os pinos (o selecionado pulsa).
        void CareerPlacePins()
        {
            if (kScreen != KScreen.Map || kPinLabels.Count == 0 || kLayer.panel == null || !panel.CareerMode) return;
            var c = cam.Cam;
            for (int i = 0; i < kPins.Count; i++)
            {
                var s = c.WorldToScreenPoint(kPins[i].transform.position + Vector3.up * 0.65f);
                var l = kPinLabels[i];
                l.style.display = s.z > 0f ? DisplayStyle.Flex : DisplayStyle.None;
                var p = RuntimePanelUtils.ScreenToPanel(kLayer.panel, new Vector2(s.x, Screen.height - s.y));
                l.style.left = p.x; l.style.top = p.y;
                float pulse = i == kSel ? 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
                kPins[i].transform.localScale = Vector3.one * pulse;
            }
        }

        // O carrinho anda pela maquete até o pino da prova escolhida, pelo lado mais curto.
        void MapCarTick()
        {
            if (kFrame > 0 && --kFrame >= 0) panel.FrameTrack();
            if (!sim.Ready || kSel >= kPinS.Count) return;
            float total = sim.PathOf(0).Total;
            float d = Mathf.Repeat(kPinS[kSel] - kCarS, total);
            if (d > total / 2f) d -= total;
            float step = Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), 10f * Time.deltaTime);
            kCarS = Mathf.Repeat(kCarS + step, total);
            var car = sim.cars[0];
            car.s = kCarS;
            car.v = Mathf.Abs(step) > 1e-4f ? 6f : 0f;
        }

        // ---------- prova ----------

        // Rival (seção 71): bateria atual, placar e o que vale na série inteira.
        int kHeat, kWins, kLosses, kSerOffs;
        float kSerBest;
        bool kSerTurbo, kInterim, kOut;
        bool IsRival => KNode != null && KNode.tipo == "rival";
        int HeatsToWin => KNode.baterias / 2 + 1;

        // Correr a prova (no rival, começa a série).
        void CareerRun()
        {
            kHeat = kWins = kLosses = kSerOffs = 0; kSerBest = -1f; kSerTurbo = false;
            RunHeat();
        }

        void RunHeat()
        {
            var n = KNode;
            if (n == null) return;
            bool clock = n.tipo == "relogio";
            CareerScreen(KScreen.Race);
            ClearPins();
            sim.set = new RaceSettings
            {
                laps = n.voltas, vmax = set.vmax, ramp = set.ramp, grip = set.grip * (n.Has("molhada") ? Career.Wet : 1f), collide = set.collide,
                turbo = !clock && (n.turbo || n.Has("turboLivre")), reslotTime = set.reslotTime, halfGas = set.halfGas,
            };
            sim.cutoff = null;
            int ais = clock ? 0 : n.adversarios;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                sim.present[k] = k <= ais;
                sim.aiSkill[k] = k > 0 && k <= ais ? Mathf.Clamp(n.ia, 1, RaceSim.AiMaxSkill) : 0;
                sim.setup[k] = k == 0 && n.Has("carro") ? n.carro.Clamped : k == 1 && IsRival ? n.rivalCarro.Clamped : default;
            }
            var t = Career.Track(n.pista);
            if (t != null) builder.ShowTrack(t);
            ApplyCameraSettings();
            sim.Reset();
            kTurbo = false; kSaved = false; kInterim = false; kOut = false; kObjSig = null;
            DrawPadHeads();
            StartRace();
            // Rival: a cada bateria os dois trocam de trilho (o jogador continua o carro 0, com as mesmas teclas).
            if (IsRival && kHeat % 2 == 1) { sim.cars[0].lane = 1; sim.cars[1].lane = 0; }
        }

        void CareerPause(bool on)
        {
            kPause = on;
            kPauseBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Quadro a quadro, só na aba Carreira: teclas, carrinho do mapa, regras, objetivos ao vivo e o fim da prova.
        void CareerTick()
        {
            CareerKeys();
            if (kScreen == KScreen.Map) { MapCarTick(); return; }
            if (kScreen != KScreen.Race) return;
            var n = KNode;
            if (n == null) return;
            var me = sim.cars[0];
            if (sim.phase == RaceSim.Phase.Running)
            {
                if (me.boost) kTurbo = true;
                // Largada atrasada: preso no grid até passar o atraso.
                if (n.Has("atrasada") && sim.t < n.atraso && me.mode == CarMode.OnSlot) { me.v = 0f; me.s = sim.GridPos; }
                if (n.Has("turboLivre")) for (int k = 0; k < sim.N; k++) if (sim.present[k]) sim.cars[k].turbo = 1f;
                // Sobrevivência: passou do limite de saídas, está fora.
                if (n.Has("sobrevivencia") && me.offs > n.limite && !kSaved) kOut = true;
            }
            DrawObjectives();
            if (sim.phase != RaceSim.Phase.Done && !kOut) return;
            if (!kSaved)
            {
                kSaved = true;
                kDoneT = Time.unscaledTime;
                var r = Result();
                if (kOut) r.terminou = false;
                if (IsRival) Tally(r); else Finish(r);
            }
            if (Time.unscaledTime - kDoneT > 1.2f) ShowResult();       // um respiro depois da bandeirada
        }

        void Finish(ResultadoCorrida r)
        {
            kRes = r;
            kGot = Career.Avaliar(KNode, kRes);
            kNew = Career.Record(kSave, KNode, kRes);
            if (!Career.Save(kSlot, kSave)) panel.Toast("Não deu para salvar a carreira.");
        }

        // Rival: soma a bateria; a série acaba quando alguém faz a maioria ou acabam as baterias.
        void Tally(ResultadoCorrida r)
        {
            if (r.terminou && r.posicao == 1) kWins++; else kLosses++;
            kSerOffs += r.saidas;
            kSerTurbo |= r.turbo;
            if (r.melhorVolta >= 0f && (kSerBest < 0f || r.melhorVolta < kSerBest)) kSerBest = r.melhorVolta;
            kRes = r;
            kInterim = kWins < HeatsToWin && kLosses < HeatsToWin && kHeat + 1 < KNode.baterias;
            if (kInterim) return;
            Finish(new ResultadoCorrida
            {
                terminou = true, posicao = kWins > kLosses ? 1 : 2, carros = 2, saidas = kSerOffs, turbo = kSerTurbo,
                melhorVolta = kSerBest, vitorias = kWins, derrotas = kLosses,
            });
        }

        ResultadoCorrida Result()
        {
            var c = sim.cars[0];
            var ord = Order();
            return new ResultadoCorrida
            {
                terminou = c.finish.HasValue, posicao = ord.IndexOf(0) + 1, carros = ord.Count, saidas = c.offs,
                turbo = kTurbo, melhorVolta = c.best ?? -1f, tempo = c.finish ?? sim.t, vitorias = kWins, derrotas = kLosses,
            };
        }

        // ✓ cumprido · ✗ perdido · ↑ cumprindo agora (posição) · … ainda em aberto. Embaixo, as regras que contam.
        string kObjSig;
        void DrawObjectives()
        {
            var n = KNode;
            var r = Result();
            if (IsRival) { r.saidas += kSerOffs; r.turbo |= kSerTurbo; }
            int need = IsRival ? HeatsToWin : 0;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n.estrelas.Length; i++)
            {
                var o = n.estrelas[i];
                string mark;
                switch (o.tipo)
                {
                    case "semSair": mark = r.saidas > o.valor ? "✗" : r.terminou && !IsRival ? "✓" : "…"; break;
                    case "semTurbo": mark = r.turbo ? "✗" : r.terminou && !IsRival ? "✓" : "…"; break;
                    case "volta": mark = r.melhorVolta >= 0f && r.melhorVolta <= o.valor ? "✓" : "…"; break;
                    case "posicao": mark = r.terminou ? (r.posicao <= o.valor ? "✓" : "✗") : sim.phase == RaceSim.Phase.Running && r.posicao <= o.valor ? "↑" : "…"; break;
                    case "serie": mark = kWins >= need ? "✓" : kLosses >= need ? "✗" : "…"; break;
                    case "invicto": mark = kLosses > 0 ? "✗" : kWins >= need ? "✓" : "…"; break;
                    default: mark = r.terminou && Career.Cumpre(o, r) ? "✓" : "…"; break;
                }
                string color = mark == "✓" ? "#3DBE74" : mark == "✗" ? "#F07A3A" : "#94A197";
                sb.Append(i > 0 ? "\n" : "").Append($"<color={color}>{mark}</color>  {new string('★', i + 1)}  {Career.Texto(o)}");
            }
            if (IsRival) sb.Append($"\n<color=#F5C21B>Bateria {kHeat + 1} de {n.baterias} · você {kWins} × {kLosses} {n.rival}</color>");
            if (n.Has("sobrevivencia")) sb.Append($"\n<color={(sim.cars[0].offs >= n.limite ? "#F07A3A" : "#94A197")}>Saídas {sim.cars[0].offs} de {n.limite}</color>");
            if (n.Has("atrasada") && sim.phase == RaceSim.Phase.Running && sim.t < n.atraso) sb.Append($"\n<color=#F07A3A>Você sai em {Num(n.atraso - sim.t, 1)} s</color>");
            if (n.Has("molhada")) sb.Append("\n<color=#50A0F0>Pista molhada</color>");
            string sig = sb.ToString();
            if (sig == kObjSig) return;
            kObjSig = sig;
            kObjBox.Clear();
            Text(n.nome.ToUpperInvariant(), "k-obj-title", kObjBox);
            Text(sig, "k-obj-text", kObjBox);
        }

        // ---------- resultado ----------

        void ShowResult()
        {
            if (kScreen == KScreen.Result) return;
            CareerScreen(KScreen.Result);
            var box = kResBox.Q("res");
            box.Clear();
            var n = KNode;
            if (kInterim) { ShowHeat(box); return; }
            var stars = Text("☆☆☆", "k-res-stars", box);
            for (int i = 0; i < kGot; i++)
            {
                int k = i + 1;
                box.schedule.Execute(() => stars.text = Stars(k)).ExecuteLater(300 + 400 * i);   // uma estrela de cada vez
            }
            if (kGot == 3) box.schedule.Execute(Fogos).ExecuteLater(1500);
            string head = IsRival ? (kWins > kLosses ? $"Você venceu a série · {kWins} × {kLosses}" : $"{n.rival} venceu a série · {kLosses} × {kWins}")
                : kOut ? $"Fora: mais de {n.limite} {(n.limite == 1 ? "saída" : "saídas")}"
                : n.tipo == "relogio" ? (kRes.melhorVolta >= 0f ? $"Melhor volta {Fmt(kRes.melhorVolta)}" : "Nenhuma volta completa")
                : !kRes.terminou ? "Não terminou" : $"{kRes.posicao}º lugar · {Fmt(kRes.tempo)}";
            Text(head, "k-title", box);
            if (IsRival)
            {
                string fala = kWins > kLosses ? n.rivalDerrota : n.rivalVitoria;   // a fala é do rival: ele perdeu ou ganhou
                if (!string.IsNullOrEmpty(fala)) Text($"{n.rival}: “{fala}”", "k-sub", box);
            }
            Text(kGot == 0 ? "Não foi desta vez." : kNew > 0 ? $"+{kNew} {(kNew == 1 ? "estrela nova" : "estrelas novas")}" : "Sem estrela nova.", "k-sub", box);
            for (int i = 0; i < n.estrelas.Length; i++)
            {
                bool ok = kGot > 0 && Career.Cumpre(n.estrelas[i], kRes);
                Text($"{(ok ? "<color=#3DBE74>✓</color>" : "<color=#F07A3A>✗</color>")}  {Career.Texto(n.estrelas[i])}", "k-goal", box);
            }
            foreach (var x in NewlyOpened()) Text($"Abriu: {x}", "k-open", box);
            var row = El("row k-res-row", box);
            Btn("Correr de novo", CareerRun, "btn", row);
            Btn("Mapa (Esc)", () => CareerScreen(KScreen.Map), "btn", row);
            Btn("Próxima prova (Enter)", CareerNext, "btn btn--primary", row);
        }

        // Rival, no meio da série: quem ganhou a bateria e o placar.
        void ShowHeat(VisualElement box)
        {
            var n = KNode;
            bool won = kRes.terminou && kRes.posicao == 1;
            Text($"Bateria {kHeat + 1} de {n.baterias}", "k-sub", box);
            Text(won ? "Você venceu a bateria" : $"{n.rival} venceu a bateria", "k-title", box);
            Text($"Você {kWins} × {kLosses} {n.rival}", "k-res-score", box);
            Text("Na próxima, vocês trocam de trilho.", "k-sub", box);
            var row = El("row k-res-row", box);
            Btn("Desistir (Esc)", () => CareerScreen(KScreen.Map), "btn", row);
            Btn("Próxima bateria (Enter)", NextHeat, "btn btn--primary", row);
        }

        void NextHeat() { kHeat++; RunHeat(); }

        // Provas que esta corrida abriu: a 1ª estrela aqui (antes não tinha nenhuma) e nenhuma outra porta delas aberta.
        List<string> NewlyOpened()
        {
            var list = new List<string>();
            if (kGot == 0 || kNew != kGot) return list;
            foreach (var x in KRegion.nos)
            {
                if (x.depois == null || System.Array.IndexOf(x.depois, KNode.id) < 0) continue;
                bool other = false;
                foreach (var d in x.depois) if (d != KNode.id && Career.Stars(kSave, d) > 0) other = true;
                if (!other) list.Add(x.nome);
            }
            return list;
        }

        void CareerNext()
        {
            kSel = FirstOpen();
            CareerScreen(KScreen.Map);
            kCard = true;
            DrawMapUI();
        }

        // ---------- teclas ----------

        void CareerKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || IsTyping || keysOpen) return;
            bool esc = kb.escapeKey.wasPressedThisFrame, enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || AnyPadStart();
            switch (kScreen)
            {
                case KScreen.Map:
                    if (kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) CareerStep(1);
                    if (kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) CareerStep(-1);
                    if (enter) { if (kCard) CareerRun(); else { kCard = true; DrawMapUI(); } }
                    else if (esc) { if (kCard) { kCard = false; DrawMapUI(); } else CareerScreen(KScreen.Saves); }
                    break;
                case KScreen.Saves:
                    if (esc) CareerExit();
                    break;
                case KScreen.Race:
                    if (esc && sim.phase != RaceSim.Phase.Done) CareerPause(!kPause);
                    break;
                case KScreen.Result:
                    if (enter) { if (kInterim) NextHeat(); else CareerNext(); }
                    else if (esc) CareerScreen(KScreen.Map);
                    break;
            }
        }

        static string Stars(int n) => new string('★', n) + new string('☆', 3 - n);
        static string TipoNome(string t) => t == "relogio" ? "Contra o relógio" : t == "corrida" ? "Corrida" : t == "desafio" ? "Desafio" : t == "rival" ? "Rival" : t;

        // Na prova da carreira: o jogador é "VOCÊ" e o rival tem nome e cor (placar de cima, controles, etiquetas).
        bool CareerRacing => panel.CareerMode && kScreen != KScreen.Map && kScreen != KScreen.Saves;
        string CareerName(int k) => !CareerRacing ? null : k == 0 ? "VOCÊ" : k == 1 && IsRival ? KNode.rival.ToUpperInvariant() : null;
        Color? CareerColor(int k) => CareerRacing && k == 1 && IsRival && ColorUtility.TryParseHtmlString(KNode.rivalCor, out var c) ? c : (Color?)null;
    }
}
