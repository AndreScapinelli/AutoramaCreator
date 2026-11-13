using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Autorama
{
    // Calibração do ajuste do carro (seção 47 do guia). Sem cena: `script eval 'return Autorama.SetupCalib.Run(0, 5);'`.
    // Um piloto de teste dá voltas com cada uma das 27 combinações em seis pistas e a tabela mostra quanto cada peça
    // vale em cada pista. O piloto não é a IA do jogo (que freia pelo limite da curva e não usa a folga da
    // derrapagem): a cada passo ele procura o maior acelerador (de `GasMin` a 100%, como um gatilho de controle) com
    // que ainda dá para frear depois sem sair da pista, testando isso numa cópia da simulação. Ele freia com o gatilho
    // no mínimo, sem soltar. Para tirar o ruído, vale o melhor tempo entre as tentativas com cada folga de `Slips`.
    public static class SetupCalib
    {
        const float Dt = 1f / 60f, Horizon = 0.8f;
        public static float[] Slips = { 0.85f, 0.92f, 0.98f };
        public static int Laps = 4;
        public static float GasMin = 0.02f;
        static float SlipMax;

        static readonly string[] Motor = { "padrão", "curto", "longo" }, Bolha = { "padrão", "asa", "lisa" }, Pneu = { "médio", "macio", "duro" };

        static PieceSave P(string tipo, string sentido = "", string altura = "") => new PieceSave { tipo = tipo, sentido = sentido, altura = altura };

        static TrackSave T(string nome, params PieceSave[] p)
        {
            var s = new TrackSave { nome = nome, largada = new OriginSave { x = 8, y = 7.5f, h = 0 } };
            s.pecas.AddRange(p);
            return s;
        }

        public static TrackSave[] Tracks()
        {
            PieceSave L = P("reta-comprida"), C = P("reta-curta"), D = P("curva-longa", "direita"), B = P("curva-longa-inclinada", "direita");
            return new[]
            {
                TrackSave.Oval(),
                TrackSave.Eight(),
                T("Retas longas", L, L, L, D, D, L, L, L, C, D, D),
                T("Retas longas inclinada", L, L, L, B, B, L, L, L, C, B, B),
                T("Quadrado", D, C, D, C, D, C, D),
                T("Oval curto", D, D, C, D, D),
            };
        }

        static RaceSim New(List<Segment> segs, int lane, CarSetup su, int laps)
        {
            var sim = new RaceSim(new RaceSettings { laps = laps, collide = false, turbo = false });
            sim.SetTrack(segs, true, 2);
            for (int k = 0; k < sim.N; k++) sim.present[k] = k == lane;
            sim.setup[lane] = su;
            return sim;
        }

        static CarState Copy(CarState c) => new CarState
        {
            s = c.s, v = c.v, hold = c.hold, T = c.T, slip = c.slip, side = c.side, lane = c.lane,
            cross = c.cross, crossBase = c.crossBase, laps = c.laps, lapStart = c.lapStart,
        };

        // Dá para acelerar até `gas` neste passo e ainda frear depois sem sair?
        static bool Safe(RaceSim probe, CarState c, int lane, float gas)
        {
            var p = Copy(c);
            p.held = true; p.gas = gas;
            probe.cars[lane] = p;
            probe.Step(Dt);
            if (p.mode != CarMode.OnSlot || p.slip > SlipMax) return false;
            p.gas = GasMin;
            for (float t = 0f; t < Horizon; t += Dt)
            {
                probe.Step(Dt);
                if (p.mode != CarMode.OnSlot || p.slip > SlipMax) return false;
                if (p.v < 1f) break;
            }
            return true;
        }

        // O tempo das voltas lançadas (todas menos a primeira) num trilho: o melhor entre as tentativas, uma por folga
        // de derrapagem. −1: saiu da pista em todas.
        public static float Drive(List<Segment> segs, int lane, CarSetup su)
        {
            float best = -1f;
            foreach (float sm in Slips)
            {
                SlipMax = sm;
                float t = Attempt(segs, lane, su);
                if (t > 0f && (best < 0f || t < best)) best = t;
            }
            return best;
        }

        static float Attempt(List<Segment> segs, int lane, CarSetup su)
        {
            var sim = New(segs, lane, su, Laps);
            var probe = New(segs, lane, su, 100000);
            probe.Mirror(RaceSim.Phase.Running, 0f, -1);
            sim.StartRace();
            float lap1 = -1f;
            for (int i = 0; i < 60 * 300; i++)
            {
                var c = sim.cars[lane];
                if (lap1 < 0f && c.laps >= 1) lap1 = sim.t;
                if (c.finish != null) return c.finish.Value - lap1;
                if (sim.phase == RaceSim.Phase.Countdown) { c.held = true; c.gas = 1f; }
                else if (c.mode != CarMode.OnSlot) return -1f;
                else
                {
                    c.held = true; c.gas = 1f;
                    if (!Safe(probe, c, lane, 1f))
                    {
                        c.gas = GasMin;
                        if (Safe(probe, c, lane, GasMin))
                        {
                            float lo = GasMin, hi = 1f;
                            for (int b = 0; b < 6; b++) { float mid = (lo + hi) * 0.5f; if (Safe(probe, c, lane, mid)) lo = mid; else hi = mid; }
                            c.gas = lo;
                        }
                    }
                }
                sim.Step(Dt);
            }
            return -1f;
        }

        // Quanto cada número do ajuste vale sozinho em cada pista: liga um de cada vez (os outros em zero) e compara
        // com o carro padrão. Ordem: aceleração do motor, final do motor, aderência da bolha, final da bolha,
        // aderência do pneu, derrapagem do pneu (esta é a do macio: enche mais rápido).
        public static string Sens(int from, int to)
        {
            float[] keep = { CarSetup.MotorAccel, CarSetup.MotorTop, CarSetup.BodyGrip, CarSetup.BodyTop, CarSetup.TireGrip, CarSetup.TireSlip };
            string[] names = { "motor acel", "motor final", "bolha ader", "bolha final", "pneu ader", "pneu derrap" };
            var with = new[] { new CarSetup { motor = 1 }, new CarSetup { motor = 2 }, new CarSetup { bolha = 1 }, new CarSetup { bolha = 2 }, new CarSetup { pneu = 1 }, new CarSetup { pneu = 1 } };
            void Set(float[] v) { CarSetup.MotorAccel = v[0]; CarSetup.MotorTop = v[1]; CarSetup.BodyGrip = v[2]; CarSetup.BodyTop = v[3]; CarSetup.TireGrip = v[4]; CarSetup.TireSlip = v[5]; }
            var sb = new StringBuilder();
            var tracks = Tracks();
            for (int ti = from; ti <= to && ti < tracks.Length; ti++)
            {
                var s = tracks[ti];
                var segs = TrackLayout.Build(s.Origin, s.Pieces(), s.Tail, s.StartZ);
                float Both(CarSetup su) => Drive(segs, 0, su) + Drive(segs, 1, su);
                float t0 = Both(default);
                sb.Append($"{s.nome}: {t0:0.000} s");
                for (int p = 0; p < 6; p++)
                {
                    var one = new float[6];
                    one[p] = keep[p];
                    Set(one);
                    sb.Append($" · {names[p]} {keep[p] * 100f:0.#}%: {((Both(with[p]) / t0 - 1f) * 100f).ToString("+0.00;-0.00")}%");
                }
                Set(keep);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static float Mean(float[] t, System.Func<int, bool> pick)
        {
            float sum = 0f; int n = 0;
            for (int i = 0; i < 27; i++) if (pick(i)) { sum += t[i]; n++; }
            return sum / n;
        }

        // Pistas de `from` a `to` (índices de Tracks). Em cada uma: o tempo do carro padrão (voltas lançadas, soma dos dois trilhos), a
        // melhor e a pior combinação e, para cada opção de cada peça, a média das 9 combinações com ela contra a média geral.
        public static string Run(int from, int to)
        {
            var sb = new StringBuilder();
            var tracks = Tracks();
            for (int ti = from; ti <= to && ti < tracks.Length; ti++)
            {
                var s = tracks[ti];
                var segs = TrackLayout.Build(s.Origin, s.Pieces(), s.Tail, s.StartZ);
                if (!TrackLayout.Analyze(s.Origin, segs, s.Tail, 2).closed) { sb.AppendLine($"{s.nome}: ABERTA"); continue; }
                var t = new float[27];
                bool fail = false;
                for (int i = 0; i < 27; i++)
                {
                    var su = new CarSetup { motor = i / 9, bolha = i / 3 % 3, pneu = i % 3 };
                    float a = Drive(segs, 0, su), b = Drive(segs, 1, su);
                    if (a < 0f || b < 0f) fail = true;
                    t[i] = a + b;
                }
                int best = 0, worst = 0;
                for (int i = 1; i < 27; i++) { if (t[i] < t[best]) best = i; if (t[i] > t[worst]) worst = i; }
                string Name(int i) => $"{Motor[i / 9]}/{Bolha[i / 3 % 3]}/{Pneu[i % 3]}";
                string Pct(float x) => ((x / t[0] - 1f) * 100f).ToString("+0.00;-0.00") + "%";
                float all = Mean(t, i => true);
                sb.AppendLine($"{s.nome}{(fail ? " (ALGUÉM SAIU)" : "")}: padrão {t[0]:0.000} s · melhor {Name(best)} {Pct(t[best])} · pior {Name(worst)} {Pct(t[worst])}");
                string Row(string[] names, System.Func<int, int> part)
                {
                    var r = new StringBuilder();
                    for (int o = 0; o < 3; o++) { int oo = o; r.Append($" {names[o]} {((Mean(t, i => part(i) == oo) / all - 1f) * 100f).ToString("+0.00;-0.00")}%"); }
                    return r.ToString();
                }
                sb.AppendLine("  motor:" + Row(Motor, i => i / 9));
                sb.AppendLine("  bolha:" + Row(Bolha, i => i / 3 % 3));
                sb.AppendLine("  pneu: " + Row(Pneu, i => i % 3));
            }
            return sb.ToString();
        }
    }
}
