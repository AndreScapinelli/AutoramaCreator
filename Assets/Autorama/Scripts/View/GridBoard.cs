using UnityEngine;

namespace Autorama
{
    // A mesa quadriculada: um quad no chão com a textura de 4×4 células repetida.
    // Linha fina a cada célula, linha forte a cada 4 (o tamanho de uma reta comprida).
    // Também aparece fora do Play; o quad gerado não é salvo na cena.
    [ExecuteAlways]
    public class GridBoard : MonoBehaviour
    {
        public Material material;          // URP/Lit com grade-mesa.png (recebe a sombra das pontes)
        public Vector2Int size = new Vector2Int(32, 20);
        const int MajorEvery = 4;

        GameObject quad;

        void OnEnable() => Build(size, scale, other);

        void OnDisable()
        {
            TrackPieceView.Kill(quad);
            quad = null;
        }

        float scale = 1f;
        Material other;

        // cellScale: unidades do mundo por célula (2 na pista de 4 carros), com a mesa própria dela (wideMaterial).
        public void Build(Vector2Int boardSize, float cellScale = 1f, Material wideMaterial = null)
        {
            size = boardSize;
            scale = cellScale;
            other = cellScale > 1f ? wideMaterial : null;
            if (material == null) return;
            if (!quad)
            {
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Mesa (gerada)";
                quad.hideFlags = HideFlags.DontSave;
                TrackPieceView.Kill(quad.GetComponent<Collider>());
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);    // virado para cima: u = +X, v = +Z
            }
            quad.transform.position = transform.position + new Vector3(size.x * scale / 2f, -0.01f, size.y * scale / 2f);
            quad.transform.localScale = new Vector3(size.x * scale, size.y * scale, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = other != null ? other : material;
            // Escala da textura por objeto, sem mexer no material compartilhado.
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetVector("_BaseMap_ST", new Vector4(size.x / (float)MajorEvery, size.y / (float)MajorEvery, 0f, 0f));
            r.SetPropertyBlock(block);
        }
    }
}
