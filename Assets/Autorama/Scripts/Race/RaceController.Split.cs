using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Tela dividida (corrida offline com 2 a 4 humanos): uma câmera por jogador, cada uma no seu quadro, e o painel de
    // cada jogador no pé do quadro dele. 2 humanos: um em cima, outro embaixo. 3 ou 4: grade 2×2 (com 3, o 4º quadro é a
    // Transmissão de TV). A câmera principal é o quadro 0; as outras são cópias dela (splitCams).
    public partial class RaceController
    {
        static readonly Rect[] Split2 = { new Rect(0f, 0.5f, 1f, 0.5f), new Rect(0f, 0f, 1f, 0.5f) };
        static readonly Rect[] Split4 = { new Rect(0f, 0.5f, 0.5f, 0.5f), new Rect(0.5f, 0.5f, 0.5f, 0.5f), new Rect(0f, 0f, 0.5f, 0.5f), new Rect(0.5f, 0f, 0.5f, 0.5f) };
        static readonly Rect FullSafe = Rect.MinMaxRect(0.06f, 0.24f, 0.94f, 0.90f);
        const float TopHudPx = 60f;          // placar em cima da tela
        const float PadWidth2 = 520f, PadWidth4 = 420f;   // largura do painel do jogador no quadro (px do painel UI)

        readonly List<TopDownCamera> splitCams = new List<TopDownCamera>();   // quadros 1 a 3
        readonly int[] splitCar = new int[4];                                  // carrinho de cada quadro (-1: TV)
        readonly bool[][] splitMask = { new bool[RaceSim.MaxCars], new bool[RaceSim.MaxCars], new bool[RaceSim.MaxCars], new bool[RaceSim.MaxCars] };
        int splitPanes, splitHumans;          // quadros na tela (0: sem divisão) e quantos são de jogador

        bool SplitOn => splitPanes > 0;

        TopDownCamera PaneCam(int i) => i == 0 ? cam : splitCams[i - 1];

        // Todas as câmeras da corrida (a principal e as cópias ativas) recebem o estado dos carrinhos.
        int CamCount => 1 + (SplitOn ? splitPanes - 1 : 0);

        void SplitStart() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        void SplitDestroy() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        // A cabine some só no desenho da câmera que está dentro daquele carrinho.
        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (sim == null) return;
            for (int i = 0; i < CamCount; i++)
            {
                var tdc = PaneCam(i);
                if (tdc.Cam != c) continue;
                for (int k = 0; k < sim.N; k++) carViews[k].SetCockpit(tdc.InFirstPersonOf(k));
                return;
            }
        }

        // Chamado a cada quadro em PlaceCars, depois de a câmera principal receber os alvos.
        void UpdateSplit(int leader)
        {
            int n = 0;
            if (set.splitScreen && !OnlineRun && panel != null && panel.RaceMode)
                for (int k = 0; k < sim.N && n <= 4; k++)
                    if (sim.present[k] && sim.aiSkill[k] == 0) { if (n < 4) splitCar[n] = k; n++; }
            if (n < 2 || n > 4) n = 0;
            int panes = n == 0 ? 0 : n == 2 ? 2 : 4;
            if (panes != splitPanes || n != splitHumans) LayoutSplit(n, panes);
            if (!SplitOn) return;

            for (int i = 0; i < splitPanes; i++)
            {
                var c = PaneCam(i);
                bool tv = i >= splitHumans;
                var want = tv ? TopDownCamera.RaceViewMode.Tv : PaneModes[PaneModeIndex(i)];
                if (c.RaceMode != want) c.SetRaceViewMode(want);
                if (tv) { c.SetRaceTargets(carPos, carHeading, sim.N, leader); continue; }
                int car = splitCar[i];
                c.SetThirdPersonCar(car);
                c.SetRaceTargets(carPos, carHeading, sim.N, car);
                // Os quadros da esquerda começam depois do painel lateral (que abre e fecha com Tab).
                pads[car].root.style.marginLeft = (c.Cam.rect.xMin <= 0f ? panel.SideWidth : 0f) + 12f;
                var mask = splitMask[i];
                for (int k = 0; k < mask.Length; k++) mask[k] = k == car;
                c.SetFollowCars(mask);
                // Faixa livre do quadro: o painel do jogador embaixo; o placar em cima, só nos quadros de cima.
                Rect r = c.Cam.pixelRect;
                float padH = pads[car].root.resolvedStyle.height;
                float y0 = Mathf.Clamp((float.IsNaN(padH) ? 110f : padH) + 24f, 0f, r.height * 0.5f) / r.height;
                float y1 = r.yMax >= Screen.height - 1 ? 1f - TopHudPx / r.height : 0.95f;
                c.SetSafeArea(Rect.MinMaxRect(0.04f, y0, 0.96f, y1));
            }
        }

        // Cada quadro tem a sua câmera (set.paneCam[i], valor de RaceViewMode), trocada no botão do canto do quadro.
        // A TV fica de fora: ela não é de um jogador só.
        static readonly TopDownCamera.RaceViewMode[] PaneModes = { TopDownCamera.RaceViewMode.ThirdPerson, TopDownCamera.RaceViewMode.FirstPerson, TopDownCamera.RaceViewMode.TopFollow };
        static readonly string[] PaneModeNames = { "Perseguição", "Primeira pessoa", "De cima" };
        readonly Button[] paneBtns = new Button[4];

        int PaneModeIndex(int i)
        {
            if (set.paneCam == null || set.paneCam.Length < 4) set.paneCam = new[] { 2, 2, 2, 2 };
            return Mathf.Max(0, System.Array.IndexOf(PaneModes, (TopDownCamera.RaceViewMode)set.paneCam[i]));
        }

        void CyclePaneCam(int i)
        {
            set.paneCam[i] = (int)PaneModes[(PaneModeIndex(i) + 1) % PaneModes.Length];
            set.Save();
            DrawPaneBtn(i);
        }

        void DrawPaneBtn(int i) => paneBtns[i].text = $"Câmera: <b>{PaneModeNames[PaneModeIndex(i)]}</b>  ⟳";

        void LayoutSplit(int humans, int panes)
        {
            splitHumans = humans;
            splitPanes = panes;
            while (splitCams.Count < 3)
            {
                var go = Instantiate(cam.gameObject, cam.transform.parent);
                go.name = $"Câmera {splitCams.Count + 2}";
                go.tag = "Untagged";
                var listener = go.GetComponent<AudioListener>();
                if (listener) Destroy(listener);
                go.SetActive(false);
                splitCams.Add(go.GetComponent<TopDownCamera>());
            }
            var rects = panes == 2 ? Split2 : Split4;
            // Botão de câmera de cada jogador, no canto de cima à direita do quadro (abaixo do menu geral nos de cima).
            for (int i = 0; i < 4; i++)
            {
                if (paneBtns[i] == null) { int p = i; paneBtns[i] = Btn("", () => CyclePaneCam(p), "btn cam-btn pane-cam pad--" + LaneCls[0], camMenu.parent); }
                var b = paneBtns[i];
                bool on = SplitOn && i < humans;
                b.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                Rect r = rects[i];
                b.style.right = Length.Percent((1f - r.xMax) * 100f);
                b.style.top = Length.Percent((1f - r.yMax) * 100f);
                b.style.marginTop = r.yMax >= 1f ? 54f : 10f;
                for (int c = 0; c < LaneCls.Length; c++) b.EnableInClassList("pad--" + LaneCls[c], LaneCls[c] == LaneCls[splitCar[i]]);
                DrawPaneBtn(i);
            }
            cam.Cam.rect = SplitOn ? rects[0] : new Rect(0f, 0f, 1f, 1f);
            for (int i = 1; i < 4; i++)
            {
                var c = splitCams[i - 1];
                bool on = i < panes;
                if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
                if (!on) continue;
                c.Cam.rect = rects[i];
                if (framedPath != null) c.SetTrackFrame(trackFrameRect, trackFrameTop, sim.Scale);
            }
            if (!SplitOn) cam.SetSafeArea(FullSafe);
            ApplyCameraSettings();

            // Painéis: no pé do quadro de cada jogador; os dos robôs somem. Sem divisão, volta ao layout da linha de baixo.
            padsBox.EnableInClassList("pads--split", SplitOn);
            // A caixa cobre a tela toda: não pode pegar os cliques (minimapa, menu de câmera); só os painéis pegam.
            padsBox.pickingMode = SplitOn ? PickingMode.Ignore : PickingMode.Position;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var s = pads[k].root.style;
                int pane = SplitOn ? System.Array.IndexOf(splitCar, k, 0, humans) : -1;
                if (pane < 0)
                {
                    s.position = StyleKeyword.Null; s.left = s.right = s.bottom = s.width = s.marginLeft = StyleKeyword.Null;
                    if (SplitOn) s.display = DisplayStyle.None;
                    else s.display = k < sim.N && sim.present[k] ? DisplayStyle.Flex : DisplayStyle.None;
                    continue;
                }
                Rect r = rects[pane];
                s.display = DisplayStyle.Flex;
                s.position = Position.Absolute;
                // Largura de painel normal, no canto de baixo à esquerda do quadro (não estica pelo quadro todo).
                s.left = Length.Percent(r.xMin * 100f);
                s.right = StyleKeyword.Null;
                s.width = panes == 2 ? PadWidth2 : PadWidth4;
                s.bottom = Length.Percent(r.yMin * 100f);
            }
        }

        // LayoutForCars refaz os painéis: a divisão é refeita no próximo quadro.
        void SplitDirty() => splitPanes = -1;
    }
}
