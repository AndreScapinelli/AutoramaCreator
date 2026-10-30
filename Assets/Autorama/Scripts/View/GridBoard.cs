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

        void OnEnable() => Build(size);

        void OnDisable()
        {
            TrackPieceView.Kill(quad);
            quad = null;
        }

        public void Build(Vector2Int boardSize)
        {
            size = boardSize;
            if (material == null) return;
            if (!quad)
            {
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Mesa (gerada)";
                quad.hideFlags = HideFlags.DontSave;
                TrackPieceView.Kill(quad.GetComponent<Collider>());
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);    // virado para cima: u = +X, v = +Z
            }
            quad.transform.position = transform.position + new Vector3(size.x / 2f, -0.01f, size.y / 2f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            // Escala da textura por objeto, sem mexer no material compartilhado.
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetVector("_BaseMap_ST", new Vector4(size.x / (float)MajorEvery, size.y / (float)MajorEvery, 0f, 0f));
            r.SetPropertyBlock(block);
        }
    }
}
