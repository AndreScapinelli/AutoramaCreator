using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Posição no plano da mesa + direção.
    [System.Serializable]
    public struct Pose2
    {
        public Vector2 pos;     // (x, z) no plano da mesa
        public float heading;   // graus: 0 = +X, cresce no sentido anti-horário visto de cima (rumo a +Z)

        public Pose2(Vector2 pos, float heading) { this.pos = pos; this.heading = heading; }
        public Vector3 World(float y = 0f) => new Vector3(pos.x, y, pos.y);
        public Quaternion Rotation => Quaternion.Euler(0f, -heading, 0f);
    }

    public static class TrackMath
    {
        public const float TrackWidth = 1f;
        public const float LaneOffset = 0.25f;
        public static readonly float[] Lanes = { -LaneOffset, LaneOffset };   // trilho 1 (esquerda), trilho 2 (direita)

        // Pista de 4 carros (seção 25 do guia): a geometria em células é a mesma, mas 1 célula vale 2 no mundo
        // (Scale) e a largura tem 4 trilhos, a cada 0,25 célula. No mundo: trilhos a ±0,25 e ±0,75 e raio 5 no centro.
        public static readonly float[] Lanes4 = { -0.375f, -0.125f, 0.125f, 0.375f };
        // Pista de 6 carros (seção 31 do guia): célula = 3 no mundo, trilhos a ±0,25, ±0,75 e ±1,25 no mundo
        // (em células: ±1/12, ±1/4 e ±5/12). O espaço entre trilhos é sempre 0,5 no mundo, nos três tipos de pista.
        public static readonly float[] Lanes6 = { -5f / 12f, -0.25f, -1f / 12f, 1f / 12f, 0.25f, 5f / 12f };
        // Pista de 8 carros (seção 43 do guia): célula = 4 no mundo, trilhos a ±0,25, ±0,75, ±1,25 e ±1,75 no mundo.
        public static readonly float[] Lanes8 = { -7f / 16f, -5f / 16f, -3f / 16f, -1f / 16f, 1f / 16f, 3f / 16f, 5f / 16f, 7f / 16f };
        public static int Faixas(int f) => f == 8 ? 8 : f == 6 ? 6 : f == 4 ? 4 : 2;
        public static float[] LanesOf(int faixas) => faixas == 8 ? Lanes8 : faixas == 6 ? Lanes6 : faixas == 4 ? Lanes4 : Lanes;
        public static float Scale(int faixas) => faixas == 8 ? 4f : faixas == 6 ? 3f : faixas == 4 ? 2f : 1f;
        public const int MaxLevel = 8;            // 0 = chão, 1 = ponte, 2 = ponte sobre ponte... até 8 andares
        public const float LevelHeight = 0.75f;   // 1 nível = 3/4 de célula: vão para o carrinho e sombra visível de cima

        public static Vector2 Dir(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        public static float Normalize(float h) => Mathf.Repeat(h, 360f);

        // Pose a uma fração t (0..1) da peça, partindo da pose de entrada a.
        // dir +1 vira à direita: o ângulo diminui.
        public static Pose2 PoseAt(Pose2 a, PieceDef d, int dir, float t)
        {
            if (d.IsLoop)
            {
                LoopAt(t, out float fwd, out float lat, out _);
                return new Pose2(a.pos + Dir(a.heading) * fwd + Dir(a.heading - 90f * dir) * lat, a.heading);
            }
            if (!d.IsCurve)
                return new Pose2(a.pos + Dir(a.heading) * d.Length * t, a.heading);

            if (d.Lead > 0f)
            {
                // Reta de ajuste antes (ou depois) do arco.
                float t0 = d.ArcFrom, t1 = d.ArcTo;
                if (t < t0) return new Pose2(a.pos + Dir(a.heading) * d.CenterLength * t, a.heading);
                if (t0 > 0f) { a = new Pose2(a.pos + Dir(a.heading) * d.Lead, a.heading); }
                float u = Mathf.Min((t - t0) / (t1 - t0), 1f);
                var e = Arc(a, d, dir, u);
                if (t > t1) e.pos += Dir(e.heading) * d.CenterLength * (t - t1);
                return e;
            }
            return Arc(a, d, dir, t);
        }

        static Pose2 Arc(Pose2 a, PieceDef d, int dir, float t)
        {
            Vector2 center = a.pos + Dir(a.heading - 90f * dir) * d.Radius;
            float h = a.heading - dir * d.Angle * t;
            return new Pose2(center + Dir(h + 90f * dir) * d.Radius, h);
        }

        // Ponto da grade mais perto de p para quem vai no rumo (eixo): no meio da fileira e na divisa das células.
        public static Vector2 GridSnap(Vector2 p, float heading)
        {
            bool horizontal = Mathf.Abs(Mathf.DeltaAngle(heading, 0f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(heading, 180f)) < 1f;
            return horizontal ? new Vector2(Mathf.Round(p.x), Mathf.Round(p.y - 0.5f) + 0.5f)
                              : new Vector2(Mathf.Round(p.x - 0.5f) + 0.5f, Mathf.Round(p.y));
        }

        // A peça como ela fica a partir da pose a (seção 51 do guia): reta na diagonal fica √2 maior (At) e a meia-curva
        // que sai da diagonal para um eixo fora da grade (depois de um S) ganha uma reta de ajuste na diagonal e outro
        // raio, para a ponta cair na grade.
        public const float FitRadiusMin = 1.5f, FitRadiusMax = 3.5f;
        public static PieceDef Fit(PieceDef d, Pose2 a, int dir)
        {
            d = d.At(a.heading);
            if (!d.IsCurve || !Mathf.Approximately(d.Angle, 45f) || Mathf.Abs(Mathf.Repeat(a.heading, 90f) - 45f) > 1f) return d;
            var e = Arc(a, d, dir, 1f);
            var t0 = GridSnap(e.pos, e.heading);
            if ((t0 - e.pos).sqrMagnitude < 1e-6f) return d;
            Vector2 g = Dir(a.heading), d1 = Dir(a.heading - 90f * dir) + Dir(e.heading + 90f * dir);
            Vector2 f = Dir(e.heading), l = Dir(e.heading - 90f);
            float det = g.x * d1.y - g.y * d1.x, best = float.MaxValue, bR = 0f, bL = 0f;
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                var r = t0 + f * i + l * j - a.pos;
                float lead = (r.x * d1.y - r.y * d1.x) / det, rad = (g.x * r.y - g.y * r.x) / det;
                if (lead < -1e-4f || rad < FitRadiusMin || rad > FitRadiusMax) continue;
                float cost = Mathf.Abs(rad - d.Radius) + 0.25f * lead;
                if (cost < best) { best = cost; bR = rad; bL = Mathf.Max(0f, lead); }
            }
            return best == float.MaxValue ? d : d.Fitted(bR, bL);
        }

        // Peça de trás (de ré a partir da saída c): a mesma conta, na pose invertida.
        public static PieceDef FitBack(PieceDef d, Pose2 c, int dir) =>
            Fit(d, new Pose2(c.pos, c.heading + 180f), -dir).Reversed;

        // Loop a uma fração t do caminho (entrada reta, volta, saída reta): quanto avançou na planta (fwd), quanto
        // deslizou para o lado (lat, de 0 a LoopShift, sem quina nas pontas) e o ângulo da volta (phi, 0 a 2π).
        // A altura é LoopRadius · (1 − cos phi); a normal da pista aponta para o centro da volta.
        public static void LoopAt(float t, out float fwd, out float lat, out float phi)
        {
            float R = PieceCatalog.LoopRadius, turn = 2f * Mathf.PI * R, u = Mathf.Clamp01(t) * (2f * R + turn);
            if (u <= R) { fwd = u; lat = 0f; phi = 0f; }
            else if (u >= R + turn) { fwd = u - turn; lat = PieceCatalog.LoopShift; phi = 2f * Mathf.PI; }
            else
            {
                phi = (u - R) / R;
                fwd = R + R * Mathf.Sin(phi);
                lat = PieceCatalog.LoopShift * (phi - Mathf.Sin(phi)) / (2f * Mathf.PI);
            }
        }

        // Deslocamento lateral: o > 0 = direita de quem pilota.
        public static Vector2 Offset(Pose2 p, float o) => p.pos + Dir(p.heading - 90f) * o;

        public static int Steps(PieceDef d, float degPerStep) =>
            d.IsCurve ? Mathf.Max(1, Mathf.CeilToInt(d.Angle / degPerStep * (1f + d.Lead / Mathf.Max(0.1f, d.CenterLength - d.Lead)))) : d.IsLoop ? Mathf.CeilToInt(360f / degPerStep) + 2 : 1;

        public static List<Pose2> Sample(Pose2 a, PieceDef d, int dir, float degPerStep = 4f)
        {
            int n = Steps(d, degPerStep);
            var list = new List<Pose2>(n + 1);
            for (int k = 0; k <= n; k++) list.Add(PoseAt(a, d, dir, k / (float)n));
            return list;
        }

        // Encaixa a largada na grade: a pista sempre ocupa uma fileira inteira de células.
        public static Pose2 SnapStart(Pose2 o)
        {
            float h = Normalize(Mathf.Round(o.heading / 90f) * 90f);
            bool horizontal = Mathf.Approximately(Mathf.Repeat(h, 180f), 0f);
            return horizontal
                ? new Pose2(new Vector2(Mathf.Round(o.pos.x), Mathf.Floor(o.pos.y) + 0.5f), h)
                : new Pose2(new Vector2(Mathf.Floor(o.pos.x) + 0.5f, Mathf.Round(o.pos.y)), h);
        }

        // Pose de entrada de uma peça que termina em c: a peça percorrida de ré (as curvas viram para o outro lado;
        // o loop, que só desliza de lado, continua no mesmo).
        public static Pose2 EntryFor(Pose2 c, PieceDef d, int dir)
        {
            var e = PoseAt(new Pose2(c.pos, c.heading + 180f), d.Reversed, d.IsLoop ? dir : -dir, 1f);
            return new Pose2(e.pos, e.heading + 180f);
        }
    }
}
