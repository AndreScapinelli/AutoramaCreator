using System.IO;
using UnityEditor;
using UnityEngine;

namespace Autorama
{
    // Gera os prefabs das arquibancadas (seção 101) e põe no catálogo de cosméticos os que faltam.
    public static class ArquibancadaGen
    {
        const string Pasta = "Assets/Autorama/Prefabs/Cosmeticos";

        static readonly (string id, string nome, int filas, int colunas, bool teto, int preco)[] Itens =
        {
            ("arquibancada-pequena", "Arquibancada pequena", 2, 8, false, 150),
            ("arquibancada-media", "Arquibancada média", 3, 16, false, 250),
            ("arquibancada-grande", "Arquibancada grande", 4, 24, false, 400),
            ("arquibancada-coberta", "Arquibancada coberta", 3, 16, true, 350),
        };

        [MenuItem("Autorama/Gerar arquibancadas")]
        public static string Gerar()
        {
            if (!AssetDatabase.IsValidFolder(Pasta)) { Directory.CreateDirectory(Pasta); AssetDatabase.Refresh(); }
            var concreto = Mat("Arquibancada concreto", new Color(0.62f, 0.62f, 0.6f));
            var assento = Mat("Arquibancada assento", new Color(0.2f, 0.3f, 0.55f));
            var telhado = Mat("Arquibancada telhado", new Color(0.8f, 0.15f, 0.12f));
            var cat = EnfeiteCatalog.Get;
            foreach (var it in Itens)
            {
                var go = new GameObject(it.id);
                float largura = it.colunas * Torcida.Passo + 0.2f, alto = 0f;
                for (int f = 0; f < it.filas; f++)
                {
                    float h = (f + 1) * Torcida.Degrau;
                    Cubo(go, concreto, new Vector3(0f, h * 0.5f, (f + 0.5f) * Torcida.Fundo), new Vector3(largura, h, Torcida.Fundo));
                    Cubo(go, assento, new Vector3(0f, h + 0.02f, (f + 0.75f) * Torcida.Fundo), new Vector3(largura - 0.1f, 0.04f, Torcida.Fundo * 0.4f));
                    alto = h;
                }
                float fundo = it.filas * Torcida.Fundo;
                Cubo(go, concreto, new Vector3(0f, alto + 0.3f, fundo - 0.04f), new Vector3(largura, 0.6f, 0.08f));   // muro de trás
                alto += 0.6f;
                if (it.teto)
                {
                    float topo = alto + 0.55f;
                    for (int s = -1; s <= 1; s += 2)
                        Cubo(go, concreto, new Vector3(s * (largura * 0.5f - 0.05f), topo * 0.5f, fundo - 0.06f), new Vector3(0.08f, topo, 0.08f));
                    var t = Cubo(go, telhado, new Vector3(0f, topo, fundo * 0.5f), new Vector3(largura + 0.2f, 0.06f, fundo + 0.3f));
                    t.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
                    alto = topo + 0.1f;
                }
                var tor = go.AddComponent<Torcida>();
                tor.filas = it.filas;
                tor.colunas = it.colunas;
                tor.teto = it.teto;
                string caminho = $"{Pasta}/{it.id}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, caminho);
                Object.DestroyImmediate(go);

                var def = EnfeiteCatalog.ById(it.id);
                if (def == null) { def = new EnfeiteDef { id = it.id }; cat.itens.Add(def); }
                def.nome = it.nome; def.grupo = "Arquibancadas"; def.prefab = prefab; def.preco = it.preco; def.altura = alto * Torcida.Escala;
            }
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            return $"{Itens.Length} arquibancadas em {Pasta}";
        }

        static GameObject Cubo(GameObject pai, Material m, Vector3 pos, Vector3 tam)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.transform.SetParent(pai.transform, false);
            c.transform.localPosition = pos;
            c.transform.localScale = tam;
            c.GetComponent<MeshRenderer>().sharedMaterial = m;
            return c;
        }

        static Material Mat(string nome, Color c)
        {
            string p = $"{Pasta}/{nome}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
