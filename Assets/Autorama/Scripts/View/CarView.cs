using UnityEngine;

namespace Autorama
{
    // O carrinho 3D (seção 28 do guia). Por enquanto é um carrinho low-poly de duas cores montado com cubos e
    // cilindros; quando houver um modelo importado (campo "model"), ele entra no lugar e as cores vão para os
    // materiais dele. A frente aponta para +X, como o carrinho antigo; o chão do carrinho é y = 0.
    // Caixa de colisão da física: 0,60 × 0,32 (comprimento × largura).
    public class CarView : MonoBehaviour
    {
        Transform visual;                          // filho que tomba quando o carrinho sai da pista
        readonly Transform[] wheels = new Transform[4];
        Material bodyMat, trimMat, darkMat;
        float spin, tumble;

        const float WheelR = 0.05f;

        // lit: um material URP/Lit liso (o dos pilares serve) para copiar; main e trim: as duas cores.
        public static CarView Create(string name, Material lit, Color main, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<CarView>();
            v.Build(lit);
            v.SetColors(main);
            return v;
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
            return go.transform;
        }

        void Build(Material lit)
        {
            bodyMat = Copy(lit, Color.red, 0.45f);
            trimMat = Copy(lit, Color.white, 0.3f);
            darkMat = Copy(lit, new Color32(0x16, 0x19, 0x1B, 0xFF), 0.2f);
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            var cube = PrimitiveType.Cube;
            // Carroceria: assoalho largo, capô mais baixo na frente, cabine escura e faixa da segunda cor por cima.
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
            // Rodas: cilindros deitados (o eixo do cilindro, Y local, vira o eixo da roda).
            int i = 0;
            foreach (float x in new[] { 0.185f, -0.185f })
                foreach (float z in new[] { 0.145f, -0.145f })
                {
                    var hub = new GameObject("Roda").transform;
                    hub.SetParent(visual, false);
                    hub.localPosition = new Vector3(x, WheelR, z);
                    hub.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Part(PrimitiveType.Cylinder, "Pneu", darkMat, Vector3.zero, new Vector3(WheelR * 2f, 0.028f, WheelR * 2f), hub);
                    Part(cube, "Calota", trimMat, new Vector3(0f, z > 0f ? -0.03f : 0.03f, 0f), new Vector3(0.05f, 0.004f, 0.012f), hub);
                    wheels[i++] = hub;
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

        // speed: velocidade ao longo do trilho; off: fora da pista (o carrinho capota enquanto desliza).
        // Primeira pessoa: esconde o teto e a cabine para a câmera (dentro do carrinho) não ficar presa neles.
        public void SetCockpit(bool hidden)
        {
            if (visual == null) return;
            foreach (string n in new[] { "Teto", "Cabine" })
            {
                var t = visual.Find(n);
                if (t != null && t.gameObject.activeSelf == hidden) t.gameObject.SetActive(!hidden);
            }
        }

        public void Tick(float speed, bool off, float dt)
        {
            spin += speed * dt / WheelR * Mathf.Rad2Deg;
            foreach (var w in wheels) if (w) w.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, -spin, 0f);
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
