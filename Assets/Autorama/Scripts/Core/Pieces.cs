using UnityEngine;

namespace Autorama
{
    public enum PieceKind { Straight, Curve }

    // Uma peça do catálogo. Medidas em células (1 célula = largura da pista com os 2 trilhos).
    public sealed class PieceDef
    {
        public readonly string Id, Name;
        public readonly PieceKind Kind;
        public readonly float Length, Angle, Radius;
        public readonly bool Banked, IsStart, IsCross;

        PieceDef(string id, string name, PieceKind kind, float length, float angle, bool banked, bool isStart, bool isCross = false)
        {
            Id = id; Name = name; Kind = kind; Length = length; Angle = angle; Radius = 2.5f; Banked = banked; IsStart = isStart; IsCross = isCross;
        }

        public static PieceDef MakeStraight(string id, string name, float length, bool isStart = false, bool isCross = false) =>
            new PieceDef(id, name, PieceKind.Straight, length, 0f, false, isStart, isCross);
        public static PieceDef MakeCurve(string id, string name, float angle, bool banked) =>
            new PieceDef(id, name, PieceKind.Curve, 0f, angle, banked, false);

        public bool IsCurve => Kind == PieceKind.Curve;
        public float CenterLength => IsCurve ? Angle * Mathf.Deg2Rad * Radius : Length;

        // Deslocamento do trilho num ponto t da peça. No cruzamento o trilho faz um S de um lado para o outro
        // (offset·cos(π·t)): quem entra pela esquerda sai pela direita. Nas outras peças, fixo.
        public float LaneOffsetAt(float offset, float t) => IsCross ? offset * Mathf.Cos(Mathf.PI * t) : offset;

        // offset > 0 = lado direito. Na curva à direita (dir +1) o centro fica à direita, então o trilho da direita tem raio menor.
        public float LaneLength(int dir, float offset)
        {
            if (IsCross) return CrossLaneLength(Length);
            return IsCurve ? Angle * Mathf.Deg2Rad * (Radius - dir * offset) : Length;
        }

        // Comprimento do S do cruzamento (o mesmo para os dois trilhos).
        static float CrossLaneLength(float length)
        {
            float s = 0f; const int n = 200;
            for (int k = 0; k < n; k++)
            {
                float t = (k + 0.5f) / n, d = TrackMath.LaneOffset * Mathf.PI / length * Mathf.Sin(Mathf.PI * t);
                s += Mathf.Sqrt(1f + d * d) * length / n;
            }
            return s;
        }

        // Quanto a peça aguenta de força lateral, em relação a uma curva plana.
        public float GripMultiplier => Banked ? PieceCatalog.BankGrip : 1f;
    }

    // As peças disponíveis. Os ids são os mesmos do JSON exportado pelo construtor em HTML.
    public static class PieceCatalog
    {
        public const float BankGrip = 1.7f;    // curva inclinada (~15°) aguenta 1,7× a força lateral da plana
        public const float BankAngle = 15f;

        public static readonly PieceDef Start  = PieceDef.MakeStraight("largada", "Base de largada", 2f, isStart: true);
        public static readonly PieceDef Short  = PieceDef.MakeStraight("reta-curta", "Reta curta", 2f);
        public static readonly PieceDef Long   = PieceDef.MakeStraight("reta-comprida", "Reta comprida", 4f);
        public static readonly PieceDef Tiny   = PieceDef.MakeStraight("reta-curtinha", "Reta curtinha", 1f);
        public static readonly PieceDef Cross  = PieceDef.MakeStraight("cruzamento", "Cruzamento", 4f, isCross: true);
        public static readonly PieceDef CrossS = PieceDef.MakeStraight("cruzamento-curto", "Cruzamento curto", 2f, isCross: true);   // S mais fechado
        public static readonly PieceDef Curve  = PieceDef.MakeCurve("curva-longa", "Curva longa", 90f, false);
        public static readonly PieceDef Half   = PieceDef.MakeCurve("meia-curva", "Meia curva", 45f, false);
        public static readonly PieceDef CurveB = PieceDef.MakeCurve("curva-longa-inclinada", "Curva longa inclinada", 90f, true);
        public static readonly PieceDef HalfB  = PieceDef.MakeCurve("curva-curta-inclinada", "Curva curta inclinada", 45f, true);

        public static readonly PieceDef[] All = { Start, Short, Long, Tiny, Cross, CrossS, Curve, Half, CurveB, HalfB };

        public static PieceDef ById(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return null;
        }
    }

    // Uma peça colocada na pista. A posição não é guardada: ela é recalculada a partir da largada.
    [System.Serializable]
    public class TrackPiece
    {
        public string id;
        public int dir;     // 0 nas retas, -1 vira à esquerda, +1 vira à direita
        public int rise;    // -1 desce um nível, 0 plana, +1 sobe um nível

        public TrackPiece(string id, int dir = 0, int rise = 0) { this.id = id; this.dir = dir; this.rise = rise; }

        public PieceDef Def => PieceCatalog.ById(id);
        public TrackPiece Clone() => new TrackPiece(id, dir, rise);

        public string Describe()
        {
            var d = Def;
            string s = d.Name;
            if (dir != 0) s += dir < 0 ? " · esquerda" : " · direita";
            if (rise != 0) s += rise > 0 ? " · sobe" : " · desce";
            return s;
        }
    }
}
