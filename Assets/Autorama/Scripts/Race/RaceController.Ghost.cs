using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Fantasma na corrida livre (seção 55 do guia): a melhor volta limpa de gente nesta pista, repetida ao lado de cada
    // piloto de gente, largando junto a cada volta dele. Só aparece; a física não sabe dele. Grava pelo RaceRecorder.
    public partial class RaceController
    {
        readonly RaceRecorder recorder = new RaceRecorder();
        readonly CarView[] ghostViews = new CarView[RaceSim.MaxCars];
        readonly List<(int k, float t0, float t1)> ghostLaps = new List<(int, float, float)>();
        GhostFile ghost;
        CarTape ghostTape;
        string ghostHash;
        bool ghostLoaded;
        VisualElement ghostInfo;

        bool GhostFree => panel != null && panel.RaceMode && !panel.ChampMode && !panel.CareerMode && !OnlineRun;

        void GhostStart()
        {
            sim.LapDone += GhostLap;
            var mat = GhostMaterial(builder.theme.support);
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                ghostViews[k] = CarView.Create("Fantasma " + (k + 1), builder.theme.support, Color.white, transform, builder.theme.carModels);
                ghostViews[k].MakeGhost(mat);
                ghostViews[k].gameObject.SetActive(false);
            }
        }

        static Material GhostMaterial(Material lit)
        {
            var c = new Color(0.75f, 0.9f, 1f, 0.35f);
            var m = new Material(lit) { color = c, name = "Fantasma" };
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        // Volta limpa (sem turbo, sem troca de trilho): o RaceSim só chama LapDone para essas. Guarda para depois do
        // passo, quando o gravador já tem a amostra do instante em que a volta fechou.
        void GhostLap(int k, float lap)
        {
            if (!GhostFree || sim.aiSkill[k] > 0) return;
            ghostLaps.Add((k, sim.t - lap, sim.t));
        }

        // O cartão do fantasma na aba Corrida: o botão e, embaixo, a volta e o carro do fantasma.
        void BuildGhostUI(VisualElement box)
        {
            var sec = Section(box, "FANTASMA");
            var tg = new Toggle("Correr contra o fantasma") { value = set.ghost };
            tg.AddToClassList("setting");
            sec.Add(tg);
            tg.RegisterValueChangedCallback(e => { set.ghost = e.newValue; set.Save(); DrawGhostInfo(); });
            ghostInfo = El(null, sec);
            DrawGhostInfo();
        }

        void DrawGhostInfo()
        {
            if (ghostInfo == null) return;
            ghostInfo.Clear();
            if (!set.ghost) { Text("A melhor volta da pista corre ao seu lado, transparente, largando junto a cada volta.", "small", ghostInfo); return; }
            if (ghost == null) { Text("Esta pista ainda não tem fantasma. A primeira volta limpa de um piloto de gente vira o fantasma.", "small", ghostInfo); return; }
            var day = System.DateTimeOffset.FromUnixTimeMilliseconds(ghost.date).LocalDateTime;
            Text($"Melhor volta: <b>{Fmt(ghost.lap)}</b> · {LaneNames[Mathf.Clamp(ghost.lane, 0, LaneNames.Length - 1)]} · {day:dd/MM/yyyy}", "small", ghostInfo);
            if (!Mathf.Approximately(ghost.vmax, set.vmax) || !Mathf.Approximately(ghost.grip, set.grip) || !Mathf.Approximately(ghost.ramp, set.ramp))
                Text($"Gravado com velocidade máx. {Num(ghost.vmax)}, aderência {Mathf.RoundToInt(ghost.grip)} e tempo até 100% {Num(ghost.ramp, 2)} s (agora {Num(set.vmax)}, {Mathf.RoundToInt(set.grip)} e {Num(set.ramp, 2)} s).", "small", ghostInfo);
            Text("O carro do fantasma:", "small", ghostInfo);
            DrawCarCard(ghostInfo, ghost.carro, _ => { }, false);
        }

        // Fim do PlaceCars: grava, troca o fantasma se a volta foi melhor e põe os fantasmas no lugar.
        void GhostTick()
        {
            bool free = GhostFree;
            if (free)
            {
                string h = FreeTrackHash();
                if (h != ghostHash || !ghostLoaded)
                {
                    ghostHash = h; ghostLoaded = true;
                    ghost = GhostStore.Load(h);
                    ghostTape = ghost != null ? CarTape.FromBase64(ghost.tape) : null;
                    ghostLaps.Clear();
                    DrawGhostInfo();
                }
                recorder.Sample(sim, ghostLaps.Count > 0);
                foreach (var (k, t0, t1) in ghostLaps)
                {
                    float lap = t1 - t0;
                    if (ghostHash == null || (ghost != null && lap >= ghost.lap)) continue;
                    var tape = recorder.tapes[k].Slice(t0, t1);
                    if (tape.frames.Count < 2) continue;
                    ghost = new GhostFile
                    {
                        lap = lap, date = Champ.Now, lane = k, carro = sim.setup[k],
                        vmax = set.vmax, grip = set.grip, ramp = set.ramp, tape = tape.ToBase64(),
                    };
                    ghostTape = tape;
                    GhostStore.Save(ghostHash, ghost);
                    DrawGhostInfo();
                    if (set.ghost) { var pad = pads[k]; pad.flash = $"Novo fantasma! {Fmt(lap)}"; pad.flashRec = false; pad.flashT = 2.5f; }
                }
            }
            ghostLaps.Clear();

            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var gv = ghostViews[k];
                if (gv == null) continue;
                bool on = free && set.ghost && ghostTape != null && k < sim.N && sim.present[k] && sim.aiSkill[k] == 0
                          && sim.phase == RaceSim.Phase.Running && sim.cars[k].finish == null;
                float gt = on ? sim.t - sim.cars[k].lapStart : 0f;
                on &= gt <= ghostTape?.Length;
                if (gv.gameObject.activeSelf != on) gv.gameObject.SetActive(on);
                if (!on) continue;
                var f = ghostTape.At(gt);
                gv.transform.position = new Vector3(f.x, f.y, f.z);
                gv.transform.rotation = Quaternion.Euler(0f, -f.heading, 0f) * Quaternion.Euler(0f, 0f, f.pitch) * Quaternion.Euler(f.bank, 0f, 0f);
                gv.SetBody((CarBody)ghost.carro.bolha);
                gv.SetChassi(ghost.carro.chassi);
                gv.Tick(f.off ? 0f : f.v, f.off, Paused ? 0f : Time.deltaTime);
            }
        }
    }
}
