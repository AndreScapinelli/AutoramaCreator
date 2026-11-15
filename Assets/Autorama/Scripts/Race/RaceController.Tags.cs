using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Posição em cima de cada carrinho (seção 54 do guia): "1º", "2º"… na cor do painel do carro, e o líder com a
    // setinha. F1 passa por: só a posição → posição e nome → posição, nome e tempo para o da frente → nada
    // (RaceSettings.carTagMode, salvo). Uma etiqueta por carro em cada quadro da tela dividida, posicionada depois que
    // as câmeras se movem (TopDownCamera.Moved).
    public partial class RaceController
    {
        VisualElement tagRoot;
        readonly Label[,] tags = new Label[4, RaceSim.MaxCars];       // [quadro, carro]
        const float TagUp = 0.25f;                                     // altura acima do carrinho, em células × Scale
        const float TagSample = 0.05f;                                 // de quanto em quanto tempo guarda a distância de cada carro
        static readonly string[] TagModeNames = { "desligada", "só a posição", "posição e nome", "posição, nome e tempo" };
        // A distância (Dist) de cada carro ao longo da prova, para o tempo até o da frente: quanto tempo faz que ele
        // passou por onde eu estou agora.
        readonly List<float>[] tagT = new List<float>[RaceSim.MaxCars], tagD = new List<float>[RaceSim.MaxCars];
        float tagLastT = -1f;

        void BuildTags()
        {
            tagRoot = El("car-tags", panel.Root);
            tagRoot.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 4; i++)
                for (int k = 0; k < RaceSim.MaxCars; k++)
                {
                    var l = Text("", "car-tag", tagRoot);
                    l.pickingMode = PickingMode.Ignore;
                    l.style.display = DisplayStyle.None;
                    tags[i, k] = l;
                }
            TopDownCamera.Moved += PlaceTags;
        }

        void TagsDestroy() => TopDownCamera.Moved -= PlaceTags;

        void TagsKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || IsTyping || keysOpen || !kb.f1Key.wasPressedThisFrame || panel == null || !panel.RaceMode) return;
            set.carTagMode = (Mathf.Clamp(set.carTagMode, 0, 3) + 1) % 4; set.Save();
            panel.Toast($"Etiqueta nos carros: {TagModeNames[set.carTagMode]} (F1)");
        }

        void PlaceTags()
        {
            if (tagRoot == null || tagRoot.panel == null) return;
            int mode = Mathf.Clamp(set.carTagMode, 0, 3);
            if (mode == 3 && sim != null && sim.Ready) TagRecord();
            bool on = mode > 0 && panel.RaceMode && sim != null && sim.Ready && !keysOpen;
            var ord = on ? Order() : null;
            for (int i = 0; i < 4; i++)
            {
                var c = on && i < CamCount ? PaneCam(i) : null;
                Rect px = c != null ? c.Cam.pixelRect : default;
                for (int k = 0; k < RaceSim.MaxCars; k++)
                {
                    var l = tags[i, k];
                    int pos = ord != null ? ord.IndexOf(k) : -1;
                    bool show = c != null && k < sim.N && sim.present[k] && pos >= 0 && sim.cars[k].mode == CarMode.OnSlot
                                && !(c.RaceMode == TopDownCamera.RaceViewMode.FirstPerson && c.ThirdPersonCar == k);
                    Vector3 s = show ? c.Cam.WorldToScreenPoint(carPos[k] + Vector3.up * TagUp * sim.Scale) : default;
                    show &= s.z > 0f && px.Contains(new Vector2(s.x, s.y));
                    if (!show) { if (l.style.display != DisplayStyle.None) l.style.display = DisplayStyle.None; continue; }
                    var p = RuntimePanelUtils.ScreenToPanel(tagRoot.panel, new Vector2(s.x, Screen.height - s.y));
                    string txt = TagText(k, pos, ord, mode);
                    if (l.text != txt) l.text = txt;
                    l.style.display = DisplayStyle.Flex;
                    l.style.left = p.x;
                    l.style.top = p.y;
                    l.style.color = pads[k].root.resolvedStyle.borderTopColor;
                    l.EnableInClassList("car-tag--lider", pos == 0);
                }
            }
        }

        // "2º" · "2º Ana" · "2º Ana" e, embaixo, "+0,8 s" para o da frente. O líder leva a setinha.
        string TagText(int k, int pos, List<int> ord, int mode)
        {
            string t = $"{pos + 1}º";
            if (mode >= 2) t += $" <size=75%>{hud[k].name.text}</size>";
            if (mode >= 3 && pos > 0 && sim.cars[k].finish == null && sim.phase == RaceSim.Phase.Running)
            {
                float g = TagGap(ord[pos - 1], k);
                if (g >= 0f) t += $"\n<size=70%>+{Num(g, 1)} s</size>";
            }
            if (pos == 0) t += "\n▼";
            return t;
        }

        // Guarda a distância de cada carro a cada TagSample segundos da prova; recomeça quando o relógio volta.
        void TagRecord()
        {
            if (sim.t < tagLastT) { tagLastT = -1f; foreach (var l in tagT) l?.Clear(); foreach (var l in tagD) l?.Clear(); }
            if (sim.phase != RaceSim.Phase.Running || sim.t - tagLastT < TagSample) return;
            tagLastT = sim.t;
            for (int k = 0; k < sim.N; k++)
            {
                (tagT[k] ??= new List<float>()).Add(sim.t);
                (tagD[k] ??= new List<float>()).Add(sim.Dist(k));
            }
        }

        // Quanto tempo faz que o carro "front" passou pela distância em que o carro k está agora. −1: não dá para saber.
        float TagGap(int front, int k)
        {
            var T = tagT[front]; var D = tagD[front];
            if (T == null || T.Count == 0) return -1f;
            float d = sim.Dist(k);
            int lo = 0, hi = D.Count - 1;
            if (D[hi] < d) return 0f;
            if (D[0] >= d) return -1f;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (D[m] >= d) hi = m; else lo = m; }
            float f = D[hi] > D[lo] ? (d - D[lo]) / (D[hi] - D[lo]) : 1f;
            return Mathf.Max(0f, sim.t - Mathf.Lerp(T[lo], T[hi], f));
        }
    }
}
