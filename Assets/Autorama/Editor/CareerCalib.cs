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

        // Média de `runs` provas de `laps` voltas da IA sozinha (semente fixa): tempo total, melhor volta e saídas.
        public static Vector3 Race(TrackSave t, int skill, int runs = 12, int laps = 5)
        {
            var segs = TrackLayout.Build(t.Origin, t.Pieces(), t.Tail, t.StartZ);
            Random.InitState(1234 + skill);
            float tot = 0f, best = 0f, offs = 0f; int n = 0;
            for (int r = 0; r < runs; r++)
            {
                var sim = new RaceSim(new RaceSettings { laps = laps, collide = false, turbo = false });
                sim.SetTrack(segs, true, t.Faixas);
                for (int k = 0; k < sim.N; k++) { sim.present[k] = k == 0; sim.aiSkill[k] = k == 0 ? skill : 0; }
                sim.StartRace();
                for (int i = 0; i < 60 * 900 && sim.phase != RaceSim.Phase.Done; i++) sim.Step(1f / 60f);
                var c = sim.cars[0];
                if (!c.finish.HasValue) continue;
                tot += c.finish.Value; best += c.best ?? 0f; offs += c.offs; n++;
            }
            return n == 0 ? Vector3.zero : new Vector3(tot / n, best / n, offs / n);
        }

        public static string Table(string pista, params int[] skills)
        {
            var t = Career.Track(pista);
            var sb = new StringBuilder(pista);
            var segs = TrackLayout.Build(t.Origin, t.Pieces(), t.Tail, t.StartZ);
            sb.Append($" · piloto ótimo: volta {Optimal(t):0.00}");
            foreach (int s in skills) { var v = Race(t, s); sb.Append($"\n ia {s}: prova {v.x:0.00} · volta {v.y:0.00} · saídas {v.z:0.0}"); }
            return sb.ToString();
        }

        // Volta do piloto ótimo do SetupCalib (a referência de um jogador muito bom), no tamanho da pista.
        public static float Optimal(TrackSave t)
        {
            var segs = TrackLayout.Build(t.Origin, t.Pieces(), t.Tail, t.StartZ);
            SetupCalib.Faixas = t.Faixas;
            float v = SetupCalib.Drive(segs, 0, default) / (SetupCalib.Laps - 1);
            SetupCalib.Faixas = 2;
            return v;
        }

        // Tempos do relógio (seção 70): bronze = melhor volta média da IA 90, prata = do mestre 125, ouro = piloto ótimo + 4%.
        public static string Clock(string pista)
        {
            var t = Career.Track(pista);
            float b = Race(t, 90, 8, 3).y, p = Race(t, 125, 8, 3).y, o = Optimal(t) * 1.04f;
            return $"{pista}: bronze {b:0.00} · prata {p:0.00} · ouro {o:0.00}";
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
