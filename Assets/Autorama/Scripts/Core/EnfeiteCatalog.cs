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
            L.N("Árvores"), L.N("Pedras"), L.N("Árvore redonda"), L.N("Pinheiro"), L.N("Bétula"), L.N("Árvore seca"),
            L.N("Pedra"), L.N("Pedrinha"), L.N("Pedregulho"), L.N("Monte de pedras"),
        };

        static EnfeiteCatalog cache;
        public static EnfeiteCatalog Get => cache ? cache : cache = Resources.Load<EnfeiteCatalog>("Cosmeticos/Catalogo");
        public static IReadOnlyList<EnfeiteDef> Itens => Get ? Get.itens : (IReadOnlyList<EnfeiteDef>)Array.Empty<EnfeiteDef>();
        public static EnfeiteDef ById(string id) { foreach (var d in Itens) if (d.id == id) return d; return null; }
    }
}
