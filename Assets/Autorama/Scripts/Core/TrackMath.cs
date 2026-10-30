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
    }
}
