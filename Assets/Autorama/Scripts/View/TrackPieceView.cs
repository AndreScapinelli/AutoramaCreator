using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // A malha de uma peça: uma faixa ao longo da linha central com a textura da pista
    // (u atravessa a pista da esquerda para a direita, v repete a cada célula).
    // A altura vem do nível da peça (rampas sobem/descem) e da inclinação das curvas inclinadas.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TrackPieceView : MonoBehaviour
    {
        public int index;

        Mesh mesh;
        MeshRenderer overlay;
        readonly List<GameObject> pillars = new List<GameObject>();

        static readonly List<Vector3> V = new List<Vector3>();
        static readonly List<Vector2> UV = new List<Vector2>();
        static readonly List<int> Surface = new List<int>(), Checker = new List<int>(), Guard = new List<int>(), Side = new List<int>();

        // only: desenha tudo com este material (peça fantasma), sem pilares.
        // faixas 4 ou 6: a arte da pista larga (a malha é em células; a raiz da pista tem escala 2 ou 3).
        // all: a pista inteira, para os pilares que caem sobre outra pista virarem pórtico (seção 58 do guia).
        public void Build(Segment s, TrackTheme theme, Material only = null, int faixas = 2, IList<Segment> all = null)
        {
            this.all = all;
            bool wide = faixas >= 4 && theme != null && theme.HasWide(faixas);
            V.Clear(); UV.Clear(); Surface.Clear(); Checker.Clear(); Guard.Clear(); Side.Clear();
            var d = s.def;
            int dir = s.piece.dir;
            // Passo da malha com o mesmo tamanho no mundo nas três pistas: 4° de curva na de 2 (0,175 célula), 2° na de 4
            // e 1,33° na de 6. A peça plana vizinha de inclinada também é fatiada, para a malha seguir a suavização
            // (Spill) em vez de ligar as duas pontas com uma rampa reta.
            float stepK = TrackMath.Scale(faixas);
            // Loop e salto: fatiados no mesmo passo; no salto, em múltiplo de 4 (as pontas do vão caem em fatias exatas).
            int n = d.IsCurve ? TrackMath.Steps(d, 4f / stepK)
                : d.IsJump ? 4 * Mathf.CeilToInt(d.Length * stepK / 0.175f / 4f)
                : d.IsLoop ? Mathf.CeilToInt(d.CenterLength * stepK / 0.175f)
                : d.IsTwist ? Mathf.CeilToInt(d.Length * stepK / 0.1f)        // muro e parafuso (seção 99): a torção pede fatias finas
                : d.IsFunnel ? Mathf.CeilToInt(d.Length * stepK / 0.125f)
                : s.IsRamp || s.bankDirIn != 0 || s.bankDirOut != 0 ?Mathf.CeilToInt(d.Length * stepK / 0.175f) : 1;
            var ts = new float[n + 1];
            for (int j = 0; j <= n; j++) ts[j] = j / (float)n;

            // Cruzamento de retas no chão (seção 63): a peça que vem antes na lista some no quadrado do cruzamento e a
            // outra passa inteira, com as fendas de verdade; as fendas da de baixo ficam pintadas por cima dela.
            cuts.Clear();
            var paint = new List<TrackLayout.FlatX>();
            int me = all != null ? all.IndexOf(s) : -1;
            if (me >= 0 && only == null)
                foreach (var c in TrackLayout.FlatCrossings(all))
                {
                    float len = Vector2.Distance(s.a.pos, s.b.pos);
                    if (me >= c.b0 && me <= c.b1)
                    {
                        float at = Vector2.Dot(c.x - s.a.pos, c.ub);
                        if (at >= 0f && at < len) paint.Add(c);      // só a peça onde cai o ponto pinta as fendas
                        continue;
                    }
                    if (me < c.a0 || me > c.a1) continue;
                    float half = (0.5f + 0.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - c.sin * c.sin))) / c.sin;
                    float d0 = Vector2.Dot(c.x - s.a.pos, c.ua);
                    float a = Mathf.Max(0f, (d0 - half) / len), b = Mathf.Min(1f, (d0 + half) / len);
                    if (b <= a) continue;
                    cuts.Add(new Vector2(a, b));
                    var list = new List<float>(ts) { a, b };
                    list.Sort();
                    ts = list.ToArray();
                }

            // A mesma altura que os carrinhos usam (nível da peça + inclinação das curvas inclinadas).
            float Y(float t, float lateral) => TrackLayout.SurfaceY(s, t, lateral);

            // A textura do cruzamento cobre a peça inteira (v de 0 a 1 nas 4 células); as outras repetem a cada célula.
            // Na pista de 4, a largada também é uma imagem só (boxes numerados e bandeirada pintados nela).
            bool whole = d.IsCross || (wide && d.IsStart);
            float vScale = whole ? 1f / d.Length : 1f;

            // ----- Volume (seção 28 do guia) -----
            // Medidas no mundo; a malha é em células, então na pista de 4 (raiz com escala 2) dividem por 2.
            float k = TrackMath.Scale(faixas);
            float thick = Thickness / k, depth = SlotDepth / k, hw = 0.022f / k;      // meia largura da fenda: 0,022 no mundo, nas três pistas
            Edge At(float o, float dy = 0f) => t => (o, dy, o + 0.5f);

            // Tampo: no cruzamento as fendas se cruzam, então ele fica liso (a fenda é só pintada). Nas outras peças o
            // tampo é cortado em faixas entre as fendas, e cada fenda desce "depth" com duas paredes e um fundo.
            // O u da textura é sempre o deslocamento lateral (o + 0,5): a pintura continua no lugar; paredes e fundo
            // usam o u do meio da fenda, que na textura é preto.
            if (d.IsCross) AddBand(Surface, s, ts, At(-0.5f), At(0.5f), vScale, Y);
            else
            {
                // Zebra em relevo no lado de fora das curvas planas (onde a textura pinta a zebra): sobe em chanfro,
                // fica plana e desce na borda. Nas peças no alto o muro ocupa o lugar.
                float kIn = faixas >= 6 ? 0.5f - 0.28f / faixas : wide ? 0.43f : 0.40f, kOut = faixas >= 6 ? 0.5f - 0.1f / faixas : wide ? 0.475f : 0.48f, kH = 0.022f / k, kB = 0.012f;
                bool kerbL = only == null && d.IsCurve && !d.Banked && !s.High && dir > 0;
                bool kerbR = only == null && d.IsCurve && !d.Banked && !s.High && dir < 0;
                float from = -0.5f;
                if (kerbL)
                {
                    AddBand(Surface, s, ts, At(-0.5f), At(-kOut, kH), vScale, Y);
                    AddBand(Surface, s, ts, At(-kOut, kH), At(-kIn - kB, kH), vScale, Y);
                    AddBand(Surface, s, ts, At(-kIn - kB, kH), At(-kIn), vScale, Y);
                    from = -kIn;
                }
                if (d.IsFunnel) { FunnelSurface(s, ts, faixas, hw, depth, vScale, Y); from = 0.5f; }
                else foreach (float l in TrackMath.LanesOf(faixas))
                {
                    Edge dark(float o, float dy) => t => (o, dy, l + 0.5f);
                    AddBand(Surface, s, ts, At(from), At(l - hw), vScale, Y);
                    AddBand(Surface, s, ts, dark(l - hw, 0f), dark(l - hw, -depth), vScale, Y);          // parede esquerda (olha para a direita)
                    AddBand(Surface, s, ts, dark(l - hw, -depth), dark(l + hw, -depth), vScale, Y);      // fundo
                    AddBand(Surface, s, ts, dark(l + hw, -depth), dark(l + hw, 0f), vScale, Y);          // parede direita
                    from = l + hw;
                }
                if (kerbR)
                {
                    AddBand(Surface, s, ts, At(from), At(kIn), vScale, Y);
                    AddBand(Surface, s, ts, At(kIn), At(kIn + kB, kH), vScale, Y);
                    AddBand(Surface, s, ts, At(kIn + kB, kH), At(kOut, kH), vScale, Y);
                    AddBand(Surface, s, ts, At(kOut, kH), At(0.5f), vScale, Y);
                }
                else AddBand(Surface, s, ts, At(from), At(0.5f), vScale, Y);
            }

            foreach (var c in paint) PaintCross(s, all[c.a0], c.x, faixas, hw, Y(0f, 0f) + 0.002f / k);

            // Muro com corpo: nas peças no alto (dos dois lados) e no lado de fora das curvas inclinadas.
            float wallIn = faixas >= 6 ? 0.5f - 0.08f / faixas : wide ? 0.48f : 0.45f;
            float wallH = (s.High ? (wide ? 0.12f : 0.1f) : 0.14f) / k;
            bool wallL = only == null && (s.High || (d.Banked && dir > 0)), wallR = only == null && (s.High || (d.Banked && dir < 0));
            float gv = wide ? 2f : 1f;
            if (wallL)
            {
                AddBand(Guard, s, ts, At(-0.5f, wallH), At(-wallIn, wallH), gv, Y);                      // topo
                AddBand(Guard, s, ts, At(-wallIn, wallH), At(-wallIn, 0f), gv, Y);                       // face de dentro
            }
            if (wallR)
            {
                AddBand(Guard, s, ts, At(wallIn, wallH), At(0.5f, wallH), gv, Y);
                AddBand(Guard, s, ts, At(wallIn, 0f), At(wallIn, wallH), gv, Y);
            }
            // Vizinha plana de peça inclinada: o muro da lateral de fora cresce com a inclinação e some longe dela.
            // Onde a altura é zero, o muro fica 0,003 abaixo da pista (escondido), para não brigar com a superfície.
            // Peças no alto já têm muro dos dois lados (acima), que acompanha a inclinação sozinho.
            if (only == null && !d.Banked && !s.High)
            {
                foreach (int dirSide in new[] { 1, -1 })     // +1: a curva é para a direita, o muro de fora é o da esquerda
                {
                    if (s.bankDirIn != dirSide && s.bankDirOut != dirSide) continue;
                    float e = -dirSide;                      // lateral do muro: -1 esquerda, +1 direita
                    Edge WallEdge(float o, float h) => t => (o, h * TrackLayout.Spill(s, t, dirSide) - 0.003f, o + 0.5f);
                    AddBand(Guard, s, ts, WallEdge(e * 0.5f, wallH), WallEdge(e * wallIn, wallH), gv, Y);     // topo
                    AddBand(Guard, s, ts, WallEdge(e * wallIn, wallH), WallEdge(e * wallIn, 0f), gv, Y);      // face de dentro
                }
            }
            // Laterais da base (do topo do muro, se houver, até embaixo) e, nas peças no alto, o fundo.
            if (only == null)
            {
                // No salto a base é maciça: as laterais descem da rampa até a mesa. Também na inclinada no chão e na
                // vizinha plana que recebe a inclinação (seção 50.7): a borda de fora sobe até 0,7 célula (35°) e,
                // sem isso, sobrava um vão por onde se via o lado de baixo da pista.
                float ground = s.zIn * TrackMath.LevelHeight;
                bool solid = d.IsJump || (!s.High && (d.Banked || s.bankDirIn != 0 || s.bankDirOut != 0));
                Edge Foot(float o) => t => (o, solid ? ground - Y(t, o) - thick : -thick, o + 0.5f);
                AddBand(Side, s, ts, Foot(-0.5f), At(-0.5f, wallL ? wallH : 0f), 1f, Y);
                AddBand(Side, s, ts, At(0.5f, wallR ? wallH : 0f), Foot(0.5f), 1f, Y);
                if (d.IsJump) { if (s.High) AddBand(Side, s, ts, Foot(0.5f), Foot(-0.5f), 1f, Y); }     // salto no alto: fundo reto
                else if (s.High || d.IsLoop || d.IsTwist) AddBand(Side, s, ts, At(0.5f, -thick), At(-0.5f, -thick), 1f, Y);     // o lado de fora da volta aparece
                AddCaps(s, thick, Y, solid);
            }

            if (d.IsStart && !wide)
            {
                // Bandeirada: 1 célula de largura × 0,25 ao longo, a 1,2 célula do começo.
                float t0 = 1.2f / d.Length, t1 = 1.45f / d.Length;
                AddStrip(Checker, s, new[] { t0, t1 }, -0.5f, 0.5f, 0.004f, 1f / 0.25f, Y);
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "Peça", hideFlags = HideFlags.DontSave };
                GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            mesh.Clear();
            mesh.SetVertices(V);
            mesh.SetUVs(0, UV);
            mesh.subMeshCount = 4;
            mesh.SetTriangles(Surface, 0);
            mesh.SetTriangles(Checker, 1);
            mesh.SetTriangles(Guard, 2);
            mesh.SetTriangles(Side, 3);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var mr = GetComponent<MeshRenderer>();
            var side = theme != null && theme.side != null ? theme.side : theme != null ? theme.support : null;
            mr.sharedMaterials = only != null ? new[] { only, only, only, only }
                : wide ? new[] { theme.Surface(d, dir, faixas), theme.checker, theme.Guard(faixas), theme.side4 != null ? theme.side4 : side }
                : new[] { theme.Surface(d, dir), theme.checker, theme.guard, side };
            mr.shadowCastingMode = only != null ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;

            var col = GetComponent<MeshCollider>();
            if (col) { col.sharedMesh = null; col.sharedMesh = mesh; }

            BuildPillars(s, only == null ? theme : null, wide ? faixas : 2);
            if (only == null && d.IsStart) BuildGantry(s, theme, wide ? faixas : 2, k);
        }

        // Pórtico sobre a bandeirada: dois postes fora da pista, uma travessa quadriculada e o semáforo de três luzes
        // (StartLights acende com a contagem da corrida). Medidas no mundo, divididas pela escala da pista.
        public static readonly List<Renderer> StartLights = new List<Renderer>();

        void BuildGantry(Segment s, TrackTheme theme, int faixas, float k)
        {
            StartLights.Clear();
            float t = RaceSim.LineS / s.def.Length;
            var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
            var rot = Quaternion.Euler(0f, -q.heading, 0f);
            float baseY = TrackLayout.SurfaceY(s, t, 0f);
            float h = 0.95f / k, post = 0.07f / k, beamH = 0.16f / k, half = 0.5f + 0.09f / k;
            var steel = theme.Support(faixas);
            foreach (float side in new[] { -half, half })
            {
                var o = TrackMath.Offset(q, side);
                pillars.Add(Box("Poste da largada", steel, new Vector3(o.x, baseY + (h - Thickness / k) / 2f, o.y), new Vector3(post, h + Thickness / k, post), rot));
            }
            // Travessa: comprimento atravessando a pista (eixo Z local da peça, depois da rotação do rumo).
            pillars.Add(Box("Travessa da largada", theme.checker, new Vector3(q.pos.x, baseY + h - beamH / 2f, q.pos.y), new Vector3(post * 0.8f, beamH, half * 2f + post), rot));
            // Semáforo: três luzes penduradas na frente da travessa, viradas para quem larga.
            var back = TrackMath.Dir(q.heading) * -(post * 0.8f);
            for (int i = -1; i <= 1; i++)
            {
                var o = TrackMath.Offset(q, i * 0.16f) + back;
                var go = Box("Luz da largada", steel, new Vector3(o.x, baseY + h - beamH - 0.05f / k, o.y), Vector3.one * (0.09f / k), rot);
                // Cada luz tem o próprio material, para acender sem mexer nas outras.
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = new Material(steel) { hideFlags = HideFlags.DontSave };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                StartLights.Add(r);
                pillars.Add(go);
            }
            SetStartLights(0, false);
        }

        // red: quantas luzes vermelhas acesas (0 a 3); green: todas verdes (largada).
        public static void SetStartLights(int red, bool green)
        {
            for (int i = 0; i < StartLights.Count; i++)
            {
                var r = StartLights[i];
                if (!r) continue;
                Color c = green ? new Color(0.17f, 0.76f, 0.4f) : i < red ? new Color(0.88f, 0.22f, 0.17f) : new Color(0.16f, 0.18f, 0.18f);
                var m = r.sharedMaterial;
                m.SetColor("_BaseColor", c);
                bool on = green || i < red;
                if (on) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", on ? c * 2.2f : Color.black);
            }
        }

        // Destaque por cima da peça: seleção (amarelo) ou sobreposição (vermelho).
        public void SetHighlight(Material m)
        {
            if (m == null) { if (overlay) overlay.enabled = false; return; }
            if (!overlay)
            {
                var go = new GameObject("Destaque") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                go.AddComponent<MeshFilter>();
                overlay = go.AddComponent<MeshRenderer>();
                overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            overlay.GetComponent<MeshFilter>().sharedMesh = mesh;
            overlay.sharedMaterials = new[] { m, m, m, m };
            overlay.enabled = true;
        }

        // Um pilar por peça, no meio dela (t = 0,5) e no eixo da pista. Pista de 2: redondo. Pista de 4 ou mais:
        // quadrado de aço escuro, com uma travessa embaixo do tabuleiro. Sobre outra pista, vira pórtico.
        const float PillarT = 0.5f;

        void BuildPillars(Segment s, TrackTheme theme, int faixas = 2)
        {
            foreach (var p in pillars)
            {
                // As luzes do pórtico têm material próprio: vai junto.
                if (p != null && p.name == "Luz da largada") Kill(p.GetComponent<MeshRenderer>().sharedMaterial);
                Kill(p);
            }
            pillars.Clear();
            portals.Clear();
            if (theme == null || !s.High) return;
            if (faixas >= 4) { BuildPillars4(s, theme, faixas); return; }
            // Até a superfície no eixo, que na curva inclinada (e na vizinha que recebe a inclinação) fica acima do
            // nível: metade da subida da borda de fora (seção 50.7). Loop e salto: até a base da peça.
            float t = PillarT, d = 0.16f;
            float h = s.def.IsLoop || s.def.IsJump ? s.ZSmooth(t) * TrackMath.LevelHeight : TrackLayout.SurfaceY(s, t, 0f);
            if (s.ZAt(t) < 0.25f) return;
            var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
            if (Portal(s, q, new[] { 0f }, h - Thickness, theme.support, 1f, d / 2f)) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Pilar";
            go.hideFlags = HideFlags.DontSave;
            Kill(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            // Do tampo da mesa (que fica Thickness abaixo do nível 0) até a pista.
            go.transform.localPosition = new Vector3(q.pos.x, (h - Thickness) / 2f, q.pos.y);
            go.transform.localScale = new Vector3(d, (h + Thickness) / 2f, d);
            go.GetComponent<MeshRenderer>().sharedMaterial = theme.support;
            pillars.Add(go);
        }

        // Pista de 4 e de 6: a malha é em células (raiz com escala 2 ou 3), então espessura e espaçamento dos pilares
        // dividem por "k / 2" para ficarem do mesmo tamanho no mundo nas duas.
        void BuildPillars4(Segment s, TrackTheme theme, int faixas)
        {
            var mat = theme.Support(faixas);
            float k = TrackMath.Scale(faixas), thin = 0.09f * 2f / k, t = PillarT;
            if (s.ZAt(t) < 0.25f) return;
            var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
            var rot = Quaternion.Euler(0f, -q.heading, 0f);
            if (!s.def.IsLoop && !s.def.IsJump && Portal(s, q, new[] { 0f }, TrackLayout.SurfaceY(s, t, 0f) - 0.06f, mat, k, thin / 2f)) return;
            // No loop o pilar vai até a base da volta, não até a volta; no salto, até a base maciça.
            float top = (s.def.IsLoop || s.def.IsJump ? s.zIn * TrackMath.LevelHeight : TrackLayout.SurfaceY(s, t, 0f)) - 0.06f;
            float foot = Thickness / k;      // a mesa fica Thickness (mundo) abaixo do nível 0
            pillars.Add(Box("Pilar", mat, new Vector3(q.pos.x, (top - foot) / 2f, q.pos.y), new Vector3(thin, top + foot, thin), rot));
            // Travessa logo abaixo da pista, de borda a borda, seguindo a inclinação da curva inclinada.
            var ea = TrackMath.Offset(q, -0.475f); var eb = TrackMath.Offset(q, 0.475f);
            var pa = new Vector3(ea.x, TrackLayout.SurfaceY(s, t, -0.475f) - 0.035f, ea.y);
            var pb = new Vector3(eb.x, TrackLayout.SurfaceY(s, t, 0.475f) - 0.035f, eb.y);
            pillars.Add(Box("Travessa", mat, (pa + pb) / 2f, new Vector3(0.06f * 2f / k, 0.05f * 2f / k, Vector3.Distance(pa, pb)), Quaternion.LookRotation(pb - pa)));
        }

        // Pista sobre pista (seção 58 do guia): se algum pilar da fileira q cairia sobre uma peça mais baixa, a fileira
        // vira um pórtico: dois postes do lado de fora da pista de baixo e uma viga atravessando por cima dela, logo
        // abaixo da de cima (beamTop). Pórticos a menos de 0,9 célula um do outro na mesma peça viram um só.
        // Sem vão para a viga (as pistas quase se encostam), a fileira fica sem pilar. Devolve se tratou a fileira.
        IList<Segment> all;
        readonly List<Vector2> portals = new List<Vector2>();

        bool Portal(Segment s, Pose2 q, float[] sides, float beamTop, Material mat, float k, float postHalf)
        {
            if (all == null) return false;
            float reach = 0.5f + 0.05f / k + postHalf;      // pilar a menos disso do meio da pista de baixo bate nela
            Segment low = null;
            float tL = 0f;
            foreach (float side in sides)
            {
                var o = TrackMath.Offset(q, side);
                foreach (var l in all)
                    if (l != s && Under(l, o, reach, beamTop - 0.02f, out tL)) { low = l; break; }
                if (low != null) break;
            }
            if (low == null) return false;
            if (Under(low, q.pos, reach, beamTop - 0.02f, out float tC)) tL = tC;     // o pórtico sob o meio da fileira, se der
            var qL = TrackMath.PoseAt(low.a, low.def, low.piece.dir, tL);
            foreach (var p in portals) if (Vector2.Distance(p, qL.pos) < 0.9f) return true;
            portals.Add(qL.pos);

            float beamH = 0.06f / k, foot = Thickness / k;
            float lowTop = Mathf.Max(TrackLayout.SurfaceY(low, tL, -0.5f), TrackLayout.SurfaceY(low, tL, 0.5f)) + 0.14f / k;
            // A viga é reta e a pista de cima pode ser rampa ou inclinada: o topo fica abaixo da pista de cima em todo o
            // comprimento (9 pontos), com a mesma folga que tem no meio da fileira.
            if (Closest(s, q.pos, out float tS, out _))
            {
                float gap = TrackLayout.SurfaceY(s, tS, 0f) - beamTop;
                for (int j = -4; j <= 4; j++)
                {
                    var pt = TrackMath.Offset(qL, j / 4f * (reach + postHalf));
                    foreach (var l in all)
                        if (l != low && Closest(l, pt, out float tl, out float lat) && Mathf.Abs(lat) <= 0.5f)
                        {
                            float y = TrackLayout.SurfaceY(l, tl, lat);
                            if (y > lowTop) beamTop = Mathf.Min(beamTop, y - gap);
                        }
                }
            }
            if (beamTop - beamH < lowTop + 0.02f / k) return true;
            var rot = Quaternion.Euler(0f, -qL.heading, 0f);
            foreach (float sd in new[] { -reach, reach })
            {
                var o = TrackMath.Offset(qL, sd);
                var pos = new Vector3(o.x, (beamTop - foot) / 2f, o.y);
                if (k > 1f) { pillars.Add(Box("Poste do pórtico", mat, pos, new Vector3(postHalf * 2f, beamTop + foot, postHalf * 2f), rot)); continue; }
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Poste do pórtico";
                go.hideFlags = HideFlags.DontSave;
                Kill(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.transform.localPosition = pos;
                go.transform.localScale = new Vector3(postHalf * 2f, (beamTop + foot) / 2f, postHalf * 2f);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                pillars.Add(go);
            }
            var c = qL.pos;
            pillars.Add(Box("Viga do pórtico", mat, new Vector3(c.x, beamTop - beamH / 2f, c.y), new Vector3(beamH * 1.2f, beamH, reach * 2f + postHalf * 2f), rot));
            return true;
        }

        // A parte da peça l embaixo de p: o ponto mais perto de p entre os que ficam a até "reach" de lado e com a
        // superfície abaixo de maxY. Não é só o ponto mais perto da peça: na hélice (curva em C sobre curva em C) o mais
        // perto é a emenda, no alto, e a volta de baixo passa ao lado.
        static bool Under(Segment l, Vector2 p, float reach, float maxY, out float t)
        {
            t = 0f;
            if (l.def.IsLoop || Vector2.Distance(p, l.a.pos) > l.def.CenterLength + 1.5f) return false;
            int n = Mathf.CeilToInt(l.def.CenterLength / 0.1f);
            float best = float.MaxValue;
            for (int j = 0; j <= n; j++)
            {
                float tj = j / (float)n;
                var pj = TrackMath.PoseAt(l.a, l.def, l.piece.dir, tj);
                var v = p - pj.pos;
                float along = Vector2.Dot(v, TrackMath.Dir(pj.heading)), lat = Vector2.Dot(v, TrackMath.Dir(pj.heading - 90f));
                if (Mathf.Abs(along) > 0.08f || Mathf.Abs(lat) > reach || v.sqrMagnitude >= best) continue;
                if (TrackLayout.SurfaceY(l, tj, Mathf.Clamp(lat, -0.5f, 0.5f)) >= maxY) continue;
                best = v.sqrMagnitude; t = tj;
            }
            return best < float.MaxValue;
        }

        // Ponto da peça l mais perto de p (amostras a cada 0,1 célula): t e o deslocamento lateral. Falso se p fica
        // antes ou depois das pontas, ou se l é um loop (o pilar de quem passa por cima do loop segue normal).
        static bool Closest(Segment l, Vector2 p, out float t, out float lat)
        {
            t = 0f; lat = 0f;
            if (l.def.IsLoop || Vector2.Distance(p, l.a.pos) > l.def.CenterLength + 1.5f) return false;
            int n = Mathf.CeilToInt(l.def.CenterLength / 0.1f);
            float best = float.MaxValue, along = 0f;
            for (int j = 0; j <= n; j++)
            {
                float tj = j / (float)n;
                var pj = TrackMath.PoseAt(l.a, l.def, l.piece.dir, tj);
                var v = p - pj.pos;
                if (v.sqrMagnitude >= best) continue;
                best = v.sqrMagnitude; t = tj;
                along = Vector2.Dot(v, TrackMath.Dir(pj.heading));
                lat = Vector2.Dot(v, TrackMath.Dir(pj.heading - 90f));
            }
            return (t > 0f && t < 1f) || Mathf.Abs(along) < 0.06f;      // no meio, a projeção numa curva estica com o raio
        }

        GameObject Box(string name, Material mat, Vector3 pos, Vector3 size, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            Kill(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // Medidas do volume, no mundo: espessura da base e fundura da fenda.
        public const float Thickness = 0.09f, SlotDepth = 0.035f;

        // Uma borda de faixa num ponto t da peça: deslocamento lateral, altura sobre a superfície e u da textura.
        delegate (float o, float dy, float u) Edge(float t);

        // Faixa entre duas bordas ao longo da peça. Indo de a para b "para a direita", a face olha para cima;
        // indo para baixo, olha para a direita; para cima, para a esquerda; para a esquerda, para baixo.
        // Cada faixa tem os próprios vértices: as quinas ficam vivas (low-poly).
        static readonly List<Vector2> cuts = new List<Vector2>();     // trechos (em t) sem malha: os cruzamentos com outras retas
        static bool InCut(float t) { foreach (var c in cuts) if (t > c.x && t < c.y) return true; return false; }

        // Fendas da reta de baixo pintadas sobre a de cima (s), no quadrado do cruzamento: faixas escuras finas.
        static void PaintCross(Segment s, Segment o, Vector2 x, int faixas, float hw, float y)
        {
            Vector2 ub = (o.b.pos - o.a.pos).normalized, nb = TrackMath.Dir(o.a.heading - 90f), nt = TrackMath.Dir(s.a.heading - 90f);
            Vector2 side = new Vector2(-ub.y, ub.x) * hw;
            float du = Vector2.Dot(ub, nt), dn = Vector2.Dot(nb, nt);
            float u = TrackMath.LanesOf(faixas)[0] + 0.5f;    // o u do meio da fenda: preto na textura
            foreach (float l in TrackMath.LanesOf(faixas))
            {
                float s0 = (-0.5f - l * dn) / du, s1 = (0.5f - l * dn) / du;
                Vector2 p0 = x + nb * l + ub * s0, p1 = x + nb * l + ub * s1;
                int v = V.Count;
                foreach (var p in new[] { p0 - side, p0 + side, p1 - side, p1 + side })
                {
                    V.Add(new Vector3(p.x, y, p.y));
                    UV.Add(new Vector2(u, 0f));
                }
                Surface.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3, v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            }
        }

        // Fechada (seção 99.4): as fendas andam de lado e cada par vira uma. O tampo entre elas segue junto, e o u da
        // textura é refeito por trechos (lateral de agora → a do trilho na reta), para a pintura das fendas acompanhar.
        static void FunnelSurface(Segment s, float[] ts, int faixas, float hw, float depth, float vScale, System.Func<float, float, float> y)
        {
            var lanes = TrackMath.LanesOf(faixas);
            var d = s.def;
            float Lo(int i, float t) => d.FunnelOffset(lanes[i], t, faixas);
            float U(float o, float t)
            {
                float a0 = -0.5f, b0 = -0.5f;
                for (int i = 0; i <= lanes.Length; i++)
                {
                    float a1 = i < lanes.Length ? Lo(i, t) : 0.5f, b1 = i < lanes.Length ? lanes[i] : 0.5f;
                    if (o <= a1 + 1e-5f) return (a1 - a0 < 1e-4f ? b1 : b0 + (b1 - b0) * (o - a0) / (a1 - a0)) + 0.5f;
                    a0 = a1; b0 = b1;
                }
                return 1f;
            }
            Edge E(System.Func<float, float> o, float dy = 0f) => t => (o(t), dy, U(o(t), t));
            System.Func<float, float> from = t => -0.5f;
            for (int i = 0; i < lanes.Length; i++)
            {
                int li = i;
                float uDark = lanes[i] + 0.5f;
                System.Func<float, float> l0 = t => Lo(li, t) - hw, l1 = t => Lo(li, t) + hw;
                Edge dark(System.Func<float, float> o, float dy) => t => (o(t), dy, uDark);
                AddBand(Surface, s, ts, E(from), E(l0), vScale, y);
                AddBand(Surface, s, ts, dark(l0, 0f), dark(l0, -depth), vScale, y);
                AddBand(Surface, s, ts, dark(l0, -depth), dark(l1, -depth), vScale, y);
                AddBand(Surface, s, ts, dark(l1, -depth), dark(l1, 0f), vScale, y);
                from = l1;
            }
            AddBand(Surface, s, ts, E(from), E(t => 0.5f), vScale, y);
        }

        static void AddBand(List<int> tri, Segment s, float[] ts, Edge a, Edge b, float vScale, System.Func<float, float, float> y)
        {
            int start = V.Count;
            float dist = 0f;
            Vector3 prev = default;
            for (int i = 0; i < ts.Length; i++)
            {
                var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, ts[i]);
                // A textura anda com a distância na planta; no loop (que volta sobre si mesmo), com a do caminho.
                var mid = s.def.IsLoop ? Point(s, q, ts[i], 0f, 0f, y) : new Vector3(q.pos.x, 0f, q.pos.y);
                if (i > 0) dist += Vector3.Distance(mid, prev);
                prev = mid;
                var ea = a(ts[i]); var eb = b(ts[i]);
                V.Add(Point(s, q, ts[i], ea.o, ea.dy, y));
                V.Add(Point(s, q, ts[i], eb.o, eb.dy, y));
                UV.Add(new Vector2(ea.u, dist * vScale));
                UV.Add(new Vector2(eb.u, dist * vScale));
                float tm = i > 0 ? (ts[i - 1] + ts[i]) / 2f : 0f;
                if (i > 0 && !(s.def.IsJump && s.def.JumpGapAt(ts[i] * s.def.Length)) && !InCut(tm))     // no vão do salto não há pista; no cruzamento, a outra reta
                {
                    int k = start + i * 2;
                    tri.Add(k - 2); tri.Add(k); tri.Add(k - 1);
                    tri.Add(k - 1); tri.Add(k); tri.Add(k + 1);
                }
            }
        }

        // Ponto da malha: deslocamento lateral o e altura dy sobre a superfície. No loop, "sobre a superfície" é na
        // direção do centro da volta (a pista fica de lado e de cabeça para baixo), não para cima.
        static Vector3 Point(Segment s, Pose2 q, float t, float o, float dy, System.Func<float, float, float> y)
        {
            if (s.def.IsTwist)
            {
                // Muro e parafuso (seção 99): o ponto (o, dy) gira com a pista em volta do eixo da peça.
                s.def.TwistPoint(s.piece.dir, t, o, dy, out float lat, out float h, out _, s.wallIn, s.wallOut);
                Vector2 w = TrackMath.Offset(q, lat);
                return new Vector3(w.x, y(t, 0f) + h, w.y);
            }
            Vector2 p = TrackMath.Offset(q, o);
            if (!s.def.IsLoop) return new Vector3(p.x, y(t, o) + dy, p.y);
            TrackMath.LoopAt(t, out _, out _, out float phi);
            p -= TrackMath.Dir(q.heading) * (Mathf.Sin(phi) * dy);
            return new Vector3(p.x, y(t, o) + Mathf.Cos(phi) * dy, p.y);
        }

        // Tampas nas duas pontas da peça (o corte da base), para a ponta aberta da pista não ficar oca.
        // No salto, também nas duas pontas do vão, descendo até a mesa.
        static void AddCaps(Segment s, float thick, System.Func<float, float, float> y, bool solid)
        {
            float L = s.def.Length, ground = s.zIn * TrackMath.LevelHeight;
            var ends = s.def.IsJump
                ? new[] { (0f, true), (PieceCatalog.JumpUp / L, false), ((PieceCatalog.JumpUp + s.def.Gap) / L, true), (1f, false) }
                : new[] { (0f, true), (1f, false) };
            foreach (var (t, front) in ends)
            {
                var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
                Vector2 l = TrackMath.Offset(q, -0.5f), r = TrackMath.Offset(q, 0.5f);
                float yl = y(t, -0.5f), yr = y(t, 0.5f);
                float bl = solid ? ground - thick : yl - thick, br = solid ? ground - thick : yr - thick;
                int k = V.Count;
                V.Add(new Vector3(l.x, yl, l.y)); V.Add(new Vector3(r.x, yr, r.y));
                V.Add(new Vector3(l.x, bl, l.y)); V.Add(new Vector3(r.x, br, r.y));
                for (int i = 0; i < 4; i++) UV.Add(new Vector2(i % 2, i / 2));
                if (front) { Side.Add(k); Side.Add(k + 1); Side.Add(k + 2); Side.Add(k + 2); Side.Add(k + 1); Side.Add(k + 3); }
                else { Side.Add(k); Side.Add(k + 2); Side.Add(k + 1); Side.Add(k + 1); Side.Add(k + 2); Side.Add(k + 3); }
            }
        }

        // Faixa entre os deslocamentos o1 (esquerda) e o2 (direita) nos pontos ts da peça.
        // Triângulos no sentido horário visto de cima: a face fica virada para a câmera.
        static void AddStrip(List<int> tri, Segment s, float[] ts, float o1, float o2, float yOff, float vScale, System.Func<float, float, float> y)
        {
            int start = V.Count;
            float dist = 0f;
            Vector2 prev = default;
            for (int i = 0; i < ts.Length; i++)
            {
                var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, ts[i]);
                if (i > 0) dist += Vector2.Distance(q.pos, prev);
                prev = q.pos;
                Vector2 l = TrackMath.Offset(q, o1), r = TrackMath.Offset(q, o2);
                V.Add(new Vector3(l.x, y(ts[i], o1) + yOff, l.y));
                V.Add(new Vector3(r.x, y(ts[i], o2) + yOff, r.y));
                UV.Add(new Vector2(0f, dist * vScale));
                UV.Add(new Vector2(1f, dist * vScale));
                if (i > 0)
                {
                    int k = start + i * 2;
                    tri.Add(k - 2); tri.Add(k); tri.Add(k - 1);
                    tri.Add(k - 1); tri.Add(k); tri.Add(k + 1);
                }
            }
        }

        // Fora do Play (pista desenhada no Editor), destruir tem que ser imediato.
        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void OnDestroy() => Kill(mesh);
    }
}
