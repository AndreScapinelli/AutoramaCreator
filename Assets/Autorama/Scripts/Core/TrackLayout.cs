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

        public float ZAt(float t) => zIn + (zOut - zIn) * t;
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
        public readonly float[] laneLengths = new float[2];
        public Rect bounds;          // em células, plano da mesa (x, z)
        public int highCount;
    }

    public static class TrackLayout
    {
        // Cada peça herda a pose e a altura de onde a anterior terminou; rampas sobem ou descem um nível.
        public static List<Segment> Build(Pose2 origin, IList<TrackPiece> pieces)
        {
            var list = new List<Segment>(pieces.Count);
            var conn = origin;
            int z = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                var d = p.Def;
                var b = TrackMath.PoseAt(conn, d, p.dir, 1f);
                b.heading = TrackMath.Normalize(b.heading);
                int zOut = Mathf.Clamp(z + (i == 0 ? 0 : p.rise), 0, TrackMath.MaxLevel);
                list.Add(new Segment { index = i, piece = p, def = d, a = conn, b = b, zIn = z, zOut = zOut });
                conn = b;
                z = zOut;
            }
            return list;
        }

        public static TrackAnalysis Analyze(Pose2 origin, List<Segment> segs)
        {
            var r = new TrackAnalysis();
            var last = segs[segs.Count - 1];
            r.gap = Vector2.Distance(last.b.pos, origin.pos);
            float dh = Mathf.Abs(Mathf.DeltaAngle(last.b.heading, origin.heading));
            r.closedXY = segs.Count > 1 && r.gap < 0.02f && dh < 0.5f;
            r.endZ = last.zOut;
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
            for (int j = i + 2; j < n; j++)
            {
                if (r.closed && i == 0 && j == n - 1) continue;    // vizinhas pelo fechamento
                Rect a = boxes[i], b = boxes[j];
                if (a.xMax + 1 < b.xMin || b.xMax + 1 < a.xMin || a.yMax + 1 < b.yMin || b.yMax + 1 < a.yMin) continue;
                if (Touches(pts[i], pts[j], minSqr)) { r.overlaps.Add(i); r.overlaps.Add(j); }
            }

            // Cada carrinho troca de lado a cada cruzamento. Com um número ímpar deles, cada um alterna de trilho
            // a cada volta: na média das duas voltas, os dois correm a mesma distância.
            float[] offs = { TrackMath.Lanes[0], TrackMath.Lanes[1] };
            int crosses = 0;
            foreach (var s in segs)
            {
                r.centerLength += s.def.CenterLength;
                for (int l = 0; l < 2; l++) r.laneLengths[l] += s.def.LaneLength(s.piece.dir, offs[l]);
                if (s.def.IsCross) { crosses++; offs[0] = -offs[0]; offs[1] = -offs[1]; }
                if (s.High) r.highCount++;
            }
            if (crosses % 2 == 1) r.laneLengths[0] = r.laneLengths[1] = (r.laneLengths[0] + r.laneLengths[1]) / 2f;
            return r;
        }

        // Marca em cada curva inclinada se a vizinha continua a inclinação (aí não há rampa de transição naquela ponta).
        public static void ComputeBanking(List<Segment> segs, bool closed)
        {
            int n = segs.Count;
            for (int i = 0; i < n; i++)
            {
                var s = segs[i];
                s.bankIn = i > 0 ? SameBank(segs[i - 1], s) : closed && SameBank(segs[n - 1], s);
                s.bankOut = i < n - 1 ? SameBank(segs[i + 1], s) : closed && SameBank(segs[0], s);
            }
        }

        static bool SameBank(Segment a, Segment b) => a.def.Banked && b.def.Banked && a.piece.dir == b.piece.dir;

        // Altura (y do mundo) da superfície num ponto da peça: nível da peça + inclinação da curva.
        // A borda de dentro fica na altura da peça e a de fora sobe (na curva à direita, dir +1, o lado de fora é o esquerdo).
        // Inclinação entra e sai em 20% da peça, a não ser que a vizinha também seja inclinada.
        public static float SurfaceY(Segment s, float t, float lateral)
        {
            float y = s.ZAt(t) * TrackMath.LevelHeight;
            if (!s.def.Banked) return y;
            float w = 1f;
            if (!s.bankIn) w = Mathf.Min(w, t / 0.2f);
            if (!s.bankOut) w = Mathf.Min(w, (1f - t) / 0.2f);
            return y + (0.5f - s.piece.dir * lateral) * Mathf.Tan(PieceCatalog.BankAngle * Mathf.Deg2Rad) * Mathf.Clamp01(w);
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
