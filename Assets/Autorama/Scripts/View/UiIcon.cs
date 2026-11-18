using UnityEngine;
using UnityEngine.UIElements;

namespace Autorama
{
    // Ícone de traço desenhado com o Painter2D (proposta D de docs/PROPOSTAS-UI.md): no lugar dos glifos Unicode,
    // que a fonte nem sempre tem. Desenho numa caixa de 24 × 24, na cor do texto (color da USS), traço de 2.
    public class UiIcon : VisualElement
    {
        public string Kind { get; private set; }

        public UiIcon(string kind, string cls = "ui-icon")
        {
            Kind = kind;
            pickingMode = PickingMode.Ignore;
            if (cls != null) foreach (var c in cls.Split(' ')) AddToClassList(c);
            generateVisualContent += Draw;
        }

        public void Set(string kind) { Kind = kind; MarkDirtyRepaint(); }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float k = Mathf.Min(r.width, r.height) / 24f;
            if (k <= 0f) return;
            var o = new Vector2(r.x + (r.width - 24f * k) / 2f, r.y + (r.height - 24f * k) / 2f);
            var p = ctx.painter2D;
            p.strokeColor = resolvedStyle.color;
            p.fillColor = resolvedStyle.color;
            p.lineWidth = Mathf.Max(1.2f, 2f * k);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            Vector2 P(float x, float y) => o + new Vector2(x, y) * k;
            void Line(params float[] xy)
            {
                p.BeginPath(); p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                p.Stroke();
            }
            void Shape(bool fill, params float[] xy)
            {
                p.BeginPath(); p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                p.ClosePath();
                if (fill) p.Fill(); else p.Stroke();
            }
            void Circle(float x, float y, float rad, bool fill = false)
            {
                p.BeginPath(); p.Arc(P(x, y), rad * k, 0f, 360f);
                if (fill) p.Fill(); else p.Stroke();
            }

            switch (Kind)
            {
                case "construir":      // degraus de peças
                    Shape(false, 3, 21, 3, 15, 9, 15, 9, 21); Shape(false, 9, 21, 9, 11, 15, 11, 15, 21); Shape(false, 15, 21, 15, 7, 21, 7, 21, 21);
                    Line(3, 9, 8, 4, 12, 7); break;
                case "carreira":       // bandeira
                    Line(5, 21, 5, 3); Shape(false, 5, 4, 18, 4, 15.5f, 8, 18, 12, 5, 12); break;
                case "correr":
                    Shape(true, 7, 4, 20, 12, 7, 20); break;
                case "campeonato":     // troféu
                    Line(8, 4, 16, 4, 16, 9); p.BeginPath(); p.Arc(P(12, 9), 4 * k, 0f, 180f); p.Stroke(); Line(8, 9, 8, 4);
                    Line(8, 6, 4, 6, 4, 8, 8, 11); Line(16, 6, 20, 6, 20, 8, 16, 11); Line(12, 13, 12, 17); Line(8, 21, 16, 21); Line(9, 17, 15, 17); break;
                case "online":         // globo
                    Circle(12, 12, 9); Line(3, 12, 21, 12);
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.BezierCurveTo(P(16, 7), P(16, 17), P(12, 21)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.BezierCurveTo(P(8, 7), P(8, 17), P(12, 21)); p.Stroke(); break;
                case "pecas":          // peça de pista
                    Shape(false, 4, 8, 20, 8, 20, 16, 4, 16); Line(4, 11, 20, 11); Line(4, 13, 20, 13); break;
                case "opcoes":         // controles deslizantes
                    Line(4, 7, 20, 7); Line(4, 17, 20, 17); Circle(9, 7, 2.2f, true); Circle(15, 17, 2.2f, true); break;
                case "sair":
                    Line(14, 4, 5, 4, 5, 20, 14, 20); Line(10, 12, 21, 12); Line(17, 8, 21, 12, 17, 16); break;
                case "inicio":         // casa
                    Line(3, 11, 12, 3, 21, 11); Line(6, 9, 6, 20, 18, 20, 18, 9); break;
                case "esconder":
                    Line(15, 5, 8, 12, 15, 19); break;
                case "painel":
                    Line(4, 6, 20, 6); Line(4, 12, 20, 12); Line(4, 18, 20, 18); break;
                case "desfazer":
                    Line(8, 5, 4, 9, 8, 13); p.BeginPath(); p.MoveTo(P(4, 9)); p.LineTo(P(14, 9)); p.BezierCurveTo(P(22, 9), P(22, 19), P(14, 19)); p.LineTo(P(9, 19)); p.Stroke(); break;
                case "refazer":
                    Line(16, 5, 20, 9, 16, 13); p.BeginPath(); p.MoveTo(P(20, 9)); p.LineTo(P(10, 9)); p.BezierCurveTo(P(2, 9), P(2, 19), P(10, 19)); p.LineTo(P(15, 19)); p.Stroke(); break;
                case "inverter":       // setas opostas
                    Line(4, 8, 18, 8); Line(15, 5, 18, 8, 15, 11); Line(20, 16, 6, 16); Line(9, 13, 6, 16, 9, 19); break;
                case "apagar":         // lixeira
                    Line(4, 6, 20, 6); Line(9, 6, 9, 3, 15, 3, 15, 6); Line(6, 6, 7, 21, 17, 21, 18, 6); Line(10, 10, 10, 17); Line(14, 10, 14, 17); break;
                case "enquadrar":
                    Line(4, 9, 4, 4, 9, 4); Line(15, 4, 20, 4, 20, 9); Line(20, 15, 20, 20, 15, 20); Line(9, 20, 4, 20, 4, 15); break;
                case "girar":
                    p.BeginPath(); p.Arc(P(12, 12), 8 * k, -60f, 220f); p.Stroke(); Line(16, 3, 16.5f, 5.2f, 14, 6.5f); break;
                case "menos":
                    Line(5, 12, 19, 12); break;
                case "mais":
                    Line(5, 12, 19, 12); Line(12, 5, 12, 19); break;
                case "parar":
                    Shape(true, 6, 6, 18, 6, 18, 18, 6, 18); break;
                case "fechar":         // circuito fechado (laço)
                    p.BeginPath(); p.Arc(P(12, 12), 8 * k, 200f, 520f); p.Stroke(); Line(3, 9, 4.5f, 12.5f, 8, 11); break;
                case "camera":
                    Shape(false, 3, 8, 16, 8, 16, 18, 3, 18); Shape(false, 16, 11, 21, 8, 21, 18, 16, 15); break;
                case "carro":
                    Line(4, 15, 6, 9, 18, 9, 20, 15, 20, 18, 4, 18, 4, 15, 20, 15); Line(7, 18, 7, 20); Line(17, 18, 17, 20); break;
                case "estrela":
                    Shape(true, 12, 3, 14.6f, 9, 21, 9.4f, 16, 13.6f, 17.6f, 20, 12, 16.4f, 6.4f, 20, 8, 13.6f, 3, 9.4f, 9.4f, 9); break;
            }
        }
    }
}
