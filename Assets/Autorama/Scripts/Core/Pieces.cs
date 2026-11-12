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
        // Loop e salto (seção 42 do guia): na planta são retas (Length = quanto a peça avança), sempre no chão.
        public readonly bool IsLoop, IsJump;

        PieceDef(string id, string name, PieceKind kind, float length, float angle, bool banked, bool isStart, bool isCross = false, bool isLoop = false, bool isJump = false)
        {
            Id = id; Name = name; Kind = kind; Length = length; Angle = angle; Radius = 2.5f; Banked = banked; IsStart = isStart; IsCross = isCross;
            IsLoop = isLoop; IsJump = isJump;
        }

        public static PieceDef MakeStraight(string id, string name, float length, bool isStart = false, bool isCross = false) =>
            new PieceDef(id, name, PieceKind.Straight, length, 0f, false, isStart, isCross);
        public static PieceDef MakeCurve(string id, string name, float angle, bool banked) =>
            new PieceDef(id, name, PieceKind.Curve, 0f, angle, banked, false);
        public static PieceDef MakeLoop(string id, string name) =>
            new PieceDef(id, name, PieceKind.Straight, 2f * PieceCatalog.LoopRadius, 0f, false, false, isLoop: true);
        public static PieceDef MakeJump(string id, string name) =>
            new PieceDef(id, name, PieceKind.Straight, PieceCatalog.JumpLength, 0f, false, false, isJump: true);

        public bool IsCurve => Kind == PieceKind.Curve;
        public bool Sided => IsCurve || IsLoop;        // tem lado (dir ±1): a curva vira, o loop sai deslocado para ele
        public bool GroundOnly => IsLoop || IsJump;    // não sobe nem desce de nível
        // Comprimento do caminho: no loop, a entrada, a volta inteira e a saída.
        public float CenterLength => IsCurve ? Angle * Mathf.Deg2Rad * Radius : IsLoop ? Length + 2f * Mathf.PI * PieceCatalog.LoopRadius : Length;

        // Deslocamento do trilho num ponto t da peça. No cruzamento o trilho faz um S de um lado para o outro
        // (offset·cos(π·t)): quem entra pela esquerda sai pela direita. Nas outras peças, fixo.
        public float LaneOffsetAt(float offset, float t) => IsCross ? offset * Mathf.Cos(Mathf.PI * t) : offset;

        // offset > 0 = lado direito. Na curva à direita (dir +1) o centro fica à direita, então o trilho da direita tem raio menor.
        public float LaneLength(int dir, float offset)
        {
            if (IsCross) return CrossLaneLength(Length, Mathf.Abs(offset));
            if (IsLoop) return CenterLength;
            return IsCurve ? Angle * Mathf.Deg2Rad * (Radius - dir * offset) : Length;
        }

        // Comprimento do S do cruzamento: o mesmo para os trilhos simétricos (na pista de 4, o 1 e o 4 fazem um S
        // maior que o 2 e o 3).
        static float CrossLaneLength(float length, float amp)
        {
            float s = 0f; const int n = 200;
            for (int k = 0; k < n; k++)
            {
                float t = (k + 0.5f) / n, d = amp * Mathf.PI / length * Mathf.Sin(Mathf.PI * t);
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

        // Loop: entrada reta de LoopRadius, a volta (raio LoopRadius, em pé) e saída reta de LoopRadius. Durante a
        // volta a pista desliza LoopShift para o lado (dir), para a saída passar ao lado da entrada.
        public const float LoopRadius = 1f, LoopShift = 1f;
        // Salto: rampa de JumpUp células que sobe até JumpLip (parábola, plana no encaixe), vão de JumpGap e uma
        // rampa de pouso que desce de JumpLip até o chão no resto da peça.
        public const float JumpLength = 4f, JumpUp = 1f, JumpGap = 1f, JumpLip = 0.2f;

        // Altura da superfície do salto (células) a u células do começo da peça. No vão não há pista: 0.
        public static float JumpY(float u)
        {
            if (u <= JumpUp) { float p = u / JumpUp; return JumpLip * p * p; }
            float land = JumpLength - JumpUp - JumpGap, q = (JumpLength - u) / land;
            return q > 1f ? 0f : JumpLip * q * q;
        }
        // Inclinação (dy/du) da superfície do salto.
        public static float JumpSlope(float u)
        {
            if (u <= JumpUp) return 2f * JumpLip * u / (JumpUp * JumpUp);
            float land = JumpLength - JumpUp - JumpGap, q = (JumpLength - u) / land;
            return q > 1f ? 0f : -2f * JumpLip * q / land;
        }
        // O trecho que termina em u está no vão? (O ponto u = JumpUp é a ponta da rampa; u = JumpUp + JumpGap, a do pouso.)
        public static bool JumpGapAt(float u) => u > JumpUp + 1e-3f && u <= JumpUp + JumpGap + 1e-3f;

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

        public static readonly PieceDef Loop   = PieceDef.MakeLoop("loop", "Loop");
        public static readonly PieceDef Jump   = PieceDef.MakeJump("salto", "Rampa de salto");

        public static readonly PieceDef[] All = { Start, Short, Long, Tiny, Cross, CrossS, Curve, Half, CurveB, HalfB, Loop, Jump };

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
            if (dir != 0 && d.Sided) s += dir < 0 ? " · esquerda" : " · direita";
            if (rise != 0) s += rise > 0 ? " · sobe" : " · desce";
            return s;
        }
    }
}
