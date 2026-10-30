using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Fecha o circuito sozinho: acha a sequência mais curta de peças que leva a ponta da pista de volta à largada,
    // no lugar e no rumo exatos, sem passar por cima da pista (na mesma altura).
    //
    // Como as curvas de 45° geram coordenadas com √2, a conta é feita em aritmética exata: cada coordenada é
    // (a + b·√2) / 8 com a e b inteiros. Assim "fechou" quer dizer fechou mesmo, sem erro de arredondamento.
    // A busca vai pelos dois lados (da ponta para a frente e da largada para trás) e junta onde se encontram.
    public static class TrackCloser
    {
        public struct Result
        {
            public bool ok;
            public List<TrackPiece> pieces;
            public string message;
        }

        // ---------- Número exato (a + b√2) / 8 ----------

        struct Q
        {
            public long a, b;
            public Q(long a, long b) { this.a = a; this.b = b; }
            public static Q operator +(Q x, Q y) => new Q(x.a + y.a, x.b + y.b);
            public static Q operator -(Q x, Q y) => new Q(x.a - y.a, x.b - y.b);
            public float Value => (a + b * 1.41421356f) / 8f;

            // Converte um valor pequeno calculado em float para a forma exata (só usado para montar a tabela).
            public static Q From(float v)
            {
                for (int k = 0; k <= 64; k++)
                foreach (int b in k == 0 ? new[] { 0 } : new[] { k, -k })
                {
                    double a = System.Math.Round(8.0 * v - b * System.Math.Sqrt(2.0));
                    if (System.Math.Abs(a + b * System.Math.Sqrt(2.0) - 8.0 * v) < 1e-3) return new Q((long)a, b);
                }
                throw new System.InvalidOperationException("Valor fora da grade exata: " + v);
            }
        }

        struct State
        {
            public Q x, y;
            public int h;          // rumo em oitavos de volta (0..7, 45° cada, anti-horário)
            public override int GetHashCode() => (int)(x.a * 73856093 ^ x.b * 19349663 ^ y.a * 83492791 ^ y.b * 2654435761 ^ h * 97);
        }

        sealed class StateEq : IEqualityComparer<State>
        {
            public bool Equals(State p, State q) => p.h == q.h && p.x.a == q.x.a && p.x.b == q.x.b && p.y.a == q.y.a && p.y.b == q.y.b;
            public int GetHashCode(State s) => s.GetHashCode();
        }

        // ---------- Movimentos (as peças, cada uma num dos 8 rumos) ----------

        struct Move { public PieceDef def; public int dir; }

        static readonly Move[] Moves =
        {
            new Move { def = PieceCatalog.Long, dir = 0 },
            new Move { def = PieceCatalog.Short, dir = 0 },
            new Move { def = PieceCatalog.Curve, dir = 1 },
            new Move { def = PieceCatalog.Curve, dir = -1 },
            new Move { def = PieceCatalog.Half, dir = 1 },
            new Move { def = PieceCatalog.Half, dir = -1 },
            new Move { def = PieceCatalog.Tiny, dir = 0 },
        };

        static Q[,] dxTab, dyTab;
        static int[,] dhTab;

        static void BuildTables()
        {
            if (dxTab != null) return;
            int n = Moves.Length;
            dxTab = new Q[n, 8]; dyTab = new Q[n, 8]; dhTab = new int[n, 8];
            for (int m = 0; m < n; m++)
            for (int h = 0; h < 8; h++)
            {
                var b = TrackMath.PoseAt(new Pose2(Vector2.zero, h * 45f), Moves[m].def, Moves[m].dir, 1f);
                dxTab[m, h] = Q.From(b.pos.x);
                dyTab[m, h] = Q.From(b.pos.y);
                dhTab[m, h] = Mod8(Mathf.RoundToInt(b.heading / 45f));
            }
        }

        static int Mod8(int v) => ((v % 8) + 8) % 8;

        static State Apply(State s, int m)
        {
            return new State { x = s.x + dxTab[m, s.h], y = s.y + dyTab[m, s.h], h = dhTab[m, s.h] };
        }

        // Movimento com a mesma geometria de uma peça (a largada é uma reta curta; as inclinadas são iguais às planas).
        static int MoveOf(TrackPiece p)
        {
            var d = p.Def;
            if (!d.IsCurve)
            {
                if (Mathf.Approximately(d.Length, 4f)) return 0;
                if (Mathf.Approximately(d.Length, 2f)) return 1;
                return 6;
            }
            bool half = Mathf.Approximately(d.Angle, 45f);
            return half ? (p.dir > 0 ? 4 : 5) : (p.dir > 0 ? 2 : 3);
        }

        // ---------- Busca ----------

        const int Depth = 6;              // peças de cada lado: até 12 no total
        const int KeepPerState = 3;       // sequências guardadas por estado
        const int MaxChecks = 600;        // candidatos testados contra sobreposição

        public static Result Close(Pose2 origin, IList<TrackPiece> existing, bool preferBanked)
        {
            BuildTables();
            int ho = Mod8(Mathf.RoundToInt(origin.heading / 45f));
            var start = new State { x = new Q((long)Mathf.Round(origin.pos.x * 8f), 0), y = new Q((long)Mathf.Round(origin.pos.y * 8f), 0), h = ho };

            // Ponta da pista, em aritmética exata.
            var end = start;
            foreach (var p in existing) end = Apply(end, MoveOf(p));
            var cmp = new StateEq();
            if (cmp.Equals(end, start)) return new Result { ok = false, message = "A ponta já está na largada." };

            var segs = TrackLayout.Build(origin, existing);
            int endZ = segs[segs.Count - 1].zOut;
            var before = TrackLayout.Analyze(origin, segs);

            // Para a frente, a partir da ponta.
            var fwd = Expand(end, false);
            // Para trás, a partir da largada: anda de ré (rumo + 180°) com as curvas espelhadas.
            var back = Expand(new State { x = start.x, y = start.y, h = Mod8(start.h + 4) }, true);

            // Junta: estado alcançado pelos dois lados.
            var candidates = new List<List<int>>();
            foreach (var kv in back)
            {
                // kv.Key está em ré; o estado "de frente" equivalente tem rumo − 180°.
                var real = kv.Key; real.h = Mod8(real.h + 4);
                if (!fwd.TryGetValue(real, out var fs)) continue;
                foreach (var f in fs)
                foreach (var b in kv.Value)
                {
                    var seq = new List<int>(f);
                    // A sequência de ré, lida de trás para a frente e com as curvas desfeitas do espelho.
                    for (int i = b.Length - 1; i >= 0; i--) seq.Add(Mirror(b[i]));
                    if (seq.Count > 0) candidates.Add(seq);
                }
            }
            if (candidates.Count == 0)
                return new Result { ok = false, message = $"Não achei um fechamento com até {Depth * 2} peças. Leve a ponta mais perto da largada e tente de novo." };

            // Menos peças primeiro; depois menor comprimento; depois menos curvas.
            candidates.Sort((p, q) =>
            {
                int c = p.Count.CompareTo(q.Count);
                if (c != 0) return c;
                c = Length(p).CompareTo(Length(q));
                return c != 0 ? c : Curves(p).CompareTo(Curves(q));
            });

            var seen = new HashSet<string>();
            int checks = 0;
            List<TrackPiece> fallback = null;
            int fallbackOverlaps = int.MaxValue;
            foreach (var seq in candidates)
            {
                string key = string.Join(",", seq);
                if (!seen.Add(key)) continue;
                if (++checks > MaxChecks) break;
                var add = ToPieces(seq, endZ, preferBanked);
                if (add == null) continue;                        // poucas peças para descer da ponte
                var all = new List<TrackPiece>(existing);
                all.AddRange(add);
                var s2 = TrackLayout.Build(origin, all);
                var a2 = TrackLayout.Analyze(origin, s2);
                if (!a2.closed) continue;
                int extra = a2.overlaps.Count - before.overlaps.Count;
                if (extra <= 0) return new Result { ok = true, pieces = add, message = Describe(add) };
                if (extra < fallbackOverlaps) { fallbackOverlaps = extra; fallback = add; }
            }
            if (fallback != null)
                return new Result { ok = false, message = "Todo fechamento curto passa por cima da pista. Mexa um pouco a ponta (ou faça uma ponte) e tente de novo." };
            return new Result { ok = false, message = "Não achei um fechamento que funcione daqui. Leve a ponta mais perto da largada e tente de novo." };
        }

        // Todas as sequências de até Depth peças a partir de s; para cada estado, guarda as mais curtas.
        static Dictionary<State, List<int[]>> Expand(State s, bool reverse)
        {
            var map = new Dictionary<State, List<int[]>>(new StateEq()) { [s] = new List<int[]> { new int[0] } };
            var frontier = new List<(State st, int[] seq)> { (s, new int[0]) };
            for (int depth = 1; depth <= Depth; depth++)
            {
                var next = new List<(State, int[])>();
                foreach (var (st, seq) in frontier)
                for (int m = 0; m < Moves.Length; m++)
                {
                    var ns = Apply(st, m);
                    var nseq = new int[seq.Length + 1];
                    seq.CopyTo(nseq, 0);
                    nseq[seq.Length] = m;
                    if (!map.TryGetValue(ns, out var list)) { list = new List<int[]>(); map[ns] = list; }
                    if (list.Count >= KeepPerState) continue;    // já há caminhos mais curtos até aqui
                    list.Add(nseq);
                    next.Add((ns, nseq));
                }
                frontier = next;
            }
            return map;
        }

        // Curva à direita percorrida de ré vira curva à esquerda (e vice-versa); retas não mudam.
        static int Mirror(int m) => m switch { 2 => 3, 3 => 2, 4 => 5, 5 => 4, _ => m };

        static float Length(List<int> seq) { float s = 0; foreach (int m in seq) s += Moves[m].def.CenterLength; return s; }
        static int Curves(List<int> seq) { int c = 0; foreach (int m in seq) if (Moves[m].def.IsCurve) c++; return c; }

        // Vira peças; se a ponta está no alto, as primeiras retas descem (uma por nível).
        static List<TrackPiece> ToPieces(List<int> seq, int endZ, bool preferBanked)
        {
            var list = new List<TrackPiece>();
            foreach (int m in seq)
            {
                var d = Moves[m].def;
                if (preferBanked && d == PieceCatalog.Curve) d = PieceCatalog.CurveB;
                if (preferBanked && d == PieceCatalog.Half) d = PieceCatalog.HalfB;
                list.Add(new TrackPiece(d.Id, Moves[m].dir));
            }
            int need = endZ;
            // A rampa vai na reta mais suave que houver: comprida, depois curta, depois qualquer peça.
            foreach (var pass in new[] { 0, 1, 2 })
                for (int i = 0; i < list.Count && need > 0; i++)
                {
                    var d = list[i].Def;
                    bool ok = pass == 0 ? d == PieceCatalog.Long : pass == 1 ? d == PieceCatalog.Short : true;
                    if (ok && list[i].rise == 0) { list[i].rise = -1; need--; }
                }
            return need > 0 ? null : list;
        }

        static string Describe(List<TrackPiece> add)
        {
            var names = new List<string>();
            foreach (var p in add) names.Add(p.Describe().ToLowerInvariant());
            return $"Circuito fechado com {add.Count} {(add.Count == 1 ? "peça" : "peças")}: {string.Join(", ", names)}. Ctrl+Z desfaz.";
        }
    }
}
