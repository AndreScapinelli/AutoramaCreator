using System;
using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Cenas do enredo (seção 106, docs/enredo.md): falas em quadrinho por cima da carreira, em Resources/Carreira/cenas.json.
    // quando: "regiao" (no = id da região; ao abrir o mapa dela pela 1ª vez), "antes" (no = id do nó; ao correr pela
    // 1ª vez) ou "depois" (quando o nó chega às estrelas pedidas pela 1ª vez). quem "" = narração.
    [Serializable] public class Fala { public string quem, texto; }
    [Serializable] public class CenaDef { public string id, quando, no; public int estrelas; public Fala[] falas; }
    [Serializable] public class Personagem { public string nome, cor; }
    [Serializable] public class CenasDef { public Personagem[] elenco; public CenaDef[] cenas; }

    public static class Cenas
    {
        static CenasDef def;
        public static CenasDef Def => def ??= Traduz(Load());

        static CenasDef Load()
        {
            var t = Resources.Load<TextAsset>("Carreira/cenas");
            var d = t != null ? JsonUtility.FromJson<CenasDef>(t.text) : null;
            d ??= new CenasDef();
            d.elenco ??= new Personagem[0];
            d.cenas ??= new CenaDef[0];
            return d;
        }

        // O texto no idioma do jogo (seção 88); o nome de quem fala fica em português (a chave da cor) e é traduzido na tela.
        static CenasDef Traduz(CenasDef d)
        {
            foreach (var c in d.cenas) if (c.falas != null) foreach (var f in c.falas) f.texto = L.T(f.texto);
            return d;
        }

        public static void Reload() => def = null;

        public static int Estrelas(CenaDef c) => c.estrelas > 0 ? c.estrelas : 1;

        public static IEnumerable<CenaDef> De(string quando, string no)
        {
            foreach (var c in Def.cenas) if (c.quando == quando && c.no == no && c.falas != null && c.falas.Length > 0) yield return c;
        }

        public static Color Cor(string quem)
        {
            foreach (var p in Def.elenco) if (p.nome == quem && ColorUtility.TryParseHtmlString(p.cor, out var c)) return c;
            return new Color(0.55f, 0.6f, 0.58f);
        }

        // As iniciais do retrato: "Dona Célia" → DC, "Dr. Curva" → DC, "Ianzin" → IA.
        public static string Iniciais(string nome)
        {
            var w = nome.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 0) return "";
            if (w.Length == 1) return w[0].Substring(0, Math.Min(2, w[0].Length)).ToUpperInvariant();
            return (w[0].Substring(0, 1) + w[w.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }
    }
}
