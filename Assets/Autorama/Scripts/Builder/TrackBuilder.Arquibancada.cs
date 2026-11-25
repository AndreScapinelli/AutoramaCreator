using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Arquibancada esticada ao longo da pista (seção 101): a frente segue o caminho da pista a ArqDist células do centro,
    // dos dois lados e nas curvas; as pontas têm pegadores para esticar. A linha fica gravada no enfeite, então mexer
    // na pista depois não a move (só a tira, se a pista passar por cima).
    public partial class TrackBuilder
    {
        public const float ArqDist = 0.8f;        // frente da arquibancada até o centro da pista, em células
        const float ArqPasso = 0.2f;              // espaço entre os pontos da linha, em células
        const float ArqMinCol = 4;                // menor arquibancada, em cadeiras

        struct TP { public Vector2 p, t; public float u; }

        // O caminho da pista em pontos, com o comprimento acumulado (u) e a direção.
        List<TP> Caminho(out bool fechado)
        {
            var l = new List<TP>();
            float u = 0f;
            foreach (var sg in Segments)
                for (int k = 0; k <= 24; k++)
                {
                    var p = TrackMath.PoseAt(sg.a, sg.def, sg.piece.dir, k / 24f).pos;
                    if (l.Count > 0) { float d = (p - l[l.Count - 1].p).magnitude; if (d < 1e-4f) continue; u += d; }
                    l.Add(new TP { p = p, u = u });
                }
            for (int i = 0; i < l.Count; i++)
            {
                var a = l[Mathf.Max(0, i - 1)].p; var b = l[Mathf.Min(l.Count - 1, i + 1)].p;
                var c = l[i]; c.t = (b - a).normalized; l[i] = c;
            }
            fechado = l.Count > 2 && (l[0].p - l[l.Count - 1].p).magnitude < 0.05f;
            return l;
        }

        static float Comp(List<TP> c) => c.Count > 0 ? c[c.Count - 1].u : 0f;

        static TP At(List<TP> c, bool fechado, float u)
        {
            float L = Comp(c);
            u = fechado && L > 0 ? Mathf.Repeat(u, L) : Mathf.Clamp(u, 0f, L);
            int lo = 0, hi = c.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (c[m].u <= u) lo = m; else hi = m; }
            float s = c[hi].u > c[lo].u ? (u - c[lo].u) / (c[hi].u - c[lo].u) : 0f;
            return new TP { p = Vector2.Lerp(c[lo].p, c[hi].p, s), t = Vector2.Lerp(c[lo].t, c[hi].t, s).normalized, u = u };
        }

        static Vector2 Esq(Vector2 t) => new Vector2(-t.y, t.x);

        // Ponto do caminho mais perto de cell (com janela em volta de perto, para não pular para outro trecho da pista).
        static float Perto(List<TP> c, bool fechado, Vector2 cell, out int lado, float perto = -1f, float janela = 0f)
        {
            float best = float.MaxValue, bu = 0f, L = Comp(c); lado = 1;
            for (int i = 0; i + 1 < c.Count; i++)
            {
                if (janela > 0f)
                {
                    float du = Mathf.Abs(c[i].u - perto);
                    if (fechado) du = Mathf.Min(du, L - du);
                    if (du > janela) continue;
                }
                var a = c[i].p; var ab = c[i + 1].p - a;
                float s = Mathf.Clamp01(Vector2.Dot(cell - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                var q = a + ab * s;
                float d = (cell - q).sqrMagnitude;
                if (d < best)
                {
                    best = d; bu = Mathf.Lerp(c[i].u, c[i + 1].u, s);
                    var t = ab.normalized;
                    lado = t.x * (cell - q).y - t.y * (cell - q).x >= 0f ? 1 : -1;
                }
            }
            return bu;
        }

        static float PassoCel(float scale) => Torcida.Passo * Torcida.Escala / scale;

        // Refaz a linha do enfeite a partir de u0, comp e lado; false se não cabe.
        bool Esticar(Enfeite e, List<TP> c, bool fechado)
        {
            float L = Comp(c);
            if (L < 0.5f) return false;
            if (!fechado) { e.u0 = Mathf.Clamp(e.u0, 0f, L); e.comp = Mathf.Min(e.comp, L - e.u0); }
            else e.comp = Mathf.Min(e.comp, L - 0.5f);
            int n = Mathf.Max(2, Mathf.CeilToInt(e.comp / ArqPasso) + 1);
            e.linha = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                var tp = At(c, fechado, e.u0 + e.comp * i / (n - 1));
                e.linha.Add(tp.p + Esq(tp.t) * e.lado * ArqDist);
            }
            e.colunas = Mathf.Max(1, Mathf.RoundToInt(e.comp / PassoCel(Scale)));
            var meio = e.linha[n / 2];
            e.pos = meio + Normal(e.linha, n / 2, e.lado) * Fundo(e) * 0.5f;
            e.rot = 0f;
            return true;
        }

        static Vector2 Normal(List<Vector2> l, int i, int lado)
        {
            var t = (l[Mathf.Min(l.Count - 1, i + 1)] - l[Mathf.Max(0, i - 1)]).normalized;
            return Esq(t) * lado;
        }

        static Torcida TorcidaDe(string id) { var d = EnfeiteCatalog.ById(id); return d != null && d.prefab ? d.prefab.GetComponent<Torcida>() : null; }

        // Profundidade da arquibancada, em células.
        float Fundo(Enfeite e)
        {
            var t = TorcidaDe(e.id);
            return ((t ? t.filas : 3) * Torcida.Fundo + 0.08f) * Torcida.Escala / Scale;
        }

        // Arquibancada nova no ponto da pista mais perto do clique, do lado do clique, com as cadeiras do prefab.
        Enfeite NovaArquibancada(EnfeiteDef def, Vector2 cell) => NovaArquibancada(def, cell, out _);

        Enfeite NovaArquibancada(EnfeiteDef def, Vector2 cell, out float u)
        {
            u = 0f;
            var c = Caminho(out bool fechado);
            if (c.Count < 2) return null;
            var t = def.prefab.GetComponent<Torcida>();
            u = Perto(c, fechado, cell, out int lado);
            float comp = t.colunas * PassoCel(Scale);
            var e = new Enfeite { id = def.id, lado = lado, comp = comp, u0 = u - comp * 0.5f, esc = 1f };
            if (!fechado) e.u0 = Mathf.Clamp(e.u0, 0f, Mathf.Max(0f, Comp(c) - comp));
            return Esticar(e, c, fechado) ? e : null;
        }

        // Clicar e arrastar ao pôr (seção 101.2): o clique é uma ponta (âncora) e o mouse puxa a outra, para qualquer lado.
        float arqAncora, arqUlt;
        public int NovaIndex { get; private set; } = -1;     // a arquibancada que o último clique pôs (−1 = nenhuma)

        public void EsticarNova(Vector2 cell)
        {
            int i = NovaIndex;
            if (i < 0 || i >= enfeites.Count || !enfeites[i].Trecho) return;
            var c = Caminho(out bool fechado);
            if (c.Count < 2) return;
            float L = Comp(c), min = ArqMinCol * PassoCel(Scale);
            float u = Perto(c, fechado, cell, out _, arqUlt, 3f);
            arqUlt = u;
            float d = u - arqAncora;
            if (fechado) d = Mathf.Repeat(d + L * 0.5f, L) - L * 0.5f;
            if (Mathf.Abs(d) < min) d = d < 0f ? -min : min;
            var e = enfeites[i].Clone();
            e.u0 = d > 0f ? arqAncora : arqAncora + d;
            e.comp = Mathf.Abs(d);
            if (fechado) e.u0 = Mathf.Repeat(e.u0, L);
            if (!Esticar(e, c, fechado) || Why(e, i) != null) return;
            enfeites[i] = e;
            Rebuild();
        }

        // Pegador: ponta 0 (começo) ou 1 (fim) do enfeite i vai até o ponto da pista mais perto de cell.
        public void EsticarPonta(int i, int ponta, Vector2 cell)
        {
            if (i < 0 || i >= enfeites.Count || !enfeites[i].Trecho) return;
            var c = Caminho(out bool fechado);
            if (c.Count < 2) return;
            var e = enfeites[i].Clone();
            float L = Comp(c), ref0 = ponta == 0 ? e.u0 : e.u0 + e.comp;
            float u = Perto(c, fechado, cell, out _, ref0, 3f);
            float d = u - ref0;
            if (fechado) d = Mathf.Repeat(d + L * 0.5f, L) - L * 0.5f;
            float min = ArqMinCol * PassoCel(Scale);
            if (ponta == 0) { d = Mathf.Min(d, e.comp - min); e.u0 += d; e.comp -= d; }
            else e.comp = Mathf.Max(min, e.comp + d);
            if (fechado) e.u0 = Mathf.Repeat(e.u0, L);
            if (!Esticar(e, c, fechado) || Why(e, i) != null) return;
            enfeites[i] = e;
            Rebuild();
        }

        // Arrastar a arquibancada inteira: escorrega ao longo da pista e troca de lado se o mouse passar para o outro.
        bool DeslizarArquibancada(int i, Vector2 cell)
        {
            var c = Caminho(out bool fechado);
            if (c.Count < 2) return false;
            var e = enfeites[i].Clone();
            float u = Perto(c, fechado, cell, out int lado, e.u0 + e.comp * 0.5f, 4f);
            e.lado = lado;
            e.u0 = u - e.comp * 0.5f;
            if (!Esticar(e, c, fechado) || Why(e, i) != null) return false;
            enfeites[i] = e;
            return true;
        }

        // R na arquibancada: passa para o outro lado da pista.
        bool VirarArquibancada(Enfeite e)
        {
            var c = Caminho(out bool fechado);
            e.lado = -e.lado;
            return c.Count >= 2 && Esticar(e, c, fechado);
        }

        // A pista (ou a mesa) passa por cima? Confere a frente e o fundo.
        bool ArqNaPista(Enfeite e, List<Segment> segs)
        {
            float fundo = Fundo(e), min = TrackHalf + 0.12f;
            var pts = new List<Vector2>();
            for (int i = 0; i < e.linha.Count; i += 2) { pts.Add(e.linha[i]); pts.Add(e.linha[i] + Normal(e.linha, i, e.lado) * fundo); }
            foreach (var sg in segs)
                for (int k = 0; k <= 24; k++)
                {
                    var p = TrackMath.PoseAt(sg.a, sg.def, sg.piece.dir, k / 24f).pos;
                    foreach (var q in pts) if ((p - q).sqrMagnitude < min * min) return true;
                }
            return false;
        }

        bool ArqForaDaMesa(Enfeite e)
        {
            float fundo = Fundo(e);
            for (int i = 0; i < e.linha.Count; i++)
                foreach (var q in new[] { e.linha[i], e.linha[i] + Normal(e.linha, i, e.lado) * fundo })
                    if (q.x < 0f || q.y < 0f || q.x > boardSize.x || q.y > boardSize.y) return true;
            return false;
        }

        float ArqRaio(Enfeite e)
        {
            float r = 0f;
            foreach (var p in e.linha) r = Mathf.Max(r, (p - e.pos).magnitude);
            return r + Fundo(e);
        }

        // ----- desenho: degraus extrudados ao longo da linha e a torcida em cada cadeira -----

        string ArqChave(Enfeite e)
        {
            unchecked
            {
                int h = (e.lado * 31 + e.linha.Count) * 31 + Faixas;      // a largura da pista muda a escala da malha
                foreach (var p in e.linha) h = h * 31 + p.GetHashCode();
                return e.id + "|" + h;
            }
        }

        void MontarArquibancada(GameObject go, EnfeiteDef def, Enfeite e)
        {
            var tor0 = def.prefab.GetComponent<Torcida>();
            go.transform.localPosition = new Vector3(0f, GroundY, 0f);     // o tampo muda de altura na raiz quando muda a largura da pista
            if (e.comp > 0f) e.colunas = Mathf.Max(1, Mathf.RoundToInt(e.comp / PassoCel(Scale)));   // outra largura de pista, outra conta de cadeiras
            int filas = tor0.filas;
            float k = Torcida.Escala / Scale, F = Torcida.Fundo, D = Torcida.Degrau, W = 0.6f, T = 0.08f;
            var l = e.linha;
            int n = l.Count;
            var nrm = new Vector2[n];
            for (int i = 0; i < n; i++) nrm[i] = Normal(l, i, e.lado);
            var tg = new Vector2[n];
            for (int i = 0; i < n; i++) tg[i] = (l[Mathf.Min(n - 1, i + 1)] - l[Mathf.Max(0, i - 1)]).normalized;

            var v = new List<Vector3>();
            var tris = new[] { new List<int>(), new List<int>(), new List<int>() };   // concreto, assento, telhado
            Vector3 P(int i, float b, float h) { var q = l[i] + nrm[i] * b * k; return new Vector3(q.x, h * k, q.y); }
            Vector3 Out(int i, float ob, float oh) { var q = nrm[i] * ob; return new Vector3(q.x, oh, q.y); }
            void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 fora)
            {
                int o = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                bool ok = Vector3.Dot(Vector3.Cross(b - a, c - a), fora) >= 0f;
                if (ok) tris[sub].AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
                else tris[sub].AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 });
            }
            // Uma aresta do perfil (b0,h0)→(b1,h1), com a normal (ob, oh) no plano do perfil, puxada ao longo da linha.
            void Aresta(int sub, float b0, float h0, float b1, float h1, float ob, float oh)
            {
                for (int i = 0; i + 1 < n; i++)
                    Quad(sub, P(i, b0, h0), P(i + 1, b0, h0), P(i + 1, b1, h1), P(i, b1, h1), Out(i, ob, oh) + Out(i + 1, ob, oh));
            }
            // Tampa de um retângulo do perfil nas duas pontas.
            void Tampa(int sub, float b0, float b1, float h0, float h1)
            {
                foreach (var (i, s) in new[] { (0, -1f), (n - 1, 1f) })
                {
                    var f = new Vector3(tg[i].x, 0f, tg[i].y) * s;
                    Quad(sub, P(i, b0, h0), P(i, b1, h0), P(i, b1, h1), P(i, b0, h1), f);
                }
            }

            for (int f = 0; f < filas; f++)
            {
                Aresta(0, f * F, f * D, f * F, (f + 1) * D, -1f, 0f);               // espelho do degrau
                Aresta(0, f * F, (f + 1) * D, (f + 1) * F, (f + 1) * D, 0f, 1f);     // piso
                Tampa(0, f * F, (f + 1) * F, 0f, (f + 1) * D);
                float s0 = (f + 0.55f) * F, s1 = (f + 0.95f) * F, h = (f + 1) * D, hs = h + 0.04f;
                Aresta(1, s0, h, s0, hs, -1f, 0f);
                Aresta(1, s0, hs, s1, hs, 0f, 1f);
                Aresta(1, s1, h, s1, hs, 1f, 0f);
            }
            float fb = filas * F, fh = filas * D;
            Aresta(0, fb, fh, fb, fh + W, -1f, 0f);
            Aresta(0, fb, fh + W, fb + T, fh + W, 0f, 1f);
            Aresta(0, fb + T, 0f, fb + T, fh + W, 1f, 0f);
            Tampa(0, fb, fb + T, 0f, fh + W);
            if (tor0.teto)
            {
                float ht = fh + W + 0.55f, b0 = -0.15f, b1 = fb + T + 0.1f;
                Aresta(2, b0, ht, b1, ht, 0f, 1f);
                Aresta(2, b0, ht - 0.06f, b1, ht - 0.06f, 0f, -1f);
                Aresta(2, b0, ht - 0.06f, b0, ht, -1f, 0f);
                Tampa(2, b0, b1, ht - 0.06f, ht);
                // Pilares atrás, a cada ~6 cadeiras.
                int passo = Mathf.Max(2, Mathf.RoundToInt(6 * Torcida.Passo * k / ArqPasso));
                for (int i = 0; i < n; i += passo)
                {
                    int j = Mathf.Min(n - 1, i + 1); if (j == i) break;
                    float pb0 = fb + T, pb1 = fb + T + 0.08f;
                    Quad(0, P(i, pb0, 0), P(j, pb0, 0), P(j, pb0, ht), P(i, pb0, ht), Out(i, -1f, 0f));
                    Quad(0, P(i, pb1, 0), P(j, pb1, 0), P(j, pb1, ht), P(i, pb1, ht), Out(i, 1f, 0f));
                    Quad(0, P(i, pb0, 0), P(i, pb1, 0), P(i, pb1, ht), P(i, pb0, ht), new Vector3(-tg[i].x, 0, -tg[i].y));
                    Quad(0, P(j, pb0, 0), P(j, pb1, 0), P(j, pb1, ht), P(j, pb0, ht), new Vector3(tg[j].x, 0, tg[j].y));
                }
            }

            var mf = go.GetComponent<MeshFilter>(); if (!mf) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>(); if (!mr) mr = go.AddComponent<MeshRenderer>();
            var mesh = mf.sharedMesh;
            if (!mesh) { mesh = new Mesh { name = "Arquibancada", hideFlags = HideFlags.DontSave }; mf.sharedMesh = mesh; }
            mesh.Clear();
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v);
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(tris[s], s);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mr.sharedMaterials = ArqMats(def);
            var col = go.GetComponent<MeshCollider>();
            if (col) { col.sharedMesh = null; col.sharedMesh = mesh; }

            // Cadeiras: em cada fila, ao longo da linha daquela fila, uma a cada Passo, centradas.
            var pos = new List<Vector3>(); var yaw = new List<float>();
            for (int f = 0; f < filas; f++)
            {
                float b = (f + 0.45f) * F * k;
                var row = new Vector2[n]; var ac = new float[n];
                for (int i = 0; i < n; i++) { row[i] = l[i] + nrm[i] * b; if (i > 0) ac[i] = ac[i - 1] + (row[i] - row[i - 1]).magnitude; }
                float Lr = ac[n - 1], passo = Torcida.Passo * k;
                int ns = Mathf.FloorToInt(Lr / passo);
                if (ns < 1) continue;
                int j = 0;
                for (int s = 0; s < ns; s++)
                {
                    float a = (s + 0.5f) * Lr / ns;
                    while (j < n - 2 && ac[j + 1] < a) j++;
                    float t = ac[j + 1] > ac[j] ? (a - ac[j]) / (ac[j + 1] - ac[j]) : 0f;
                    var p = Vector2.Lerp(row[j], row[j + 1], t);
                    var nn = Vector2.Lerp(nrm[j], nrm[j + 1], t);
                    pos.Add(new Vector3(p.x, (f + 1) * D * k, p.y));
                    yaw.Add(Mathf.Atan2(nn.x, nn.y) * Mathf.Rad2Deg);
                }
            }
            var tor = go.GetComponent<Torcida>(); if (!tor) tor = go.AddComponent<Torcida>();
            tor.filas = filas; tor.colunas = tor0.colunas; tor.teto = tor0.teto;
            tor.Assentos(pos, yaw, k);
        }

        static readonly Dictionary<string, Material[]> arqMats = new Dictionary<string, Material[]>();

        // Os materiais do prefab (concreto, assento, telhado), achados pelo nome.
        static Material[] ArqMats(EnfeiteDef def)
        {
            if (arqMats.TryGetValue(def.id, out var m) && m[0]) return m;
            m = new Material[3];
            foreach (var r in def.prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mat = r.sharedMaterial; if (!mat) continue;
                if (mat.name.Contains("concreto")) m[0] = mat;
                else if (mat.name.Contains("assento")) m[1] = mat;
                else if (mat.name.Contains("telhado")) m[2] = mat;
            }
            m[1] = m[1] ? m[1] : m[0]; m[2] = m[2] ? m[2] : m[0];
            return arqMats[def.id] = m;
        }

        GameObject NovaVistaArquibancada(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(0f, GroundY, 0f);
            go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>();
            return go;
        }

        // ----- pegadores nas pontas da arquibancada selecionada -----

        readonly Transform[] alcas = new Transform[2];
        static Mesh setaMesh;
        static Material setaMat;

        // Pegador: uma seta dupla (⟷) grossa, deitada ao longo da pista e solta além da ponta: "puxe por aqui".
        void MostrarAlcas()
        {
            bool on = !RaceMode && EnfSel >= 0 && EnfSel < enfeites.Count && enfeites[EnfSel].Trecho;
            for (int h = 0; h < 2; h++)
            {
                if (on && !alcas[h])
                {
                    var go = new GameObject("Pegador da arquibancada " + h) { hideFlags = HideFlags.DontSave };
                    go.AddComponent<MeshFilter>().sharedMesh = SetaMesh();
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = SetaMat();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var box = go.AddComponent<BoxCollider>();           // maior que a seta, para o clique pegar fácil
                    box.size = new Vector3(0.7f, 0.5f, 1.2f);
                    go.transform.SetParent(Root, false);
                    alcas[h] = go.transform;
                }
                if (!alcas[h]) continue;
                alcas[h].gameObject.SetActive(on);
                if (!on) continue;
                var e = enfeites[EnfSel];
                int i = h == 0 ? 0 : e.linha.Count - 1;
                var t = (e.linha[h == 0 ? 0 : i] - e.linha[h == 0 ? 1 : i - 1]).normalized;   // para fora da ponta
                float d = 0.5f / Scale;                       // do tamanho da arquibancada, que encolhe nas pistas largas
                var p = e.linha[i] + Normal(e.linha, i, e.lado) * Fundo(e) * 0.5f + t * d * 0.75f;
                float pulso = Application.isPlaying ? 1f + 0.08f * Mathf.Sin(Time.time * 5f) : 1f;
                alcas[h].localPosition = new Vector3(p.x, GroundY + 0.25f * d, p.y);
                alcas[h].localRotation = Quaternion.LookRotation(new Vector3(t.x, 0f, t.y));
                alcas[h].localScale = Vector3.one * d * pulso;
            }
        }

        void LateUpdate() { if (Application.isPlaying && alcas[0] && alcas[0].gameObject.activeSelf) MostrarAlcas(); }   // o pulso

        static Material SetaMat()
        {
            if (setaMat) return setaMat;
            setaMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
            var c = new Color(1f, 0.78f, 0.1f);
            setaMat.SetColor("_BaseColor", c);
            setaMat.EnableKeyword("_EMISSION");
            setaMat.SetColor("_EmissionColor", c * 0.6f);
            return setaMat;
        }

        // Seta dupla ao longo de z (−0,5 a 0,5), 0,4 de largura e 0,12 de espessura, com faces de cima, de baixo e dos lados.
        static Mesh SetaMesh()
        {
            if (setaMesh) return setaMesh;
            var o = new[] { new Vector2(0.06f, -0.22f), new Vector2(0.2f, -0.22f), new Vector2(0f, -0.5f), new Vector2(-0.2f, -0.22f), new Vector2(-0.06f, -0.22f),
                            new Vector2(-0.06f, 0.22f), new Vector2(-0.2f, 0.22f), new Vector2(0f, 0.5f), new Vector2(0.2f, 0.22f), new Vector2(0.06f, 0.22f) };
            const float y0 = 0f, y1 = 0.12f;
            var v = new List<Vector3>(); var tri = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 fora)
            {
                int k = v.Count; v.Add(a); v.Add(b); v.Add(c);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), fora) >= 0f) tri.AddRange(new[] { k, k + 1, k + 2 }); else tri.AddRange(new[] { k, k + 2, k + 1 });
            }
            Vector3 P(Vector2 q, float y) => new Vector3(q.x, y, q.y);
            foreach (var (y, n) in new[] { (y1, Vector3.up), (y0, Vector3.down) })
            {
                Tri(P(o[0], y), P(o[4], y), P(o[5], y), n); Tri(P(o[0], y), P(o[5], y), P(o[9], y), n);   // haste
                Tri(P(o[1], y), P(o[2], y), P(o[3], y), n);                                             // ponta de trás
                Tri(P(o[6], y), P(o[7], y), P(o[8], y), n);                                             // ponta da frente
            }
            float area = 0f;                                  // sentido do contorno: a normal de fora é a da direita (anti-horário) ou da esquerda
            for (int k = 0; k < o.Length; k++) { var a0 = o[k]; var b0 = o[(k + 1) % o.Length]; area += a0.x * b0.y - b0.x * a0.y; }
            for (int k = 0; k < o.Length; k++)
            {
                var a = o[k]; var b = o[(k + 1) % o.Length];
                var ed = b - a; var nn = new Vector2(ed.y, -ed.x) * Mathf.Sign(area);
                var f = new Vector3(nn.x, 0f, nn.y);
                Tri(P(a, y0), P(b, y0), P(b, y1), f); Tri(P(a, y0), P(b, y1), P(a, y1), f);
            }
            setaMesh = new Mesh { name = "Seta do pegador", hideFlags = HideFlags.HideAndDontSave };
            setaMesh.SetVertices(v); setaMesh.SetTriangles(tri, 0);
            setaMesh.RecalculateNormals(); setaMesh.RecalculateBounds();
            return setaMesh;
        }

        // 0 ou 1 se o colisor é um pegador; −1 se não.
        public int PegadorDe(Collider c)
        {
            if (!c) return -1;
            for (int h = 0; h < 2; h++) if (alcas[h] && c.transform == alcas[h]) return h;
            return -1;
        }
    }
}
