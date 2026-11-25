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
        string enfGhostId, ghostChave;

        public void Arm(string id) { Armed = id; EnfSel = -1; if (Selected >= 0) Selected = -1; Rebuild(); }
        public void Disarm() { if (Armed == null) return; Armed = null; if (enfGhost) enfGhost.SetActive(false); }

        public void SelectEnfeite(int i) { EnfSel = i == EnfSel ? -1 : i; if (EnfSel >= 0) Selected = -1; Rebuild(); }

        // Põe o enfeite armado no ponto da mesa (em células). Giro e tamanho variam um pouco, para não sair tudo igual.
        public bool PlaceArmed(Vector2 cell)
        {
            var def = Armed != null ? EnfeiteCatalog.ById(Armed) : null;
            if (def == null) return false;
            Enfeite e;
            if (TorcidaDe(def.id))
            {
                e = NovaArquibancada(def, cell, out arqAncora);       // arquibancada (seção 101): trecho ao longo da pista
                if (e == null) { Notice?.Invoke(L.T("A arquibancada precisa de uma pista para acompanhar.")); return false; }
                arqUlt = arqAncora;
                string w = Why(e);
                if (w != null) { Notice?.Invoke(w); return false; }
                if (!Commit(() => enfeites.Add(e))) return false;
                // Já sai selecionada, com os pegadores, e o mesmo clique, se arrastar, estica (BuilderInput).
                NovaIndex = enfeites.Count - 1;
                Armed = null; if (enfGhost) enfGhost.SetActive(false);
                SelectEnfeite(NovaIndex);
                return true;
            }
            else e = new Enfeite { id = def.id, pos = cell, rot = Random.Range(0, 8) * EnfRotStep, esc = Random.Range(0.9f, 1.1f) };
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
            if (enfeites[EnfSel].Trecho)
            {
                var e = enfeites[EnfSel].Clone();
                if (!VirarArquibancada(e) || Why(e, EnfSel) != null) { Notice?.Invoke(L.T("Do outro lado da pista a arquibancada não cabe.")); return; }
                Commit(() => enfeites[EnfSel] = e);
                return;
            }
            Commit(() => enfeites[EnfSel].rot = TrackMath.Normalize(enfeites[EnfSel].rot + EnfRotStep));
        }

        // Arrastar: BeginEdit uma vez, MoveEnfeite a cada passo (não passa por cima da pista nem sai da mesa).
        public void MoveEnfeite(int i, Vector2 cell)
        {
            if (i < 0 || i >= enfeites.Count) return;
            if (enfeites[i].Trecho) { if (DeslizarArquibancada(i, cell)) Rebuild(); return; }
            var e = enfeites[i].Clone();
            e.pos = cell;
            if (Why(e, i) != null) return;
            enfeites[i].pos = cell;
            Rebuild();
        }

        // null se cabe; senão o aviso.
        string Why(Enfeite e, int skip = -1)
        {
            if (e.Trecho)
            {
                if (ArqForaDaMesa(e)) return L.T("O enfeite precisa ficar dentro da mesa.");
                return ArqNaPista(e, Segments) ? L.T("O enfeite não pode ficar em cima da pista.") : null;
            }
            float r = Raio(e) * FootShare;
            if (e.pos.x < r || e.pos.y < r || e.pos.x > boardSize.x - r || e.pos.y > boardSize.y - r) return L.T("O enfeite precisa ficar dentro da mesa.");
            return OnTrack(e, Segments) ? L.T("O enfeite não pode ficar em cima da pista.") : null;
        }

        bool OnTrack(Enfeite e, List<Segment> segs)
        {
            if (e.Trecho) return ArqNaPista(e, segs);
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

        // Células por metro do modelo. A altura do catálogo é em células da pista de 2; nas largas (escala 2–4) a célula
        // é maior no mundo e o carrinho não, então o enfeite encolhe em células para ficar do mesmo tamanho ao lado do carro.
        float CellsPerMeter(EnfeiteDef def, Enfeite e) => def.altura / Mathf.Max(0.01f, Measure(def).size.y) * e.esc / Scale;

        // Raio da pegada no chão, em células.
        public float Raio(Enfeite e)
        {
            if (e.Trecho) return ArqRaio(e);
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
                bool trecho = e.Trecho && def?.prefab;
                string chave = trecho ? ArqChave(e) : e.id;
                if (i < enfViews.Count && enfViews[i] && enfIds[i] == chave) { if (!trecho) Pose(enfViews[i], def, e); continue; }
                if (trecho && i < enfViews.Count && enfViews[i] && enfIds[i].StartsWith(e.id + "|"))
                {
                    MontarArquibancada(enfViews[i], def, e);           // mesma arquibancada, outra linha: refaz a malha no lugar
                    enfIds[i] = chave;
                    continue;
                }
                if (i < enfViews.Count) TrackPieceView.Kill(enfViews[i]);
                GameObject go;
                if (trecho)
                {
                    go = NovaVistaArquibancada("Enfeite " + e.id);
                    go.AddComponent<MeshCollider>();
                    MontarArquibancada(go, def, e);
                }
                else go = def?.prefab ? Spawn(def, "Enfeite " + e.id) : new GameObject("Enfeite sem modelo") { hideFlags = HideFlags.DontSave };
                if (!trecho && def?.prefab)
                {
                    var b = Measure(def);
                    var box = go.AddComponent<BoxCollider>();          // para o clique achar o enfeite
                    box.center = b.center;
                    box.size = b.size;
                    Pose(go, def, e);
                }
                if (i < enfViews.Count) { enfViews[i] = go; enfIds[i] = chave; } else { enfViews.Add(go); enfIds.Add(chave); }
            }
            while (enfViews.Count > enfeites.Count) { TrackPieceView.Kill(enfViews[enfViews.Count - 1]); enfViews.RemoveAt(enfViews.Count - 1); enfIds.RemoveAt(enfIds.Count - 1); }

            for (int i = 0; i < enfViews.Count; i++) Destacar(enfViews[i], !RaceMode && i == EnfSel);
            MostrarAlcas();
            if (RaceMode) Disarm();
        }

        // Destaque do selecionado, como o das peças (TrackPieceView.SetHighlight): uma cópia de cada malha por cima,
        // no material de seleção, um pouco maior (ou, na arquibancada, que fica na origem da raiz, um pouco acima).
        void Destacar(GameObject go, bool on)
        {
            if (!go) return;
            bool trecho = go.GetComponent<Torcida>() && go.GetComponent<MeshFilter>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name == "Destaque") continue;
                var t = mf.transform.Find("Destaque");
                if (!on) { if (t) t.gameObject.SetActive(false); continue; }
                if (!mf.sharedMesh) continue;
                if (!t)
                {
                    var d = new GameObject("Destaque") { hideFlags = HideFlags.DontSave };
                    d.transform.SetParent(mf.transform, false);
                    d.AddComponent<MeshFilter>();
                    var r = d.AddComponent<MeshRenderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    t = d.transform;
                }
                t.gameObject.SetActive(true);
                var tf = t.GetComponent<MeshFilter>();
                if (trecho)
                {
                    // Arquibancada: a cópia é inflada pelas normais (cada face anda para fora), sem brigar com a malha de baixo.
                    var src = mf.sharedMesh;
                    var m = tf.sharedMesh && tf.sharedMesh != src ? tf.sharedMesh : new Mesh { name = "Destaque", hideFlags = HideFlags.DontSave };
                    var vs = src.vertices; var ns = src.normals;
                    for (int k = 0; k < vs.Length; k++) vs[k] += ns[k] * (0.004f / Scale);
                    m.Clear(); m.indexFormat = src.indexFormat; m.vertices = vs; m.normals = ns;
                    m.subMeshCount = src.subMeshCount;
                    for (int k = 0; k < src.subMeshCount; k++) m.SetTriangles(src.GetTriangles(k), k);
                    m.RecalculateBounds();
                    tf.sharedMesh = m;
                }
                else tf.sharedMesh = mf.sharedMesh;
                var mats = new Material[tf.sharedMesh.subMeshCount];
                for (int k = 0; k < mats.Length; k++) mats[k] = theme.selected;
                t.GetComponent<MeshRenderer>().sharedMaterials = mats;
                if (trecho) { t.localScale = Vector3.one; t.localPosition = Vector3.zero; }
                else { t.localScale = Vector3.one * 1.04f; t.localPosition = Vector3.zero; }
            }
        }

        // O enfeite armado acompanha o mouse (null esconde).
        public void ShowArmed(Vector2? cell)
        {
            var def = Armed != null ? EnfeiteCatalog.ById(Armed) : null;
            if (def == null || def.prefab == null || !cell.HasValue || RaceMode) { if (enfGhost) enfGhost.SetActive(false); return; }
            if (TorcidaDe(def.id))
            {
                var a = NovaArquibancada(def, cell.Value);
                if (a == null) { if (enfGhost) enfGhost.SetActive(false); return; }
                if (!enfGhost || enfGhostId != def.id + "|")
                {
                    TrackPieceView.Kill(enfGhost);
                    enfGhost = NovaVistaArquibancada("Enfeite fantasma");
                    enfGhostId = def.id + "|";
                    ghostChave = null;
                }
                enfGhost.SetActive(true);
                string k = ArqChave(a);
                if (k != ghostChave) { MontarArquibancada(enfGhost, def, a); ghostChave = k; }
                return;
            }
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
