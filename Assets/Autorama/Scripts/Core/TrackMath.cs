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
        public static int Faixas(int f) => f == 4 ? 4 : 2;
        public static float[] LanesOf(int faixas) => faixas == 4 ? Lanes4 : Lanes;
        public static float Scale(int faixas) => faixas == 4 ? 2f : 1f;
        public const int MaxLevel = 2;            // 0 = chão, 1 = ponte, 2 = ponte sobre ponte
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
            if (!d.IsCurve)
                return new Pose2(a.pos + Dir(a.heading) * d.Length * t, a.heading);

            Vector2 center = a.pos + Dir(a.heading - 90f * dir) * d.Radius;
            float h = a.heading - dir * d.Angle * t;
            return new Pose2(center + Dir(h + 90f * dir) * d.Radius, h);
        }

        // Deslocamento lateral: o > 0 = direita de quem pilota.
        public static Vector2 Offset(Pose2 p, float o) => p.pos + Dir(p.heading - 90f) * o;

        public static int Steps(PieceDef d, float degPerStep) => d.IsCurve ? Mathf.Max(1, Mathf.CeilToInt(d.Angle / degPerStep)) : 1;

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

        // Pose de entrada de uma peça que termina em c: a peça percorrida de ré (as curvas viram para o outro lado).
        public static Pose2 EntryFor(Pose2 c, PieceDef d, int dir)
        {
            var e = PoseAt(new Pose2(c.pos, c.heading + 180f), d, -dir, 1f);
            return new Pose2(e.pos, e.heading + 180f);
        }
    }
}
