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
        public void Build(Segment s, TrackTheme theme, Material only = null, int faixas = 2)
        {
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
                : s.IsRamp || s.bankDirIn != 0 || s.bankDirOut != 0 ?Mathf.CeilToInt(d.Length * stepK / 0.175f) : 1;
            var ts = new float[n + 1];
            for (int j = 0; j <= n; j++) ts[j] = j / (float)n;

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
                foreach (float l in TrackMath.LanesOf(faixas))
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
                // No salto a base é maciça: as laterais descem da rampa até a mesa.
                float ground = s.zIn * TrackMath.LevelHeight;
                Edge Foot(float o) => t => (o, d.IsJump ? ground - Y(t, o) - thick : -thick, o + 0.5f);
                AddBand(Side, s, ts, Foot(-0.5f), At(-0.5f, wallL ? wallH : 0f), 1f, Y);
                AddBand(Side, s, ts, At(0.5f, wallR ? wallH : 0f), Foot(0.5f), 1f, Y);
                if (s.High || d.IsLoop) AddBand(Side, s, ts, At(0.5f, -thick), At(-0.5f, -thick), 1f, Y);     // o lado de fora da volta aparece
                AddCaps(s, thick, Y);
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

        // Pista de 2: dois pilares redondos por fileira. Pista de 4: três pilares quadrados e finos de aço escuro
        // (bordas e meio), com uma travessa embaixo do tabuleiro.
        void BuildPillars(Segment s, TrackTheme theme, int faixas = 2)
        {
            foreach (var p in pillars)
            {
                // As luzes do pórtico têm material próprio: vai junto.
                if (p != null && p.name == "Luz da largada") Kill(p.GetComponent<MeshRenderer>().sharedMaterial);
                Kill(p);
            }
            pillars.Clear();
            if (theme == null || !s.High) return;
            if (faixas >= 4) { BuildPillars4(s, theme, faixas); return; }
            int count = Mathf.Max(1, Mathf.RoundToInt(s.def.CenterLength / 1.25f));
            for (int j = 0; j <= count; j++)
            {
                float t = j / (float)count, h = s.ZSmooth(t) * TrackMath.LevelHeight;
                if (s.ZAt(t) < 0.25f) continue;
                var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
                foreach (float side in new[] { -0.4f, 0.4f })
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.name = "Pilar";
                    go.hideFlags = HideFlags.DontSave;
                    Kill(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    var o = TrackMath.Offset(q, side);
                    // Do tampo da mesa (que fica Thickness abaixo do nível 0) até a pista.
                    go.transform.localPosition = new Vector3(o.x, (h - Thickness) / 2f, o.y);
                    go.transform.localScale = new Vector3(0.1f, (h + Thickness) / 2f, 0.1f);
                    go.GetComponent<MeshRenderer>().sharedMaterial = theme.support;
                    pillars.Add(go);
                }
            }
        }

        // Pista de 4 e de 6: a malha é em células (raiz com escala 2 ou 3), então espessura e espaçamento dos pilares
        // dividem por "k / 2" para ficarem do mesmo tamanho no mundo nas duas.
        void BuildPillars4(Segment s, TrackTheme theme, int faixas)
        {
            var mat = theme.Support(faixas);
            float k = TrackMath.Scale(faixas), thin = 0.05f * 2f / k;
            int count = Mathf.Max(1, Mathf.RoundToInt(s.def.CenterLength * k / 2f));
            for (int j = 0; j <= count; j++)
            {
                float t = j / (float)count;
                if (s.ZAt(t) < 0.25f) continue;
                var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
                var rot = Quaternion.Euler(0f, -q.heading, 0f);
                foreach (float side in new[] { -0.44f, 0f, 0.44f })
                {
                    var o = TrackMath.Offset(q, side);
                    // No loop o pilar vai até a base da volta, não até a volta.
                    float top = (s.def.IsLoop ? s.zIn * TrackMath.LevelHeight : TrackLayout.SurfaceY(s, t, side)) - 0.06f;
                    float foot = Thickness / k;      // a mesa fica Thickness (mundo) abaixo do nível 0
                    pillars.Add(Box("Pilar", mat, new Vector3(o.x, (top - foot) / 2f, o.y), new Vector3(thin, top + foot, thin), rot));
                }
                // Travessa logo abaixo da pista, de borda a borda.
                pillars.Add(Box("Travessa", mat, new Vector3(q.pos.x, TrackLayout.SurfaceY(s, t, 0f) - 0.035f, q.pos.y), new Vector3(0.06f * 2f / k, 0.05f * 2f / k, 0.95f), rot));
            }
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
                if (i > 0 && !(s.def.IsJump && PieceCatalog.JumpGapAt(ts[i] * s.def.Length)))     // no vão do salto não há pista
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
            Vector2 p = TrackMath.Offset(q, o);
            if (!s.def.IsLoop) return new Vector3(p.x, y(t, o) + dy, p.y);
            TrackMath.LoopAt(t, out _, out _, out float phi);
            p -= TrackMath.Dir(q.heading) * (Mathf.Sin(phi) * dy);
            return new Vector3(p.x, y(t, o) + Mathf.Cos(phi) * dy, p.y);
        }

        // Tampas nas duas pontas da peça (o corte da base), para a ponta aberta da pista não ficar oca.
        // No salto, também nas duas pontas do vão, descendo até a mesa.
        static void AddCaps(Segment s, float thick, System.Func<float, float, float> y)
        {
            float L = s.def.Length, ground = s.zIn * TrackMath.LevelHeight;
            var ends = s.def.IsJump
                ? new[] { (0f, true), (PieceCatalog.JumpUp / L, false), ((PieceCatalog.JumpUp + PieceCatalog.JumpGap) / L, true), (1f, false) }
                : new[] { (0f, true), (1f, false) };
            foreach (var (t, front) in ends)
            {
                var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
                Vector2 l = TrackMath.Offset(q, -0.5f), r = TrackMath.Offset(q, 0.5f);
                float yl = y(t, -0.5f), yr = y(t, 0.5f);
                float bl = s.def.IsJump ? ground - thick : yl - thick, br = s.def.IsJump ? ground - thick : yr - thick;
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
