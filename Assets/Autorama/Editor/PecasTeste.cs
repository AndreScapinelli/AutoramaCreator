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
        public static string Limite(string id, int dir = 1, int faixas = 2, int ang = 0)
        {
            var origin = TrackMath.SnapStart(new Pose2(new Vector2(10f, 12.5f), 0f));
            var ps = new List<TrackPiece> { new TrackPiece(PieceCatalog.Start.Id), new TrackPiece("reta-comprida"), new TrackPiece(id, dir) { ang = ang }, new TrackPiece("reta-comprida") };
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
    
        // Emendas (seção 100): para cada par de tipos de peça, monta reta + A + B + reta e anda pela pista em passos de
        // 0,05 célula medindo a altura das duas bordas (como a malha: SurfaceY ou TwistPoint). Salto = desnível de um
        // passo para o outro; quina = mudança de inclinação (1/célula) de um passo para o outro. Devolve os piores.
        public static string Emendas(int top = 40)
        {
            string[] ids = { "reta-comprida", "curva-longa", "curva-longa-inclinada", "meia-curva", "curva-curta-inclinada", "curva-c", "curva-c-inclinada",
                             "meia-curva-c", "meia-curva-c-inclinada", "muro", "parafuso", "fechada", "cruzamento", "salto" };
            var casos = new List<(string, TrackPiece, TrackPiece)>();
            var lista = new List<TrackPiece>();
            foreach (var id in ids)
            {
                var d = PieceCatalog.ById(id);
                lista.Add(new TrackPiece(id, d.Sided ? 1 : 0));
                if (d.Sided) lista.Add(new TrackPiece(id, -1));
            }
            lista.Add(new TrackPiece("reta-comprida", 0, 1));
            lista.Add(new TrackPiece("curva-longa", 1, 1));
            lista.Add(new TrackPiece("curva-longa-inclinada", 1, 1));
            foreach (var a in lista) foreach (var b in lista) casos.Add((Nome(a) + " > " + Nome(b), a, b));
            var res = new List<(float score, string txt)>();
            var origin = new Pose2(new Vector2(40f, 40f), 0f);
            foreach (var (nome, a, b) in casos)
            {
                var ps = new List<TrackPiece> { new TrackPiece("largada"), new TrackPiece("reta-comprida"), a.Clone(), b.Clone(), new TrackPiece("reta-comprida") };
                var segs = TrackLayout.Build(origin, ps);
                TrackLayout.ComputeBanking(segs, false);
                float worstJ = 0f, worstK = 0f; string where = "";
                foreach (float o in new[] { -0.5f, 0.5f })
                {
                    var hs = new List<float>(); var at = new List<string>();
                    for (int i = 1; i < segs.Count; i++)
                    {
                        var sg = segs[i];
                        int n = Mathf.Max(2, Mathf.CeilToInt(sg.def.CenterLength / 0.05f));
                        for (int j = (i == 1 ? 0 : 1); j <= n; j++)
                        {
                            float t = j / (float)n;
                            if (sg.def.IsJump && sg.def.JumpOpenAt(t * sg.def.Length)) { hs.Add(float.NaN); at.Add(""); continue; }
                            hs.Add(Edge(sg, t, o)); at.Add(Nome(sg.piece) + $" t={t:0.00}");
                        }
                    }
                    float st = 0.05f;
                    for (int k = 2; k < hs.Count; k++)
                    {
                        if (float.IsNaN(hs[k]) || float.IsNaN(hs[k - 1]) || float.IsNaN(hs[k - 2])) continue;
                        float jmp = Mathf.Abs(hs[k] - hs[k - 1]) - 0.05f;            // mais que 45° de rampa num passo
                        float kink = Mathf.Abs((hs[k] - hs[k - 1]) - (hs[k - 1] - hs[k - 2])) / (st * st);
                        if (jmp > worstJ) worstJ = jmp;
                        if (kink > worstK) { worstK = kink; where = at[k - 1]; }
                    }
                }
                res.Add((worstK + worstJ * 100f, $"{nome}: quina {worstK:0.0} em {where}" + (worstJ > 0.001f ? $" · salto {worstJ:0.000}" : "")));
            }
            res.Sort((x, y) => y.score.CompareTo(x.score));
            var sb = new StringBuilder($"{res.Count} pares\n");
            for (int i = 0; i < Mathf.Min(top, res.Count); i++) sb.Append(res[i].txt).Append('\n');
            return sb.ToString();
        }

        static string Nome(TrackPiece p) => p.id + (p.dir > 0 ? "+" : p.dir < 0 ? "-" : "") + (p.rise > 0 ? "^" : p.rise < 0 ? "v" : "");

        // Altura da borda o (±0,5) da pista em t, como a malha a desenha.
        static float Edge(Segment s, float t, float o)
        {
            if (s.def.IsTwist)
            {
                s.def.TwistPoint(s.piece.dir, t, o, 0f, out _, out float h, out _, s.wallIn, s.wallOut, s.piece.MuroDeg);
                return TrackLayout.SurfaceY(s, t, 0f) + h;
            }
            return TrackLayout.SurfaceY(s, t, o);
        }
}
}
