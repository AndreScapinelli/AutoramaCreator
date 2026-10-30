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

        [Header("Destaques")]
        public Material selected;
        public Material overlap;

        [Header("Ícones da paleta")]
        public List<Texture2D> icons = new List<Texture2D>();

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
