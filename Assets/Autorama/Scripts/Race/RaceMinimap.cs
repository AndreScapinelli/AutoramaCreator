using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Autorama
{
    // Minimapa da corrida: uma janela solta sobre a mesa. Tem o traçado visto de cima (Painter2D), um ponto por
    // carrinho, e uma aba "Pilotos" que abre e fecha com as infos de cada um. Arrasta-se pelo cabeçalho e
    // redimensiona-se pelo canto. A posição, o tamanho e a aba aberta ficam salvos em RaceSettings.
    public class RaceMinimap : VisualElement
    {
        const float Step = 0.25f;               // distância (em unidades do mundo) entre os pontos do traçado
        const float Pad = 10f;                  // margem interna do traçado, em pixels
        const float MinW = 220f, MaxW = 460f;   // largura da janela (a altura do mapa é igual)
        const float Margin = 12f, TopGap = 56f; // onde ela nasce: canto superior direito, abaixo do botão da câmera

        readonly List<Vector2> track = new List<Vector2>();   // traçado em coordenadas do mundo (x, z)
        readonly List<int> trackLane = new List<int>();        // trilho de cada ponto; -1 separa os trilhos
        Rect box;                                              // caixa do traçado no mundo
        LanePath[] builtFor;                                   // caminhos usados para montar o traçado
        RaceSim sim;
        readonly Vector2[] dots = new Vector2[RaceSim.MaxCars];
        readonly bool[] dotOn = new bool[RaceSim.MaxCars];
        Color[] laneColors = new Color[RaceSim.MaxCars];
        Vector2 finishPos; bool hasFinish;

        // Partes da janela.
        readonly VisualElement head, canvas, grip, info;
        readonly Label toggle, headTitle;
        readonly VisualElement[] rows = new VisualElement[RaceSim.MaxCars];
        readonly VisualElement[] rowDot = new VisualElement[RaceSim.MaxCars];
        readonly Label[] rowPos = new Label[RaceSim.MaxCars], rowName = new Label[RaceSim.MaxCars], rowLap = new Label[RaceSim.MaxCars], rowStats = new Label[RaceSim.MaxCars];

        RaceSettings set;                 // onde a posição, o tamanho e a aba ficam salvos
        Vector2 lastParent = Vector2.zero;
        bool placed;

        public RaceMinimap()
        {
            AddToClassList("minimap");
            style.position = Position.Absolute;

            head = new VisualElement(); head.AddToClassList("minimap-head"); Add(head);
            headTitle = new Label("MINIMAPA"); headTitle.AddToClassList("minimap-title"); head.Add(headTitle);
            var hint = new Label("⠿ arraste"); hint.AddToClassList("minimap-hint"); head.Add(hint);
            head.RegisterCallback<PointerDownEvent>(e => StartDrag(e, head, false));
            head.RegisterCallback<PointerMoveEvent>(e => Move(e, false));
            head.RegisterCallback<PointerUpEvent>(e => EndDrag(e, head, false));

            canvas = new VisualElement(); canvas.AddToClassList("minimap-canvas"); canvas.pickingMode = PickingMode.Ignore;
            canvas.generateVisualContent += Draw;
            Add(canvas);

            grip = new VisualElement(); grip.AddToClassList("minimap-grip"); canvas.Add(grip);
            grip.RegisterCallback<PointerDownEvent>(e => StartDrag(e, grip, true));
            grip.RegisterCallback<PointerMoveEvent>(e => Move(e, true));
            grip.RegisterCallback<PointerUpEvent>(e => EndDrag(e, grip, true));

            toggle = new Label("Pilotos ▸"); toggle.AddToClassList("minimap-toggle");
            toggle.RegisterCallback<ClickEvent>(_ => SetInfoOpen(!InfoOpen));
            Add(toggle);

            info = new VisualElement(); info.AddToClassList("minimap-info"); Add(info);
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var r = new VisualElement(); r.AddToClassList("minimap-row"); info.Add(r);
                rows[k] = r;
                var top = new VisualElement(); top.AddToClassList("minimap-row-top"); r.Add(top);
                rowDot[k] = new VisualElement(); rowDot[k].AddToClassList("minimap-dot"); top.Add(rowDot[k]);
                rowPos[k] = new Label(); rowPos[k].AddToClassList("minimap-pos"); top.Add(rowPos[k]);
                rowName[k] = new Label(); rowName[k].AddToClassList("minimap-name"); top.Add(rowName[k]);
                rowLap[k] = new Label(); rowLap[k].AddToClassList("minimap-lap"); top.Add(rowLap[k]);
                rowStats[k] = new Label(); rowStats[k].AddToClassList("minimap-stats"); r.Add(rowStats[k]);
            }
        }

        bool InfoOpen => set != null && set.miniOpen;

        // Mostra a janela (com a aba do jeito salvo) e posiciona-a na primeira vez. Chamado a cada quadro.
        public void Refresh(RaceSim s, RaceSettings settings, string[] names, Color[] colors, IList<int> order, float cellCm)
        {
            sim = s; set = settings; laneColors = colors;
            EnsureTrack();
            Place();
            for (int k = 0; k < sim.N; k++)
            {
                bool on = sim.present[k] && sim.paths[sim.cars[k].lane] != null;
                dotOn[k] = on;
                if (on) dots[k] = sim.CarPose(k, out _, out _).pos;
            }
            UpdateInfo(names, colors, order, cellCm);
            MarkDirtyRepaint();
            canvas.MarkDirtyRepaint();
        }

        // ---------- Posição e tamanho ----------

        // Primeira vez: canto superior direito do pai. Depois: a posição salva, sempre dentro da tela.
        void Place()
        {
            if (parent == null || set == null) return;
            Vector2 pw = new Vector2(parent.resolvedStyle.width, parent.resolvedStyle.height);
            if (pw.x <= 0f) return;
            if (!placed)
            {
                placed = true;
                float w = Mathf.Clamp(set.miniW, MinW, MaxW);
                set.miniW = w;
                SetWidth(w);
                if (set.miniX < 0f || set.miniY < 0f) { set.miniX = pw.x - w - Margin; set.miniY = TopGap; }
                ApplyPos();
            }
            if (pw != lastParent) { lastParent = pw; ApplyPos(); }
        }

        void SetWidth(float w)
        {
            style.width = w;
            canvas.style.width = w;
            canvas.style.height = w;
        }

        // Mantém a janela dentro do pai (o cabeçalho sempre visível).
        void ApplyPos()
        {
            if (set == null || parent == null) return;
            float pw = parent.resolvedStyle.width, ph = parent.resolvedStyle.height;
            // Na base fica sempre o cabeçalho (24 px), para a janela nunca sumir da tela.
            float x = Mathf.Clamp(set.miniX, 0f, Mathf.Max(0f, pw - set.miniW));
            float y = Mathf.Clamp(set.miniY, 0f, Mathf.Max(0f, ph - 24f));
            set.miniX = x; set.miniY = y;
            style.left = x; style.top = y;
        }

        // ---------- Arrastar e redimensionar ----------
        bool dragging, resizing;
        Vector2 dragStart; float startX, startY, startW;

        void StartDrag(PointerDownEvent e, VisualElement who, bool resize)
        {
            if (e.button != 0) return;
            dragging = !resize; resizing = resize;
            dragStart = e.position;
            startX = set.miniX; startY = set.miniY; startW = set.miniW;
            who.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void Move(PointerMoveEvent e, bool resize)
        {
            if (!(dragging || resizing) || set == null) return;
            Vector2 d = (Vector2)e.position - dragStart;
            if (resize)
            {
                set.miniW = Mathf.Clamp(startW + d.x, MinW, MaxW);
                SetWidth(set.miniW);
            }
            else
            {
                set.miniX = startX + d.x; set.miniY = startY + d.y;
            }
            ApplyPos();
            e.StopPropagation();
        }

        void EndDrag(PointerUpEvent e, VisualElement who, bool resize)
        {
            if (!(dragging || resizing)) return;
            dragging = resizing = false;
            who.ReleasePointer(e.pointerId);
            ApplyPos();
            set?.Save();
            e.StopPropagation();
        }

        // ---------- Aba de pilotos ----------

        void SetInfoOpen(bool open)
        {
            if (set == null) return;
            set.miniOpen = open;
            set.Save();
            info.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            toggle.text = open ? "Pilotos ▾" : "Pilotos ▸";
            ApplyPos();
        }

        void UpdateInfo(string[] names, Color[] colors, IList<int> order, float cellCm)
        {
            bool open = InfoOpen;
            info.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            toggle.text = open ? "Pilotos ▾" : "Pilotos ▸";
            if (!open) return;
            int L = sim.set.laps;
            for (int i = 0; i < RaceSim.MaxCars; i++)
            {
                if (i >= order.Count) { rows[i].style.display = DisplayStyle.None; continue; }
                int k = order[i];
                var c = sim.cars[k];
                rows[i].style.display = DisplayStyle.Flex;
                rowDot[i].style.backgroundColor = k < colors.Length ? colors[k] : Color.white;
                rowPos[i].text = sim.winner == k ? "🏁" : $"{i + 1}º";
                rowName[i].text = k < names.Length ? names[k] : "";
                rowLap[i].text = sim.ByTime ? $"{c.laps} v · T{c.lane + 1}" : c.finish != null ? "Chegou" : $"Volta {Mathf.Clamp(c.laps + 1, 1, L)}/{L}";
                float metros = sim.Travel[k] * cellCm / 100f;
                rowStats[i].text = $"última {RaceController.Fmt(c.lastLap)} · melhor {RaceController.Fmt(c.best)} · {metros:0.0} m";
            }
        }

        // ---------- Traçado ----------

        // Monta o traçado de novo só quando a pista mudou (SetTrack cria caminhos novos).
        void EnsureTrack()
        {
            if (sim == null || !sim.Ready) { track.Clear(); trackLane.Clear(); builtFor = null; return; }
            if (builtFor != null && SameAs(sim.paths)) return;
            builtFor = (LanePath[])sim.paths.Clone();
            track.Clear(); trackLane.Clear();
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int k = 0; k < sim.N; k++)
            {
                var P = sim.paths[k];
                if (P == null || P.Total <= 0f) continue;
                int n = Mathf.Max(2, Mathf.CeilToInt(P.Total / Step));
                for (int i = 0; i <= n; i++)
                {
                    var p = P.At(P.Total * i / n).pos;
                    track.Add(p); trackLane.Add(k);
                    x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
                }
                track.Add(Vector2.zero); trackLane.Add(-1);
            }
            if (track.Count == 0) return;
            box = Rect.MinMaxRect(x0 - 0.5f, y0 - 0.5f, x1 + 0.5f, y1 + 0.5f);
            hasFinish = sim.paths[0] != null && sim.paths[0].marks.Count > 0;
            if (hasFinish) finishPos = sim.paths[0].At(sim.paths[0].marks[0]).pos;
        }

        bool SameAs(LanePath[] other)
        {
            for (int i = 0; i < other.Length; i++) if (other[i] != builtFor[i]) return false;
            return true;
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (track.Count < 2 || box.width <= 0f || box.height <= 0f) return;
            var r = canvas.contentRect;
            float inner = Mathf.Max(1f, Mathf.Min(r.width, r.height) - Pad * 2f);
            float k = inner / Mathf.Max(box.width, box.height);
            float ox = r.x + (r.width - box.width * k) / 2f, oy = r.y + (r.height - box.height * k) / 2f;
            // z do mundo cresce para cima na mesa; na tela, y cresce para baixo.
            Vector2 P(Vector2 p) => new Vector2(ox + (p.x - box.xMin) * k, oy + (box.yMax - p.y) * k);

            var g = ctx.painter2D;
            g.lineJoin = LineJoin.Round;
            g.lineCap = LineCap.Round;

            // Asfalto: uma faixa grossa e escura por trilho.
            g.lineWidth = 6f;
            g.strokeColor = new Color(0.27f, 0.31f, 0.29f, 1f);
            for (int lane = 0; lane < sim.N; lane++) StrokeLane(g, P, lane);

            // Linha clara no centro de cada trilho.
            g.lineWidth = 1.5f;
            g.strokeColor = new Color(0.72f, 0.76f, 0.73f, 0.7f);
            for (int lane = 0; lane < sim.N; lane++) StrokeLane(g, P, lane);

            // Chegada: tracinho branco na bandeirada.
            if (hasFinish)
            {
                var f = P(finishPos);
                g.lineWidth = 2.5f;
                g.strokeColor = Color.white;
                g.BeginPath(); g.MoveTo(f + new Vector2(-4f, -4f)); g.LineTo(f + new Vector2(4f, 4f)); g.Stroke();
            }

            // Carrinhos: ponto com borda escura, na cor do painel do carrinho.
            for (int c = 0; c < sim.N; c++)
            {
                if (!dotOn[c] || c >= laneColors.Length) continue;
                var p = P(dots[c]);
                g.BeginPath(); g.Arc(p, 6f, Angle.Degrees(0f), Angle.Degrees(360f)); g.fillColor = new Color(0.05f, 0.06f, 0.05f, 1f); g.Fill();
                g.BeginPath(); g.Arc(p, 4.5f, Angle.Degrees(0f), Angle.Degrees(360f)); g.fillColor = laneColors[c]; g.Fill();
            }
        }

        void StrokeLane(Painter2D g, System.Func<Vector2, Vector2> P, int lane)
        {
            g.BeginPath();
            bool open = false;
            for (int i = 0; i < track.Count; i++)
            {
                if (trackLane[i] != lane) { if (open) g.Stroke(); open = false; continue; }
                var p = P(track[i]);
                if (!open) { g.BeginPath(); g.MoveTo(p); open = true; } else g.LineTo(p);
            }
            if (open) g.Stroke();
        }
    }
}
