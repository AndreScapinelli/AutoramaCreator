using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // A bolha do carrinho (seção 46 do guia). O número vai para o JSON: 0 = padrão.
    public enum CarBody { Padrao, Asa, Lisa }
    // Cada bolha tem 3 modelos (seção 80): só o visual muda; física e caixa de colisão são as da bolha.

    // O carrinho 3D (seções 28 e 46 do guia). Low-poly de duas cores montado por código: cubos, cilindros e peças
    // de perfil (`Shape`). Se houver um modelo importado para a bolha (`models`), ele entra no lugar e as cores vão
    // para os materiais dele. A frente aponta para +X; o chão do carrinho é y = 0.
    // Caixa de colisão da física: 0,60 × 0,32 (comprimento × largura), igual para todas as bolhas.
    public class CarView : MonoBehaviour
    {
        Transform visual;                          // filho que tomba quando o carrinho sai da pista
        readonly Transform[] wheels = new Transform[4];
        readonly Quaternion[] wheelRest = new Quaternion[4];
        Vector3 wheelAxis;                         // eixo em que a roda gira, no espaço dela
        readonly List<GameObject> cockpit = new List<GameObject>();   // some na câmera em primeira pessoa
        Material bodyMat, trimMat, darkMat, tireMat, silverMat, magnetMat;
        GameObject[] models;                       // modelo importado de cada bolha (índice = CarBody); sem ele, por código
        float spin, tumble;

        public CarBody Body { get; private set; }
        public int Model { get; private set; }

        // Modelos de cada bolha (índice = CarSetup.modelo). O 0 é o de sempre.
        public static readonly string[][] ModelNames =
        {
            new[] { "Clássico", "Coupé GT", "Rali" },
            new[] { "Fórmula", "Protótipo", "Asa dupla" },
            new[] { "Gota", "Flecha", "Bala" },
        };
        public static string ModelName(int bolha, int modelo) => ModelNames[Mathf.Clamp(bolha, 0, 2)][Mathf.Clamp(modelo, 0, 2)];
        public int Tire { get; private set; } = -1;

        // A faixa do pneu (seção 48): médio amarelo, macio vermelho, duro branco. Índice = CarSetup.pneu.
        static readonly Color[] TireColors = { new Color32(0xF5, 0xC2, 0x1B, 0xFF), new Color32(0xE5, 0x39, 0x35, 0xFF), new Color32(0xF2, 0xF2, 0xEC, 0xFF) };

        const float WheelR = 0.05f;
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        // lit: um material URP/Lit liso (o dos pilares serve) para copiar; main: a cor principal.
        public static CarView Create(string name, Material lit, Color main, Transform parent, GameObject[] models = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<CarView>();
            v.models = models;
            v.bodyMat = v.Copy(lit, Color.red, 0.45f);
            v.trimMat = v.Copy(lit, Color.white, 0.3f);
            v.darkMat = v.Copy(lit, new Color32(0x16, 0x19, 0x1B, 0xFF), 0.2f);
            v.tireMat = v.Copy(lit, TireColors[0], 0.3f);
            v.silverMat = v.Copy(lit, new Color32(0xB8, 0xBE, 0xC4, 0xFF), 0.8f);
            v.magnetMat = v.Copy(lit, new Color32(0xC6, 0x28, 0x28, 0xFF), 0.4f);
            v.SetTire(0);
            v.visual = new GameObject("Visual").transform;
            v.visual.SetParent(go.transform, false);
            v.Build();
            v.SetColors(main);
            return v;
        }

        public int Chassi { get; private set; }

        // Chassi (seção 52): placa embaixo do carro, prata no leve e escura com o ímã vermelho no pesado; o padrão não tem.
        public void SetChassi(int chassi)
        {
            if (chassi == Chassi) return;
            Chassi = chassi;
            Rebuild();
        }

        public void SetBody(CarBody body, int model = 0)
        {
            model = Mathf.Clamp(model, 0, 2);
            if (body == Body && model == Model) return;
            Body = body;
            Model = model;
            Rebuild();
        }

        void Rebuild()
        {
            for (int i = visual.childCount - 1; i >= 0; i--)
            {
                var old = visual.GetChild(i);
                old.SetParent(null);
                Destroy(old.gameObject);
            }
            Build();
            ApplyGhost();
        }

        // Fantasma (seção 55): todas as peças com um material só, transparente, sem sombra.
        Material ghostMat;

        public void MakeGhost(Material m) { ghostMat = m; ApplyGhost(); }

        void ApplyGhost()
        {
            if (ghostMat == null) return;
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = ghostMat;
                r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        public void SetTire(int pneu)
        {
            pneu = Mathf.Clamp(pneu, 0, TireColors.Length - 1);
            if (pneu == Tire) return;
            Tire = pneu;
            tireMat.SetColor("_BaseColor", TireColors[pneu]); tireMat.color = TireColors[pneu];
        }

        Material Copy(Material lit, Color c, float smooth)
        {
            var m = new Material(lit) { color = c };
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        Transform Part(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 size, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (name == "Cabine" || name == "Teto") cockpit.Add(go);
            return go.transform;
        }

        static Vector3 Mirror(Vector3 q) => new Vector3(q.x, q.y, -q.z);

        // Um polígono convexo com a face para o lado de `outward`, com vértices próprios (quinas vivas).
        static void Face(List<Vector3> v, List<Vector3> nr, List<int> t, Vector3 outward, params Vector3[] p)
        {
            Vector3 n = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
            bool flip = Vector3.Dot(n, outward) < 0f;
            if (flip) n = -n;
            int b = v.Count;
            foreach (var q in p) { v.Add(q); nr.Add(n); }
            for (int i = 1; i < p.Length - 1; i++) { t.Add(b); t.Add(flip ? b + i + 1 : b + i); t.Add(flip ? b + i : b + i + 1); }
        }

        // Peça de perfil: o contorno lateral (x, y) estendido para os dois lados. O z de cada ponto é a meia largura
        // da peça naquele ponto (a carroceria pode afinar no bico e na traseira). O contorno tem que ser convexo.
        void Shape(string name, Material m, params Vector3[] p)
        {
            string key = Body + "/" + Model + "/" + name;
            if (!meshes.TryGetValue(key, out var mesh) || mesh == null)
            {
                var v = new List<Vector3>(); var nr = new List<Vector3>(); var t = new List<int>();
                Vector3 c = Vector3.zero;
                foreach (var q in p) c += q;
                c /= p.Length;
                foreach (var q in p) c.z = Mathf.Max(c.z, q.z);     // o meio do lado na maior largura: a lateral fica abaulada
                for (int i = 0; i < p.Length; i++)
                {
                    Vector3 a = p[i], b = p[(i + 1) % p.Length];
                    Vector3 mid = (a + b) * 0.5f - c;
                    mid.z = 0f;
                    Face(v, nr, t, mid, a, b, Mirror(b), Mirror(a));
                    Face(v, nr, t, Vector3.forward, c, a, b);
                    Face(v, nr, t, Vector3.back, Mirror(c), Mirror(a), Mirror(b));
                }
                mesh = new Mesh { name = key };
                mesh.SetVertices(v); mesh.SetNormals(nr); mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                meshes[key] = mesh;
            }
            var go = new GameObject(name);
            go.transform.SetParent(visual, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            if (name == "Cabine") cockpit.Add(go);
        }

        static Vector3 P(float x, float y, float halfWidth) => new Vector3(x, y, halfWidth);

        void Build()
        {
            cockpit.Clear();
            for (int i = 0; i < 4; i++) wheels[i] = null;
            if (Chassi == 1) Part(PrimitiveType.Cube, "Chassi leve", silverMat, new Vector3(0f, 0.022f, 0f), new Vector3(0.5f, 0.008f, 0.2f), visual);
            else if (Chassi == 2)
            {
                Part(PrimitiveType.Cube, "Chassi pesado", darkMat, new Vector3(0f, 0.022f, 0f), new Vector3(0.52f, 0.014f, 0.22f), visual);
                Part(PrimitiveType.Cube, "Ímã", magnetMat, new Vector3(0.02f, 0.012f, 0f), new Vector3(0.12f, 0.012f, 0.12f), visual);
            }
            var model = Model == 0 && models != null && (int)Body < models.Length ? models[(int)Body] : null;
            if (model != null) { UseModel(model); return; }

            var cube = PrimitiveType.Cube;
            float wheelZ = 0.145f;
            if (Model != 0) wheelZ = BuildModel();
            else if (Body == CarBody.Asa)
            {
                // Asa: cunha baixa de rodas descobertas, lâmina na frente e aerofólio alto e largo.
                Shape("Casco", bodyMat, P(-0.29f, 0.035f, 0.115f), P(0.29f, 0.035f, 0.085f), P(0.29f, 0.055f, 0.085f),
                                        P(0.10f, 0.085f, 0.11f), P(-0.05f, 0.105f, 0.115f), P(-0.29f, 0.115f, 0.115f));
                Shape("Cabine", darkMat, P(0.08f, 0.085f, 0.055f), P(-0.02f, 0.15f, 0.07f), P(-0.11f, 0.15f, 0.07f), P(-0.2f, 0.105f, 0.075f));
                Part(cube, "Tomada de ar", trimMat, new Vector3(-0.17f, 0.14f, 0f), new Vector3(0.08f, 0.05f, 0.05f), visual);
                Part(cube, "Lâmina", trimMat, new Vector3(0.275f, 0.03f, 0f), new Vector3(0.05f, 0.012f, 0.31f), visual);
                Part(cube, "Aerofólio", trimMat, new Vector3(-0.255f, 0.215f, 0f), new Vector3(0.085f, 0.014f, 0.3f), visual);
                foreach (float z in new[] { -1f, 1f })
                {
                    Part(cube, "Aleta", bodyMat, new Vector3(-0.25f, 0.19f, 0.155f * z), new Vector3(0.1f, 0.075f, 0.01f), visual);
                    Part(cube, "Apoio", darkMat, new Vector3(-0.255f, 0.16f, 0.06f * z), new Vector3(0.02f, 0.1f, 0.012f), visual);
                }
                foreach (float x in new[] { 0.185f, -0.185f })
                    Part(cube, "Eixo", darkMat, new Vector3(x, WheelR, 0f), new Vector3(0.02f, 0.015f, 0.27f), visual);
            }
            else if (Body == CarBody.Lisa)
            {
                // Lisa: gota fechada que cobre as rodas, sem aerofólio, com barbatana na traseira.
                wheelZ = 0.115f;
                Shape("Casco", bodyMat, P(-0.30f, 0.05f, 0.09f), P(-0.22f, 0.03f, 0.15f), P(0.24f, 0.03f, 0.15f), P(0.30f, 0.045f, 0.11f),
                                        P(0.27f, 0.08f, 0.13f), P(0.15f, 0.115f, 0.158f), P(-0.05f, 0.125f, 0.158f), P(-0.22f, 0.10f, 0.15f),
                                        P(-0.30f, 0.07f, 0.09f));
                Shape("Cabine", darkMat, P(0.12f, 0.11f, 0.07f), P(0.02f, 0.165f, 0.075f), P(-0.08f, 0.165f, 0.07f), P(-0.2f, 0.095f, 0.05f));
                Shape("Barbatana", trimMat, P(-0.08f, 0.12f, 0.006f), P(-0.27f, 0.185f, 0.006f), P(-0.30f, 0.185f, 0.006f), P(-0.30f, 0.08f, 0.006f));
                Part(cube, "Saia", trimMat, new Vector3(0.01f, 0.039f, 0f), new Vector3(0.46f, 0.022f, 0.32f), visual);
            }
            else
            {
                // Padrão: assoalho largo, capô mais baixo na frente, cabine escura e faixa da segunda cor por cima.
                Part(cube, "Assoalho", bodyMat, new Vector3(0f, 0.06f, 0f), new Vector3(0.58f, 0.05f, 0.25f), visual);
                Part(cube, "Capô", bodyMat, new Vector3(0.17f, 0.095f, 0f), new Vector3(0.22f, 0.03f, 0.22f), visual);
                Part(cube, "Traseira", bodyMat, new Vector3(-0.17f, 0.105f, 0f), new Vector3(0.22f, 0.05f, 0.24f), visual);
                Part(cube, "Cabine", darkMat, new Vector3(-0.01f, 0.13f, 0f), new Vector3(0.17f, 0.07f, 0.19f), visual);
                Part(cube, "Teto", bodyMat, new Vector3(-0.02f, 0.168f, 0f), new Vector3(0.13f, 0.012f, 0.17f), visual);
                Part(cube, "Faixa", trimMat, new Vector3(0.17f, 0.1115f, 0f), new Vector3(0.222f, 0.004f, 0.06f), visual);
                Part(cube, "Faixa traseira", trimMat, new Vector3(-0.17f, 0.1315f, 0f), new Vector3(0.222f, 0.004f, 0.06f), visual);
                Part(cube, "Bico", trimMat, new Vector3(0.292f, 0.06f, 0f), new Vector3(0.012f, 0.03f, 0.2f), visual);
                // Aerofólio na segunda cor, sobre dois apoios.
                Part(cube, "Aerofólio", trimMat, new Vector3(-0.27f, 0.175f, 0f), new Vector3(0.06f, 0.012f, 0.3f), visual);
                foreach (float z in new[] { -0.09f, 0.09f })
                    Part(cube, "Apoio", darkMat, new Vector3(-0.265f, 0.15f, z), new Vector3(0.02f, 0.04f, 0.015f), visual);
            }
            // Rodas: cilindros deitados (o eixo do cilindro, Y local, vira o eixo da roda).
            wheelAxis = Vector3.up;
            int k = 0;
            foreach (float x in new[] { 0.185f, -0.185f })
                foreach (float z in new[] { wheelZ, -wheelZ })
                {
                    var hub = new GameObject("Roda").transform;
                    hub.SetParent(visual, false);
                    hub.localPosition = new Vector3(x, WheelR, z);
                    hub.localRotation = wheelRest[k] = Quaternion.Euler(90f, 0f, 0f);
                    Part(PrimitiveType.Cylinder, "Pneu", darkMat, Vector3.zero, new Vector3(WheelR * 2f, 0.028f, WheelR * 2f), hub);
                    Part(cube, "Calota", trimMat, new Vector3(0f, z > 0f ? 0.03f : -0.03f, 0f), new Vector3(0.05f, 0.004f, 0.012f), hub);
                    // Faixa do pneu: um anel na cor do tipo no flanco de fora (disco colorido com o miolo escuro por cima).
                    float side = z > 0f ? 1f : -1f;      // o Y da roda aponta para +Z do carro: o lado de fora
                    Part(PrimitiveType.Cylinder, "Faixa do pneu", tireMat, new Vector3(0f, 0.0285f * side, 0f), new Vector3(0.096f, 0.001f, 0.096f), hub);
                    Part(PrimitiveType.Cylinder, "Miolo do pneu", darkMat, new Vector3(0f, 0.029f * side, 0f), new Vector3(0.056f, 0.0012f, 0.056f), hub);
                    wheels[k++] = hub;
                }
        }

        // Os modelos 1 e 2 de cada bolha (seção 80), desenhados pelo que a bolha faz. Devolve o z das rodas.
        float BuildModel()
        {
            var cube = PrimitiveType.Cube;
            switch ((int)Body * 3 + Model)
            {
                case 1:
                    // Padrão · Coupé GT: fastback de turismo, faixas duplas no capô, faróis e rabeta levantada.
                    Shape("Casco", bodyMat, P(-0.29f, 0.035f, 0.125f), P(0.29f, 0.035f, 0.11f), P(0.295f, 0.065f, 0.105f),
                                            P(0.20f, 0.095f, 0.12f), P(-0.20f, 0.11f, 0.125f), P(-0.29f, 0.095f, 0.12f));
                    Shape("Cabine", darkMat, P(0.12f, 0.095f, 0.085f), P(0.02f, 0.16f, 0.08f), P(-0.06f, 0.16f, 0.08f), P(-0.24f, 0.105f, 0.09f));
                    Part(cube, "Teto", bodyMat, new Vector3(-0.02f, 0.162f, 0f), new Vector3(0.09f, 0.008f, 0.15f), visual);
                    foreach (float z in new[] { -0.03f, 0.03f })
                        Part(cube, "Faixa", trimMat, new Vector3(0.12f, 0.1f, z), new Vector3(0.18f, 0.006f, 0.025f), visual);
                    foreach (float z in new[] { -0.075f, 0.075f })
                        Part(cube, "Farol", trimMat, new Vector3(0.288f, 0.058f, z), new Vector3(0.016f, 0.018f, 0.04f), visual);
                    Part(cube, "Rabeta", trimMat, new Vector3(-0.282f, 0.108f, 0f), new Vector3(0.025f, 0.018f, 0.24f), visual);
                    return 0.145f;
                case 2:
                    // Padrão · Rali: hatch alto e quadrado, tomada no teto, barra de faróis, número na porta e para-lamas.
                    Part(cube, "Assoalho", bodyMat, new Vector3(0f, 0.068f, 0f), new Vector3(0.56f, 0.062f, 0.25f), visual);
                    Part(cube, "Capô", bodyMat, new Vector3(0.18f, 0.104f, 0f), new Vector3(0.2f, 0.022f, 0.23f), visual);
                    Part(cube, "Cabine", darkMat, new Vector3(-0.05f, 0.14f, 0f), new Vector3(0.3f, 0.082f, 0.21f), visual);
                    Part(cube, "Teto", bodyMat, new Vector3(-0.05f, 0.184f, 0f), new Vector3(0.27f, 0.012f, 0.2f), visual);
                    Part(cube, "Tomada de ar", trimMat, new Vector3(0.02f, 0.196f, 0f), new Vector3(0.07f, 0.016f, 0.05f), visual);
                    Part(cube, "Barra de faróis", darkMat, new Vector3(0.085f, 0.2f, 0f), new Vector3(0.018f, 0.018f, 0.18f), visual);
                    foreach (float z in new[] { -0.06f, -0.02f, 0.02f, 0.06f })
                        Part(cube, "Farol", trimMat, new Vector3(0.096f, 0.2f, z), new Vector3(0.008f, 0.022f, 0.03f), visual);
                    Part(cube, "Aerofólio", bodyMat, new Vector3(-0.205f, 0.19f, 0f), new Vector3(0.04f, 0.01f, 0.22f), visual);
                    foreach (float z in new[] { -1f, 1f })
                    {
                        Part(cube, "Número", trimMat, new Vector3(-0.02f, 0.078f, 0.126f * z), new Vector3(0.07f, 0.042f, 0.003f), visual);
                        Part(cube, "Para-lama", darkMat, new Vector3(-0.238f, 0.042f, 0.14f * z), new Vector3(0.006f, 0.05f, 0.04f), visual);
                    }
                    return 0.145f;
                case 4:
                    // Asa · Protótipo: carro fechado de endurance, rodas cobertas, bolha de cabine, barbatana e asa larga.
                    Shape("Casco", bodyMat, P(-0.30f, 0.035f, 0.15f), P(0.27f, 0.03f, 0.15f), P(0.30f, 0.045f, 0.12f),
                                            P(0.22f, 0.085f, 0.15f), P(-0.30f, 0.095f, 0.15f));
                    Shape("Cabine", darkMat, P(0.12f, 0.085f, 0.06f), P(0.04f, 0.135f, 0.065f), P(-0.06f, 0.14f, 0.06f), P(-0.14f, 0.095f, 0.05f));
                    Shape("Barbatana", bodyMat, P(-0.10f, 0.1f, 0.005f), P(-0.25f, 0.19f, 0.005f), P(-0.29f, 0.19f, 0.005f), P(-0.29f, 0.09f, 0.005f));
                    Part(cube, "Aerofólio", trimMat, new Vector3(-0.265f, 0.2f, 0f), new Vector3(0.07f, 0.012f, 0.31f), visual);
                    foreach (float z in new[] { -1f, 1f })
                    {
                        Part(cube, "Aleta", bodyMat, new Vector3(-0.26f, 0.165f, 0.152f * z), new Vector3(0.09f, 0.08f, 0.008f), visual);
                        Part(cube, "Farol", trimMat, new Vector3(0.262f, 0.062f, 0.1f * z), new Vector3(0.04f, 0.012f, 0.04f), visual);
                        Part(cube, "Entrada de ar", darkMat, new Vector3(-0.02f, 0.07f, 0.151f * z), new Vector3(0.1f, 0.03f, 0.003f), visual);
                    }
                    Part(cube, "Lâmina", trimMat, new Vector3(0.285f, 0.028f, 0f), new Vector3(0.04f, 0.008f, 0.3f), visual);
                    return 0.115f;
                case 5:
                    // Asa · Asa dupla: rodas descobertas, casco fino e uma asa enorme sobre a cabine, mais duas lâminas na frente.
                    Shape("Casco", bodyMat, P(-0.26f, 0.035f, 0.08f), P(0.29f, 0.035f, 0.05f), P(0.29f, 0.055f, 0.05f),
                                            P(0.05f, 0.09f, 0.08f), P(-0.26f, 0.1f, 0.085f));
                    Shape("Cabine", darkMat, P(0.06f, 0.09f, 0.05f), P(-0.02f, 0.14f, 0.055f), P(-0.10f, 0.14f, 0.055f), P(-0.16f, 0.1f, 0.06f));
                    Part(cube, "Asa alta", trimMat, new Vector3(-0.05f, 0.24f, 0f), new Vector3(0.21f, 0.012f, 0.27f), visual);
                    foreach (float z in new[] { -1f, 1f })
                    {
                        Part(cube, "Aleta", bodyMat, new Vector3(-0.05f, 0.22f, 0.137f * z), new Vector3(0.23f, 0.06f, 0.008f), visual);
                        Part(cube, "Apoio", darkMat, new Vector3(0.01f, 0.19f, 0.05f * z), new Vector3(0.012f, 0.1f, 0.012f), visual);
                        Part(cube, "Apoio", darkMat, new Vector3(-0.11f, 0.19f, 0.05f * z), new Vector3(0.012f, 0.1f, 0.012f), visual);
                    }
                    Part(cube, "Lâmina", trimMat, new Vector3(0.275f, 0.03f, 0f), new Vector3(0.05f, 0.01f, 0.31f), visual);
                    Part(cube, "Lâmina alta", trimMat, new Vector3(0.255f, 0.052f, 0f), new Vector3(0.035f, 0.008f, 0.26f), visual);
                    Part(cube, "Aerofólio", trimMat, new Vector3(-0.27f, 0.12f, 0f), new Vector3(0.05f, 0.01f, 0.26f), visual);
                    foreach (float x in new[] { 0.185f, -0.185f })
                        Part(cube, "Eixo", darkMat, new Vector3(x, WheelR, 0f), new Vector3(0.02f, 0.015f, 0.27f), visual);
                    return 0.145f;
                case 7:
                    // Lisa · Flecha: cunha baixa de bico fino, rodas cobertas, duas barbatanas pequenas e difusor atrás.
                    Shape("Casco", bodyMat, P(-0.30f, 0.03f, 0.15f), P(0.30f, 0.03f, 0.1f), P(0.30f, 0.04f, 0.09f), P(-0.30f, 0.1f, 0.15f));
                    Shape("Cabine", darkMat, P(0.10f, 0.065f, 0.06f), P(0.0f, 0.11f, 0.065f), P(-0.12f, 0.11f, 0.06f), P(-0.2f, 0.09f, 0.06f));
                    Part(cube, "Saia", trimMat, new Vector3(0.01f, 0.034f, 0f), new Vector3(0.5f, 0.012f, 0.31f), visual);
                    Part(cube, "Difusor", darkMat, new Vector3(-0.296f, 0.06f, 0f), new Vector3(0.01f, 0.05f, 0.26f), visual);
                    foreach (float z in new[] { -1f, 1f })
                        Part(cube, "Barbatana", trimMat, new Vector3(-0.255f, 0.115f, 0.1f * z), new Vector3(0.07f, 0.04f, 0.006f), visual);
                    return 0.115f;
                default:
                    // Lisa · Bala: charuto de recorde, bico redondo, bolha da cabine lá na frente e barbatana alta.
                    Shape("Casco", bodyMat, P(-0.30f, 0.05f, 0.07f), P(-0.22f, 0.03f, 0.12f), P(0.18f, 0.03f, 0.12f), P(0.30f, 0.06f, 0.03f),
                                            P(0.22f, 0.09f, 0.09f), P(0.0f, 0.12f, 0.12f), P(-0.22f, 0.11f, 0.11f), P(-0.30f, 0.08f, 0.07f));
                    Shape("Cabine", darkMat, P(0.17f, 0.1f, 0.04f), P(0.12f, 0.135f, 0.045f), P(0.05f, 0.135f, 0.045f), P(0.02f, 0.115f, 0.04f));
                    Shape("Barbatana", trimMat, P(-0.05f, 0.115f, 0.005f), P(-0.27f, 0.22f, 0.005f), P(-0.30f, 0.22f, 0.005f), P(-0.30f, 0.09f, 0.005f));
                    foreach (float z in new[] { -1f, 1f })
                        Part(cube, "Faixa", trimMat, new Vector3(-0.02f, 0.075f, 0.121f * z), new Vector3(0.36f, 0.015f, 0.003f), visual);
                    Part(cube, "Bico", trimMat, new Vector3(0.288f, 0.06f, 0f), new Vector3(0.02f, 0.02f, 0.04f), visual);
                    return 0.105f;
            }
        }

        // Modelo importado: frente em +X, chão em y = 0, dentro da caixa de 0,60 × 0,32. Os materiais com nome começando
        // por "Carroceria", "Detalhe" e "Escuro" recebem as cores do carrinho; os objetos "Roda…" giram no eixo Z deles;
        // "Cabine" e "Teto" somem na câmera em primeira pessoa.
        void UseModel(GameObject model)
        {
            var go = Instantiate(model, visual, false);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] != null ? ms[i].name : "";
                    if (n.StartsWith("Carroceria")) ms[i] = bodyMat;
                    else if (n.StartsWith("Detalhe")) ms[i] = trimMat;
                    else if (n.StartsWith("Escuro")) ms[i] = darkMat;
                    else if (n.StartsWith("Pneu")) ms[i] = tireMat;       // a cor do tipo de pneu
                }
                r.sharedMaterials = ms;
            }
            wheelAxis = Vector3.forward;
            int k = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>())
            {
                if (k < 4 && t.name.StartsWith("Roda")) { wheels[k] = t; wheelRest[k++] = t.localRotation; }
                else if (t.name == "Cabine" || t.name == "Teto") cockpit.Add(t.gameObject);
            }
        }

        // A cor principal é a do piloto (ou a do trilho). A segunda é branca, ou escura se a principal for clara.
        public void SetColors(Color main)
        {
            float lum = 0.2126f * main.r + 0.7152f * main.g + 0.0722f * main.b;
            Color trim = lum > 0.6f ? new Color32(0x1E, 0x22, 0x24, 0xFF) : new Color32(0xF2, 0xF2, 0xEC, 0xFF);
            bodyMat.SetColor("_BaseColor", main); bodyMat.color = main;
            trimMat.SetColor("_BaseColor", trim); trimMat.color = trim;
        }

        // Primeira pessoa: esconde o teto e a cabine para a câmera (dentro do carrinho) não ficar presa neles.
        public void SetCockpit(bool hidden)
        {
            foreach (var g in cockpit)
                if (g != null && g.activeSelf == hidden) g.SetActive(!hidden);
        }

        // speed: velocidade ao longo do trilho; off: fora da pista (o carrinho capota enquanto desliza).
        public void Tick(float speed, bool off, float dt)
        {
            spin += speed * dt / WheelR * Mathf.Rad2Deg;
            for (int i = 0; i < 4; i++)
                if (wheels[i]) wheels[i].localRotation = wheelRest[i] * Quaternion.AngleAxis(-spin, wheelAxis);
            // Fora da pista: tomba de lado e fica de rodas para cima até ser recolocado.
            tumble = off ? Mathf.MoveTowards(tumble, 180f, 540f * dt) : 0f;
            visual.localRotation = Quaternion.Euler(tumble, 0f, 0f);
            visual.localPosition = new Vector3(0f, off ? Mathf.Sin(tumble * Mathf.Deg2Rad) * 0.16f + (tumble >= 180f ? 0.17f : tumble / 180f * 0.17f) : 0f, 0f);
        }

        void OnDestroy()
        {
            Destroy(bodyMat); Destroy(trimMat); Destroy(darkMat); Destroy(tireMat); Destroy(silverMat); Destroy(magnetMat);
        }
    }
}
