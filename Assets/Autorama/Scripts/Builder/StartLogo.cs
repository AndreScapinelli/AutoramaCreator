using UnityEngine;
using UnityEngine.UIElements;

namespace Autorama
{
    // Logo do menu inicial (seção 85): entra girando e quicando, flutua devagar e, de tempos em tempos, um brilho
    // passa em diagonal só por cima das letras. O brilho é a máscara branca (logo-brilho.png) desenhada numa malha
    // em grade cuja transparência acompanha a faixa.
    public class StartLogo : VisualElement
    {
        const float Aspect = 785f / 1766f;      // altura / largura do logo.png
        const float InTime = 0.75f;             // entrada: cai de cima girando e quica
        const float FloatPeriod = 3.4f, FloatPx = 4f;
        const float ShineFirst = 0.55f, ShineEvery = 4.5f, ShineTime = 0.9f, ShinePeak = 0.75f;
        const int Cols = 48, Rows = 6;

        readonly VisualElement shineSrc;        // só carrega a textura do brilho (tinta transparente)
        readonly IVisualElementScheduledItem tick;
        float t0, shine = -1f;                  // shine: posição da faixa de 0 a 1, ou < 0 sem brilho

        public StartLogo()
        {
            AddToClassList("start-logo");
            pickingMode = PickingMode.Ignore;
            shineSrc = new VisualElement { pickingMode = PickingMode.Ignore };
            shineSrc.AddToClassList("start-logo-brilho");
            Add(shineSrc);
            generateVisualContent += DrawShine;
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float h = Mathf.Round(resolvedStyle.width * Aspect);
                if (Mathf.Abs(resolvedStyle.height - h) > 0.5f) style.height = h;
            });
            tick = schedule.Execute(Step).Every(16);
            Play();
        }

        public void Play() { t0 = Time.unscaledTime; tick.Resume(); Step(); }
        public void Stop() => tick.Pause();

        void Step()
        {
            float t = Time.unscaledTime - t0;
            if (t < InTime)
            {
                float k = t / InTime;
                float s = Mathf.LerpUnclamped(0.35f, 1f, BackOut(k));
                style.opacity = Mathf.Clamp01(k * 2.5f);
                style.scale = new Scale(new Vector3(s, s, 1f));
                style.rotate = new Rotate(Mathf.Lerp(-7f, 0f, BackOut(k)));
                style.translate = new Translate(0f, Mathf.Lerp(-60f, 0f, 1f - (1f - k) * (1f - k) * (1f - k)));
            }
            else
            {
                float w = (t - InTime) * 2f * Mathf.PI / FloatPeriod;
                float s = 1f + 0.012f * Mathf.Sin(w + 1f);
                style.opacity = 1f;
                style.scale = new Scale(new Vector3(s, s, 1f));
                style.rotate = new Rotate(0.5f * Mathf.Sin(w * 0.5f));
                style.translate = new Translate(0f, FloatPx * Mathf.Sin(w));
            }
            float ts = t - ShineFirst;
            float old = shine;
            shine = ts >= 0f && ts % ShineEvery < ShineTime ? ts % ShineEvery / ShineTime : -1f;
            if (shine >= 0f || old >= 0f) MarkDirtyRepaint();
        }

        static float BackOut(float k) { const float c = 1.9f; k -= 1f; return 1f + (c + 1f) * k * k * k + c * k * k; }

        void DrawShine(MeshGenerationContext mgc)
        {
            var tex = shineSrc.resolvedStyle.backgroundImage.texture;
            float w = contentRect.width, h = contentRect.height;
            if (shine < 0f || tex == null || w < 1f) return;
            var md = mgc.Allocate((Cols + 1) * (Rows + 1), Cols * Rows * 6, tex);
            var uv = md.uvRegion;
            float c = Mathf.Lerp(-0.35f, 1.6f, shine), half = 0.13f;   // centro e meia largura da faixa, em larguras
            for (int r = 0; r <= Rows; r++)
                for (int i = 0; i <= Cols; i++)
                {
                    float u = (float)i / Cols, v = (float)r / Rows;
                    float d = Mathf.Abs(u + 0.25f * v - c) / half;      // faixa inclinada
                    float a = d < 1f ? ShinePeak * (1f - d) * (1f - d) : 0f;
                    md.SetNextVertex(new Vertex
                    {
                        position = new Vector3(u * w, v * h, Vertex.nearZ),
                        tint = new Color(1f, 1f, 1f, a),
                        uv = new Vector2(uv.x + u * uv.width, uv.y + (1f - v) * uv.height),
                    });
                }
            for (int r = 0; r < Rows; r++)
                for (int i = 0; i < Cols; i++)
                {
                    ushort a = (ushort)(r * (Cols + 1) + i), b = (ushort)(a + 1), d = (ushort)(a + Cols + 1), e = (ushort)(d + 1);
                    md.SetNextIndex(a); md.SetNextIndex(b); md.SetNextIndex(d);
                    md.SetNextIndex(b); md.SetNextIndex(e); md.SetNextIndex(d);
                }
        }
    }
}
