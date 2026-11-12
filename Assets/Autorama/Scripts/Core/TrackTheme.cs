using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Materiais e ícones da pista. O menu Autorama > Montar cena do construtor cria e preenche este asset.
    [CreateAssetMenu(menuName = "Autorama/Tema da pista")]
    public class TrackTheme : ScriptableObject
    {
        [Header("Superfície da pista")]
        public Material straight;
        public Material curveRight, curveLeft;      // curva plana: zebra do lado de fora
        public Material bankRight, bankLeft;        // curva inclinada: listras amarelas e muro
        public Material cross;                      // cruzamento: a peça inteira numa imagem, com as fendas em X
        public Material crossShort;                 // o mesmo X numa reta curta

        [Header("Detalhes")]
        public Material checker;                    // bandeirada da largada
        public Material guard;                      // mureta das peças no alto
        public Material support;                    // pilares
        public Material arrow;                      // seta do ponto de inserção
        public Material side, side4;                // laterais e fundo da base da pista (3D, seção 28); sem eles, usa o dos pilares

        [Header("Pista de 4 carros (arte própria, seção 25)")]
        public Material straight4;
        public Material curveRight4, curveLeft4;    // zebra azul e branca do lado de fora
        public Material bankRight4, bankLeft4;      // chevrons brancos e muro azul
        public Material cross4, crossShort4;        // os 4 trilhos em S, losango no meio
        public Material start4;                     // a base inteira: boxes numerados e bandeirada
        public Material guard4;                     // muro azul listrado das peças no alto
        public Material support4;                   // pilares de aço escuro
        public Material board4;                     // mesa em tom frio
        public Material[] cars34 = new Material[0]; // carrinhos 3 (verde) e 4 (amarelo), mesmo molde dos de 2

        [Header("Pista de 6 carros (arte própria, seção 31)")]
        public WideArt art6 = new WideArt();
        public Material[] cars56 = new Material[0]; // carrinhos 5 (roxo) e 6 (ciano)

        [Header("Pista de 8 carros (arte própria, seção 43)")]
        public WideArt art8 = new WideArt();
        public Material[] cars78 = new Material[0]; // carrinhos 7 (laranja) e 8 (rosa)

        [Header("Destaques")]
        public Material selected;
        public Material overlap;

        [Header("Ícones da paleta")]
        public List<Texture2D> icons = new List<Texture2D>();

        // As superfícies de uma pista larga (a de 6 usa este conjunto; a de 4 tem os campos soltos acima, de antes).
        [System.Serializable]
        public class WideArt
        {
            public Material straight, curveRight, curveLeft, bankRight, bankLeft, cross, crossShort, start, guard, support, board;
        }

        public bool Has4 => straight4 != null;
        public bool Has6 => art6 != null && art6.straight != null;
        // Existe arte própria para esse tipo de pista? (2 sempre; 4 e 6 se o menu Atualizar materiais já rodou.)
        public bool Has8 => art8 != null && art8.straight != null;
        public bool HasWide(int faixas) => faixas == 8 ? Has8 : faixas == 6 ? Has6 : faixas == 4 && Has4;
        WideArt Art(int faixas) => faixas == 8 ? art8 : faixas == 6 ? art6 : null;

        public Material Guard(int faixas) => Art(faixas)?.guard != null ? Art(faixas).guard : faixas >= 4 && guard4 != null ? guard4 : guard;
        public Material Support(int faixas) => Art(faixas)?.support != null ? Art(faixas).support : faixas >= 4 && support4 != null ? support4 : support;
        public Material Board(int faixas) => Art(faixas) != null ? Art(faixas).board : faixas == 4 ? board4 : null;

        public Material Surface(PieceDef d, int dir, int faixas)
        {
            if (faixas >= 6 && HasWide(faixas))
            {
                var a = Art(faixas);
                if (d.IsStart && a.start != null) return a.start;
                if (d.IsCross && d.Length < 3f && a.crossShort != null) return a.crossShort;
                if (d.IsCross && a.cross != null) return a.cross;
                if (!d.IsCurve) return a.straight;
                if (d.Banked) return dir > 0 ? a.bankRight : a.bankLeft;
                return dir > 0 ? a.curveRight : a.curveLeft;
            }
            if (faixas == 4 && Has4)
            {
                if (d.IsStart && start4 != null) return start4;
                if (d.IsCross && d.Length < 3f && crossShort4 != null) return crossShort4;
                if (d.IsCross && cross4 != null) return cross4;
                if (!d.IsCurve) return straight4;
                if (d.Banked) return dir > 0 ? bankRight4 : bankLeft4;
                return dir > 0 ? curveRight4 : curveLeft4;
            }
            return Surface(d, dir);
        }

        public Material Surface(PieceDef d, int dir)
        {
            if (d.IsCross && d.Length < 3f && crossShort != null) return crossShort;
            if (d.IsCross && cross != null) return cross;
            if (!d.IsCurve) return straight;
            if (d.Banked) return dir > 0 ? bankRight : bankLeft;
            return dir > 0 ? curveRight : curveLeft;
        }

        public Texture2D Icon(string name)
        {
            foreach (var t in icons) if (t != null && t.name == name) return t;
            return null;
        }
    }
}
