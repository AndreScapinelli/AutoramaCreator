using System;
using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Catálogo dos cosméticos (seção 97): Resources/Cosmeticos/Catalogo.asset.
    [CreateAssetMenu(menuName = "Autorama/Catálogo de cosméticos")]
    public class EnfeiteCatalog : ScriptableObject
    {
        public List<EnfeiteDef> itens = new List<EnfeiteDef>();

        // Nomes e grupos do Catalogo.asset, para o ferramentas/idiomas.js achar (item novo: ponha o nome aqui também).
        static readonly string[] Textos =
        {
            L.N("Árvores"), L.N("Pedras"), L.N("Árvore frondosa"), L.N("Pinheiro"), L.N("Bétula"), L.N("Árvore seca"),
            L.N("Pedra"), L.N("Pedrinha"), L.N("Pedregulho"), L.N("Monte de pedras"),
            L.N("Arquibancadas"), L.N("Arquibancada pequena"), L.N("Arquibancada média"), L.N("Arquibancada grande"), L.N("Arquibancada coberta"),
        };

        static EnfeiteCatalog cache;
        public static EnfeiteCatalog Get => cache ? cache : cache = Resources.Load<EnfeiteCatalog>("Cosmeticos/Catalogo");
        public static IReadOnlyList<EnfeiteDef> Itens => Get ? Get.itens : (IReadOnlyList<EnfeiteDef>)Array.Empty<EnfeiteDef>();
        public static EnfeiteDef ById(string id) { foreach (var d in Itens) if (d.id == id) return d; return null; }

        // Preço de um enfeite na mesa: a arquibancada esticada paga o preço do catálogo a cada "colunas" do prefab (seção 101).
        public static int Preco(Enfeite e)
        {
            var d = ById(e.id);
            if (d == null) return 0;
            var t = d.prefab ? d.prefab.GetComponent<Torcida>() : null;
            if (t == null || e.colunas <= 0) return d.preco;
            return Mathf.Max(1, Mathf.RoundToInt(d.preco * e.colunas / (float)t.colunas));
        }
    }
}
