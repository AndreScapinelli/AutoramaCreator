using System.Collections;
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
            public override int GetHashCode() => System.HashCode.Combine(x.a, x.b, y.a, y.b, h);
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
            // Só para percorrer a pista que já existe (o loop sai deslocado para o lado); a busca não usa.
            new Move { def = PieceCatalog.Loop, dir = 1 },
            new Move { def = PieceCatalog.Loop, dir = -1 },
        };
        const int SearchMoves = 7;         // os primeiros de Moves: os que a busca pode colocar

        static Q[,] dxTab, dyTab;
        static int[,] dhTab;
        static Vector2[,][] sampleTab;     // pontos da peça a cada ~0,5 célula, a partir do começo (sem ele), por rumo

        static void BuildTables()
        {
            if (dxTab != null) return;
            int n = Moves.Length;
            dxTab = new Q[n, 8]; dyTab = new Q[n, 8]; dhTab = new int[n, 8]; sampleTab = new Vector2[n, 8][];
            for (int m = 0; m < n; m++)
            for (int h = 0; h < 8; h++)
            {
                var a = new Pose2(Vector2.zero, h * 45f);
                var b = TrackMath.PoseAt(a, Moves[m].def, Moves[m].dir, 1f);
                int k = Mathf.Max(2, Mathf.CeilToInt(Moves[m].def.CenterLength / 0.5f));
                sampleTab[m, h] = new Vector2[k];
                for (int j = 1; j <= k; j++) sampleTab[m, h][j - 1] = TrackMath.PoseAt(a, Moves[m].def, Moves[m].dir, j / (float)k).pos;
                dxTab[m, h] = Q.From(b.pos.x);
                dyTab[m, h] = Q.From(b.pos.y);
                dhTab[m, h] = Mod8(Mathf.RoundToInt(b.heading / 45f));
            }
        }

        static int Mod8(int v) => ((v % 8) + 8) % 8;

        static Vector2 Pos(State s) => new Vector2(s.x.Value, s.y.Value);

        static State Apply(State s, int m)
        {
            return new State { x = s.x + dxTab[m, s.h], y = s.y + dyTab[m, s.h], h = dhTab[m, s.h] };
        }

        // Movimento com a mesma geometria de uma peça (a largada é uma reta curta; as inclinadas são iguais às planas;
        // o cruzamento e a rampa de salto são retas na planta).
        static int MoveOf(TrackPiece p)
        {
            var d = p.Def;
            if (d.IsLoop) return p.dir > 0 ? 7 : 8;
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

        const int KeepPerState = 8;        // sequências guardadas por estado
        const int Beam = 20000;            // estados por camada que seguem na expansão

        // Busca progressiva: tenta primeiro com valores pequenos (rápido), depois aumenta se não achar.
        // ProgressiveDepths = máximo de peças de cada lado (o fechamento tem até o dobro); ProgressiveChecks = candidatos testados.
        static readonly int[] ProgressiveDepths = { 6, 9, 12, 15 };
        static readonly int[] ProgressiveChecks = { 200, 600, 1500, 3000 };
        // O custo cresce muito com a profundidade: o peso de cada etapa na barra de progresso.
        static readonly float[] StageWeights = { 1f, 2f, 4f, 8f };

        static float StageFrom(int i) { float a = 0f; for (int k = 0; k < i; k++) a += StageWeights[k]; return a / StageTotal; }
        static float StageSpan(int i) => StageWeights[i] / StageTotal;
        static float StageTotal { get { float t = 0f; foreach (float w in StageWeights) t += w; return t; } }

        const string NotFound = "Não achei um fechamento que funcione daqui. Leve a ponta mais perto da largada e tente de novo.";
        const string CrossesTrack = "Todo fechamento curto passa por cima da pista. Mexa um pouco a ponta (ou faça uma ponte) e tente de novo.";

        // Uma busca em andamento. Ela anda aos poucos: quem chama dá Pump(ms) a cada quadro, mostra Progress/Status/Elapsed
        // e pode dar Cancel() a qualquer hora. Assim o editor não congela em pistas que não têm fechamento.
        public sealed class Job
        {
            public bool Done { get; private set; }
            public bool Cancelled { get; private set; }
            public Result Result { get; private set; }
            public float Progress { get; internal set; }       // 0..1
            public string Status { get; internal set; } = "Começando…";
            public double Elapsed => watch.Elapsed.TotalSeconds;

            internal Result step;                               // resultado da etapa (profundidade) em andamento
            internal bool stop, crossed;                        // stop: nenhuma profundidade maior adianta
            readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            IEnumerator run;

            internal void Begin(IEnumerator it) { run = it; }
            internal void Finish(Result r) { Result = r; Progress = 1f; }

            // Trabalha por até budgetMs milissegundos. Devolve true enquanto ainda não acabou.
            public bool Pump(long budgetMs)
            {
                if (Done) return false;
                var t = System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    if (!run.MoveNext()) { Done = true; run = null; watch.Stop(); break; }
                } while (t.ElapsedMilliseconds < budgetMs);
                return !Done;
            }

            public void Cancel()
            {
                if (Done) return;
                Done = Cancelled = true;
                run = null;
                watch.Stop();
                Result = new Result { ok = false, message = "Busca cancelada." };
            }
        }

        // tail: as peças que já ficam atrás da largada; a pista da frente (existing) tem de chegar na ponta de trás delas.
        // Começa a busca sem rodar nada ainda: use Pump no Job.
        public static Job Start(Pose2 origin, IList<TrackPiece> existing, IList<TrackPiece> tail, bool preferBanked)
        {
            BuildTables();
            var job = new Job();
            job.Begin(Run(job, origin, new List<TrackPiece>(existing), new List<TrackPiece>(tail), preferBanked));
            return job;
        }

        // Versão de uma vez só (testes e scripts): roda a busca inteira sem devolver o controle.
        public static Result Close(Pose2 origin, IList<TrackPiece> existing, IList<TrackPiece> tail, bool preferBanked)
        {
            var job = Start(origin, existing, tail, preferBanked);
            job.Pump(long.MaxValue);
            return job.Result;
        }

        static IEnumerator Run(Job job, Pose2 origin, List<TrackPiece> existing, List<TrackPiece> tail, bool preferBanked)
        {
            for (int i = 0; i < ProgressiveDepths.Length; i++)
            {
                job.step = default;
                var step = CloseAtDepth(job, origin, existing, tail, preferBanked, i);
                while (step.MoveNext()) yield return null;
                if (job.step.ok || job.stop) { job.Finish(job.step); yield break; }
            }
            job.Finish(new Result { ok = false, message = job.crossed ? CrossesTrack : NotFound });
        }

        static IEnumerator CloseAtDepth(Job job, Pose2 origin, List<TrackPiece> existing, List<TrackPiece> tail, bool preferBanked, int stage)
        {
            int depth = ProgressiveDepths[stage], maxChecks = ProgressiveChecks[stage];
            float from = StageFrom(stage), span = StageSpan(stage);
            job.Progress = from;
            job.Status = $"Procurando com até {depth * 2} peças…";
            int ho = Mod8(Mathf.RoundToInt(origin.heading / 45f));
            var start = new State { x = new Q((long)Mathf.Round(origin.pos.x * 8f), 0), y = new Q((long)Mathf.Round(origin.pos.y * 8f), 0), h = ho };

            // Ponta da pista, em aritmética exata.
            var end = start;
            foreach (var p in existing) end = Apply(end, MoveOf(p));
            var cmp = new StateEq();

            // O destino, de ré: da largada, voltando peça por peça pelas de trás (a última da lista encosta na largada).
            var goal = new State { x = start.x, y = start.y, h = Mod8(start.h + 4) };
            for (int i = tail.Count - 1; i >= 0; i--) goal = Apply(goal, Mirror(MoveOf(tail[i])));
            var goalFront = goal; goalFront.h = Mod8(goal.h + 4);
            if (cmp.Equals(end, goalFront))
            {
                job.stop = true;
                job.step = new Result { ok = false, message = tail.Count > 0 ? "A ponta já encosta na pista de trás." : "A ponta já está na largada." };
                yield break;
            }

            var both = new List<TrackPiece>(existing);
            both.AddRange(tail);
            var segs = TrackLayout.Build(origin, both, tail.Count);
            int endZ = segs[existing.Count - 1].zOut;
            var before = TrackLayout.Analyze(origin, segs, tail.Count);

            // A pista no chão, para a busca não andar por cima dela. Com a ponta no alto as rampas que descem podem cair em
            // qualquer peça do fechamento, então aí não há poda (só o teste final).
            var ob = endZ == 0 ? Obstacles.Of(segs, existing.Count, tail.Count > 0 ? existing.Count : 0) : null;

            // Para a frente, a partir da ponta.
            var fwd = new Tree();
            var ex = ExpandAt(job, end, depth, fwd, Pos(goal), ob, from, from + span * 0.2f);
            while (ex.MoveNext()) yield return null;
            // Para trás, a partir da largada: anda de ré (rumo + 180°) com as curvas espelhadas.
            var back = new Tree();
            ex = ExpandAt(job, goal, depth, back, Pos(end), ob, from + span * 0.2f, from + span * 0.4f);
            while (ex.MoveNext()) yield return null;

            // Junta: estado alcançado pelos dois lados. Os candidatos ficam separados pelo número de peças,
            // e só se ordena um grupo quando chega a vez dele (quase sempre os maiores nem são tocados).
            var byLen = new List<Cand>[depth * 2 + 1];
            int total = 0, visited = 0;
            foreach (var kv in back.head)
            {
                if ((++visited & 63) == 0) { job.Progress = from + span * (0.4f + 0.1f * visited / back.head.Count); yield return null; }
                // kv.Key está em ré; o estado "de frente" equivalente tem rumo − 180°.
                var real = kv.Key; real.h = Mod8(real.h + 4);
                if (!fwd.head.TryGetValue(real, out int fh)) continue;
                for (int f = fh; f >= 0; f = fwd.nodes[f].next)
                {
                    var fs = fwd.Seq(f);
                    for (int b = kv.Value; b >= 0; b = back.nodes[b].next)
                    {
                        var bs = back.Seq(b);
                        var seq = new List<int>(fs);
                        // A sequência de ré, lida de trás para a frente e com as curvas desfeitas do espelho.
                        for (int i = bs.Count - 1; i >= 0; i--) seq.Add(Mirror(bs[i]));
                        if (seq.Count == 0) continue;
                        (byLen[seq.Count] ??= new List<Cand>()).Add(new Cand(seq));
                        total++;
                    }
                }
            }
            if (total == 0)
            {
                job.step = new Result { ok = false, message = $"Não achei um fechamento com até {depth * 2} peças. Leve a ponta mais perto da largada e tente de novo." };
                yield break;
            }

            // Menos peças, depois menos curvas (melhor para velocidade), depois menor comprimento,
            // depois menos meias-curvas, depois mais retas.
            var seen = new HashSet<string>();
            int checks = 0, planned = Mathf.Min(total, maxChecks);
            List<TrackPiece> fallback = null;
            int fallbackOverlaps = int.MaxValue;
            for (int len = 1; len < byLen.Length && checks <= maxChecks; len++)
            {
                var group = byLen[len];
                if (group == null) continue;
                group.Sort(Cand.Compare);
                foreach (var cand in group)
                {
                    var seq = cand.seq;
                    string key = string.Join(",", seq);
                    if (!seen.Add(key)) continue;
                    if (++checks > maxChecks) break;
                    job.Progress = from + span * (0.5f + 0.5f * checks / planned);
                    job.Status = $"Procurando com até {depth * 2} peças… testando {checks} de {planned}";
                    yield return null;
                    var add = ToPieces(seq, endZ, preferBanked);
                    if (add == null) continue;                        // poucas peças para descer da ponte
                    var all = new List<TrackPiece>(existing);
                    all.AddRange(add);
                    all.AddRange(tail);
                    var s2 = TrackLayout.Build(origin, all, tail.Count);
                    var a2 = TrackLayout.Analyze(origin, s2, tail.Count);
                    if (!a2.closed) continue;
                    int extra = a2.overlaps.Count - before.overlaps.Count;
                    if (extra <= 0) { job.step = new Result { ok = true, pieces = add, message = Describe(add) }; yield break; }
                    if (extra < fallbackOverlaps) { fallbackOverlaps = extra; fallback = add; }
                }
            }
            // Passou por cima da pista: uma profundidade maior ainda pode achar um que não passe.
            if (fallback != null) job.crossed = true;
            job.step = new Result { ok = false, message = fallback != null ? CrossesTrack : NotFound };
        }

        // Candidato de fechamento, com os critérios de ordenação já calculados.
        sealed class Cand
        {
            public readonly List<int> seq;
            readonly int curves, half, straights;
            readonly float length;
            public Cand(List<int> seq)
            {
                this.seq = seq;
                curves = Curves(seq); half = HalfCurves(seq); straights = Straights(seq); length = Length(seq);
            }
            public static int Compare(Cand p, Cand q)
            {
                if (p.curves != q.curves) return p.curves.CompareTo(q.curves);
                if (p.length != q.length) return p.length.CompareTo(q.length);
                if (p.half != q.half) return p.half.CompareTo(q.half);
                return q.straights.CompareTo(p.straights);        // mais retas é melhor, então inverte
            }
        }

        // Árvore da busca: cada nó guarda só o estado, o pai e a peça; a sequência é refeita subindo pelos pais.
        // Os nós do mesmo estado (até KeepPerState) formam uma lista ligada que começa em head[estado].
        struct Node { public State s; public int parent, next, move; }

        sealed class Tree
        {
            public readonly List<Node> nodes = new List<Node>();
            public readonly Dictionary<State, int> head = new Dictionary<State, int>(new StateEq());
            public List<int> Seq(int n)
            {
                var seq = new List<int>();
                for (; nodes[n].parent >= 0; n = nodes[n].parent) seq.Add(nodes[n].move);
                seq.Reverse();
                return seq;
            }
        }

        // Pontos da linha central da pista que já existe, no chão, numa grade de 1 célula. Uma peça nova que passa a
        // menos de Radius de um deles com certeza sobrepõe (o teste final usa 0,92), então a busca nem segue por ali.
        // As duas peças que encostam no fechamento ficam de fora: elas podem tocar a primeira e a última peça nova.
        sealed class Obstacles
        {
            const float Radius = 0.8f;
            readonly Dictionary<long, List<Vector2>> cells = new Dictionary<long, List<Vector2>>();
            static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

            public static Obstacles Of(List<Segment> segs, int skipA, int skipB)
            {
                var o = new Obstacles();
                for (int i = 0; i < segs.Count; i++)
                {
                    if (i == skipA - 1 || i == skipB) continue;
                    var s = segs[i];
                    int k = Mathf.Max(2, Mathf.CeilToInt(s.def.CenterLength / 0.25f));
                    for (int j = 0; j <= k; j++)
                    {
                        float t = j / (float)k, u = t * s.def.Length;
                        if (s.ZAt(t) > 0.01f) continue;                     // no alto (ponte, rampa, volta do loop): passa por baixo
                        if (s.def.IsJump && u > PieceCatalog.JumpUp - 0.49f && u < PieceCatalog.JumpUp + PieceCatalog.JumpGap + 0.49f) continue;
                        var p = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t).pos;
                        long key = Key(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                        if (!o.cells.TryGetValue(key, out var l)) o.cells[key] = l = new List<Vector2>();
                        l.Add(p);
                    }
                }
                return o;
            }

            public bool Hit(Vector2 p)
            {
                int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (cells.TryGetValue(Key(cx + dx, cy + dy), out var l))
                        foreach (var q in l) if ((q - p).sqrMagnitude < Radius * Radius) return true;
                return false;
            }

            // A peça m, saindo de s, passa por cima da pista? (De ré o traçado no chão é o mesmo.)
            public bool Blocks(State s, int m)
            {
                var b = Pos(s);
                foreach (var off in sampleTab[m, s.h]) if (Hit(b + off)) return true;
                return false;
            }
        }

        // Expande até maxDepth peças a partir de s. Poda: (1) estado a mais de 4·maxDepth células de `other` (a raiz do
        // outro lado), porque o outro lado tem no máximo maxDepth peças e nenhuma avança mais de 4; (2) peça que passa
        // por cima da pista (ob). Anda aos poucos (yield) e leva o progresso de `from` até `to`.
        static IEnumerator ExpandAt(Job job, State s, int maxDepth, Tree tree, Vector2 other, Obstacles ob, float from, float to)
        {
            tree.nodes.Add(new Node { s = s, parent = -1, next = -1, move = -1 });
            tree.head[s] = 0;
            var frontier = new List<int> { 0 };
            float reach = 4f * maxDepth + 1e-3f;
            int work = 0;
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                var next = new List<int>();
                for (int i = 0; i < frontier.Count; i++)
                {
                    if ((++work & 31) == 0)
                    {
                        job.Progress = from + (to - from) * ((depth - 1) + (float)i / frontier.Count) / maxDepth;
                        yield return null;
                    }
                    int pi = frontier[i];
                    var st = tree.nodes[pi].s;
                    for (int m = 0; m < SearchMoves; m++)
                    {
                        var ns = Apply(st, m);
                        if ((Pos(ns) - other).sqrMagnitude > reach * reach) continue;
                        int h = tree.head.TryGetValue(ns, out int hh) ? hh : -1, count = 0;
                        for (int c = h; c >= 0 && count < KeepPerState; c = tree.nodes[c].next) count++;
                        if (count >= KeepPerState) continue;
                        if (ob != null && ob.Blocks(st, m)) continue;
                        int idx = tree.nodes.Count;
                        tree.nodes.Add(new Node { s = ns, parent = pi, next = h, move = m });
                        tree.head[ns] = idx;
                        if (count == 0) next.Add(idx);      // só o primeiro caminho até o estado segue; os outros são variantes para a junção
                    }
                }
                // Feixe: só os Beam estados mais perto do outro lado seguem para a próxima camada.
                if (next.Count > Beam)
                {
                    var key = new float[next.Count];
                    for (int i = 0; i < next.Count; i++) key[i] = (Pos(tree.nodes[next[i]].s) - other).sqrMagnitude;
                    var arr = next.ToArray();
                    System.Array.Sort(key, arr);
                    next = new List<int>(arr);
                    next.RemoveRange(Beam, next.Count - Beam);
                }
                frontier = next;
                if (frontier.Count == 0) break;
            }
        }

        // Curva à direita percorrida de ré vira curva à esquerda (e vice-versa); retas não mudam, nem o loop
        // (de ré ele desliza para o mesmo lado: é o TrackMath.EntryFor).
        static int Mirror(int m) => m switch { 2 => 3, 3 => 2, 4 => 5, 5 => 4, _ => m };

        static float Length(List<int> seq) { float s = 0; foreach (int m in seq) s += Moves[m].def.CenterLength; return s; }
        static int Curves(List<int> seq) { int c = 0; foreach (int m in seq) if (Moves[m].def.IsCurve) c++; return c; }
        static int HalfCurves(List<int> seq) { int c = 0; foreach (int m in seq) if (m == 4 || m == 5) c++; return c; }
        static int Straights(List<int> seq) { int c = 0; foreach (int m in seq) if (m == 0 || m == 1 || m == 6) c++; return c; }

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
