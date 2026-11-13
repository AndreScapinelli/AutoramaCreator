using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // A bolha do carrinho (seção 46 do guia). O número vai para o JSON: 0 = padrão.
    public enum CarBody { Padrao, Asa, Lisa }

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
        Material bodyMat, trimMat, darkMat;
        GameObject[] models;                       // modelo importado de cada bolha (índice = CarBody); sem ele, por código
        float spin, tumble;

        public CarBody Body { get; private set; }

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
            v.visual = new GameObject("Visual").transform;
            v.visual.SetParent(go.transform, false);
            v.Build();
            v.SetColors(main);
            return v;
        }

        public void SetBody(CarBody body)
        {
            if (body == Body) return;
            Body = body;
            for (int i = visual.childCount - 1; i >= 0; i--)
            {
                var old = visual.GetChild(i);
                old.SetParent(null);
                Destroy(old.gameObject);
            }
            Build();
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
            string key = Body + "/" + name;
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
            var model = models != null && (int)Body < models.Length ? models[(int)Body] : null;
            if (model != null) { UseModel(model); return; }

            var cube = PrimitiveType.Cube;
            float wheelZ = 0.145f;
            if (Body == CarBody.Asa)
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
                    Part(cube, "Calota", trimMat, new Vector3(0f, z > 0f ? -0.03f : 0.03f, 0f), new Vector3(0.05f, 0.004f, 0.012f), hub);
                    wheels[k++] = hub;
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
            Destroy(bodyMat); Destroy(trimMat); Destroy(darkMat);
        }
    }
}
