using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Autorama
{
    // Testes das peças da seção 99 (meia curva em C, muro, parafuso e fechada): identidade das pistas antigas, o Q
    // fechando uma pista com cada peça e a IA correndo nela (saídas por volta, por qualidade).
    // script eval 'return Autorama.PecasTeste.Run();'
    public static class PecasTeste
    {
        public static string Run(int faixas = 2)
        {
            var sb = new StringBuilder();
            sb.Append($"oval {Champ.TrackHash(TrackSave.Oval())} (a34f4cb9) · 8 {Champ.TrackHash(TrackSave.Eight())} (81546106)\n");
            foreach (var (nome, ps) in Casos())
            {
                var t = Fecha(ps, faixas, out string msg);
                sb.Append(nome).Append(": ").Append(msg);
                if (t != null) sb.Append(" · ").Append(Corre(t));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        static IEnumerable<(string, TrackPiece[])> Casos()
        {
            yield return ("meia C r1", new[] { new TrackPiece("reta-comprida"), new TrackPiece("meia-curva-c", 1, 0, 1, 1), new TrackPiece("reta-curta") });
            yield return ("meia C r2 incl", new[] { new TrackPiece("reta-comprida"), new TrackPiece("meia-curva-c-inclinada", -1, 0, 1, 2), new TrackPiece("reta-curta") });
            yield return ("meia C r3", new[] { new TrackPiece("reta-curta"), new TrackPiece("meia-curva-c", 1, 0, 1, 3), new TrackPiece("meia-curva-c", 1, 0, 1, 3) });
            yield return ("muro dir", new[] { new TrackPiece("reta-curta"), new TrackPiece("muro", 1) });
            yield return ("muro esq após C", new[] { new TrackPiece("curva-c", 1, 0, 1, 2), new TrackPiece("muro", -1) });
            yield return ("parafuso", new[] { new TrackPiece("reta-curta"), new TrackPiece("parafuso", 1) });
            yield return ("parafuso após curva", new[] { new TrackPiece("curva-longa", -1), new TrackPiece("parafuso", -1), new TrackPiece("reta-curta") });
            yield return ("fechada", new[] { new TrackPiece("reta-curta"), new TrackPiece("fechada") });
            yield return ("diagonal", new[] { new TrackPiece("meia-curva", -1), new TrackPiece("muro", 1), new TrackPiece("meia-curva", 1) });
        }

        // A largada, as peças do caso e o Q fecha o resto.
        public static TrackSave Fecha(TrackPiece[] caso, int faixas, out string msg)
        {
            var origin = TrackMath.SnapStart(new Pose2(new Vector2(10f, 12.5f), 0f));
            var ps = new List<TrackPiece> { new TrackPiece(PieceCatalog.Start.Id) };
            ps.AddRange(caso);
            var r = TrackCloser.Close(origin, ps, new List<TrackPiece>(), false);
            if (!r.ok) { msg = "Q falhou: " + r.message; return null; }
            ps.AddRange(r.pieces);
            var segs = TrackLayout.Build(origin, ps);
            var a = TrackLayout.Analyze(origin, segs, 0, faixas);
            msg = $"Q +{r.pieces.Count} peças, fechada {a.closed}, sobreposições {a.overlaps.Count}";
            return TrackSave.From("teste", new Vector2Int(60, 40), 15f, origin, ps, 0, faixas);
        }

        // Saídas por volta da IA sozinha, por qualidade (5 voltas, 4 provas).
        public static string Corre(TrackSave t, params int[] skills)
        {
            if (skills.Length == 0) skills = new[] { 20, 60, 100, 150, 200 };
            var segs = TrackLayout.Build(t.Origin, t.Pieces(), t.Tail, t.StartZ);
            var sb = new StringBuilder();
            foreach (int sk in skills)
            {
                Random.InitState(77 + sk);
                float offs = 0f, best = 0f; int n = 0;
                for (int run = 0; run < 4; run++)
                {
                    var sim = new RaceSim(new RaceSettings { laps = 5, collide = false, turbo = false });
                    sim.SetTrack(segs, true, t.Faixas);
                    for (int k = 0; k < sim.N; k++) { sim.present[k] = k == 0; sim.aiSkill[k] = k == 0 ? sk : 0; }
                    sim.StartRace();
                    for (int i = 0; i < 60 * 600 && sim.phase != RaceSim.Phase.Done; i++) sim.Step(1f / 60f);
                    var c = sim.cars[0];
                    offs += c.offs; best += c.best ?? 99f; n++;
                }
                sb.Append($"ia{sk} {offs / (5f * n):0.00}/v {best / n:0.00}s  ");
            }
            return sb.ToString();
        }

        // Todos os trilhos com robôs da mesma qualidade e batida ligada: saídas e batidas por volta (por carro).
        public static string Duelo(TrackPiece[] caso, int faixas, int skill, int runs = 4)
        {
            var t = Fecha(caso, faixas, out string msg);
            if (t == null) return msg;
            var segs = TrackLayout.Build(t.Origin, t.Pieces(), t.Tail, t.StartZ);
            float offs = 0f, hits = 0f; int n = 0;
            Random.InitState(5 + skill);
            for (int r = 0; r < runs; r++)
            {
                var sim = new RaceSim(new RaceSettings { laps = 5, collide = true, turbo = false });
                sim.SetTrack(segs, true, t.Faixas);
                for (int k = 0; k < sim.N; k++) { sim.present[k] = true; sim.aiSkill[k] = skill; }
                sim.StartRace();
                for (int i = 0; i < 60 * 900 && sim.phase != RaceSim.Phase.Done; i++) sim.Step(1f / 60f);
                for (int k = 0; k < sim.N; k++) { offs += sim.cars[k].offs; hits += sim.cars[k].hits; n++; }
            }
            return $"{faixas} faixas ia{skill}: saídas {offs / (5f * n):0.00}/v · batidas {hits / (5f * n):0.00}/v";
        }

        public static string Fechadas()
        {
            var sb = new StringBuilder();
            var caso = new[] { new TrackPiece("reta-curta"), new TrackPiece("fechada") };
            var reta = new[] { new TrackPiece("reta-curta"), new TrackPiece("reta-comprida") };
            foreach (int f in new[] { 2, 4, 8 })
                foreach (int sk in new[] { 50, 100, 180 })
                    sb.Append(Duelo(caso, f, sk)).Append("  (sem fechada: ").Append(Duelo(reta, f, sk)).Append(")\n");
            return sb.ToString();
        }

        // Velocidade constante (o gatilho na fração certa) pela peça: cai ou não? Devolve a menor velocidade (células/s)
        // que passa, de 0,5 em 0,5.
        public static string Limite(string id, int dir = 1, int faixas = 2)
        {
            var origin = TrackMath.SnapStart(new Pose2(new Vector2(10f, 12.5f), 0f));
            var ps = new List<TrackPiece> { new TrackPiece(PieceCatalog.Start.Id), new TrackPiece("reta-comprida"), new TrackPiece(id, dir), new TrackPiece("reta-comprida") };
            var segs = TrackLayout.Build(origin, ps);
            var sb = new StringBuilder(id + ":");
            foreach (var su in new[] { new CarSetup(), new CarSetup { chassi = 1 }, new CarSetup { chassi = 2 }, new CarSetup { pneu = 1 } })
            {
                float pass = -1f;
                for (float v = 2f; v <= 14f; v += 0.5f)
                {
                    var sim = new RaceSim(new RaceSettings { collide = false, turbo = false });
                    sim.SetTrack(segs, false, faixas);
                    sim.setup[0] = su;
                    sim.present[1] = false;
                    sim.StartTest();
                    var c = sim.cars[0];
                    c.s = 3f * sim.Scale; c.v = v * sim.Speed;
                    bool fell = false;
                    for (int i = 0; i < 12000 && c.s < 15f * sim.Scale; i++)
                    {
                        c.held = true; c.hold = 99f; c.gas = v / (sim.set.vmax * su.Top);
                        sim.Step(1f / 120f);
                        if (c.mode != CarMode.OnSlot) { fell = true; break; }
                    }
                    if (!fell) { pass = v; break; }
                }
                sb.Append($" {su.Resumo} {pass:0.0}");
            }
            return sb.ToString();
        }
    }
}
