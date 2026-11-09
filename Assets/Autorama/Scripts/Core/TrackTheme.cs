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

        [Header("Destaques")]
        public Material selected;
        public Material overlap;

        [Header("Ícones da paleta")]
        public List<Texture2D> icons = new List<Texture2D>();

        public bool Has4 => straight4 != null;

        public Material Surface(PieceDef d, int dir, int faixas)
        {
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
