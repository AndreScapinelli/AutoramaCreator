using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Arquibancada com torcida (seção 101 do guia). Os degraus são o prefab (Editor/ArquibancadaGen.cs);
    // os torcedores são desenhados aqui, instanciados, e reagem aos carrinhos que passam perto.
    // Só lê a posição dos CarView da cena: não mexe na corrida e vale igual no online.
    [ExecuteAlways]
    public class Torcida : MonoBehaviour
    {
        public int filas = 3, colunas = 16;
        public bool teto;
        public const float Escala = 0.45f;        // unidades do prefab → células na mesa da pista de 2 (÷ escala nas largas)
        float escala = 1f;                        // tamanho de cada torcedor (1 no prefab; Escala ÷ Scale no trecho da mesa)
        bool externo;                             // assentos postos por Assentos (arquibancada esticada ao longo da pista)

        // Medidas no espaço do prefab (1 unidade ≈ 1 célula quando a altura do catálogo é a do modelo).
        public const float Passo = 0.34f, Fundo = 0.45f, Degrau = 0.3f;
        public const float Alcance = 3f;          // até quantas unidades o carro anima a torcida
        public const float VRef = 6f;             // velocidade (unidades/s) da empolgação cheia
        public const float Queda = 0.5f;          // perdeu metade da velocidade em 0,15 s perto: "ohhh"
        public const float OhTempo = 1.6f;
        public const float Segura = 1.4f;         // quanto tempo a empolgação fica no pico depois que o carro passa

        static readonly Color[] Camisas =
        {
            new Color(0.85f, 0.15f, 0.15f), new Color(0.15f, 0.35f, 0.85f), new Color(0.95f, 0.8f, 0.1f),
            new Color(0.1f, 0.65f, 0.25f), new Color(0.95f, 0.95f, 0.95f), new Color(0.95f, 0.45f, 0.1f),
        };
        static readonly Color Pele = new Color(0.93f, 0.75f, 0.6f);

        struct Fa { public Vector3 pos; public int cor; public float fase, ritmo, alt, emp, oh, pico, segura, yaw; }

        Fa[] fas;
        static Mesh corpo, cabeca;
        static Material[] mats;
        static Material matPele;
        List<Matrix4x4>[] porCor;
        readonly List<Matrix4x4> cabecas = new List<Matrix4x4>(), bracos = new List<Matrix4x4>();

        readonly Dictionary<CarView, (Vector3 p, float v, float t)> visto = new Dictionary<CarView, (Vector3, float, float)>();

        public static Vector3 Assento(int fila, int col, int colunas) =>
            new Vector3((col - (colunas - 1) * 0.5f) * Passo, (fila + 1) * Degrau, (fila + 0.5f) * Fundo);

        void OnEnable() { if (!externo) Montar(); }

        // Arquibancada esticada (seção 101): os assentos vêm de fora, cada um com o giro de frente para a pista.
        public void Assentos(List<Vector3> pos, List<float> yaw, float esc)
        {
            externo = true;
            escala = esc;
            var rnd = new System.Random(pos.Count * 7919 + filas);
            var l = new List<Fa>();
            for (int i = 0; i < pos.Count; i++)
            {
                if (rnd.NextDouble() < 0.12) continue;
                l.Add(new Fa { pos = pos[i], yaw = yaw[i], cor = rnd.Next(Camisas.Length), fase = (float)rnd.NextDouble() * 6.28f,
                               ritmo = 0.8f + (float)rnd.NextDouble() * 0.5f, alt = 0.9f + (float)rnd.NextDouble() * 0.2f });
            }
            fas = l.ToArray();
            if (porCor == null) { porCor = new List<Matrix4x4>[Camisas.Length]; for (int i = 0; i < porCor.Length; i++) porCor[i] = new List<Matrix4x4>(); }
            Recursos();
        }

        void Montar()
        {
            var rnd = new System.Random(filas * 1000 + colunas);
            var l = new List<Fa>();
            for (int f = 0; f < filas; f++)
                for (int c = 0; c < colunas; c++)
                {
                    if (rnd.NextDouble() < 0.12) continue;      // cadeira vazia
                    l.Add(new Fa
                    {
                        pos = Assento(f, c, colunas) + new Vector3((float)(rnd.NextDouble() - 0.5) * 0.06f, 0f, 0f),
                        cor = rnd.Next(Camisas.Length),
                        fase = (float)rnd.NextDouble() * 6.28f,
                        ritmo = 0.8f + (float)rnd.NextDouble() * 0.5f,
                        alt = 0.9f + (float)rnd.NextDouble() * 0.2f,
                    });
                }
            fas = l.ToArray();
            porCor = new List<Matrix4x4>[Camisas.Length];
            for (int i = 0; i < porCor.Length; i++) porCor[i] = new List<Matrix4x4>();
            Recursos();
        }

        static void Recursos()
        {
            if (corpo && mats != null && mats[0]) return;
            corpo = Primitiva(PrimitiveType.Capsule);
            cabeca = Primitiva(PrimitiveType.Sphere);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            mats = new Material[Camisas.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = Mat(sh, Camisas[i]);
            matPele = Mat(sh, Pele);
        }

        static Material Mat(Shader sh, Color c)
        {
            var m = new Material(sh) { enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.2f);
            return m;
        }

        static Mesh Primitiva(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            TrackPieceView.Kill(go);
            return m;
        }

        void Update()
        {
            if (fas == null) { if (externo) return; Montar(); }
            Recursos();
            float agora = Time.realtimeSinceStartup, dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (!Application.isPlaying) dt = 0.016f;

            // Carros no espaço da arquibancada: posição, empolgação pela velocidade e o "ohhh" de quem freou do nada.
            if (visto.Count > 32) visto.Clear();
            var perto = new List<(Vector3 p, float emp, bool oh)>();
            foreach (var car in CarView.Todos)
            {
                if (!car) continue;
                var p = transform.InverseTransformPoint(car.transform.position);
                float v = 0f; bool oh = false;
                if (visto.TryGetValue(car, out var a) && agora > a.t)
                {
                    v = Vector3.Distance(p, a.p) / (agora - a.t);
                    oh = a.v > VRef * escala * 0.5f && v < a.v * Queda;
                    v = Mathf.Lerp(a.v, v, 0.5f);
                }
                if (!visto.ContainsKey(car) || agora - visto[car].t > 0.15f || oh) visto[car] = (p, v, agora);
                perto.Add((p, 0.35f + 0.65f * Mathf.Clamp01(v / (VRef * escala)), oh));
            }

            float t = Application.isPlaying ? Time.time : agora;
            foreach (var l in porCor) l.Clear();
            cabecas.Clear(); bracos.Clear();
            var m = transform.localToWorldMatrix;
            for (int i = 0; i < fas.Length; i++)
            {
                ref var f = ref fas[i];
                float alvo = 0f;
                foreach (var c in perto)
                {
                    float d = new Vector2(c.p.x - f.pos.x, c.p.z - f.pos.z).magnitude;
                    if (d > Alcance * escala) continue;
                    float k = 1f - d / (Alcance * escala);
                    alvo = Mathf.Max(alvo, k * c.emp);
                    if (c.oh && d < Alcance * escala * 0.8f) f.oh = OhTempo;
                }
                // O carro passa rápido: quem viu segura a empolgação por Segura s, para o braço subir até o fim.
                if (alvo > 0.1f) { f.pico = Mathf.Max(f.pico, alvo); f.segura = Segura; }
                f.segura = Mathf.Max(0f, f.segura - dt);
                if (f.segura <= 0f) f.pico = 0f;
                float meta = Mathf.Max(alvo, f.pico);
                f.emp = Mathf.MoveTowards(f.emp, meta, dt * (meta > f.emp ? 10f : 1.5f));
                f.oh = Mathf.Max(0f, f.oh - dt);
                Desenhar(ref f, t, m);
            }
            var rp = new RenderParams(mats[0]) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true, layer = gameObject.layer };
            for (int c = 0; c < porCor.Length; c++)
                if (porCor[c].Count > 0) { rp.material = mats[c]; Graphics.RenderMeshInstanced(rp, corpo, 0, porCor[c]); }
            rp.material = matPele;
            if (cabecas.Count > 0) Graphics.RenderMeshInstanced(rp, cabeca, 0, cabecas);
            if (bracos.Count > 0) Graphics.RenderMeshInstanced(rp, corpo, 0, bracos);
        }

        // Um torcedor: balança parado, pula e ergue os braços com o carro perto; no "ohhh", mãos na cabeça.
        void Desenhar(ref Fa f, float t, Matrix4x4 m)
        {
            float h = 0.34f * f.alt, w = 0.15f;
            bool oh = f.oh > 0f;
            float pulo = oh ? 0f : f.emp * Mathf.Abs(Mathf.Sin(t * 9f * f.ritmo + f.fase)) * 0.12f;
            float balanco = Mathf.Sin(t * 1.5f * f.ritmo + f.fase) * 4f * (1f - f.emp);
            float tras = oh ? -12f : 0f;
            var baseM = m * Matrix4x4.TRS(f.pos + Vector3.up * pulo * escala, Quaternion.Euler(0f, f.yaw, 0f) * Quaternion.Euler(tras, 0f, balanco), Vector3.one * escala);

            porCor[f.cor].Add(baseM * Matrix4x4.TRS(new Vector3(0f, h * 0.5f, 0f), Quaternion.identity, new Vector3(w, h * 0.5f, w)));
            float r = 0.12f;
            var topo = new Vector3(0f, h + r * 0.45f, 0f);
            cabecas.Add(baseM * Matrix4x4.TRS(topo, Quaternion.identity, Vector3.one * r));

            // Braços: ângulo a partir de "para baixo" (0) até "para cima" (180), abrindo para o lado.
            float ang = oh ? 155f : Mathf.Lerp(12f, 165f, Mathf.Clamp01(f.emp * 2.5f)) + f.emp * Mathf.Sin(t * 12f + f.fase) * 15f;
            float abre = oh ? -18f : 10f;
            for (int s = -1; s <= 1; s += 2)
            {
                var ombro = new Vector3(s * w * 0.62f, h * 0.85f, 0f);
                var rot = Quaternion.Euler(0f, 0f, s * (ang + abre * (ang > 90f ? -1f : 1f)));
                var meio = ombro + rot * Vector3.down * 0.09f;
                bracos.Add(baseM * Matrix4x4.TRS(meio, rot, new Vector3(0.045f, 0.09f, 0.045f)));
            }
        }
    }
}
