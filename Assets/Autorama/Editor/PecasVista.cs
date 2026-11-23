using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Desenha uma pista de teste longe da mesa, para capturar a malha das peças novas (seção 99). Show(...) e Clear().
    public static class PecasVista
    {
        const string Name = "Teste de peças (temporário)";

        public static string Show(int faixas, float yaw, float pitch, float dist, params TrackPiece[] caso)
        {
            Clear();
            var t = PecasTeste.Fecha(caso, faixas, out string msg);
            if (t == null) return msg;
            var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<TrackTheme>("Assets/Autorama/TemaPista.asset");
            var segs = TrackLayout.Build(t.Origin, t.Pieces(), t.Tail, t.StartZ);
            TrackLayout.ComputeBanking(segs, true, t.Tail);
            var root = new GameObject(Name) { hideFlags = HideFlags.DontSave };
            float k = TrackMath.Scale(faixas);
            root.transform.position = new Vector3(-200f, 0f, -200f);
            root.transform.localScale = Vector3.one * k;
            var bb = new Bounds(root.transform.position, Vector3.zero);
            foreach (var s in segs)
            {
                var go = new GameObject("Peça", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<TrackPieceView>().Build(s, theme, null, faixas, segs);
                bb.Encapsulate(go.GetComponent<MeshRenderer>().bounds);
            }
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            sv.LookAt(bb.center, Quaternion.Euler(pitch, yaw, 0f), bb.extents.magnitude * dist);
            sv.Repaint();
            return msg;
        }

        public static void Clear()
        {
            var go = GameObject.Find(Name);
            if (go) Object.DestroyImmediate(go);
        }
    }
}
