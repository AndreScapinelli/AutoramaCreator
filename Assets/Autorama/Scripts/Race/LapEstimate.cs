using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Duração estimada da pista no construtor (seção 91 do guia): um piloto IA profissional, sozinho no trilho 1,
    // sem turbo nem batida, simulado sem cena com os ajustes da corrida livre. Semente fixa: o mesmo número sempre.
    public static class LapEstimate
    {
        public const int Skill = 100;
        const float Dt = 1f / 60f, MaxSecs = 120f;
        const int Seed = 1234;

        // Fechada: first = 1ª volta (do grid), lap = 2ª volta (lançada). Aberta: as duas = do grid até a ponta.
        public static bool Run(List<Segment> segs, bool closed, int faixas, RaceSettings b, out float first, out float lap)
        {
            first = lap = 0f;
            if (segs == null || segs.Count < 2) return false;
            var set = new RaceSettings { laps = 2, vmax = b.vmax, ramp = b.ramp, grip = b.grip, collide = false, turbo = false, halfGas = b.halfGas, reslotTime = b.reslotTime };
            var sim = new RaceSim(set);
            sim.SetTrack(segs, closed, faixas);
            if (!sim.Ready) return false;
            for (int k = 0; k < RaceSim.MaxCars; k++) { sim.present[k] = k == 0; sim.aiSkill[k] = k == 0 ? Skill : 0; }
            int steps = Mathf.RoundToInt(MaxSecs / Dt);
            var keep = Random.state;
            Random.InitState(Seed);
            try
            {
                if (closed)
                {
                    var laps = new List<float>(2);
                    sim.LapDone += (k, t) => { if (k == 0) laps.Add(t); };
                    sim.StartRace();
                    for (int i = 0; i < steps && laps.Count < 2; i++) sim.Step(Dt);
                    if (laps.Count < 2) return false;
                    first = laps[0]; lap = laps[1];
                }
                else
                {
                    // No teste, quem chega na ponta da pista aberta volta ao grid num CarState novo.
                    sim.test = true;
                    sim.StartTest();
                    var c0 = sim.cars[0];
                    int i = 0;
                    for (; i < steps && sim.cars[0] == c0; i++) sim.Step(Dt);
                    if (sim.cars[0] == c0) return false;
                    first = lap = i * Dt;
                }
            }
            finally { Random.state = keep; }
            return true;
        }
    }
}
