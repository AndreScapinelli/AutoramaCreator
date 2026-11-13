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
        // Salto: vão (células) e altura da ponta da rampa (seção 42.4). Fora do salto, 0.
        public readonly float Gap, Lip;

        PieceDef(string id, string name, PieceKind kind, float length, float angle, bool banked, bool isStart, bool isCross = false, bool isLoop = false, bool isJump = false, float gap = 0f, float lip = 0f)
        {
            Gap = gap; Lip = lip;
            Id = id; Name = name; Kind = kind; Length = length; Angle = angle; Radius = 2.5f; Banked = banked; IsStart = isStart; IsCross = isCross;
            IsLoop = isLoop; IsJump = isJump;
        }

        public static PieceDef MakeStraight(string id, string name, float length, bool isStart = false, bool isCross = false) =>
            new PieceDef(id, name, PieceKind.Straight, length, 0f, false, isStart, isCross);
        public static PieceDef MakeCurve(string id, string name, float angle, bool banked) =>
            new PieceDef(id, name, PieceKind.Curve, 0f, angle, banked, false);
        public static PieceDef MakeLoop(string id, string name) =>
            new PieceDef(id, name, PieceKind.Straight, 2f * PieceCatalog.LoopRadius, 0f, false, false, isLoop: true);
        public static PieceDef MakeJump(string id, string name, float gap, float lip) =>
            new PieceDef(id, name, PieceKind.Straight, PieceCatalog.JumpUp + gap + PieceCatalog.JumpLand, 0f, false, false, isJump: true, gap: gap, lip: lip);

        // Altura da superfície do salto (células) a u células do começo da peça. No vão não há pista: 0.
        public float JumpY(float u)
        {
            if (u <= PieceCatalog.JumpUp) { float p = u / PieceCatalog.JumpUp; return Lip * p * p; }
            float q = (Length - u) / PieceCatalog.JumpLand;
            return q > 1f ? 0f : Lip * q * q;
        }
        // Inclinação (dy/du) da superfície do salto.
        public float JumpSlope(float u)
        {
            const float up = PieceCatalog.JumpUp, land = PieceCatalog.JumpLand;
            if (u <= up) return 2f * Lip * u / (up * up);
            float q = (Length - u) / land;
            return q > 1f ? 0f : -2f * Lip * q / land;
        }
        // O trecho que termina em u está no vão? (O ponto u = JumpUp é a ponta da rampa; u = JumpUp + Gap, a do pouso.)
        public bool JumpGapAt(float u) => u > PieceCatalog.JumpUp + 1e-3f && u <= PieceCatalog.JumpUp + Gap + 1e-3f;
        // u no vão, com meia célula de folga para cada lado (a sobreposição deixa outra peça cruzar por ali).
        public bool JumpOpenAt(float u) => IsJump && u > PieceCatalog.JumpUp - 0.49f && u < PieceCatalog.JumpUp + Gap + 0.49f;

        // Reta na diagonal (rumo a 45° de um eixo): fica √2 vezes mais comprida, para avançar um número inteiro de
        // células em x e em y e a pista não sair da grade (seção 49 do guia). Loop, salto e largada não mudam.
        PieceDef diag;
        public PieceDef At(float heading)
        {
            if (IsCurve || IsLoop || IsJump || IsStart) return this;
            if (Mathf.Abs(Mathf.Repeat(heading, 90f) - 45f) > 1f) return this;
            return diag ??= new PieceDef(Id, Name, Kind, Length * Mathf.Sqrt(2f), 0f, false, false, IsCross);
        }

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
        // Salto: rampa de JumpUp células que sobe até Lip (parábola, plana no encaixe), vão de 1 a MaxGap células
        // (a peça cresce junto: 4, 5 ou 6 células) e rampa de pouso de JumpLand que desce de Lip até o nível da peça.
        public const float JumpUp = 1f, JumpLand = 2f;
        public const int MaxGap = 3;
        public static readonly float[] JumpLips = { 0.2f, 0.3f, 0.4f };     // altura da ponta por vão (1, 2, 3)

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
        public static readonly PieceDef Jump   = PieceDef.MakeJump("salto", "Rampa de salto", 1f, JumpLips[0]);
        static readonly PieceDef[] jumps = { Jump, PieceDef.MakeJump("salto", "Rampa de salto", 2f, JumpLips[1]), PieceDef.MakeJump("salto", "Rampa de salto", 3f, JumpLips[2]) };
        public static PieceDef JumpOf(int vao) => jumps[Mathf.Clamp(vao, 1, MaxGap) - 1];

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
        public int vao = 1; // só no salto: o vão em células (1 a PieceCatalog.MaxGap)

        public TrackPiece(string id, int dir = 0, int rise = 0, int vao = 1) { this.id = id; this.dir = dir; this.rise = rise; this.vao = vao; }

        public PieceDef Def => id == PieceCatalog.Jump.Id ? PieceCatalog.JumpOf(vao) : PieceCatalog.ById(id);
        public TrackPiece Clone() => new TrackPiece(id, dir, rise, vao);

        public string Describe()
        {
            var d = Def;
            string s = d.Name;
            if (dir != 0 && d.Sided) s += dir < 0 ? " · esquerda" : " · direita";
            if (rise != 0) s += rise > 0 ? " · sobe" : " · desce";
            if (d.IsJump) s += $" · vão {vao}";
            return s;
        }
    }
}
