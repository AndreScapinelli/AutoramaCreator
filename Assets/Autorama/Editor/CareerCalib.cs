using System.Text;
using UnityEngine;

namespace Autorama
{
    // Melhor volta da IA sozinha numa pista da carreira, por qualidade (seção 68 do guia): base dos tempos do relógio.
    // script eval 'return Autorama.CareerCalib.Laps("garagem-2", 50, 75, 100);'
    public static class CareerCalib
    {
        public static float Lap(TrackSave t, int skill, int laps = 4)
        {
            var segs = TrackLayout.Build(t.Origin, t.Pieces(), t.Tail, t.StartZ);
            var sim = new RaceSim(new RaceSettings { laps = laps, collide = false, turbo = false });
            sim.SetTrack(segs, true, t.Faixas);
            for (int k = 0; k < sim.N; k++) { sim.present[k] = k == 0; sim.aiSkill[k] = k == 0 ? skill : 0; }
            sim.StartRace();
            for (int i = 0; i < 60 * 600 && sim.phase != RaceSim.Phase.Done; i++) { sim.cars[0].gas = 1f; sim.Step(1f / 60f); }
            return sim.cars[0].best ?? -1f;
        }

        public static string Laps(string pista, params int[] skills)
        {
            var t = Career.Track(pista);
            var sb = new StringBuilder(pista);
            foreach (int s in skills) sb.Append($" · ia {s}: {Lap(t, s):0.00}");
            return sb.ToString();
        }
    }
}
