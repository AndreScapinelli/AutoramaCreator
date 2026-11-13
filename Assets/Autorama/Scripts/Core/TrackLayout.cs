using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Peça já posicionada: pose de entrada a, de saída b e alturas (em níveis).
    public class Segment
    {
        public int index;
        public TrackPiece piece;
        public PieceDef def;
        public Pose2 a, b;
        public int zIn, zOut;
        public bool bankIn, bankOut;     // vizinha também inclinada para o mesmo lado (sem transição naquela ponta)
        public bool flatIn, flatOut;     // (inclinada) vizinha plana ligada: a inclinação continua nela, sem transição de 20%
        public int bankDirIn, bankDirOut; // (plana) dir da peça inclinada vizinha, 0 se não há; a inclinação espalha por ela

        public bool rampIn, rampOut;     // (rampa) a vizinha continua a subida ou a descida: sem arredondar naquela ponta

        // Nível em linha reta: vale para as regras (sobreposição, colisão, pilares). A superfície usa ZSmooth.
        // No loop soma a altura da volta (em níveis): o que passa por baixo do alto dela não é sobreposição.
        public float ZAt(float t)
        {
            if (!def.IsLoop) return zIn + (zOut - zIn) * t;
            TrackMath.LoopAt(t, out _, out _, out float phi);
            return zIn + PieceCatalog.LoopRadius * (1f - Mathf.Cos(phi)) / TrackMath.LevelHeight;
        }

        // Nível da superfície: a rampa arredonda nas pontas que encontram peça plana (inclinação zero no encaixe)
        // e segue reta na ponta em que a vizinha continua a rampa. Hermite com inclinação a na entrada e b na saída.
        public float ZSmooth(float t)
        {
            if (zIn == zOut) return zIn;
            float a = rampIn ? 1f : 0f, b = rampOut ? 1f : 0f, t2 = t * t, t3 = t2 * t;
            return zIn + (zOut - zIn) * ((t3 - 2f * t2 + t) * a + 3f * t2 - 2f * t3 + (t3 - t2) * b);
        }
        public bool High => zIn > 0 || zOut > 0;
        public bool IsRamp => zIn != zOut;
    }

    public class TrackAnalysis
    {
        public bool closed;          // fecha na largada, no chão
        public bool closedXY;        // fecha no plano (pode estar no alto)
        public int endZ;
        public float gap;            // distância da ponta até a largada
        public readonly HashSet<int> overlaps = new HashSet<int>();
        public float centerLength;
        public float[] laneLengths = new float[2];   // um por trilho (2 ou 4)
        public Rect bounds;          // em células, plano da mesa (x, z)
        public int highCount;
    }

    public static class TrackLayout
    {
        // Cada peça herda a pose e a altura de onde a anterior terminou; rampas sobem ou descem um nível.
        // As últimas "tail" peças da lista ficam ATRÁS da largada: são colocadas de ré a partir dela (a última da lista
        // encosta na largada), no chão. Na ordem da lista é a mesma volta: largada, frente..., atrás... e a largada de novo.
        public static List<Segment> Build(Pose2 origin, IList<TrackPiece> pieces, int tail = 0)
        {
            var list = new List<Segment>(pieces.Count);
            var conn = origin;
            int z = 0;
            int front = pieces.Count - tail;
            for (int i = 0; i < front; i++)
            {
                var p = pieces[i];
                var d = p.Def;
                var b = TrackMath.PoseAt(conn, d, p.dir, 1f);
                b.heading = TrackMath.Normalize(b.heading);
                int zOut = Mathf.Clamp(z + (i == 0 || d.GroundOnly ? 0 : p.rise), 0, TrackMath.MaxLevel);
                list.Add(new Segment { index = i, piece = p, def = d, a = conn, b = b, zIn = z, zOut = zOut });
                conn = b;
                z = zOut;
            }
            var behind = new List<Segment>(tail);
            var c = new Pose2(origin.pos, TrackMath.Normalize(origin.heading));
            for (int i = pieces.Count - 1; i >= front; i--)
            {
                var p = pieces[i];
                var a = TrackMath.EntryFor(c, p.Def, p.dir);
                a.heading = TrackMath.Normalize(a.heading);
                behind.Add(new Segment { index = i, piece = p, def = p.Def, a = a, b = c, zIn = 0, zOut = 0 });
                c = a;
            }
            behind.Reverse();
            list.AddRange(behind);
            return list;
        }

        // tail = quantas das últimas peças ficam atrás da largada. A pista da frente fecha quando a ponta dela
        // chega na entrada da peça de trás mais afastada (ou na largada, se não há peças atrás).
        public static TrackAnalysis Analyze(Pose2 origin, List<Segment> segs, int tail = 0, int faixas = 2)
        {
            var r = new TrackAnalysis();
            var lanes = TrackMath.LanesOf(faixas);
            r.laneLengths = new float[lanes.Length];
            int fe = segs.Count - tail;
            var tip = segs[fe - 1];
            var target = tail > 0 ? segs[fe].a : origin;
            r.gap = Vector2.Distance(tip.b.pos, target.pos);
            float dh = Mathf.Abs(Mathf.DeltaAngle(tip.b.heading, target.heading));
            r.closedXY = segs.Count > 1 && r.gap < 0.02f && dh < 0.5f;
            r.endZ = tip.zOut;
            r.closed = r.closedXY && r.endZ == 0;

            // Pontos da linha central a cada 0,25 célula (x, z da mesa, altura em níveis).
            int n = segs.Count;
            var pts = new List<Vector3>[n];
            var boxes = new Rect[n];
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var s = segs[i];
                int k = Mathf.Max(2, Mathf.CeilToInt(s.def.CenterLength / 0.25f));
                var list = new List<Vector3>(k + 1);
                float bx0 = float.MaxValue, by0 = float.MaxValue, bx1 = float.MinValue, by1 = float.MinValue;
                for (int j = 0; j <= k; j++)
                {
                    float t = j / (float)k;
                    var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
                    // O vão do salto não tem pista: outra peça (reta ou outro salto) pode cruzar por ele. Ficam de fora os
                    // pontos do vão e os da última meia célula de cada rampa: o ponto a 0,5 da ponta já cobre até ela.
                    float u = t * s.def.Length;
                    if (s.def.IsJump && u > PieceCatalog.JumpUp - 0.49f && u < PieceCatalog.JumpUp + PieceCatalog.JumpGap + 0.49f) continue;
                    list.Add(new Vector3(q.pos.x, q.pos.y, s.ZAt(t)));
                    bx0 = Mathf.Min(bx0, q.pos.x); by0 = Mathf.Min(by0, q.pos.y);
                    bx1 = Mathf.Max(bx1, q.pos.x); by1 = Mathf.Max(by1, q.pos.y);
                }
                pts[i] = list;
                boxes[i] = Rect.MinMaxRect(bx0, by0, bx1, by1);
                x0 = Mathf.Min(x0, bx0); y0 = Mathf.Min(y0, by0); x1 = Mathf.Max(x1, bx1); y1 = Mathf.Max(y1, by1);
            }
            r.bounds = Rect.MinMaxRect(x0 - 0.5f, y0 - 0.5f, x1 + 0.5f, y1 + 0.5f);

            // Duas peças não vizinhas se sobrepõem se passam a menos de 0,92 célula uma da outra NA MESMA ALTURA.
            // Retas lado a lado (distância 1,0) são permitidas; uma ponte (nível 1) passa sobre o chão (nível 0).
            const float minSqr = 0.92f * 0.92f;
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                // Vizinhas da lista se encostam; só a ponta da frente e a de trás, quando abertas, podem estar sobrepostas.
                if (j == i + 1 && !(tail > 0 && i == fe - 1 && !r.closed)) continue;
                if ((r.closed || tail > 0) && i == 0 && j == n - 1) continue;    // vizinhas pelo fechamento
                Rect a = boxes[i], b = boxes[j];
                if (a.xMax + 1 < b.xMin || b.xMax + 1 < a.xMin || a.yMax + 1 < b.yMin || b.yMax + 1 < a.yMin) continue;
                if (Touches(pts[i], pts[j], minSqr)) { r.overlaps.Add(i); r.overlaps.Add(j); }
            }
            // Salto só no chão: no alto fica marcado como sobreposição. O loop pode ficar no alto (só não sobe nem desce).
            for (int i = 0; i < n; i++) if (segs[i].def.IsJump && segs[i].High) r.overlaps.Add(i);

            // Cada carrinho troca de lado a cada cruzamento (na pista de 4: 1↔4 e 2↔3). Com um número ímpar deles,
            // cada um alterna com o trilho do espelho a cada volta: na média das duas voltas, os dois correm a mesma distância.
            // Comprimentos em células; na pista de 4, multiplique por Scale para ter o do mundo.
            int L = lanes.Length;
            var offs = (float[])lanes.Clone();
            int crosses = 0;
            foreach (var s in segs)
            {
                r.centerLength += s.def.CenterLength;
                for (int l = 0; l < L; l++) r.laneLengths[l] += s.def.LaneLength(s.piece.dir, offs[l]);
                if (s.def.IsCross) { crosses++; for (int l = 0; l < L; l++) offs[l] = -offs[l]; }
                if (s.High) r.highCount++;
            }
            if (crosses % 2 == 1)
                for (int l = 0; l < L / 2; l++)
                    r.laneLengths[l] = r.laneLengths[L - 1 - l] = (r.laneLengths[l] + r.laneLengths[L - 1 - l]) / 2f;
            return r;
        }

        // Marca em cada curva inclinada se a vizinha continua a inclinação (aí não há rampa de transição naquela ponta).
        public static void ComputeBanking(List<Segment> segs, bool closed, int tail = 0)
        {
            int n = segs.Count, fe = n - tail;
            for (int i = 0; i < n; i++)
            {
                var s = segs[i];
                // Entre a ponta da frente e a peça de trás mais afastada só há ligação se a pista está fechada.
                bool prevLinked = i == 0 ? closed || tail > 0 : i == fe && tail > 0 ? closed : true;
                bool nextLinked = i == n - 1 ? closed || tail > 0 : i == fe - 1 && tail > 0 ? closed : true;
                var prev = segs[i == 0 ? n - 1 : i - 1];
                var next = segs[i == n - 1 ? 0 : i + 1];
                s.bankIn = prevLinked && SameBank(prev, s);
                s.bankOut = nextLinked && SameBank(next, s);
                // Vizinha plana: a inclinação dela continua pela peça plana (Spill), então não precisa de rampa de 20%.
                // O loop não recebe a inclinação (a volta é a volta): a inclinada faz a transição dentro dela, como contra a oposta.
                s.flatIn = prevLinked && !prev.def.Banked && !prev.def.IsLoop;
                s.flatOut = nextLinked && !next.def.Banked && !next.def.IsLoop;
                s.bankDirIn = prevLinked && prev.def.Banked ? prev.piece.dir : 0;
                s.bankDirOut = nextLinked && next.def.Banked ? next.piece.dir : 0;
                int rise = s.zOut - s.zIn;
                s.rampIn = rise != 0 && prevLinked && prev.zOut - prev.zIn == rise;
                s.rampOut = rise != 0 && nextLinked && next.zOut - next.zIn == rise;
            }
        }

        static bool SameBank(Segment a, Segment b) => a.def.Banked && b.def.Banked && a.piece.dir == b.piece.dir;

        // Quanto da inclinação de uma peça inclinada vizinha chega numa peça plana (1 na emenda, 0 a SpillLength dentro).
        // dirSide é o dir da peça inclinada (+1 curva à direita: o lado de fora é o esquerdo). Peça inclinada: 0.
        // Vale para qualquer peça plana, inclusive reta, curva, cruzamento, rampa e ponte (no alto).
        public const float SpillLength = 1.5f;          // células: reta e curva no mesmo sentido
        public const float OppositeSpillLength = 2f;    // células: curva plana que vira para o lado oposto (S), sem vinco
        public static float Spill(Segment s, float t, int dirSide)
        {
            if (s.def.Banked) return 0f;
            // Curva para o lado oposto da inclinada: a transição é mais longa, senão a superfície dobra num vinco.
            float len = s.def.CenterLength;
            float LenIn = s.def.IsCurve && s.piece.dir == -dirSide ? OppositeSpillLength : SpillLength;
            // A transição tem que acabar dentro da peça, senão sobra um degrau no outro encaixe (reta curtinha).
            // Com inclinadas de sentidos opostos nas duas pontas, cada uma fica com metade: a pista passa plana no meio.
            LenIn = Mathf.Min(LenIn, s.bankDirIn == -s.bankDirOut ? len / 2f : len);
            float wi = s.bankDirIn == dirSide ? Ease(t * len, LenIn) : 0f;
            float wo = s.bankDirOut == dirSide ? Ease((1f - t) * len, LenIn) : 0f;
            return Mathf.Max(wi, wo);
        }

        // Suavização (smoothstep invertido): 1 na emenda, 0 no fim do trecho, com inclinação zero nas duas pontas.
        static float Ease(float dist, float length)
        {
            float p = Mathf.Clamp01(dist / length);
            return 1f - p * p * (3f - 2f * p);
        }

        // Altura (y do mundo) da superfície num ponto da peça: nível da peça + inclinação da curva.
        // A borda de dentro fica na altura da peça e a de fora sobe (na curva à direita, dir +1, o lado de fora é o esquerdo).
        // A inclinação é inteira na curva; na vizinha plana ela desce com Spill (0,6 célula); contra outra inclinada
        // de sentido oposto, entra e sai em 20% da peça.
        public static float SurfaceY(Segment s, float t, float lateral)
        {
            if (s.def.IsLoop)
            {
                TrackMath.LoopAt(t, out _, out _, out float phi);
                return s.zIn * TrackMath.LevelHeight + PieceCatalog.LoopRadius * (1f - Mathf.Cos(phi));
            }
            float y = s.ZSmooth(t) * TrackMath.LevelHeight;
            if (s.def.IsJump) y += PieceCatalog.JumpY(t * s.def.Length);
            float tan = Mathf.Tan(PieceCatalog.BankAngle * Mathf.Deg2Rad);
            if (!s.def.Banked)
            {
                // Peça no alto também recebe: a inclinação segue o mesmo caminho (a rampa só muda o nível).
                float wp = Spill(s, t, 1), wm = Spill(s, t, -1);
                if (wp <= 0f && wm <= 0f) return y;
                int dir = wp >= wm ? 1 : -1;
                return y + (0.5f - dir * lateral) * tan * Mathf.Max(wp, wm);
            }
            // Emenda com inclinada de sentido oposto (ou ponta aberta): a inclinação zera na emenda, com suavização de
            // CrossLength célula (sem vinco). Assim o lado de fora passa pela altura da pista plana no meio.
            float w = 1f;
            if (!s.bankIn && !s.flatIn) w = Mathf.Min(w, Cross(t * s.def.CenterLength));
            if (!s.bankOut && !s.flatOut) w = Mathf.Min(w, Cross((1f - t) * s.def.CenterLength));
            return y + (0.5f - s.piece.dir * lateral) * tan * Mathf.Clamp01(w);
        }

        public const float CrossLength = 1.5f;   // células: suavização de inclinada contra inclinada de sentido oposto
        // Subida suave (smoothstep): 0 na emenda, 1 a CrossLength dentro da peça.
        static float Cross(float dist)
        {
            float p = Mathf.Clamp01(dist / CrossLength);
            return p * p * (3f - 2f * p);
        }

        static bool Touches(List<Vector3> a, List<Vector3> b, float minSqr)
        {
            foreach (var p in a)
            foreach (var q in b)
            {
                if (Mathf.Abs(p.z - q.z) >= 0.75f) continue;
                float dx = p.x - q.x, dy = p.y - q.y;
                if (dx * dx + dy * dy < minSqr) return true;
            }
            return false;
        }
    }
}
