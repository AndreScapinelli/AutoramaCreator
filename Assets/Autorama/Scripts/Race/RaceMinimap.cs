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
        const float Step = 0.25f;               // distância (em células, × Scale no mundo) entre os pontos do traçado
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
        readonly VisualElement head, canvas, dotLayer, grip, info;   // canvas: traçado (só redesenha quando muda); dotLayer: os pontos, a cada quadro
        readonly Label[] num = new Label[RaceSim.MaxCars];            // a posição no meio do ponto de cada carro
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
            headTitle = new Label(L.T("MINIMAPA")); headTitle.AddToClassList("minimap-title"); head.Add(headTitle);
            var hint = new Label(L.T("⠿ arraste")); hint.AddToClassList("minimap-hint"); head.Add(hint);
            head.RegisterCallback<PointerDownEvent>(e => StartDrag(e, head, false));
            head.RegisterCallback<PointerMoveEvent>(e => Move(e, false));
            head.RegisterCallback<PointerUpEvent>(e => EndDrag(e, head, false));

            canvas = new VisualElement(); canvas.AddToClassList("minimap-canvas"); canvas.pickingMode = PickingMode.Ignore;
            canvas.generateVisualContent += DrawTrack;
            Add(canvas);
            dotLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            dotLayer.style.position = Position.Absolute;
            dotLayer.style.left = 0; dotLayer.style.top = 0; dotLayer.style.right = 0; dotLayer.style.bottom = 0;
            dotLayer.generateVisualContent += DrawDots;
            canvas.Add(dotLayer);

            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                num[k] = new Label(); num[k].AddToClassList("minimap-num"); num[k].pickingMode = PickingMode.Ignore;
                canvas.Add(num[k]);
            }
            grip = new VisualElement(); grip.AddToClassList("minimap-grip"); canvas.Add(grip);
            grip.RegisterCallback<PointerDownEvent>(e => StartDrag(e, grip, true));
            grip.RegisterCallback<PointerMoveEvent>(e => Move(e, true));
            grip.RegisterCallback<PointerUpEvent>(e => EndDrag(e, grip, true));

            toggle = new Label(L.T("Pilotos ▸")); toggle.AddToClassList("minimap-toggle");
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
            PlaceNumbers(order);
            dotLayer.MarkDirtyRepaint();
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
                // A torre de posições ocupa o canto de cima à esquerda: o minimapa volta uma vez para a direita.
                if (set.miniV < 1) { set.miniV = 1; set.miniX = -1f; }
                if (set.miniX < 0f || set.miniY < 0f) { set.miniX = pw.x - w - Margin; set.miniY = TopGap; }
                ApplyPos();
            }
            if (pw != lastParent) { lastParent = pw; ApplyPos(); }
        }

        void SetWidth(float w)
        {
            style.width = w;
            canvas.style.width = w;
            // A altura acompanha a forma da pista (sem a faixa vazia de um mapa quadrado), entre 35% e 100% da largura.
            float a = box.width > 0f && box.height > 0f ? Mathf.Clamp(box.height / box.width, 0.35f, 1f) : 1f;
            canvas.style.height = Mathf.Max(80f, (w - Pad * 2f) * a + Pad * 2f);
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
            toggle.text = open ? L.T("Pilotos ▾") : L.T("Pilotos ▸");
            ApplyPos();
        }

        void UpdateInfo(string[] names, Color[] colors, IList<int> order, float cellCm)
        {
            bool open = InfoOpen;
            info.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            toggle.text = open ? L.T("Pilotos ▾") : L.T("Pilotos ▸");
            if (!open) return;
            int nV = sim.set.laps;
            for (int i = 0; i < RaceSim.MaxCars; i++)
            {
                if (i >= order.Count) { rows[i].style.display = DisplayStyle.None; continue; }
                int k = order[i];
                var c = sim.cars[k];
                rows[i].style.display = DisplayStyle.Flex;
                rowDot[i].style.backgroundColor = k < colors.Length ? colors[k] : Color.white;
                rowPos[i].text = sim.winner == k ? "🏁" : L.F("{0}º", i + 1);
                rowName[i].text = k < names.Length ? names[k] : "";
                rowLap[i].text = sim.ByTime ? L.F("{0} v · T{1}", c.laps, c.lane + 1) : c.finish != null ? L.T("Chegou") : L.F("Volta {0}/{1}", Mathf.Clamp(c.laps + 1, 1, nV), nV);
                float metros = sim.Travel[k] * cellCm / 100f;
                rowStats[i].text = L.F("última {0} · melhor {1} · {2:0.0} m", RaceController.Fmt(c.lastLap), RaceController.Fmt(c.best), metros);
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
                int n = Mathf.Max(2, Mathf.CeilToInt(P.Total / (Step * sim.Scale)));
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
            if (set != null) SetWidth(Mathf.Clamp(set.miniW, MinW, MaxW));
            canvas.MarkDirtyRepaint();
        }

        bool SameAs(LanePath[] other)
        {
            for (int i = 0; i < other.Length; i++) if (other[i] != builtFor[i]) return false;
            return true;
        }

        // Do mundo (x, z) para o quadro do mapa, em pixels. z do mundo cresce para cima na mesa; na tela, y cresce para baixo.
        Vector2 ToMap(Vector2 p)
        {
            var r = canvas.contentRect;
            float k = Mathf.Min(Mathf.Max(1f, r.width - Pad * 2f) / box.width, Mathf.Max(1f, r.height - Pad * 2f) / box.height);
            float ox = r.x + (r.width - box.width * k) / 2f, oy = r.y + (r.height - box.height * k) / 2f;
            return new Vector2(ox + (p.x - box.xMin) * k, oy + (box.yMax - p.y) * k);
        }

        // O número da posição em cima do ponto de cada carro (o Painter2D não escreve texto).
        void PlaceNumbers(IList<int> order)
        {
            bool ok = track.Count >= 2 && box.width > 0f && box.height > 0f;
            for (int c = 0; c < RaceSim.MaxCars; c++)
            {
                int pos = order != null ? order.IndexOf(c) : -1;
                bool show = ok && c < sim.N && dotOn[c] && pos >= 0;
                num[c].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                var p = ToMap(dots[c]);
                string t = (pos + 1).ToString();
                if (num[c].text != t) num[c].text = t;
                num[c].style.left = p.x - DotR;
                num[c].style.top = p.y - DotR;
                // Número escuro nas cores claras, claro nas escuras.
                var col = c < laneColors.Length ? laneColors[c] : Color.white;
                num[c].style.color = 0.2126f * col.r + 0.7152f * col.g + 0.0722f * col.b > 0.55f ? new Color(0.06f, 0.07f, 0.06f) : Color.white;
            }
        }

        const float DotR = 8f;

        // O traçado tem milhares de pontos (8 trilhos na pista de 8): tesselar a cada quadro travava a corrida.
        // Só redesenha quando a pista muda (EnsureTrack) ou a janela muda de tamanho.
        void DrawTrack(MeshGenerationContext ctx)
        {
            if (track.Count < 2 || box.width <= 0f || box.height <= 0f) return;
            Vector2 P(Vector2 p) => ToMap(p);

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

        }

        // Carrinhos: ponto com borda escura, na cor do painel do carrinho. dotLayer cobre o canvas inteiro, então o
        // contentRect dele é o mesmo e ToMap serve.
        void DrawDots(MeshGenerationContext ctx)
        {
            if (track.Count < 2 || box.width <= 0f || box.height <= 0f || sim == null) return;
            Vector2 P(Vector2 p) => ToMap(p);
            var g = ctx.painter2D;
            for (int c = 0; c < sim.N; c++)
            {
                if (!dotOn[c] || c >= laneColors.Length) continue;
                var p = P(dots[c]);
                g.BeginPath(); g.Arc(p, DotR + 1.5f, Angle.Degrees(0f), Angle.Degrees(360f)); g.fillColor = new Color(0.05f, 0.06f, 0.05f, 1f); g.Fill();
                g.BeginPath(); g.Arc(p, DotR, Angle.Degrees(0f), Angle.Degrees(360f)); g.fillColor = laneColors[c]; g.Fill();
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
