using UnityEngine;

namespace Autorama
{
    public enum PieceKind { Straight, Curve }

    // Uma peça do catálogo. Medidas em células (1 célula = largura da pista com os 2 trilhos).
    public sealed class PieceDef
    {
        public readonly string Id;
        readonly string nome;
        public string Name => L.T(nome);
        public readonly PieceKind Kind;
        public readonly float Length, Angle, Radius;
        public readonly bool Banked, IsStart, IsCross;
        // Loop e salto (seção 42 do guia): na planta são retas (Length = quanto a peça avança), sempre no chão.
        public readonly bool IsLoop, IsJump;
        // Salto: vão (células) e altura da ponta da rampa (seção 42.4). Fora do salto, 0.
        public readonly float Gap, Lip;
        // Meia curva em C e curva em C (seções 50 e 99): a família de raio n·0,5, com a aderência da C.
        public readonly bool IsC;
        // Peças com torção (seção 99): 1 muro de parede (deita 90° de lado), 2 parafuso (gira 360°). Na planta são retas.
        public readonly int Twist;
        // Fechada (seção 99): cada par de trilhos vizinhos se junta num só no meio da peça e abre de novo.
        public readonly bool IsFunnel;
        // Meia-curva ajustada (seção 51 do guia): reta de Lead células na diagonal antes do arco (ou depois, com
        // LeadLast, quando a peça é percorrida de ré). Nas peças do catálogo, 0.
        public float Lead { get; private set; }
        public bool LeadLast { get; private set; }
        PieceDef reversed;
        // A mesma peça percorrida ao contrário (troca a ponta da reta de ajuste).
        public PieceDef Reversed => Lead <= 0f ? this : reversed;

        // Cópia com outro raio e reta de ajuste antes do arco; Reversed tem a reta depois.
        public PieceDef Fitted(float radius, float lead)
        {
            var f = new PieceDef(Id, nome, Kind, 0f, Angle, Banked, false, radius: radius, isC: IsC) { Lead = lead };
            var r = new PieceDef(Id, nome, Kind, 0f, Angle, Banked, false, radius: radius, isC: IsC) { Lead = lead, LeadLast = true };
            f.reversed = r; r.reversed = f;
            return f;
        }
        // Fração do caminho onde fica o arco (fora dele, a reta de ajuste).
        public float ArcFrom => Lead <= 0f || LeadLast ? 0f : Lead / CenterLength;
        public float ArcTo => Lead <= 0f || !LeadLast ? 1f : 1f - Lead / CenterLength;
        public bool OnArc(float t) => IsCurve && t >= ArcFrom - 1e-4f && t <= ArcTo + 1e-4f;

        PieceDef(string id, string name, PieceKind kind, float length, float angle, bool banked, bool isStart, bool isCross = false, bool isLoop = false, bool isJump = false, float gap = 0f, float lip = 0f, float radius = 2.5f, bool isC = false, int twist = 0, bool funnel = false)
        {
            Gap = gap; Lip = lip; IsC = isC; Twist = twist; IsFunnel = funnel;
            Id = id; nome = name; Kind = kind; Length = length; Angle = angle; Radius = radius; Banked = banked; IsStart = isStart; IsCross = isCross;
            IsLoop = isLoop; IsJump = isJump;
        }

        public static PieceDef MakeStraight(string id, string name, float length, bool isStart = false, bool isCross = false) =>
            new PieceDef(id, name, PieceKind.Straight, length, 0f, false, isStart, isCross);
        public static PieceDef MakeCurve(string id, string name, float angle, bool banked, float radius = 2.5f, bool isC = false) =>
            new PieceDef(id, name, PieceKind.Curve, 0f, angle, banked, false, radius: radius, isC: isC);
        public static PieceDef MakeTwist(string id, string name, float length, int twist) =>
            new PieceDef(id, name, PieceKind.Straight, length, 0f, false, false, twist: twist);
        public static PieceDef MakeFunnel(string id, string name, float length) =>
            new PieceDef(id, name, PieceKind.Straight, length, 0f, false, false, funnel: true);
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
            return diag ??= new PieceDef(Id, nome, Kind, Length * Mathf.Sqrt(2f), 0f, false, false, IsCross, twist: Twist, funnel: IsFunnel);
        }

        public bool IsCurve => Kind == PieceKind.Curve;
        public bool IsHairpin => IsC;                    // curva em C e meia curva em C, plana ou inclinada (seções 50 e 99)
        public bool IsTwist => Twist > 0;
        public bool Sided => IsCurve || IsLoop || IsTwist;   // tem lado (dir ±1): a curva vira, o loop sai deslocado, o muro e o parafuso deitam para ele
        public bool GroundOnly => IsLoop || IsJump || IsTwist || IsFunnel;    // não sobe nem desce de nível
        public bool NoSpill => IsLoop || IsTwist;      // a inclinação da curva vizinha não entra nela

        // ----- Torção (seção 99) -----
        // Rolamento da pista (graus, sem sinal) a u células do começo. Muro: deita 90° em MuroIn células, fica em pé e
        // volta em MuroIn. Parafuso: plano ScrewFlat em cada ponta e uma volta inteira no meio. Suave nas emendas.
        // holdIn/holdOut (muro): a vizinha é outro muro para o mesmo lado, então a pista fica em pé naquela ponta.
        public float RollAt(float u, bool holdIn = false, bool holdOut = false)
        {
            if (Twist == 1) return 90f * Smoother(Mathf.Min(holdIn ? float.MaxValue : u, holdOut ? float.MaxValue : Length - u) / (PieceCatalog.MuroIn * Length / 6f));
            if (Twist == 2) return 360f * Smooth((u - PieceCatalog.ScrewFlat * Length / 8f) / (Length * 0.75f));
            return 0f;
        }
        static float Smooth(float p) { p = Mathf.Clamp01(p); return p * p * (3f - 2f * p); }
        // Muro: começa e termina sem tranco também na curvatura (a emenda com a peça plana fica suave).
        static float Smoother(float p) { p = Mathf.Clamp01(p); return p * p * p * (p * (6f * p - 15f) + 10f); }

        // Ponto da pista torcida: lateral o (células, > 0 à direita) e dy acima da superfície, girados pelo rolamento.
        // dir +1: o lado direito sobe (o muro fica à direita e a pista olha para a esquerda). Devolve a lateral na planta,
        // a altura acima do nível da peça e o rolamento com sinal (o "bank" do carrinho).
        // Muro: gira em volta da borda que fica embaixo e desliza meia célula, para ficar em pé no meio da fileira.
        // Parafuso: gira em volta do eixo meia célula acima da pista (de cabeça para baixo, a pista fica a 1 célula).
        const float ScrewBase = 0.1f;   // espessura da base em células, com folga (TrackPieceView.Thickness 0,09 / escala)
        public void TwistPoint(int dir, float t, float o, float dy, out float lat, out float h, out float roll, bool holdIn = false, bool holdOut = false)
        {
            roll = (dir < 0 ? -1f : 1f) * RollAt(t * Length, holdIn, holdOut);
            float r = roll * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
            if (Twist == 1)
            {
                float pe = roll >= 0f ? -0.5f : 0.5f;          // a borda que fica embaixo
                float x = o - pe;
                lat = pe + x * c - dy * sn - pe * Mathf.Abs(sn);
                h = x * sn + dy * c;
                return;
            }
            // O canto da pista fica a √0,5 do eixo: o eixo sobe o bastante para a borda de baixo (com a base, ScrewBase
            // células) nunca entrar na mesa. Nas pontas (rolamento 0) fica nos 0,5 de sempre.
            float yy = dy - 0.5f;
            float axis = Mathf.Max(0.5f, 0.5f * Mathf.Abs(sn) + 0.5f * Mathf.Max(c, 0f)) + ScrewBase * Mathf.Abs(sn);
            lat = o * c - yy * sn;
            h = axis + o * sn + yy * c;
        }

        // ----- Fechada (seção 99) -----
        // Deslocamento do trilho na fechada: cada par (1 e 2, 3 e 4…) vai para o meio do par em FunnelIn células, fica
        // junto e abre de novo. Na pista de 2, os dois trilhos viram um só no meio.
        public float FunnelOffset(float offset, float t, int faixas)
        {
            int F = TrackMath.Faixas(faixas);
            int i = Mathf.Clamp(Mathf.RoundToInt((offset + 0.5f) * F - 0.5f), 0, F - 1);
            float center = (2 * (i / 2) + 1f) / F - 0.5f;
            float u = t * Length, w = Smooth(Mathf.Min(u, Length - u) / (PieceCatalog.FunnelIn * Length / 4f));
            return offset + (center - offset) * w;
        }
        // Comprimento do caminho: no loop, a entrada, a volta inteira e a saída.
        public float CenterLength => IsCurve ? Lead + Angle * Mathf.Deg2Rad * Radius : IsLoop ? Length + 2f * Mathf.PI * PieceCatalog.LoopRadius : Length;

        // Deslocamento do trilho num ponto t da peça. No cruzamento o trilho faz um S de um lado para o outro
        // (offset·cos(π·t)): quem entra pela esquerda sai pela direita. Nas outras peças, fixo.
        public float LaneOffsetAt(float offset, float t, int faixas = 2) => IsCross ? offset * Mathf.Cos(Mathf.PI * t) : IsFunnel ? FunnelOffset(offset, t, faixas) : offset;

        // offset > 0 = lado direito. Na curva à direita (dir +1) o centro fica à direita, então o trilho da direita tem raio menor.
        public float LaneLength(int dir, float offset)
        {
            if (IsCross) return CrossLaneLength(Length, Mathf.Abs(offset));
            if (IsLoop) return CenterLength;
            return IsCurve ? Lead + Angle * Mathf.Deg2Rad * (Radius - dir * offset) : Length;
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
        public float GripMultiplier => Banked && IsHairpin ? PieceCatalog.HairpinBankGrip : Banked ? PieceCatalog.BankGrip : IsHairpin ? PieceCatalog.HairpinGrip : 1f;
    }

    // As peças disponíveis. Os ids são os mesmos do JSON exportado pelo construtor em HTML.
    public static class PieceCatalog
    {
        public const float HairpinGrip = 1.35f; // curva em C (seção 50.5): aguenta 1,35× a plana, para o trilho de dentro não pedir parar
        public const float BankGrip = 1.7f;    // curva inclinada (~15°) aguenta 1,7× a força lateral da plana
        public const float BankAngle = 15f;     // inclinação das pistas antigas (sem "inclinacao" no JSON)
        // Inclinação escolhida em cada curva inclinada (seção 50.7): só visual, a aderência é BankGrip.
        public static readonly int[] BankAngles = { 15, 25, 35 };
        public const int NewBank = 25;          // a curva inclinada nova entra com esta
        public const float HairpinBankGrip = HairpinGrip * BankGrip;   // C inclinada (seção 50.6): as duas juntas, ~2,3×

        // Loop: entrada reta de LoopRadius, a volta (raio LoopRadius, em pé) e saída reta de LoopRadius. Durante a
        // volta a pista desliza LoopShift para o lado (dir), para a saída passar ao lado da entrada.
        public const float LoopRadius = 1f, LoopShift = 1f;
        // Salto: rampa de JumpUp células que sobe até Lip (parábola, plana no encaixe), vão de 1 a MaxGap células
        // (a peça cresce junto: 4, 5 ou 6 células) e rampa de pouso de JumpLand que desce de Lip até o nível da peça.
        public const float JumpUp = 1f, JumpLand = 2f;
        public const int MaxGap = 3;
        public static readonly float[] JumpLips = { 0.2f, 0.3f, 0.4f };     // altura da ponta por vão (1, 2, 3)

        public static readonly PieceDef Start  = PieceDef.MakeStraight("largada", L.N("Base de largada"), 2f, isStart: true);
        public static readonly PieceDef Short  = PieceDef.MakeStraight("reta-curta", L.N("Reta curta"), 2f);
        public static readonly PieceDef Long   = PieceDef.MakeStraight("reta-comprida", L.N("Reta comprida"), 4f);
        public static readonly PieceDef Tiny   = PieceDef.MakeStraight("reta-curtinha", L.N("Reta curtinha"), 1f);
        public static readonly PieceDef Cross  = PieceDef.MakeStraight("cruzamento", L.N("Cruzamento"), 4f, isCross: true);
        public static readonly PieceDef CrossS = PieceDef.MakeStraight("cruzamento-curto", L.N("Cruzamento curto"), 2f, isCross: true);   // S mais fechado
        public static readonly PieceDef Curve  = PieceDef.MakeCurve("curva-longa", L.N("Curva longa"), 90f, false);
        public static readonly PieceDef Half   = PieceDef.MakeCurve("meia-curva", L.N("Meia curva"), 45f, false);
        public static readonly PieceDef CurveB = PieceDef.MakeCurve("curva-longa-inclinada", L.N("Curva longa inclinada"), 90f, true);
        public static readonly PieceDef HalfB  = PieceDef.MakeCurve("curva-curta-inclinada", L.N("Curva curta inclinada"), 45f, true);
        // Curva em C (seção 50 do guia): 180° de raio 0,5 no eixo; a volta sai na fileira do lado, ocupa 1 × 2 células.
        public const float HairpinRadius = 0.5f;
        // Tamanho da C (seção 50.4): raio n·0,5, a saída fica n fileiras ao lado (2R inteiro: a ponta cai na grade).
        public const int MaxHairpin = 3;
        static PieceDef[] Family(string id, string name, float angle, bool banked)
        {
            var a = new PieceDef[MaxHairpin];
            for (int i = 0; i < MaxHairpin; i++) a[i] = PieceDef.MakeCurve(id, name, angle, banked, (i + 1) * HairpinRadius, true);
            return a;
        }
        static readonly PieceDef[] hairpins = Family("curva-c", L.N("Curva em C"), 180f, false);
        public static readonly PieceDef Hairpin = hairpins[0];
        public static PieceDef HairpinOf(int raio) => hairpins[Mathf.Clamp(raio, 1, MaxHairpin) - 1];
        // C inclinada (seção 50.6): a mesma geometria, com a inclinação das curvas inclinadas.
        static readonly PieceDef[] hairpinsB = Family("curva-c-inclinada", L.N("Curva em C inclinada"), 180f, true);
        public static readonly PieceDef HairpinB = hairpinsB[0];
        public static PieceDef HairpinBOf(int raio) => hairpinsB[Mathf.Clamp(raio, 1, MaxHairpin) - 1];
        // Meia curva em C (seção 99.1): 90° da família da C, raio n·0,5 (a saída fica n/2 célula para o lado e para a
        // frente: cai na grade), plana e inclinada, com a aderência da C.
        static readonly PieceDef[] halfCs = Family("meia-curva-c", L.N("Meia curva em C"), 90f, false);
        static readonly PieceDef[] halfCsB = Family("meia-curva-c-inclinada", L.N("Meia curva em C inclinada"), 90f, true);
        public static readonly PieceDef HalfC = halfCs[0], HalfCB = halfCsB[0];
        public static PieceDef HalfCOf(int raio, bool banked) => (banked ? halfCsB : halfCs)[Mathf.Clamp(raio, 1, MaxHairpin) - 1];

        // Muro de parede (99.2), parafuso (99.3) e fechada (99.4). Medidas em células, na peça no eixo.
        public const float MuroIn = 2.5f, ScrewFlat = 1f, FunnelIn = 1.25f;
        public static readonly PieceDef Muro = PieceDef.MakeTwist("muro", L.N("Muro de parede"), 6f, 1);
        public static readonly PieceDef Screw = PieceDef.MakeTwist("parafuso", L.N("Parafuso"), 8f, 2);
        public static readonly PieceDef Funnel = PieceDef.MakeFunnel("fechada", L.N("Fechada"), 4f);

        public static readonly PieceDef Loop   = PieceDef.MakeLoop("loop", L.N("Loop"));
        public static readonly PieceDef Jump   = PieceDef.MakeJump("salto", L.N("Rampa de salto"), 1f, JumpLips[0]);
        static readonly PieceDef[] jumps = { Jump, PieceDef.MakeJump("salto", L.N("Rampa de salto"), 2f, JumpLips[1]), PieceDef.MakeJump("salto", L.N("Rampa de salto"), 3f, JumpLips[2]) };
        public static PieceDef JumpOf(int vao) => jumps[Mathf.Clamp(vao, 1, MaxGap) - 1];

        public static readonly PieceDef[] All = { Start, Short, Long, Tiny, Cross, CrossS, Curve, Half, CurveB, HalfB, Hairpin, HairpinB, Loop, Jump, HalfC, HalfCB, Muro, Screw, Funnel };

        // A peça de um id com o vão (salto) e o raio (família da C).
        public static PieceDef Resolve(string id, int vao, int raio)
        {
            int r = Mathf.Clamp(raio, 1, MaxHairpin) - 1;
            return id == Jump.Id ? JumpOf(vao) : id == Hairpin.Id ? hairpins[r] : id == HairpinB.Id ? hairpinsB[r]
                 : id == HalfC.Id ? halfCs[r] : id == HalfCB.Id ? halfCsB[r] : ById(id);
        }

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
        public int raio = 1; // só na curva em C: raio raio·0,5, saída raio fileiras ao lado (1 a PieceCatalog.MaxHairpin)
        public int incl;     // só na curva inclinada: graus (0 = PieceCatalog.BankAngle, o das pistas antigas)
        public float BankDeg => incl > 0 ? incl : PieceCatalog.BankAngle;
        public float BankTan => Def != null && Def.Banked ? Mathf.Tan(BankDeg * Mathf.Deg2Rad) : 0f;

        public TrackPiece(string id, int dir = 0, int rise = 0, int vao = 1, int raio = 1) { this.id = id; this.dir = dir; this.rise = rise; this.vao = vao; this.raio = raio; }

        public PieceDef Def => PieceCatalog.Resolve(id, vao, raio);
        public TrackPiece Clone() => new TrackPiece(id, dir, rise, vao, raio) { incl = incl };

        public string Describe()
        {
            var d = Def;
            string s = d.Name;
            if (dir != 0 && d.Sided) s += dir < 0 ? L.T(" · esquerda") : L.T(" · direita");
            if (rise != 0) s += rise > 0 ? L.T(" · sobe") : L.T(" · desce");
            if (d.IsJump) s += L.F(" · vão {0}", vao);
            if (d.IsHairpin && raio > 1) s += L.F(" · {0} fileiras", raio);
            return s;
        }
    }
}
