using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Autorama
{
    // Idiomas da interface (seção 88 do guia). O texto em português é a chave: L.T("Largar (Enter)") procura a
    // tradução na tabela do idioma (Resources/Idiomas/<código>.txt) e, se não achar, devolve o português.
    // L.F é o string.Format com o modelo traduzido ("Voltas: {0}"). A tabela é uma linha por texto:
    // português, tabulação, tradução; \n e \t escritos assim. O idioma fica no PlayerPrefs e vale ao reabrir o jogo.
    // ferramentas/idiomas.js lista o que falta traduzir em cada tabela.
    public static class L
    {
        public static readonly string[] Codigos = { "pt", "en", "es", "fr", "de", "ja" };
        public static readonly string[] Nomes = { "Português", "English", "Español", "Français", "Deutsch", "日本語" };
        const string Chave = "autorama.idioma";

        static Dictionary<string, string> tabela;
        static string atual;
        static CultureInfo cultura;

        public static string Atual { get { Garante(); return atual; } }
        public static int Indice => System.Array.IndexOf(Codigos, Atual);

        // Números com a vírgula ou o ponto do idioma (1,25 s · 1.25 s).
        public static CultureInfo Cultura { get { Garante(); return cultura; } }

        static void Garante()
        {
            if (atual != null) return;
            atual = PlayerPrefs.GetString(Chave, "");
            if (System.Array.IndexOf(Codigos, atual) < 0) atual = DoSistema();
            cultura = new CultureInfo(atual switch { "pt" => "pt-BR", "en" => "en-US", "es" => "es-ES", "fr" => "fr-FR", "de" => "de-DE", _ => "ja-JP" });
            tabela = new Dictionary<string, string>();
            if (atual == "pt") return;
            var arq = Resources.Load<UnityEngine.TextAsset>("Idiomas/" + atual);
            if (arq == null) return;
            foreach (var linha in arq.text.Split('\n'))
            {
                int tab = linha.IndexOf('\t');
                if (tab <= 0) continue;
                string tr = Des(linha.Substring(tab + 1).TrimEnd('\r'));
                if (tr != "") tabela[Des(linha.Substring(0, tab))] = tr;
            }
        }

        static string Des(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");

        static string DoSistema() => Application.systemLanguage switch
        {
            SystemLanguage.Portuguese => "pt",
            SystemLanguage.Spanish => "es",
            SystemLanguage.French => "fr",
            SystemLanguage.German => "de",
            SystemLanguage.Japanese => "ja",
            _ => "en",
        };

        // Grava a escolha; a interface muda ao reabrir o jogo.
        public static void Escolhe(string codigo) { PlayerPrefs.SetString(Chave, codigo); PlayerPrefs.Save(); }
        public static string Escolhido => PlayerPrefs.GetString(Chave, Atual);

        public static string T(string pt)
        {
            Garante();
            return pt != null && tabela.TryGetValue(pt, out var tr) ? tr : pt;
        }

        public static string F(string pt, params object[] args) => string.Format(Cultura, T(pt), args);

        // Marca um texto guardado em campo ou tabela (nome de peça, de carro…) para a lista de traduções, sem traduzir
        // na hora: inicializador de campo roda antes de o PlayerPrefs poder ser lido. Quem mostra usa L.T(valor).
        public static string N(string pt) => pt;

        // O japonês precisa de uma fonte com kanji: a Noto Sans JP (OFL) vira a fonte do painel inteiro.
        static FontAsset fonteJa;
        public static void AplicaFonte(VisualElement root)
        {
            if (Atual != "ja" || root == null) return;
            if (fonteJa == null)
            {
                var f = Resources.Load<Font>("Idiomas/NotoSansJP-Regular");
                if (f == null) return;
                fonteJa = FontAsset.CreateFontAsset(f);
            }
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(fonteJa);
        }
    }
}
