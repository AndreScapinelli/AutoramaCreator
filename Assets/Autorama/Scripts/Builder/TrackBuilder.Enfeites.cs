using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Cosméticos soltos na mesa (seção 97 do guia): a lista, colocar, mover, girar, tirar e o desenho.
    // Entram no desfazer e no rascunho junto com a pista (ToSave/Apply), mas não mexem na corrida.
    public partial class TrackBuilder
    {
        public readonly List<Enfeite> enfeites = new List<Enfeite>();
        public int EnfSel { get; private set; } = -1;          // enfeite selecionado (-1 = nenhum)
        public string Armed { get; private set; }               // enfeite do catálogo pronto para pôr com o clique (null = nenhum)

        public const float EnfRotStep = 45f;
        const float TrackHalf = 0.5f, FootShare = 0.6f;        // meia largura da pista; parte da pegada que não pode cobrir a pista

        readonly List<GameObject> enfViews = new List<GameObject>();
        readonly List<string> enfIds = new List<string>();
        GameObject enfGhost;
        string enfGhostId;
        Transform enfMark;

        public void Arm(string id) { Armed = id; EnfSel = -1; if (Selected >= 0) Selected = -1; Rebuild(); }
        public void Disarm() { if (Armed == null) return; Armed = null; if (enfGhost) enfGhost.SetActive(false); }

        public void SelectEnfeite(int i) { EnfSel = i == EnfSel ? -1 : i; if (EnfSel >= 0) Selected = -1; Rebuild(); }

        // Põe o enfeite armado no ponto da mesa (em células). Giro e tamanho variam um pouco, para não sair tudo igual.
        public bool PlaceArmed(Vector2 cell)
        {
            var def = Armed != null ? EnfeiteCatalog.ById(Armed) : null;
            if (def == null) return false;
            var e = new Enfeite { id = def.id, pos = cell, rot = Random.Range(0, 8) * EnfRotStep, esc = Random.Range(0.9f, 1.1f) };
            string why = Why(e);
            if (why != null) { Notice?.Invoke(why); return false; }
            return Commit(() => enfeites.Add(e));
        }

        public void RemoveEnfeite()
        {
            if (EnfSel < 0) return;
            Commit(() => { enfeites.RemoveAt(EnfSel); EnfSel = -1; });
        }

        public void RotateEnfeite()
        {
            if (EnfSel < 0) return;
            Commit(() => enfeites[EnfSel].rot = TrackMath.Normalize(enfeites[EnfSel].rot + EnfRotStep));
        }

        // Arrastar: BeginEdit uma vez, MoveEnfeite a cada passo (não passa por cima da pista nem sai da mesa).
        public void MoveEnfeite(int i, Vector2 cell)
        {
            if (i < 0 || i >= enfeites.Count) return;
            var e = enfeites[i].Clone();
            e.pos = cell;
            if (Why(e, i) != null) return;
            enfeites[i].pos = cell;
            Rebuild();
        }

        // null se cabe; senão o aviso.
        string Why(Enfeite e, int skip = -1)
        {
            float r = Raio(e) * FootShare;
            if (e.pos.x < r || e.pos.y < r || e.pos.x > boardSize.x - r || e.pos.y > boardSize.y - r) return L.T("O enfeite precisa ficar dentro da mesa.");
            return OnTrack(e, Segments) ? L.T("O enfeite não pode ficar em cima da pista.") : null;
        }

        static bool OnTrack(Enfeite e, List<Segment> segs)
        {
            float min = TrackHalf + Raio(e) * FootShare;
            foreach (var sg in segs)
            {
                const int N = 32;
                for (int k = 0; k <= N; k++)
                    if ((TrackMath.PoseAt(sg.a, sg.def, sg.piece.dir, k / (float)N).pos - e.pos).sqrMagnitude < min * min) return true;
            }
            return false;
        }

        // A pista ganha: peça nova (ou o Q, ou arrastar a pista) por cima de um enfeite tira o enfeite (o desfazer devolve).
        void DropCovered()
        {
            if (enfeites.Count == 0 || pieces.Count == 0) return;
            var segs = TrackLayout.Build(Origin, pieces, Tail, StartZ);
            int n = enfeites.RemoveAll(e => OnTrack(e, segs));
            if (n > 0) { EnfSel = -1; Notice?.Invoke(L.F("A pista passou por cima de {0} enfeite(s), que saíram da mesa.", n)); }
        }

        // ----- medidas do modelo (em metros, como veio do pacote), uma vez por enfeite do catálogo -----

        static readonly Dictionary<string, Bounds> measured = new Dictionary<string, Bounds>();

        static Bounds Measure(EnfeiteDef def)
        {
            if (measured.TryGetValue(def.id, out var b)) return b;
            b = new Bounds(Vector3.zero, Vector3.one);
            if (def.prefab)
            {
                var go = Instantiate(def.prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                }
                TrackPieceView.Kill(go);
            }
            return measured[def.id] = b;
        }

        // Células por metro do modelo.
        static float CellsPerMeter(EnfeiteDef def, Enfeite e) => def.altura / Mathf.Max(0.01f, Measure(def).size.y) * e.esc;

        // Raio da pegada no chão, em células.
        public static float Raio(Enfeite e)
        {
            var def = EnfeiteCatalog.ById(e.id);
            if (def == null) return 0.5f;
            var s = Measure(def).extents;
            return Mathf.Max(s.x, s.z) * CellsPerMeter(def, e);
        }

        // ----- desenho -----

        float GroundY => -TrackPieceView.Thickness / Scale;     // o tampo da mesa, no espaço da raiz (células)

        GameObject Spawn(EnfeiteDef def, string name)
        {
            var go = Instantiate(def.prefab, Root, false);
            go.name = name;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) TrackPieceView.Kill(c);
            bool small = def.altura < 1f;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (small) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void Pose(GameObject go, EnfeiteDef def, Enfeite e)
        {
            float s = CellsPerMeter(def, e);
            var b = Measure(def);
            go.transform.localScale = Vector3.one * s;
            go.transform.localRotation = Quaternion.Euler(0f, e.rot, 0f);
            go.transform.localPosition = new Vector3(e.pos.x, GroundY - b.min.y * s, e.pos.y);
        }

        void RebuildEnfeites()
        {
            for (int i = 0; i < enfeites.Count; i++)
            {
                var e = enfeites[i];
                var def = EnfeiteCatalog.ById(e.id);
                if (i < enfViews.Count && enfViews[i] && enfIds[i] == e.id) { Pose(enfViews[i], def, e); continue; }
                if (i < enfViews.Count) TrackPieceView.Kill(enfViews[i]);
                var go = def?.prefab ? Spawn(def, "Enfeite " + e.id) : new GameObject("Enfeite sem modelo") { hideFlags = HideFlags.DontSave };
                if (def?.prefab)
                {
                    var b = Measure(def);
                    var box = go.AddComponent<BoxCollider>();          // para o clique achar o enfeite
                    box.center = b.center;
                    box.size = b.size;
                    Pose(go, def, e);
                }
                if (i < enfViews.Count) { enfViews[i] = go; enfIds[i] = e.id; } else { enfViews.Add(go); enfIds.Add(e.id); }
            }
            while (enfViews.Count > enfeites.Count) { TrackPieceView.Kill(enfViews[enfViews.Count - 1]); enfViews.RemoveAt(enfViews.Count - 1); enfIds.RemoveAt(enfIds.Count - 1); }

            // Marca do selecionado: um disco da cor da seleção embaixo dele.
            bool mark = EnfSel >= 0 && EnfSel < enfeites.Count && !RaceMode;
            if (mark && !enfMark) enfMark = Quad("Enfeite selecionado", theme.selected, 0f);
            if (enfMark)
            {
                enfMark.gameObject.SetActive(mark);
                if (mark)
                {
                    var e = enfeites[EnfSel];
                    float d = Mathf.Max(0.6f, Raio(e) * 2f);
                    enfMark.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    enfMark.localPosition = new Vector3(e.pos.x, GroundY + 0.004f / Scale, e.pos.y);
                    enfMark.localScale = new Vector3(d, d, 1f);
                }
            }
            if (RaceMode) Disarm();
        }

        // O enfeite armado acompanha o mouse (null esconde).
        public void ShowArmed(Vector2? cell)
        {
            var def = Armed != null ? EnfeiteCatalog.ById(Armed) : null;
            if (def == null || def.prefab == null || !cell.HasValue || RaceMode) { if (enfGhost) enfGhost.SetActive(false); return; }
            if (!enfGhost || enfGhostId != def.id)
            {
                TrackPieceView.Kill(enfGhost);
                enfGhost = Spawn(def, "Enfeite fantasma");
                enfGhostId = def.id;
            }
            enfGhost.SetActive(true);
            Pose(enfGhost, def, new Enfeite { id = def.id, pos = cell.Value });
        }

        public int IndexOfEnfeite(Collider c)
        {
            if (!c) return -1;
            for (int i = 0; i < enfViews.Count; i++) if (enfViews[i] && c.transform.IsChildOf(enfViews[i].transform)) return i;
            return -1;
        }
    }
}
