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
            TrackPieceView.Kill(furniture);
            furniture = null;
            TrackPieceView.Kill(wood); TrackPieceView.Kill(legMat); TrackPieceView.Kill(floorMat);
        }

        // ----- Mesa 3D (seção 28 do guia): tampo de madeira com moldura, quatro pés e o chão -----
        const float Frame = 0.7f, Slab = 0.5f, FloorY = -9f, Leg = 0.9f;
        GameObject furniture;
        float builtW, builtH;
        Material wood, legMat, floorMat;

        Material Plain(Color c)
        {
            // Cópia do material da mesa (URP/Lit) sem a textura da grade: recebe luz e sombra como o resto.
            var m = new Material(material) { hideFlags = HideFlags.DontSave, color = c };
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", c);
            return m;
        }

        GameObject Block(string name, Material m, Vector3 center, Vector3 sizeXYZ)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            TrackPieceView.Kill(go.GetComponent<Collider>());
            go.transform.SetParent(furniture.transform, false);
            go.transform.position = center;
            go.transform.localScale = sizeXYZ;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        // top: altura do tampo quadriculado (a moldura de madeira fica rente a ele).
        void BuildFurniture(float w, float h, float top)
        {
            if (furniture && w == builtW && h == builtH) return;     // só refaz quando a mesa muda de tamanho
            builtW = w; builtH = h;
            TrackPieceView.Kill(furniture);
            furniture = new GameObject("Mesa 3D (gerada)") { hideFlags = HideFlags.DontSave };
            if (wood == null) { wood = Plain(new Color32(0xB9, 0x8E, 0x5E, 0xFF)); legMat = Plain(new Color32(0x7A, 0x5A, 0x3A, 0xFF)); floorMat = Plain(new Color32(0xC9, 0xCF, 0xC4, 0xFF)); }
            Vector3 o = transform.position;
            float cx = o.x + w / 2f, cz = o.z + h / 2f;
            // Tampo: um bloco maior que a grade (a sobra é a moldura), logo abaixo dela.
            Block("Tampo", wood, new Vector3(cx, top - 0.005f - Slab / 2f, cz), new Vector3(w + 2f * Frame, Slab, h + 2f * Frame));
            float legH = top - Slab - FloorY;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Block("Pé", legMat, new Vector3(cx + sx * (w / 2f + Frame - Leg), FloorY + legH / 2f, cz + sz * (h / 2f + Frame - Leg)), new Vector3(Leg, legH, Leg));
            Block("Chão", floorMat, new Vector3(cx, FloorY - 0.5f, cz), new Vector3(w + 400f, 1f, h + 400f));
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
            // A base da pista tem espessura: o tampo fica esse tanto abaixo do nível 0, onde os carrinhos correm.
            float top = -TrackPieceView.Thickness - 0.01f;
            quad.transform.position = transform.position + new Vector3(size.x * scale / 2f, top, size.y * scale / 2f);
            BuildFurniture(size.x * scale, size.y * scale, transform.position.y + top);
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
