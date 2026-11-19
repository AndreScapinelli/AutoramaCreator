using System;
using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Grama na mesa da corrida (seção 28.7 do guia): tufos do pacote Wind Grass Interaction desenhados por instância,
    // fora da pista, e os carrinhos empurram a grama em volta deles (shader Autorama/Grama).
    public class GramaMesa
    {
        const float Spacing = 0.12f;       // entre tufos, no mundo (cresce se a mesa é grande)
        const int MaxTufts = 60000;
        const float TuftPerSpacing = 2.4f; // largura do tufo (1,1 na malha) em espaçamentos
        const float CarRadius = 0.35f;     // raio do empurrão, × escala da pista
        const float Refresh = 0.5f;
        const float TuftReach = 0.6f;      // alcance das folhas a partir do centro, × escala do tufo (0,55 da malha e o vento)        // de quanto em quanto tempo procura os carrinhos

        readonly List<Matrix4x4[]> batches = new List<Matrix4x4[]>();
        readonly Vector4[] cars = new Vector4[8];
        CarView[] views = new CarView[0];
        float nextFind, radius;
        static readonly int CarrosId = Shader.PropertyToID("_GramaCarros"), NId = Shader.PropertyToID("_GramaN");

        public void Clear() => batches.Clear();

        // corner: canto da área no mundo; w, h: tamanho no mundo; blocked(x, z): o ponto (no mundo) está embaixo da pista.
        public void Fill(Vector3 corner, float w, float h, float top, float scale, Func<float, float, bool> blocked)
        {
            batches.Clear();
            radius = CarRadius * scale;
            float sp = Mathf.Max(Spacing, Mathf.Sqrt(w * h / MaxTufts));
            float size = sp * TuftPerSpacing / 1.1f;
            var rnd = new System.Random(12345);
            var all = new List<Matrix4x4>();
            for (float z = sp / 2f; z < h; z += sp)
                for (float x = sp / 2f; x < w; x += sp)
                {
                    float px = corner.x + x + ((float)rnd.NextDouble() - 0.5f) * sp * 0.8f;
                    float pz = corner.z + z + ((float)rnd.NextDouble() - 0.5f) * sp * 0.8f;
                    float s = size * (0.75f + 0.5f * (float)rnd.NextDouble());
                    // O tufo inteiro fora da pista, não só o centro: as folhas abrem até 0,55 × s (a malha tem 1,1 de largura).
                    float r = TuftReach * s, d = r * 0.7071f;
                    if (blocked(px, pz) || blocked(px + r, pz) || blocked(px - r, pz) || blocked(px, pz + r) || blocked(px, pz - r)
                        || blocked(px + d, pz + d) || blocked(px - d, pz + d) || blocked(px + d, pz - d) || blocked(px - d, pz - d)) continue;
                    all.Add(Matrix4x4.TRS(new Vector3(px, top, pz), Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), Vector3.one * s));
                }
            for (int i = 0; i < all.Count; i += 1023)
                batches.Add(all.GetRange(i, Mathf.Min(1023, all.Count - i)).ToArray());
        }

        public void Draw(Mesh mesh, Material mat)
        {
            if (batches.Count == 0 || mesh == null || mat == null) return;
            if (Time.unscaledTime >= nextFind) { views = UnityEngine.Object.FindObjectsByType<CarView>(FindObjectsSortMode.None); nextFind = Time.unscaledTime + Refresh; }
            int n = 0;
            foreach (var v in views)
                if (v && v.isActiveAndEnabled && n < cars.Length) { var p = v.transform.position; cars[n++] = new Vector4(p.x, p.y, p.z, radius); }
            Shader.SetGlobalVectorArray(CarrosId, cars);
            Shader.SetGlobalFloat(NId, n);
            var rp = new RenderParams(mat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true };
            foreach (var b in batches) Graphics.RenderMeshInstanced(rp, mesh, 0, b);
        }
    }
}
