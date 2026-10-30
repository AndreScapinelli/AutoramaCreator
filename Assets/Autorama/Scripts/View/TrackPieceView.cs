using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // A malha de uma peça: uma faixa ao longo da linha central com a textura da pista
    // (u atravessa a pista da esquerda para a direita, v repete a cada célula).
    // A altura vem do nível da peça (rampas sobem/descem) e da inclinação das curvas inclinadas.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TrackPieceView : MonoBehaviour
    {
        public int index;

        Mesh mesh;
        MeshRenderer overlay;
        readonly List<GameObject> pillars = new List<GameObject>();

        static readonly List<Vector3> V = new List<Vector3>();
        static readonly List<Vector2> UV = new List<Vector2>();
        static readonly List<int> Surface = new List<int>(), Checker = new List<int>(), Guard = new List<int>();

        // only: desenha tudo com este material (peça fantasma), sem pilares.
        public void Build(Segment s, TrackTheme theme, Material only = null)
        {
            V.Clear(); UV.Clear(); Surface.Clear(); Checker.Clear(); Guard.Clear();
            var d = s.def;
            int dir = s.piece.dir;
            int n = TrackMath.Steps(d, 4f);
            var ts = new float[n + 1];
            for (int k = 0; k <= n; k++) ts[k] = k / (float)n;

            // A mesma altura que os carrinhos usam (nível da peça + inclinação das curvas inclinadas).
            float Y(float t, float lateral) => TrackLayout.SurfaceY(s, t, lateral);

            // A textura do cruzamento cobre a peça inteira (v de 0 a 1 nas 4 células); as outras repetem a cada célula.
            AddStrip(Surface, s, ts, -0.5f, 0.5f, 0f, d.IsCross ? 1f / d.Length : 1f, Y);

            if (d.IsStart)
            {
                // Bandeirada: 1 célula de largura × 0,25 ao longo, a 1,2 célula do começo.
                float t0 = 1.2f / d.Length, t1 = 1.45f / d.Length;
                AddStrip(Checker, s, new[] { t0, t1 }, -0.5f, 0.5f, 0.004f, 1f / 0.25f, Y);
            }
            if (s.High)
            {
                // Mureta clara dos dois lados das peças no alto.
                AddStrip(Guard, s, ts, -0.5f, -0.45f, 0.05f, 1f, Y);
                AddStrip(Guard, s, ts, 0.45f, 0.5f, 0.05f, 1f, Y);
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "Peça", hideFlags = HideFlags.DontSave };
                GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            mesh.Clear();
            mesh.SetVertices(V);
            mesh.SetUVs(0, UV);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(Surface, 0);
            mesh.SetTriangles(Checker, 1);
            mesh.SetTriangles(Guard, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterials = only != null ? new[] { only, only, only } : new[] { theme.Surface(d, dir), theme.checker, theme.guard };
            mr.shadowCastingMode = only != null ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;

            var col = GetComponent<MeshCollider>();
            if (col) { col.sharedMesh = null; col.sharedMesh = mesh; }

            BuildPillars(s, only == null ? theme : null);
        }

        // Destaque por cima da peça: seleção (amarelo) ou sobreposição (vermelho).
        public void SetHighlight(Material m)
        {
            if (m == null) { if (overlay) overlay.enabled = false; return; }
            if (!overlay)
            {
                var go = new GameObject("Destaque") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                go.AddComponent<MeshFilter>();
                overlay = go.AddComponent<MeshRenderer>();
                overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            overlay.GetComponent<MeshFilter>().sharedMesh = mesh;
            overlay.sharedMaterials = new[] { m, m, m };
            overlay.enabled = true;
        }

        void BuildPillars(Segment s, TrackTheme theme)
        {
            foreach (var p in pillars) Kill(p);
            pillars.Clear();
            if (theme == null || !s.High) return;
            int count = Mathf.Max(1, Mathf.RoundToInt(s.def.CenterLength / 1.25f));
            for (int j = 0; j <= count; j++)
            {
                float t = j / (float)count, h = s.ZAt(t) * TrackMath.LevelHeight;
                if (s.ZAt(t) < 0.25f) continue;
                var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, t);
                foreach (float side in new[] { -0.4f, 0.4f })
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.name = "Pilar";
                    go.hideFlags = HideFlags.DontSave;
                    Kill(go.GetComponent<Collider>());
                    go.transform.SetParent(transform, false);
                    var o = TrackMath.Offset(q, side);
                    go.transform.localPosition = new Vector3(o.x, h / 2f - 0.01f, o.y);
                    go.transform.localScale = new Vector3(0.1f, h / 2f, 0.1f);
                    go.GetComponent<MeshRenderer>().sharedMaterial = theme.support;
                    pillars.Add(go);
                }
            }
        }

        // Faixa entre os deslocamentos o1 (esquerda) e o2 (direita) nos pontos ts da peça.
        // Triângulos no sentido horário visto de cima: a face fica virada para a câmera.
        static void AddStrip(List<int> tri, Segment s, float[] ts, float o1, float o2, float yOff, float vScale, System.Func<float, float, float> y)
        {
            int start = V.Count;
            float dist = 0f;
            Vector2 prev = default;
            for (int i = 0; i < ts.Length; i++)
            {
                var q = TrackMath.PoseAt(s.a, s.def, s.piece.dir, ts[i]);
                if (i > 0) dist += Vector2.Distance(q.pos, prev);
                prev = q.pos;
                Vector2 l = TrackMath.Offset(q, o1), r = TrackMath.Offset(q, o2);
                V.Add(new Vector3(l.x, y(ts[i], o1) + yOff, l.y));
                V.Add(new Vector3(r.x, y(ts[i], o2) + yOff, r.y));
                UV.Add(new Vector2(0f, dist * vScale));
                UV.Add(new Vector2(1f, dist * vScale));
                if (i > 0)
                {
                    int k = start + i * 2;
                    tri.Add(k - 2); tri.Add(k); tri.Add(k - 1);
                    tri.Add(k - 1); tri.Add(k); tri.Add(k + 1);
                }
            }
        }

        // Fora do Play (pista desenhada no Editor), destruir tem que ser imediato.
        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void OnDestroy() => Kill(mesh);
    }
}
