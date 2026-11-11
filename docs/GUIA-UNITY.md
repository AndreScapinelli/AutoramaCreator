# Construtor de Pista de Autorama na Unity

Guia para recriar na Unity o construtor que está em `index.html`: vista de cima, mesa quadriculada, pista de 2 trilhos, peças que encaixam umas nas outras a partir de uma base de largada.

A ideia central é a mesma do HTML. **A pista é uma lista de peças, e a posição de cada peça não é guardada: ela é recalculada a partir da largada toda vez que algo muda.** Inserir, remover, trocar ou inverter uma peça é só mexer na lista e reconstruir.

---

## 1. Conceitos

| Conceito | O que é |
|---|---|
| **Célula** | Unidade da grade. 1 célula = 1 unidade da Unity = largura da pista (2 trilhos). |
| **Pose** | Posição no plano da mesa + direção (ângulo). Todo encaixe entre peças é uma pose. |
| **Peça** | Sabe calcular a pose em qualquer ponto do seu comprimento (`t` de 0 a 1), partindo da pose de entrada. |
| **Segmento** | Peça já posicionada: pose de entrada `a` e pose de saída `b`. |
| **Ponto de inserção** | Fim da pista, ou o fim da peça selecionada. |

### Medidas das peças (em células)

| Peça | Tipo | Medida | Observação |
|---|---|---|---|
| Base de largada | reta | 2 | Sempre a peça 0. Não pode ser removida. |
| Reta curta | reta | 2 | |
| Reta comprida | reta | 4 | Linha forte da grade a cada 4 células. |
| Reta curtinha | reta | 1 | Ajuste fino: sem ela, um 8 não fecha (seção 16.8). |
| Curva longa | curva | 90°, raio 2,5 | Ocupa um bloco de 3×3 células. |
| Meia curva | curva | 45°, raio 2,5 | Duas meias curvas = uma curva longa. |

O raio 2,5 na linha central faz a borda interna ter raio 2 e a externa raio 3. Com a pista ocupando exatamente uma fileira de células, uma curva de 90° termina alinhada à grade de novo. Curvas de 45° saem na diagonal, que é o comportamento esperado.

Os trilhos ficam a ±0,25 célula da linha central. **Trilho 1 à esquerda, trilho 2 à direita** de quem pilota.

### Sistema de coordenadas

- A mesa fica no plano **XZ** (Y para cima). Uma `Pose2` usa `Vector2(x, z)`.
- Ângulo em graus: **0 = +X**, crescendo no sentido **anti-horário** visto de cima (rumo a +Z).
- `dir = +1` vira à **direita** (o ângulo diminui) e `dir = -1` vira à **esquerda**.
- Para girar um objeto pela pose: `Quaternion.Euler(0, -heading, 0)`. Modele os carrinhos apontando para **+X**.

> O HTML usa Y para baixo (padrão de tela). Na seção 9 tem a conversão para abrir na Unity as pistas salvas no navegador.

---

## 2. Montagem do projeto

1. Unity 6 (ou 2022.3+), template **Universal 3D (URP)**.
2. Instale o pacote **Input System** e aceite reiniciar.
3. Câmera: posição `(16, 30, 10)`, rotação `(90, 0, 0)`, **Projection: Orthographic**. Isso dá a vista de cima.
4. Crie as pastas:

```
Assets/Autorama/
  Scripts/      TrackMath.cs, PieceDefinition.cs, TrackPieceView.cs, TrackBuilder.cs,
                GridBoard.cs, TopDownCamera.cs, LanePath.cs, CarRunner.cs, TrackSave.cs, BuilderUI.cs
  Pieces/       5 assets de PieceDefinition
  Materials/    Asfalto, Borda, Trilho, Fenda, FaixaVermelha, FaixaAzul, Fantasma
  Texturas/     as imagens da pasta Texturas/ deste projeto (seção 5.1)
  Prefabs/      TrackPiece.prefab, Carrinho1.prefab, Carrinho2.prefab
  UI/           Builder.uxml, Builder.uss
```

5. Materiais em **URP/Unlit**, que deixa a vista de cima limpa, sem sombra:
   - Asfalto `#2F3533`, Borda `#F2F2EC`, Trilho `#AEB4B8`, Fenda `#0E1110`
   - FaixaVermelha `#D3261C` e FaixaAzul `#1F5ED4`, com Surface Type **Transparent** e alfa por volta de 0,2
   - Fantasma: amarelo `#E9A800`, Transparent, alfa 0,3

---

## 3. Definição das peças

`PieceDefinition.cs`: um ScriptableObject por tipo de peça.

```csharp
using UnityEngine;

public enum PieceKind { Straight, Curve }

[CreateAssetMenu(menuName = "Autorama/Peça")]
public class PieceDefinition : ScriptableObject
{
    public string id = "reta-curta";       // usado no save: reta-curta, reta-comprida, curva-longa, meia-curva
    public string displayName = "Reta curta";
    public PieceKind kind = PieceKind.Straight;
    public float length = 2f;              // retas
    public float angle = 90f;              // curvas, em graus
    public float radius = 2.5f;            // curvas, raio da linha central
    public bool isStart;

    public float CenterLength =>
        kind == PieceKind.Straight ? length : angle * Mathf.Deg2Rad * radius;

    // offset > 0 = lado direito. Numa curva à direita (dir = +1) o centro fica à direita,
    // então o trilho da direita tem raio menor.
    public float LaneLength(int dir, float offset) =>
        kind == PieceKind.Straight ? length : angle * Mathf.Deg2Rad * (radius - dir * offset);
}
```

Crie os 5 assets em `Pieces/` (botão direito, **Create > Autorama > Peça**) com as medidas da tabela. Na Base de largada, marque `isStart`.

---

## 4. Geometria

`TrackMath.cs` é o coração do construtor. Tudo depende de `PoseAt`.

```csharp
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct Pose2
{
    public Vector2 pos;     // (x, z) no plano da mesa
    public float heading;   // graus: 0 = +X, anti-horário positivo

    public Pose2(Vector2 pos, float heading) { this.pos = pos; this.heading = heading; }
    public Vector3 World(float y = 0f) => new Vector3(pos.x, y, pos.y);
    public Quaternion Rotation => Quaternion.Euler(0f, -heading, 0f);
}

public static class TrackMath
{
    public const float TrackWidth = 1f;
    public const float LaneOffset = 0.25f;
    public static readonly float[] Lanes = { -LaneOffset, LaneOffset }; // trilho 1, trilho 2

    public static Vector2 Dir(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    public static float Normalize(float h) => Mathf.Repeat(h, 360f);

    // Pose a uma fração t (0..1) da peça, partindo da pose de entrada a.
    public static Pose2 PoseAt(Pose2 a, PieceDefinition d, int dir, float t)
    {
        if (d.kind == PieceKind.Straight)
            return new Pose2(a.pos + Dir(a.heading) * d.length * t, a.heading);

        Vector2 center = a.pos + Dir(a.heading - 90f * dir) * d.radius;
        float h = a.heading - dir * d.angle * t;
        return new Pose2(center + Dir(h + 90f * dir) * d.radius, h);
    }

    // Deslocamento lateral: o > 0 = direita de quem pilota.
    public static Vector2 Offset(Pose2 p, float o) => p.pos + Dir(p.heading - 90f) * o;

    public static List<Pose2> Sample(Pose2 a, PieceDefinition d, int dir, float degPerStep = 4f)
    {
        int n = d.kind == PieceKind.Straight ? 1 : Mathf.CeilToInt(d.angle / degPerStep);
        var list = new List<Pose2>(n + 1);
        for (int k = 0; k <= n; k++) list.Add(PoseAt(a, d, dir, k / (float)n));
        return list;
    }

    // Encaixa a largada na grade: a pista sempre ocupa uma fileira inteira de células.
    public static Pose2 SnapStart(Pose2 o)
    {
        float h = Normalize(Mathf.Round(o.heading / 90f) * 90f);
        bool horizontal = Mathf.Approximately(h % 180f, 0f);
        return horizontal
            ? new Pose2(new Vector2(Mathf.Round(o.pos.x), Mathf.Floor(o.pos.y) + 0.5f), h)
            : new Pose2(new Vector2(Mathf.Floor(o.pos.x) + 0.5f, Mathf.Round(o.pos.y)), h);
    }
}
```

**Teste rápido:** partindo de `(0,0)` com heading 0, uma curva longa à direita (`dir = +1`) precisa terminar em `(2.5, -2.5)` com heading -90. Se der isso, a geometria está certa.

---

## 5. Malha de cada peça

Cada peça vira uma malha gerada em código: várias "faixas" paralelas à linha central, uma por material.

`TrackPieceView.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TrackPieceView : MonoBehaviour
{
    public int index;           // posição na lista; usado para seleção por clique
    Mesh mesh;

    // Ordem dos materiais: 0 asfalto, 1 faixa vermelha, 2 faixa azul, 3 borda, 4 trilho, 5 fenda
    public void Build(Pose2 a, PieceDefinition def, int dir, Material[] mats)
    {
        var poses = TrackMath.Sample(a, def, dir);
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var subs = new List<int>[6];
        for (int i = 0; i < subs.Length; i++) subs[i] = new List<int>();

        AddStrip(v, uv, subs[0], poses, -0.5f, 0.5f, 0f);
        AddStrip(v, uv, subs[1], poses, -0.46f, -0.04f, 0.001f);
        AddStrip(v, uv, subs[2], poses, 0.04f, 0.46f, 0.001f);
        AddStrip(v, uv, subs[3], poses, -0.47f, -0.43f, 0.002f);
        AddStrip(v, uv, subs[3], poses, 0.43f, 0.47f, 0.002f);
        foreach (float lane in TrackMath.Lanes)
        {
            AddStrip(v, uv, subs[4], poses, lane - 0.08f, lane - 0.05f, 0.003f);
            AddStrip(v, uv, subs[4], poses, lane + 0.05f, lane + 0.08f, 0.003f);
            AddStrip(v, uv, subs[5], poses, lane - 0.022f, lane + 0.022f, 0.004f);
        }

        if (mesh == null) { mesh = new Mesh(); mesh.name = "Peça"; }
        mesh.Clear();
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = subs.Length;
        for (int i = 0; i < subs.Length; i++) mesh.SetTriangles(subs[i], i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
        GetComponent<MeshRenderer>().sharedMaterials = mats;
        var col = GetComponent<MeshCollider>();
        col.sharedMesh = null;      // força o collider a reler a malha
        col.sharedMesh = mesh;
    }

    // Faixa entre os deslocamentos o1 (esquerda) e o2 (direita), na altura y.
    static void AddStrip(List<Vector3> v, List<Vector2> uv, List<int> tri,
                         List<Pose2> poses, float o1, float o2, float y)
    {
        int start = v.Count;
        float dist = 0f;
        for (int i = 0; i < poses.Count; i++)
        {
            if (i > 0) dist += Vector2.Distance(poses[i].pos, poses[i - 1].pos);
            Vector2 l = TrackMath.Offset(poses[i], o1), r = TrackMath.Offset(poses[i], o2);
            v.Add(new Vector3(l.x, y, l.y));
            v.Add(new Vector3(r.x, y, r.y));
            uv.Add(new Vector2(0f, dist));
            uv.Add(new Vector2(1f, dist));
            if (i > 0)
            {
                int k = start + i * 2;    // sentido horário visto de cima = face virada para a câmera
                tri.Add(k - 2); tri.Add(k);     tri.Add(k - 1);
                tri.Add(k - 1); tri.Add(k);     tri.Add(k + 1);
            }
        }
    }

    void OnDestroy() { if (mesh != null) Destroy(mesh); }
}
```

Monte `TrackPiece.prefab` com esse componente (MeshFilter, MeshRenderer e MeshCollider entram sozinhos).

**Detalhes que dão cara de autorama:**
- **Zebra nas curvas:** uma faixa extra no lado de fora (`-dir * 0.43` a `-dir * 0.47`) com material de textura listrada vermelha e branca. O UV `y = dist` já faz a textura repetir ao longo da curva.
- **Bandeirada na largada:** um Quad filho com textura quadriculada, atravessado na pista a 1,2 célula do início.
- **Emendas:** um Quad bem fino e escuro na pose `a` de cada peça, a partir da segunda.

### 5.1 Texturas prontas

A pasta `Texturas/` traz as imagens. Copie para `Assets/Autorama/Texturas/`. Todas usam as cores do HTML.

| Arquivo | Tamanho | Uso | Import Settings |
|---|---|---|---|
| `pista-reta.png` | 512×512 | Pista inteira num só material: asfalto, faixas, trilhos, fendas e bordas | Default, **Wrap: Repeat** |
| `pista-curva-direita.png` | 512×512 | Igual, com zebra no lado de fora (esquerdo) | Default, Repeat |
| `pista-curva-esquerda.png` | 512×512 | Igual, com zebra no lado de fora (direito) | Default, Repeat |
| `pista-curva-inclinada-direita.png`, `pista-curva-inclinada-esquerda.png` | 512×512 | Curvas inclinadas: listras amarelas atravessando a pista (4 por célula), clareiam rumo ao lado de fora e têm muro em vez de zebra (seção 16) | Default, Repeat |
| `asfalto.png` | 512×512 | Só o asfalto, se você montar com várias faixas (seção 5) | Default, Repeat |
| `zebra.png` | 64×256 | Zebra avulsa, 4 listras por célula | Default, Repeat |
| `bandeirada.png` | 512×128 | Linha de chegada: 1 célula × 0,25 célula | Default, **Clamp** |
| `grade-mesa.png` | 256×256 | Bloco de 4×4 células para a mesa (substitui a textura gerada em código da seção 7) | Default, Repeat, Aniso 8 |
| `sobreposicao.png` | 128×128 | Listras laranja para peças sobrepostas (transparente) | Default, Repeat, Alpha Is Transparency |
| `seta-insercao.png` | 256×256 | Seta amarela do ponto de inserção, aponta para +X | Sprite ou Default, Clamp, Alpha Is Transparency |
| `carrinho-vermelho.png`, `carrinho-azul.png` | 640×384 | Carrinhos vistos de cima, frente para +X | Default, Clamp, Alpha Is Transparency |
| `icone-*.png` (13 arquivos) | 256×256 | Ícones da paleta, todos na mesma escala, incluindo curvas inclinadas e rampas | **Sprite (2D and UI)** |

**Opção mais simples para a malha:** em vez das 6 faixas da seção 5, use uma faixa só (`AddStrip(..., -0.5f, 0.5f, 0f)`) com um destes materiais:
- `pista-reta.png` nas retas;
- `pista-curva-direita.png` quando `dir = +1`;
- `pista-curva-esquerda.png` quando `dir = -1`.

O UV já está pronto: `u` atravessa a pista da esquerda (0) para a direita (1), e `v` repete a cada célula, porque `AddStrip` grava `v = distância`. Uma malha com um submesh e um material por peça.

**Mesa com a imagem:** no `GridBoard`, troque a geração da textura por `public Texture2D gridTexture;` e use `mat.mainTexture = gridTexture;`. O `mainTextureScale` continua `largura / 4` × `altura / 4`.

**Carrinhos:** um Quad deitado (`rotation (90, 0, 0)`) com escala `(0.64, 0.384, 1)` e material URP/Unlit **Transparent** com a textura do carrinho. Com esse tamanho, 1 pixel da imagem = 1 milésimo de célula, e a carroceria mede 0,60 × 0,32 célula, a mesma caixa de colisão da seção 15.8.

**Seta:** Quad de `(1.28, 1.28, 1)`. O centro da imagem fica 0,25 célula à frente da linha da seta, então desloque o Quad 0,25 para a frente da `InsertPose`.

**Ícones na UI Toolkit:** `button.style.backgroundImage = new StyleBackground(sprite);` ou, no USS, `background-image: url("project://database/Assets/Autorama/Texturas/icone-reta-curta.png");`.

**Gerar de novo com outras cores:** o script `ferramentas/gen-texturas.js` cria todas as imagens. Mude as cores no bloco `C` e rode:

```
node ferramentas/gen-texturas.js Assets/Autorama/Texturas
```

Rode na raiz do repositório. Depois, na Unity, use o menu **Autorama > Atualizar materiais e ícones**.

---

## 6. O construtor

`TrackBuilder.cs` guarda a lista, recalcula as posições e expõe as ações.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PieceInstance
{
    public PieceDefinition def;
    public int dir;                         // 0 em retas, -1 esquerda, +1 direita
    public PieceInstance(PieceDefinition def, int dir) { this.def = def; this.dir = dir; }
}

public struct Segment { public PieceInstance piece; public Pose2 a, b; }

public class TrackBuilder : MonoBehaviour
{
    [Header("Peças")]
    public PieceDefinition start, shortStraight, longStraight, curve, halfCurve;
    [Header("Visual")]
    public TrackPieceView piecePrefab;
    public Material[] materials;            // 6, na ordem do TrackPieceView
    public Vector2Int board = new Vector2Int(32, 20);
    public float cellCm = 15f;

    public Pose2 origin = new Pose2(new Vector2(8f, 12.5f), 0f);
    public readonly List<PieceInstance> pieces = new();
    public readonly List<Segment> segments = new();
    public readonly HashSet<int> overlaps = new();
    public int selected = -1;               // -1 = inserir no fim
    public bool IsClosed { get; private set; }
    public float CenterLength { get; private set; }
    public float[] LaneLengths { get; } = new float[2];
    public bool Playing;

    public event Action Changed;

    readonly List<TrackPieceView> views = new();
    readonly Stack<string> undo = new(), redo = new();

    void Start()
    {
        pieces.Add(new PieceInstance(start, 0));
        Rebuild();
    }

    // ---------- Ações ----------
    public int InsertIndex => selected < 0 ? pieces.Count : selected + 1;
    public Pose2 InsertPose => selected < 0 ? segments[^1].b : segments[selected].b;

    public void Add(PieceDefinition def, int dir) => Commit(() =>
    {
        int at = InsertIndex;
        pieces.Insert(at, new PieceInstance(def, dir));
        if (selected >= 0) selected = at;   // continua encaixando em sequência
    });

    public void RemoveSelected()
    {
        if (selected <= 0) return;          // a largada não sai
        Commit(() => { pieces.RemoveAt(selected); selected--; });
    }

    public void RemoveLast()
    {
        if (pieces.Count < 2) return;
        Commit(() => { pieces.RemoveAt(pieces.Count - 1); if (selected >= pieces.Count) selected = -1; });
    }

    public void FlipSelected()
    {
        if (selected <= 0 || pieces[selected].def.kind != PieceKind.Curve) return;
        Commit(() => pieces[selected].dir *= -1);
    }

    public void SwapSelected(PieceDefinition def, int dir)
    {
        if (selected <= 0) return;
        Commit(() => pieces[selected] = new PieceInstance(def, dir));
    }

    public void RotateStart() => Commit(() =>
    {
        Vector2 mid = origin.pos + TrackMath.Dir(origin.heading);    // centro da largada
        float h = origin.heading + 90f;
        origin = TrackMath.SnapStart(new Pose2(mid - TrackMath.Dir(h), h));
    });

    public void Select(int i) { selected = (i == selected) ? -1 : i; Rebuild(); }

    // ---------- Desfazer ----------
    public void BeginEdit() { undo.Push(TrackSave.ToJson(this)); redo.Clear(); }

    void Commit(Action change)
    {
        BeginEdit();
        change();
        Rebuild();
    }

    public void Undo() { if (undo.Count == 0) return; redo.Push(TrackSave.ToJson(this)); TrackSave.FromJson(this, undo.Pop()); }
    public void Redo() { if (redo.Count == 0) return; undo.Push(TrackSave.ToJson(this)); TrackSave.FromJson(this, redo.Pop()); }

    // ---------- Reconstrução ----------
    public void Rebuild()
    {
        if (selected >= pieces.Count) selected = -1;
        segments.Clear();
        var conn = origin;
        foreach (var p in pieces)
        {
            var b = TrackMath.PoseAt(conn, p.def, p.dir, 1f);
            b.heading = TrackMath.Normalize(b.heading);
            segments.Add(new Segment { piece = p, a = conn, b = b });
            conn = b;
        }

        while (views.Count < segments.Count) views.Add(Instantiate(piecePrefab, transform));
        while (views.Count > segments.Count) { Destroy(views[^1].gameObject); views.RemoveAt(views.Count - 1); }
        for (int i = 0; i < segments.Count; i++)
        {
            views[i].index = i;
            views[i].Build(segments[i].a, segments[i].piece.def, segments[i].piece.dir, materials);
        }

        Analyze();
        Changed?.Invoke();
    }

    void Analyze()
    {
        var last = segments[^1].b;
        IsClosed = segments.Count > 1
            && Vector2.Distance(last.pos, origin.pos) < 0.02f
            && Mathf.Abs(Mathf.DeltaAngle(last.heading, origin.heading)) < 0.5f;

        CenterLength = 0f; LaneLengths[0] = LaneLengths[1] = 0f;
        foreach (var s in segments)
        {
            CenterLength += s.piece.def.CenterLength;
            for (int k = 0; k < 2; k++) LaneLengths[k] += s.piece.def.LaneLength(s.piece.dir, TrackMath.Lanes[k]);
        }

        FindOverlaps();
    }

    // Pontos da linha central a cada 0,25 célula. Dois trechos não vizinhos a menos de
    // 0,92 célula estão sobrepostos. Retas lado a lado (distância 1,0) são permitidas.
    void FindOverlaps()
    {
        overlaps.Clear();
        var samples = new List<List<Vector2>>();
        foreach (var s in segments)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(s.piece.def.CenterLength / 0.25f));
            var pts = new List<Vector2>(n + 1);
            for (int k = 0; k <= n; k++) pts.Add(TrackMath.PoseAt(s.a, s.piece.def, s.piece.dir, k / (float)n).pos);
            samples.Add(pts);
        }

        const float min = 0.92f * 0.92f;
        int count = segments.Count;
        for (int i = 0; i < count; i++)
        for (int j = i + 2; j < count; j++)
        {
            if (IsClosed && i == 0 && j == count - 1) continue;   // vizinhas pelo fechamento
            if (Touches(samples[i], samples[j], min)) { overlaps.Add(i); overlaps.Add(j); }
        }
    }

    static bool Touches(List<Vector2> a, List<Vector2> b, float minSqr)
    {
        foreach (var p in a) foreach (var q in b) if ((p - q).sqrMagnitude < minSqr) return true;
        return false;
    }
}
```

**Destaques visuais** (dentro de `Rebuild`, depois de construir as views):
- **Peça selecionada:** troque o material 0 da view por uma versão amarelada do asfalto.
- **Peças sobrepostas:** material 0 com textura listrada laranja.
- **Ponto de inserção:** uma seta amarela posicionada em `InsertPose.World(0.01f)` com rotação `InsertPose.Rotation`.

---

### 6.x Ferramentas sobre a mesa e paleta em grupos (Unity)

- **Paleta em grupos** (`BuilderPanel.PaletteGroup`): a seção PEÇAS separa as fichas em **RETAS** (curta, comprida, curtinha), **CURVAS** (longa e meia, nos dois sentidos), **CURVAS INCLINADAS** e **CRUZAMENTOS E RAMPAS** (os dois cruzamentos e as rampas: `rise ≠ 0`). Cada grupo tem um título `.pal-group` e a sua grade `.palette`. Cada ficha continua mostrando o ícone e a tecla.
- **Selo de status sobre a mesa** (`BuildTableTools`, `RefreshTableTools`): no topo, centrado na área da mesa (à direita do painel), mostra o mesmo texto do status do painel, com borda verde (fechado) ou laranja (aviso). Ele traz o botão que resolve o problema: **Fechar circuito (Q)** quando a pista está aberta e sem sobreposição, e **Mostrar** quando há peças sobrepostas (seleciona a de menor índice entre `Info.overlaps`). Só aparece na aba Construtor.
- **Barra de ações** (`.action-bar`, canto de baixo à direita): `↶` desfazer, `↷` refazer · `⇄` inverter a curva selecionada, `⟳` girar a largada, `✕` remover a selecionada (ou a última) · `⌖` enquadrar, `3D` vista inclinada (acesa quando ligada), `−`/`+` zoom (`TopDownCamera.ZoomStep(1,25)`/`(0,8)`: muda o tamanho da vista de cima ou a distância na 3D). Botões desligam quando não há o que fazer (sem desfazer/refazer, sem peça selecionada, só a largada). Cada um tem a tecla no tooltip.

Teste (Play mode, rascunho com backup e restaurado): com o 8 do rascunho mais uma reta, o selo mostrou "Há 4 peças sobrepostas" com **Mostrar** visível e **Fechar** escondido; a barra mostrou `↷` e `⇄` desligados; os grupos ficaram na ordem RETAS, CURVAS, CURVAS INCLINADAS, CRUZAMENTOS E RAMPAS. **Mostrar** selecionou a peça 0, e o `↶` da barra voltou de 27 para 26 peças.

## 7. Mesa quadriculada

`GridBoard.cs` gera a textura da grade em código: linha fina a cada célula e linha forte a cada 4 (o tamanho de uma reta comprida).

```csharp
using UnityEngine;

public class GridBoard : MonoBehaviour
{
    public TrackBuilder track;
    public int majorEvery = 4;
    public Color boardColor = new Color32(0xF3, 0xF6, 0xF0, 0xFF);
    public Color minorColor = new Color32(0xD5, 0xDC, 0xD2, 0xFF);
    public Color majorColor = new Color32(0xA6, 0xB3, 0xA4, 0xFF);

    GameObject quad;

    void Start() => Build();

    public void Build()
    {
        const int px = 32;                              // pixels por célula
        int size = px * majorEvery;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool major = x < 2 || y < 2;
            bool minor = x % px == 0 || y % px == 0;
            pixels[y * size + x] = major ? majorColor : minor ? minorColor : boardColor;
        }
        tex.SetPixels(pixels);
        tex.Apply();

        if (quad) Destroy(quad);
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "Mesa";
        quad.transform.SetParent(transform, false);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);    // virado para cima
        var b = track.board;
        quad.transform.localPosition = new Vector3(b.x / 2f, -0.01f, b.y / 2f);
        quad.transform.localScale = new Vector3(b.x, b.y, 1f);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.mainTexture = tex;
        mat.mainTextureScale = new Vector2(b.x / (float)majorEvery, b.y / (float)majorEvery);
        quad.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
```

**Réguas de referência (colunas 1, 2, 3… e linhas A, B, C…):** faça como no HTML, em espaço de tela. Num `Label` de UI Toolkit ou num TextMeshPro, posicione cada rótulo com `Camera.WorldToScreenPoint(new Vector3(c + 0.5f, 0, borda))`. Se as células ficarem pequenas demais na tela, mostre só 1 a cada 2 ou a cada 4.

**Célula sob o mouse:** um Quad 1×1 semitransparente em `(floor(x) + 0.5, 0.005, floor(z) + 0.5)`.

---

## 8. Câmera e entrada

`TopDownCamera.cs`: arrastar com o botão direito ou do meio move a vista, a roda dá zoom no ponto do mouse.

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class TopDownCamera : MonoBehaviour
{
    public float minSize = 2f, maxSize = 40f;
    Camera cam;
    Vector3 dragOrigin;

    void Awake() => cam = GetComponent<Camera>();

    public Vector3 MouseOnTable()
    {
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter);
        return ray.GetPoint(enter);
    }

    void Update()
    {
        var m = Mouse.current; if (m == null) return;

        float scroll = m.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            Vector3 before = MouseOnTable();
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * Mathf.Pow(0.9f, scroll / 120f), minSize, maxSize);
            transform.position += before - MouseOnTable();      // mantém o ponto sob o mouse
        }

        bool panPressed = m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame;
        bool panHeld = m.rightButton.isPressed || m.middleButton.isPressed;
        if (panPressed) dragOrigin = MouseOnTable();
        else if (panHeld) transform.position += dragOrigin - MouseOnTable();
    }
}
```

> O valor da roda varia por plataforma (às vezes ±120, às vezes ±1). Ajuste o divisor `120f` se o zoom ficar lento ou rápido demais.

`BuilderInput.cs`: teclado e mouse do construtor.

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class BuilderInput : MonoBehaviour
{
    public TrackBuilder track;
    public TopDownCamera cam;

    bool draggingStart, moved;
    Vector3 grabWorld;
    Pose2 grabOrigin;

    void Update()
    {
        var kb = Keyboard.current; var m = Mouse.current;
        if (kb == null || m == null) return;

        bool ctrl = kb.ctrlKey.isPressed;
        if (ctrl && kb.zKey.wasPressedThisFrame) { if (kb.shiftKey.isPressed) track.Redo(); else track.Undo(); }
        else if (ctrl && kb.yKey.wasPressedThisFrame) track.Redo();
        else if (!ctrl)
        {
            if (kb.digit1Key.wasPressedThisFrame) track.Add(track.shortStraight, 0);
            if (kb.digit2Key.wasPressedThisFrame) track.Add(track.longStraight, 0);
            if (kb.digit3Key.wasPressedThisFrame) track.Add(track.curve, -1);
            if (kb.digit4Key.wasPressedThisFrame) track.Add(track.curve, 1);
            if (kb.digit5Key.wasPressedThisFrame) track.Add(track.halfCurve, -1);
            if (kb.digit6Key.wasPressedThisFrame) track.Add(track.halfCurve, 1);
            if (kb.fKey.wasPressedThisFrame) track.FlipSelected();
            if (kb.rKey.wasPressedThisFrame) track.RotateStart();
            if (kb.deleteKey.wasPressedThisFrame) track.RemoveSelected();
            if (kb.backspaceKey.wasPressedThisFrame) { if (track.selected > 0) track.RemoveSelected(); else track.RemoveLast(); }
            if (kb.escapeKey.wasPressedThisFrame) track.Select(-1);
            if (kb.spaceKey.wasPressedThisFrame) track.Playing = !track.Playing;
        }

        // Clique: seleciona a peça. Arrastar a largada move a pista inteira, encaixando na grade.
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (m.leftButton.wasPressedThisFrame && !overUI)
        {
            Ray ray = Camera.main.ScreenPointToRay(m.position.ReadValue());
            int hitIndex = -1;
            if (Physics.Raycast(ray, out var hit) && hit.collider.TryGetComponent(out TrackPieceView v)) hitIndex = v.index;
            draggingStart = hitIndex == 0;
            moved = false;
            grabWorld = cam.MouseOnTable();
            grabOrigin = track.origin;
            if (!draggingStart) track.Select(hitIndex);
        }
        else if (draggingStart && m.leftButton.isPressed)
        {
            Vector3 d = cam.MouseOnTable() - grabWorld;
            var o = TrackMath.SnapStart(new Pose2(grabOrigin.pos + new Vector2(d.x, d.z), grabOrigin.heading));
            if (o.pos != track.origin.pos)
            {
                if (!moved) { track.BeginEdit(); moved = true; }   // um único "desfazer" pelo arraste todo
                track.origin = o;
                track.Rebuild();
            }
        }
        else if (draggingStart && m.leftButton.wasReleasedThisFrame)
        {
            if (!moved) track.Select(0);
            draggingStart = false;
        }
    }
}
```

> `IsPointerOverGameObject` reconhece painéis de UI Toolkit desde que a cena tenha um **EventSystem** com **Input System UI Input Module**.

---

## 9. Salvar e carregar (mesmo formato do HTML)

Use o mesmo JSON do botão **Copiar** do HTML. Assim uma pista montada no navegador abre na Unity, e vice-versa.

O HTML usa Y para baixo e ângulo no sentido horário. Na Unity: **`z = altura da mesa - y`** e **`heading = -h`**.

`TrackSave.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class BoardSave  { public int w = 32, h = 20; }
[Serializable] public class OriginSave { public float x, y, h; }
[Serializable] public class PieceSave  { public string tipo; public string sentido; }

[Serializable]
public class TrackSave
{
    public string formato = "autorama-pista";
    public int versao = 1;
    public BoardSave mesa = new();
    public float escalaCm = 15f;
    public OriginSave largada = new();
    public List<PieceSave> pecas = new();

    public static string ToJson(TrackBuilder t)
    {
        var s = new TrackSave
        {
            mesa = new BoardSave { w = t.board.x, h = t.board.y },
            escalaCm = t.cellCm,
            largada = new OriginSave
            {
                x = t.origin.pos.x,
                y = t.board.y - t.origin.pos.y,
                h = TrackMath.Normalize(-t.origin.heading),
            },
        };
        for (int i = 1; i < t.pieces.Count; i++)                    // a largada não vai na lista
        {
            var p = t.pieces[i];
            s.pecas.Add(new PieceSave
            {
                tipo = p.def.id,
                sentido = p.dir < 0 ? "esquerda" : p.dir > 0 ? "direita" : null,
            });
        }
        return JsonUtility.ToJson(s, true);
    }

    public static void FromJson(TrackBuilder t, string json)
    {
        var s = JsonUtility.FromJson<TrackSave>(json);
        var byId = new Dictionary<string, PieceDefinition>
        {
            { t.shortStraight.id, t.shortStraight }, { t.longStraight.id, t.longStraight },
            { t.curve.id, t.curve }, { t.halfCurve.id, t.halfCurve },
        };

        t.board = new Vector2Int(s.mesa.w, s.mesa.h);
        t.cellCm = s.escalaCm;
        t.origin = TrackMath.SnapStart(new Pose2(new Vector2(s.largada.x, s.mesa.h - s.largada.y), -s.largada.h));
        t.pieces.Clear();
        t.pieces.Add(new PieceInstance(t.start, 0));
        foreach (var p in s.pecas)
        {
            if (!byId.TryGetValue(p.tipo, out var def)) { Debug.LogWarning($"Peça desconhecida: {p.tipo}"); continue; }
            int dir = def.kind == PieceKind.Curve ? (p.sentido == "esquerda" ? -1 : 1) : 0;
            t.pieces.Add(new PieceInstance(def, dir));
        }
        t.selected = -1;
        t.Rebuild();
    }
}
```

Para gravar em disco: `System.IO.File.WriteAllText(Path.Combine(Application.persistentDataPath, "pista.json"), TrackSave.ToJson(track))`.

**Biblioteca de pistas** (como "Minhas pistas" no HTML):
- Grave cada pista num arquivo próprio em `Application.persistentDataPath/pistas/<id>.json`.
- Use o mesmo `TrackSave`, com o campo `nome`.
- A tela de corrida lista esses arquivos com `Directory.GetFiles`, mais os exemplos que vêm no jogo (TextAssets em `Resources`).
- Na lista, mostre só as pistas fechadas como escolhíveis.
- Escolher uma pista para correr carrega uma **cópia**: a pista aberta no construtor não muda.

No `TrackSave`, acrescente `public string nome;`. O HTML já exporta esse campo.

> O desfazer/refazer do `TrackBuilder` usa esse mesmo JSON como foto do estado, então sai de graça.

---

## 10. Carrinhos e teste de volta

Cada trilho vira uma polilinha com distância acumulada. O carrinho anda por distância, o que dá velocidade constante em reta e curva.

`LanePath.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

public class LanePath
{
    readonly List<Vector2> pts = new();
    readonly List<float> cum = new();
    public float Total => cum.Count > 0 ? cum[^1] : 0f;

    public LanePath(IReadOnlyList<Segment> segs, float offset)
    {
        for (int s = 0; s < segs.Count; s++)
        {
            var seg = segs[s];
            int n = Mathf.Max(1, Mathf.CeilToInt(seg.piece.def.CenterLength / 0.08f));
            for (int j = s == 0 ? 0 : 1; j <= n; j++)
                pts.Add(TrackMath.Offset(TrackMath.PoseAt(seg.a, seg.piece.def, seg.piece.dir, j / (float)n), offset));
        }
        cum.Add(0f);
        for (int i = 1; i < pts.Count; i++) cum.Add(cum[i - 1] + Vector2.Distance(pts[i], pts[i - 1]));
    }

    public Pose2 At(float d)
    {
        d = Mathf.Clamp(d, 0f, Total);
        int lo = 0, hi = cum.Count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (cum[mid] <= d) lo = mid; else hi = mid; }
        float t = (d - cum[lo]) / Mathf.Max(cum[hi] - cum[lo], 1e-5f);
        Vector2 a = pts[lo], b = pts[hi], delta = b - a;
        return new Pose2(Vector2.Lerp(a, b, t), Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }
}
```

`CarRunner.cs`: um por carrinho, com `lane = 0` (vermelho) e `lane = 1` (azul).

```csharp
using UnityEngine;

public class CarRunner : MonoBehaviour
{
    public TrackBuilder track;
    public int lane;
    public float speed = 8f;                 // células por segundo
    public int Laps { get; private set; }

    LanePath path;
    float distance = 0.75f;                  // estacionado no grid de largada

    void OnEnable()  => track.Changed += ResetCar;
    void OnDisable() => track.Changed -= ResetCar;

    void ResetCar() { path = null; distance = 0.75f; Laps = 0; Place(); }

    void Update()
    {
        if (!track.Playing) return;
        distance += speed * Time.deltaTime;
        path ??= new LanePath(track.segments, TrackMath.Lanes[lane]);
        if (distance >= path.Total)
        {
            if (track.IsClosed) { distance -= path.Total; Laps++; }
            else distance = 0f;              // pista aberta: volta para a largada
        }
        Place();
    }

    void Place()
    {
        if (track.segments.Count == 0) return;
        path ??= new LanePath(track.segments, TrackMath.Lanes[lane]);
        var p = path.At(distance);
        transform.SetPositionAndRotation(p.World(0.05f), p.Rotation);
    }
}
```

Como os dois andam na mesma velocidade, o carrinho do trilho mais curto vai abrindo vantagem. É a mesma diferença que aparece no resumo da pista.

---

## 11. Interface (UI Toolkit)

`Builder.uxml`, só a estrutura:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
  <ui:VisualElement name="side" class="side">
    <ui:Label name="status" class="status" />
    <ui:Label text="Peças" class="h2" />
    <ui:VisualElement class="palette">
      <ui:Button name="reta-curta"       text="Reta curta [1]" />
      <ui:Button name="reta-comprida"    text="Reta comprida [2]" />
      <ui:Button name="curva-esquerda"   text="Curva longa esq. [3]" />
      <ui:Button name="curva-direita"    text="Curva longa dir. [4]" />
      <ui:Button name="meia-esquerda"    text="Meia curva esq. [5]" />
      <ui:Button name="meia-direita"     text="Meia curva dir. [6]" />
    </ui:VisualElement>
    <ui:Label name="stats" />
    <ui:Button name="play" text="Testar volta" />
  </ui:VisualElement>
</ui:UXML>
```

`BuilderUI.cs`:

```csharp
using UnityEngine;
using UnityEngine.UIElements;

public class BuilderUI : MonoBehaviour
{
    public UIDocument doc;
    public TrackBuilder track;
    VisualElement root;

    void OnEnable()
    {
        root = doc.rootVisualElement;
        root.Q<Button>("reta-curta").clicked     += () => track.Add(track.shortStraight, 0);
        root.Q<Button>("reta-comprida").clicked  += () => track.Add(track.longStraight, 0);
        root.Q<Button>("curva-esquerda").clicked += () => track.Add(track.curve, -1);
        root.Q<Button>("curva-direita").clicked  += () => track.Add(track.curve, 1);
        root.Q<Button>("meia-esquerda").clicked  += () => track.Add(track.halfCurve, -1);
        root.Q<Button>("meia-direita").clicked   += () => track.Add(track.halfCurve, 1);
        root.Q<Button>("play").clicked           += () => track.Playing = !track.Playing;
        track.Changed += Refresh;
    }

    void OnDisable() => track.Changed -= Refresh;

    void Refresh()
    {
        string status = track.overlaps.Count > 0 ? $"Há {track.overlaps.Count} peças sobrepostas"
                      : track.IsClosed ? "Circuito fechado"
                      : "Pista aberta";
        root.Q<Label>("status").text = status;

        float m(float cells) => cells * track.cellCm / 100f;
        root.Q<Label>("stats").text =
            $"Linha central: {track.CenterLength:0.0} cél. ({m(track.CenterLength):0.00} m)\n" +
            $"Trilho 1: {track.LaneLengths[0]:0.0} cél. ({m(track.LaneLengths[0]):0.00} m)\n" +
            $"Trilho 2: {track.LaneLengths[1]:0.0} cél. ({m(track.LaneLengths[1]):0.00} m)\n" +
            $"Peças: {track.pieces.Count}";
    }
}
```

**Prévia "fantasma":** no `PointerEnterEvent` de cada botão, ative um `TrackPieceView` extra com o material Fantasma e chame `Build(track.InsertPose, def, dir, ...)`. No `PointerLeaveEvent`, desative.

---

### 11.x Tela inicial e paleta de cores

**Tela inicial** (`BuilderPanel.BuildStartMenu`, `ShowMenu(bool)`, `MenuOpen`): ao abrir o jogo, um cartão no meio da tela escura mostra "AUTORAMA" e quatro opções, cada uma com ícone e uma linha de explicação: **Construir**, **Correr**, **Campeonato** e **Online**. Escolher uma fecha o menu e chama `SetMode`. O botão **⌂** no topo do painel volta para o menu (não durante um campeonato online). Qualquer `SetMode` também fecha o menu, então um convite da Steam (que chama `SetMode(Online)`) passa direto. Com o menu aberto, `EditLocked` é verdadeiro: as teclas do construtor não colocam peças por trás dele. Estilos `.start*`. Só a Unity tem essa tela.

**Paleta de cores** (topo de `UI/Construtor.uss`): todas as cores repetidas viraram variáveis num bloco `:root`, e o resto do arquivo usa `var(--nome)`. São 21 cores e 173 trocas; a aparência não muda. Principais: `--texto` (229, 235, 227), `--texto-fraco` (148, 161, 151), `--destaque` (245, 194, 27, o amarelo), `--alerta` (240, 122, 58), `--ok` (61, 190, 116), `--trilho-a` (240, 74, 63), `--trilho-b` (80, 137, 242), `--fundo` / `--fundo-vidro` / `--fundo-painel` (22, 28, 25 com 1 / 0,94 / 0,97), `--borda*`. Para mudar o visual, mexa só nesse bloco. As cores calculadas em C# (barra de derrapagem, cor dos pilotos) continuam no código.

Teste (Play mode): o cartão abre centrado (560 × 351); as cores resolvidas batem com as variáveis (título 0,961/0,761/0,106 = `--destaque`; painel 0,086/0,110/0,098 com 0,97 = `--fundo-painel`). Clicar **Correr** fecha o menu e vai para a aba Corrida; `ShowMenu(true)` reabre. Ao abrir o jogo: `MenuOpen = true` e `EditLocked = true`; `SetMode(Online)` (o caminho do convite) fecha o menu.

## 12. Montando a cena

1. **Mesa:** GameObject vazio com `GridBoard`.
2. **Pista:** GameObject vazio com `TrackBuilder`. Ligue as 5 peças, o prefab e os 6 materiais na ordem.
3. **Câmera:** `TopDownCamera` na Main Camera.
4. **Entrada:** GameObject com `BuilderInput`, ligado à pista e à câmera.
5. **Carrinhos:** dois prefabs (um cubo de 0,46 × 0,1 × 0,22 já serve), cada um com `CarRunner`, `lane` 0 e 1.
6. **UI:** `UIDocument` com o `Builder.uxml` e `BuilderUI`. Crie também um **EventSystem**.
7. Dê Play, aperte `2 2 4 4 2 2 1 4 4` e a pista deve fechar num oval. A volta precisa de uma reta curta a mais para compensar a base de largada.

### Pista de exemplo (a mesma do HTML)

Depois da largada: reta comprida, meia curva esq., meia curva dir., curva longa dir., curva longa dir., reta comprida, meia curva esq., meia curva dir., reta curta, curva longa dir., curva longa dir. Fecha certinho. O trilho 1 sai cerca de 3,1 células mais longo, porque todas as curvas de 90° viram para a direita.

---

## 13. Ordem sugerida

1. `TrackMath` + teste da curva (seção 4). Sem isso nada encaixa.
2. `PieceDefinition` + `TrackPieceView` só com o asfalto. Veja uma peça aparecer.
3. `TrackBuilder` com Add e Rebuild, pelo teclado.
4. Grade e câmera.
5. Seleção, remover, inverter, girar e arrastar a largada.
6. Fechamento e sobreposição.
7. Desfazer, salvar e carregar.
8. Trilhos, fendas, zebras e bandeirada.
9. Carrinhos.
10. UI e prévia fantasma.

## 14. Próximos passos

- **Ponte sobre ponte:** o modelo já aceita nível 2. Falta um exemplo que use.
- **Curvas de outros raios:** basta criar novos assets de `PieceDefinition`. O código não muda.
- Feitos depois: cruzamento (seção 18), fechar o circuito sozinho (seção 17) e o campeonato com baterias trocando de trilho (seção 19).

---

## 15. Modo corrida

A aba **Corrida** do `index.html` já roda esse modelo: dá para jogar com duas pessoas no mesmo teclado ou no mesmo celular. Esta seção explica a lógica e como levar para a Unity.

### 15.1 O controle: um botão que vira acelerador

Cada jogador tem **dois botões**: Acelerar e Recolocar.

| Ação | Efeito |
|---|---|
| Segurar Acelerar | O acelerador `T` sobe de 0 a 1, linear, em `rampTime` (0,5 s por padrão). |
| Soltar Acelerar | `T` volta a 0 **na hora**, e o carrinho freia. |
| Recolocar | Só funciona com o carrinho fora da pista. Devolve o carrinho ao trilho. |

```
T = segurando ? min(1, tempoSegurando / rampTime) : 0
```

Isso cria três jeitos de pilotar:
- **Reta:** segura direto até 100%.
- **Antes da curva:** solta para frear. Saber onde soltar é a habilidade principal.
- **Dentro e na saída da curva:** segura de novo. Como `T` recomeça do zero e sobe aos poucos, a potência volta suave. Toques curtos mantêm uma velocidade média.

A rampa é o que dá controle fino a um botão de liga e desliga. Com `rampTime` curto, o jogo fica nervoso. Com `rampTime` longo, fica fácil e lento.

### 15.1.1 Acelerador dosado: controle (gamepad) e tecla de meia (só Unity)

Pedido dos jogadores: poder segurar meia aceleração numa curva em vez de ficar batucando a tecla. Cada carrinho ganhou **`CarState.gas`** (0–1): até onde o acelerador vai enquanto está segurando.

```csharp
if (c.held && c.finish == null)
{
    c.hold += dt;
    float g = Mathf.Clamp01(c.gas);
    c.T = c.T > g ? g : Mathf.Min(g, Mathf.Min(1f, c.hold / set.ramp));   // sobe no ritmo de sempre, desce na hora
}
```

Com `gas = 1` (teclado A/L, IA) é exatamente a conta antiga: **a física e os recordes não mudam**. A IA fica sempre com `gas = 1`.

De onde vem o nível (`RaceController.GasLevel`, o maior dos quatro):

| Entrada | Jogador 1 | Jogador 2 | Nível |
|---|---|---|---|
| Tecla de acelerar | **A** | **L** | 100% |
| Tecla de meia | **Q** | **O** | `halfGas` (ajuste "Meia aceleração (Q · O)", 20–90%, padrão **60%**; em `RaceSettings`, não mexe nas regras do campeonato) |
| Gatilho direito do controle | 1º controle conectado | 2º controle | analógico, com zona morta de 6%: `(v − 0,06) / 0,94` |
| Botão ACELERAR da tela | toque/clique | toque/clique | 100% (não ficou analógico: o arrastar com o mouse foi descartado) |

No controle, **A/✕** recoloca e **Start** larga (como Enter). Os controles seguem o `KeySet` (19.3): com um humano só na bateria, o 1º controle e Q pilotam em qualquer trilho. O botão ACELERAR de cada jogador mostra as teclas e "controle" quando há um controle para ele: `ACELERAR  A  ·  ½ Q  ·  controle`.

**Online:** cada um pilota o seu carrinho com A/L/Espaço (100%), Q/O (meia) ou o gatilho de qualquer controle ligado. O visitante manda o nível na mensagem `in` como **`gp`** (inteiro 1–100), quando o botão muda, quando o nível anda 3 pontos ou mais, ou a cada 0,25 s. O host guarda em `remoteGas` e aplica no `RaceSim`. O snapshot leva `gp` de cada carrinho (`NetCar.gp`), para a predição do visitante usar o nível certo do adversário. `gp = 0` (campo ausente, cliente antigo) vale 100%, por isso o protocolo continua na versão 1. O servidor repassa `gp` limitado a 1–100 (ausente → 100); a Steam repassa a mensagem inteira.

Testes:
- **Física** (`script eval`, oval, sem cena): acelerador no fundo, 0,26 s depois `T = 0,520` (a conta antiga dá 0,52); meia (`gas 0,5`) estabiliza em **v = 7,00 cél/s** (metade de 14), no trilho; com o gatilho a 80% o carrinho sai na curva do oval (11,2 cél/s passa do limite dela).
- **Controles** (Play mode, dois gamepads virtuais com `InputSystem.AddDevice`): gatilho a 50% no 1º → `GasLevel` do trilho 1 = **0,468**; 100% no 2º → trilho 2 = **1,000**; os dois botões mostram "controle". Com o Editor em segundo plano a Unity não entrega a entrada dos controles ao jogo nos quadros do `Step`, então o carro andando com o gatilho precisa ser visto com o Editor em foco.
- **Servidor** (`npm test`): `in` sem `gp` chega com 100; `gp 55` chega 55; `gp 900` chega 100.

### 15.2 O motor

O carrinho não obedece `T` na hora: ele **persegue** a velocidade `T × vMax`, como um motor elétrico de autorama.

```
se T > 0:  a = accel × (T × vMax − v)          // acelera ou alivia até o alvo
se T = 0:  a = −(brake × v + brakeFixed)        // freio de motor ao soltar
v = max(0, v + a × dt)
distância += v × dt
```

| Parâmetro | Padrão | Efeito |
|---|---|---|
| `vMax` | 14 cél/s | Velocidade de reta com o acelerador no fundo |
| `accel` | 3,5 /s | Quão rápido o motor responde |
| `brake`, `brakeFixed` | 1,8 /s e 0,5 cél/s² | Força do freio ao soltar |

### 15.3 Derrapar e sair da pista (a parte difícil)

Numa curva de raio `r`, o carrinho sente uma força lateral `v² / r`. O pneu e a guia aguentam até `grip`. Daí sai a velocidade limite de cada curva:

```
vLimite = √(grip × r)
```

Com `grip = 29` e as curvas de raio 2,5:
- o trilho de dentro (r = 2,25) segura **8,1 cél/s**;
- o de fora (r = 2,75) segura **8,9 cél/s**.

Os dois ficam em torno de 60% da velocidade de reta. Numa curva à direita, o trilho 2 fica por dentro e é o mais lento. Na curva seguinte, para a esquerda, a situação inverte.

**Passar do limite não derruba na hora.** Se o carrinho voasse no primeiro décimo de segundo acima do limite, o jogo pareceria injusto. Por isso existe um acumulador de **derrapagem** (`slip`, de 0 a 1):

```
excesso = (v² × |curvatura|) / grip − 1
se excesso > 0:  slip += excesso × slipGain × dt     // passou do limite: enche
senão:           slip −= slipDecay × dt               // dentro do limite: esvazia
se slip ≥ 1:     sai da pista
```

- Passar um pouco do limite numa curva curta enche a barra, mas não derruba. O jogador vê a traseira escorregar e solta a tempo.
- Passar muito, ou por uma curva longa (duas curvas de 90° seguidas), derruba.
- Com `slipGain = 6`, uma curva de 90° a **110%** do limite enche a barra até uns 55%. A **120%**, o carrinho sai.

**O carrinho "roda" antes de sair.** No autorama, o carrinho gira em volta da **guia**, a lâmina na frente que fica presa na fenda. A frente fica no trilho e a traseira escorrega para fora da curva:

```
guia         = pontoNoTrilho + direção(ângulo) × pin            // pin = 0,22 célula
ânguloCarro  = ângulo + lado × slip^1.4 × yawMax                 // yawMax = 38°
centroCarro  = guia − direção(ânguloCarro) × pin
```

Assim a barra de derrapagem aparece no próprio carrinho: quanto mais cheia, mais de lado ele anda. O expoente 1,4 deixa uma derrapagem pequena quase invisível e faz a rodada final ser rápida.

**Saindo da pista:** com `slip` em 1, o carrinho larga a fenda:
- Sai pela **tangente** do trilho, com mais 35% da velocidade empurrando para fora da curva.
- Gira no sentido da curva, a `220 + 30 × v` graus por segundo.
- Velocidade e giro caem aos poucos (atrito) até ele parar.
- Guarda a distância em que saiu (`exitS`).

**Recolocar** (o segundo botão) faz o papel do fiscal de pista:
- O carrinho volta para `exitS − 0,4`, um pouco antes de onde saiu, para não entrar embalado no mesmo ponto.
- O retorno é uma animação de 0,6 s, da posição na mesa até o trilho.
- Ele volta parado, com `v = 0` e `slip = 0`.
- O castigo é o tempo perdido. Não precisa de mais nada.

### 15.4 Teste da afinação (simulação da pista de exemplo, 5 voltas)

| Estratégia | Melhor volta | Saídas |
|---|---|---|
| Acelerador sempre no fundo | 10,7 s | 30 (6 por volta) |
| Solta antes das curvas, alvo 95% do limite | 4,9 s | 0 |
| Solta antes das curvas, alvo 120% do limite | 4,1 s | 0 |
| Solta antes das curvas, alvo 140% do limite | 7,3 s | 15 |
| Toques de 0,3 s, sem olhar a pista | 10,2 s | 0 |

A curva de risco funciona:
- Arriscar um pouco acima do limite é o jeito mais rápido.
- Arriscar demais custa caro.
- Acelerar no fundo e ir recolocando perde de longe.

O trilho 2 é cerca de 3 células mais curto nessa pista, e por isso vence com a mesma pilotagem. Daí a ideia de baterias trocando de trilho (seção 14).

### 15.5 Na Unity

**Curvatura no `LanePath`.** Guarde a curvatura de cada ponto: `+1/raio` em curva à direita, 0 em reta. No construtor do `LanePath` (seção 10), dentro do laço por segmento, antes do `for` interno:

```csharp
var def = seg.piece.def;
float kap = def.kind == PieceKind.Curve ? seg.piece.dir / (def.radius - seg.piece.dir * offset) : 0f;
```

E, junto de cada `pts.Add(...)`, faça `ks.Add(kap);`. No resto da classe:

```csharp
readonly List<float> ks = new();

public float CurvatureAt(float dist) => ks[IndexAt(dist)];

int IndexAt(float dist)
{
    dist = Mathf.Repeat(dist, Total);
    int lo = 0, hi = cum.Count - 1;
    while (hi - lo > 1) { int mid = (lo + hi) / 2; if (cum[mid] <= dist) lo = mid; else hi = mid; }
    return hi;
}
```

Em `At`, troque `Mathf.Clamp(d, 0f, Total)` por `Mathf.Repeat(d, Total)` para o carrinho poder dar voltas.

**Entrada (Input System).** Crie um Input Actions com quatro ações do tipo Button:
- `P1/Acelerar` (tecla A, botão sul do gamepad 1)
- `P1/Recolocar` (S)
- `P2/Acelerar` (L)
- `P2/Recolocar` (K)

No celular, dois botões grandes de UI com `PointerDownEvent` e `PointerUpEvent` fazem o mesmo papel.

`SlotCar.cs`, que substitui o `CarRunner` no modo corrida:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class SlotCar : MonoBehaviour
{
    public enum State { OnSlot, Off, Returning }

    [Header("Pista")]
    public TrackBuilder track;
    public int lane;                                   // 0 = trilho 1, 1 = trilho 2
    [Header("Controle")]
    public InputActionReference gasAction, reslotAction;
    [Header("Motor")]
    public float vMax = 14f, rampTime = 0.5f, accel = 3.5f, brake = 1.8f, brakeFixed = 0.5f;
    [Header("Aderência")]
    public float grip = 29f, slipGain = 6f, slipDecay = 1.5f, yawMax = 38f, pin = 0.22f;

    public State CurrentState { get; private set; } = State.OnSlot;
    public float Throttle { get; private set; }            // 0..1, para a barra da UI
    public float Speed { get; private set; }
    public float Slip { get; private set; }                // 0..1, para a barra da UI
    public float Distance { get; private set; } = 0.75f;   // só cresce: serve para contar voltas
    public bool CanDrive { get; set; }                     // o RaceManager libera na luz verde
    public int Exits { get; private set; }
    public float LapLength => path?.Total ?? 1f;

    LanePath path;
    float hold, side = 1f, exitDistance;
    Vector2 flyPos, flyVel;
    float flyHeading, flySpin;
    Pose2 returnFrom;
    float returnT;

    void OnEnable()  { gasAction.action.Enable(); reslotAction.action.Enable(); track.Changed += ResetCar; }
    void OnDisable() { track.Changed -= ResetCar; }

    public void ResetCar()
    {
        path = new LanePath(track.segments, TrackMath.Lanes[lane]);
        Distance = 0.75f; Speed = 0; Slip = 0; Exits = 0; CurrentState = State.OnSlot;
        Place(SlotPose());
    }

    void Update()
    {
        if (path == null) ResetCar();
        float dt = Time.deltaTime;

        // 1. Acelerador: sobe enquanto segura, zera ao soltar.
        if (gasAction.action.IsPressed()) { hold += dt; Throttle = Mathf.Min(1f, hold / rampTime); }
        else { hold = 0; Throttle = 0; }

        if (reslotAction.action.WasPressedThisFrame()) Reslot();

        switch (CurrentState)
        {
            case State.OnSlot:    Drive(dt); Place(SlotPose()); break;
            case State.Off:       Fly(dt); break;
            case State.Returning: Return(dt); break;
        }
    }

    void Drive(float dt)
    {
        // 2. Motor. Antes da luz verde e depois da bandeirada, conta como acelerador solto.
        float t = CanDrive ? Throttle : 0f;
        float a = t > 0 ? accel * (t * vMax - Speed) : -(brake * Speed + brakeFixed);
        Speed = Mathf.Max(0f, Speed + a * dt);
        Distance += Speed * dt;

        // 3. Derrapagem
        float k = path.CurvatureAt(Distance);
        if (k != 0) side = Mathf.Sign(k);
        float excess = Speed * Speed * Mathf.Abs(k) / grip - 1f;
        Slip = excess > 0 ? Slip + excess * slipGain * dt : Mathf.Max(0f, Slip - slipDecay * dt);
        if (Slip >= 1f) Deslot();
    }

    // Pose no trilho, girando em volta da guia conforme derrapa.
    Pose2 SlotPose()
    {
        Pose2 q = path.At(Distance);
        // Na Unity o ângulo cresce para a esquerda: numa curva à direita (side +1) o ângulo diminui.
        float heading = q.heading - side * Mathf.Pow(Slip, 1.4f) * yawMax;
        Vector2 guide = q.pos + TrackMath.Dir(q.heading) * pin;
        return new Pose2(guide - TrackMath.Dir(heading) * pin, heading);
    }

    void Deslot()
    {
        Pose2 body = SlotPose(), q = path.At(Distance);
        Vector2 tangent = TrackMath.Dir(q.heading);
        Vector2 outward = TrackMath.Dir(q.heading + side * 90f);   // para fora da curva
        flyPos = body.pos;
        flyHeading = body.heading;
        flyVel = (tangent + outward * 0.35f) * Speed;
        flySpin = -side * (220f + Speed * 30f);                       // gira no sentido da curva
        exitDistance = Distance;
        Speed = 0; Slip = 0; Exits++;
        CurrentState = State.Off;
    }

    void Fly(float dt)
    {
        flyVel *= Mathf.Exp(-2.4f * dt);
        flySpin *= Mathf.Exp(-2.8f * dt);
        flyPos += flyVel * dt;
        flyHeading += flySpin * dt;
        Place(new Pose2(flyPos, flyHeading));
    }

    void Reslot()
    {
        if (CurrentState != State.Off) return;
        returnFrom = new Pose2(flyPos, flyHeading);
        Distance = Mathf.Max(0.75f, exitDistance - 0.4f);
        returnT = 0;
        CurrentState = State.Returning;
    }

    void Return(float dt)
    {
        returnT += dt / 0.6f;
        Pose2 to = SlotPose();
        float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(returnT));
        Place(new Pose2(Vector2.Lerp(returnFrom.pos, to.pos, e), Mathf.LerpAngle(returnFrom.heading, to.heading, e)));
        if (returnT >= 1f) { CurrentState = State.OnSlot; Speed = 0; Slip = 0; }
    }

    void Place(Pose2 p) => transform.SetPositionAndRotation(p.World(0.05f), p.Rotation);
}
```

> Na primeira vez que rodar, confira os sinais. Numa curva à direita, a traseira tem que escorregar para a **esquerda** e o carrinho tem que voar para a **esquerda**. Se ele sair para dentro da curva, inverta o sinal de `outward`. Se girar ao contrário, inverta o de `flySpin`.

**Alternativa com a física da Unity:** em `Deslot`, troque o `Rigidbody` do carrinho de kinematic para dinâmico e passe `linearVelocity` (ou `velocity`, antes do Unity 6) e `angularVelocity`. A mesa precisa de um `PhysicsMaterial` com atrito alto. Fica mais vistoso, porque o carrinho bate nas peças e capota, mas é mais difícil de afinar. A versão manual acima é a mesma do HTML.

### 15.6 Largada, voltas e vitória

`RaceManager.cs`:

```csharp
using UnityEngine;

public class RaceManager : MonoBehaviour
{
    public TrackBuilder track;
    public SlotCar[] cars;                 // os 2 carrinhos
    public int laps = 5;
    public float countdown = 2.4f;         // três luzes vermelhas, depois verde
    const float FinishLine = 1.325f;       // bandeirada, medida desde o início da largada

    public float RaceTime { get; private set; }
    public int Lights { get; private set; }        // 1..3 vermelhas; -1 = verde
    public int Winner { get; private set; } = -1;
    public bool Running { get; private set; }
    public readonly int[] lapsDone = new int[2];
    public readonly float[] lastLap = new float[2], bestLap = new float[2];

    readonly int[] crossings = new int[2];
    readonly float[] lapStart = new float[2];

    public void StartRace()
    {
        if (!track.IsClosed) return;
        for (int k = 0; k < cars.Length; k++)
        {
            cars[k].ResetCar(); cars[k].CanDrive = false;
            crossings[k] = -1; lapStart[k] = 0; lapsDone[k] = 0; lastLap[k] = 0; bestLap[k] = 0;
        }
        Winner = -1; RaceTime = -countdown; Running = true;
    }

    void Update()
    {
        if (!Running) return;
        RaceTime += Time.deltaTime;
        if (RaceTime < 0)
        {
            Lights = Mathf.Min(3, Mathf.FloorToInt((RaceTime + countdown) / (countdown / 3f)) + 1);
            return;
        }
        if (Lights != -1) { Lights = -1; foreach (var c in cars) c.CanDrive = true; }

        for (int k = 0; k < cars.Length; k++)
        {
            var car = cars[k];
            if (car.CurrentState != SlotCar.State.OnSlot || lapsDone[k] >= laps) continue;
            int cr = Mathf.FloorToInt((car.Distance - FinishLine) / car.LapLength);
            if (cr <= crossings[k]) continue;
            crossings[k] = cr;
            if (cr < 1) continue;                      // passar a linha logo após largar não conta

            lastLap[k] = RaceTime - lapStart[k];
            lapStart[k] = RaceTime;
            lapsDone[k] = cr;
            if (bestLap[k] == 0 || lastLap[k] < bestLap[k]) bestLap[k] = lastLap[k];
            if (cr >= laps)
            {
                if (Winner < 0) Winner = k;
                car.CanDrive = false;                  // cruzou: solta o acelerador e freia
            }
        }
    }
}
```

`Distance` só cresce: ele não volta a zero a cada volta. Por isso o carrinho recolocado um pouco atrás da linha não conta a mesma volta duas vezes.

### 15.7 Interface de corrida

Por jogador, o mínimo:
- **Botão Acelerar** grande, com a barra de `Throttle` dentro dele. A barra mostra que segurar mais dá mais força.
- **Barra de velocidade** (`Speed / vMax`).
- **Barra de derrapagem** (`Slip`): verde até 45%, amarela até 75%, vermelha acima. É o aviso de que vai sair.
- **Botão Recolocar:** desligado no trilho, piscando quando o carrinho está fora.
- **Placar:** voltas, última volta, melhor volta e número de saídas.

No meio da tela, as **três luzes** da largada e o **aviso de vitória**.

**Placar sobre a mesa** (Unity, `RaceController.UpdateHud`): acima das luzes, uma ficha por carrinho com posição, a bolinha na cor do carrinho (a mesma borda do controle, então no campeonato é a cor do piloto), o nome, `Volta n/N` (ou "Chegou") e a melhor volta. O líder tem borda amarela. Quem lidera (`Leader()`): quem chegou antes; senão quem tem mais voltas; senão quem está mais adiante (`s`). Na vitória, a posição vira 🏁. A ficha some no grid (`Idle`) e quando a pista não é um circuito fechado. Estilos `.hud*` no fim de `UI/Construtor.uss`.

**Painel recolhido na corrida** (`RaceController.SideForPhase`, chamado quando a fase muda, offline e online): na largada (`Countdown`/`Running`) o painel lateral se recolhe para a mesa ficar inteira; ao voltar ao grid ou terminar, ele volta. Só devolve se foi a largada que recolheu (`sideAutoHidden`): se o jogador já estava com o painel fechado, ele continua fechado; e o `Tab` abre o painel no meio da prova. `BuilderPanel.SideVisible` e `SetSideFromRace(bool)` expõem isso.

**Contagem grande** (`UpdateCountdown`): no meio da mesa, um número de 140 px acompanha as luzes vermelhas, cada um por `RaceSim.Count / 3` = 0,8 s: `3`, `2`, `1`. Na luz verde aparece `VAI!` em verde, que some em 0,8 s. Cada número entra em escala 1,35× e encolhe para 1× no primeiro terço do seu tempo. Elemento `.cd-box` (absoluto, ocupa a mesa, `left` = largura do painel) com `.cd-text` / `.cd-text--go`.

**Resultado da prova** (`SetResult` → `DrawResult`): quando a prova acaba, o aviso de vitória ganha uma tabela com as duas posições (ordem de `Leader()`), cada uma com bolinha, nome, tempo total (ou "não terminou"), melhor volta e saídas; o vencedor tem borda amarela. Embaixo, os botões:

- corrida livre: **Correr de novo (Enter)** (`StartRace`) e **Trocar pista** (abre o painel lateral);
- campeonato offline: **Salvar e seguir (Enter)** (`ChampEnter` → `ChampSave`); some quando o campeonato acaba;
- online: só a tabela, sem botões (quem comanda é o host, pela tela do campeonato).

A tabela só é redesenhada quando algo muda (assinatura com vencedor, chegadas, saídas e aba). Estilos `.res-*`.

Teste (Play mode): `StartRace()` + 10 quadros → fase `Countdown`, `t = 0,20`, contagem "3" visível no centro. Prova inteira com os dois robôs (qualidade 90, `sim.Step(0.02)`) → "Trilho 2 venceu", tabela "1º JOGADOR 2 16,00 s melhor 5,10 s 0 saídas / 2º JOGADOR 1 16,36 s melhor 5,10 s 0 saídas", botões visíveis, painel lateral de volta.

Teste (Play mode, `script eval`): `StartRace()` + 200 quadros → painel fechado, placar "1º JOGADOR 1 Volta 1/3 · 2º JOGADOR 2 Volta 1/3"; `sim.Reset()` → painel aberto, placar escondido.

### 15.7.1 Modos de câmera na corrida

A câmera fica num **menu no canto superior direito da mesa** (só nas abas Corrida e Campeonato e na corrida online). O botão mostra o modo atual (`Câmera: Dinâmica ▾`); clicar abre a lista embaixo, com os 4 modos, o alvo e a distância. O painel lateral não tem mais os seletores de câmera.

| Modo (menu) | `RaceViewMode` | O que faz |
|---|---|---|
| **De cima** | `Atual` (0) | Comportamento original da `TopDownCamera`: de cima, `V` alterna para inclinada; pan com botão direito/meio, zoom na roda. |
| **3D** | `ThreeD` (1) | Força a vista inclinada em perspectiva; pan/órbita/zoom manuais continuam. |
| **Atrás do carro** | `ThirdPerson` (2) | Persegue um carrinho. Na corrida local, o menu mostra **Seguir: Jogador 1 · Jogador 2**. A roda ajusta só a distância. |
| **Dinâmica** | `AutoDoisCarros` (3) | Ortográfica. Enquadra os dois carrinhos (centro no ponto médio, zoom automático) ou acompanha só um, conforme abaixo. |

**Qual carrinho a câmera acompanha** (`RaceController.CamFocusCar()`, recalculado a cada quadro em `PlaceCars`):

- **Corrida online:** sempre o **seu** carrinho, `MyLane()`, seja trilho 1 ou 2. Vale para *Atrás do carro* (o alvo escolhido no menu é ignorado e o menu avisa "No online, segue o seu carrinho") e para a *Dinâmica*, que centra em você com o enquadramento-base de `5.5` células. Como o seu trilho muda de uma bateria para outra, o foco é atualizado a cada quadro.
- **Corrida livre contra o robô:** o jogador 1 (trilho 0).
- **Dois humanos no mesmo teclado / campeonato local:** nenhum (`-1`); a Dinâmica enquadra os dois.

`TopDownCamera.SetRaceTargets(p0, h0, p1, h1, int focus = -1)`: `focus` 0 ou 1 centra a Dinâmica naquele carrinho; `-1` enquadra os dois. (Antes era `bool prioritizeFirst`, que só sabia focar o carrinho 0; por isso, no online, quem era o jogador 2 via a câmera seguindo o adversário.)

O slider **Distância** (dentro do menu) vale para todos os modos: `100%` é o padrão, a faixa é `70%–160%`.

Implementação Unity:

- `RaceController.BuildCamMenu(root)` monta o menu sobre a mesa: `.cam-menu` (absoluto, `top 10px`, `right 12px`), o botão `.cam-btn`, e a lista `.cam-pop` (240 px) com um `.cam-opt` por modo (nome + uma linha de dica; o ativo ganha `.cam-opt--on`), o bloco `.cam-sub` do alvo e o `SliderInt` da distância. Estilos no fim de `UI/Construtor.uss`. O selo "Vista 3D" (`.view-badge`) desceu para `top 54px` para não ficar embaixo do botão.
- `ApplyCameraSettings()` aplica modo, alvo e distância e redesenha o menu.
- `RaceSettings` salva `cameraMode` (0..3), `cameraTarget` (0..1) e `cameraDistance` (70..160, padrão 100).

Teste da correção (Play mode, `script eval` chamando `ApplyAutoDoisCarros` com os carrinhos em (−8, 0) e (8, 3)): `focus -1` → câmera em x 0,0 / z 1,5 (meio); `focus 0` → (−8, 0); `focus 1` → (8, 3).

**Folga da Dinâmica** (os carrinhos chegavam no limite da tela, principalmente na pista de 4): a folga em volta do retângulo dos carrinhos passou de fixa (1,3 de lado, 1,1 em cima) para **max(2,5; 30% da maior distância entre eles)**. O placar em cima e os painéis dos jogadores embaixo cobrem ~30% da altura: a altura pedida é dividida por `UiFree = 0,7` e o centro sobe `UiLift = 0,12 × tamanho` para ficar no meio da faixa livre. A câmera **afasta rápido** (taxa 10) e **aproxima devagar** (2,5). Com um carrinho em foco (online), o enquadramento-base subiu de 5,5 para 7. Tamanho entre 4 e `max(maxSize, 60)`. Ainda não conferido na tela.

Parâmetros usados no first pass (podem ser refinados com playtest):

- Third person:
    - distância inicial `7.0`
    - altura `2.2`
    - `fieldOfView = 50`
    - suavização exponencial de posição/rotação.
- Auto (2 carros):
    - margem horizontal `+1.3` célula
    - margem vertical `+1.1` célula
    - single player: centra no jogador 1, usa enquadramento-base de `5.5` células e ignora a posição do robô
    - zoom limitado por `minSize/maxSize` da câmera
    - suavização exponencial para centro e zoom.
- Distância global da câmera:
    - escala aplicada a enquadramento ortográfico, distância em perspectiva, third person e zoom automático
    - faixa `0.70×–1.60×`, padrão `1.00×`

Teste rápido realizado:

- Compilação C# sem erros nos arquivos alterados (`TopDownCamera`, `RaceController`, `RaceSim`).
- Troca de modo em runtime não altera física, trilhos, voltas nem regras da IA; afeta só a visualização.

### 15.8 Colisão entre os carrinhos

Os dois carrinhos correm em trilhos separados. Na reta e dentro do limite, eles **nunca** se tocam. A colisão aparece em dois casos, os mesmos do autorama de verdade:

1. **Raspão lado a lado:** numa curva, o carrinho de **dentro** derrapa, a traseira dele sai para fora e bate no carrinho do trilho de fora. O carrinho de fora, ao derrapar, joga a traseira para fora da pista e não alcança ninguém.
2. **Carrinho solto:** quem saiu da pista atravessa o outro trilho e acerta o carrinho que vem ali, ou os dois soltos batem entre si.

**O tamanho do carrinho é o que faz isso acontecer.** Os trilhos ficam a 0,5 célula um do outro. No autorama real o carrinho ocupa uns 3/4 desse espaço, e por isso ele tem **0,60 × 0,32 célula**. A traseira do carrinho de dentro alcança o vizinho com uns **20° de derrapagem**, o que dá cerca de 65% da barra. Com carrinhos finos (0,46 × 0,22), a traseira só chegava no outro trilho quando o carrinho já estava saindo: a colisão nunca acontecia.

#### Detecção: teste de eixos separados (SAT)

Cada carroceria é um retângulo girado (meio comprimento 0,30 e meia largura 0,16). Dois retângulos **não** se tocam se existe um eixo em que as sombras deles não se sobrepõem. Bastam 4 eixos: frente e lado de cada carrinho. O eixo de menor sobreposição dá a **normal** do choque (para onde empurrar) e a **profundidade**.

```csharp
public static class CarCollision
{
    public const float HalfLength = 0.30f, HalfWidth = 0.16f;

    // Devolve true se as carrocerias se sobrepõem. normal aponta de A para B.
    public static bool Overlap(Pose2 a, Pose2 b, out Vector2 normal, out float depth)
    {
        Vector2[] axes = { TrackMath.Dir(a.heading), TrackMath.Dir(a.heading + 90f),
                           TrackMath.Dir(b.heading), TrackMath.Dir(b.heading + 90f) };
        Vector2 d = b.pos - a.pos;
        normal = Vector2.zero; depth = float.MaxValue;
        foreach (var ax in axes)
        {
            float overlap = Radius(a, ax) + Radius(b, ax) - Mathf.Abs(Vector2.Dot(d, ax));
            if (overlap <= 0f) return false;                 // achou um eixo que separa
            if (overlap < depth) { depth = overlap; normal = Vector2.Dot(d, ax) < 0 ? -ax : ax; }
        }
        return true;
    }

    // Meia "sombra" do retângulo projetado no eixo.
    static float Radius(Pose2 p, Vector2 ax) =>
        HalfLength * Mathf.Abs(Vector2.Dot(TrackMath.Dir(p.heading), ax)) +
        HalfWidth  * Mathf.Abs(Vector2.Dot(TrackMath.Dir(p.heading + 90f), ax));
}
```

> Também dá para usar dois `BoxCollider` com `isTrigger` e `OnTriggerStay`. Mas os carrinhos se movem por código, sem física, e o SAT acima devolve a normal e a profundidade de que a resposta precisa.

#### Resposta

**Os dois no trilho (raspão).** O trilho prende os carrinhos de lado, então ninguém é empurrado para fora da fenda. A batida vira **derrapagem**:

| Momento | Efeito nos dois carrinhos |
|---|---|
| Primeiro contato | `slip += 0,15 + 0,03 × maior velocidade` |
| Enquanto encostam | `slip += profundidade × 8 × dt` e `v *= e^(−1,2 × dt)` (atrito) |

Quem estava derrapando já vinha com a barra alta e costuma sair. Quem foi atingido leva um susto: na simulação, o carrinho de fora chegou a 85–92% da barra sem sair. Se o jogador dele estiver no limite também, sai junto.

**Pelo menos um solto (batida de verdade).** É um choque entre massas iguais, com impulso na direção da normal:

```
vRel = (velB − velA) · normal          // < 0: estão se aproximando
J    = −(1 + 0,35) × vRel / 2          // 0,35 = quanto quica
velA −= J × normal ;  velB += J × normal
```

- **Carrinho solto:** recebe o impulso inteiro na velocidade e ganha giro (`± |J| × 60` graus por segundo). O sentido do giro vem do lado em que foi atingido: o produto vetorial entre "centro → ponto de contato" e o impulso.
- **Carrinho no trilho:** separe o impulso em duas partes. A parte **na direção da pista** muda a velocidade: atingido por trás, ele ganha velocidade; de frente, perde. A parte **de lado** vira derrapagem: `slip += 0,12 + 0,14 × |lateral|`. Se a barra encher, ele sai da pista, e o impulso da batida soma na velocidade de voo.
- **Separação:** afaste o carrinho solto pela profundidade, ao longo da normal, para eles não ficarem grudados. Se os dois estão soltos, cada um anda metade. O do trilho não se move de lado.
- **Carrinho voltando ao trilho** (estado `Returning`) não bate: está "na mão do fiscal".

```csharp
bool touching;

// Chame depois de mover os dois carrinhos, no mesmo frame (por exemplo, no RaceManager).
public void Collide(SlotCar a, SlotCar b, float dt)
{
    if (a.CurrentState == SlotCar.State.Returning || b.CurrentState == SlotCar.State.Returning) { touching = false; return; }
    if (!CarCollision.Overlap(a.Pose, b.Pose, out var n, out float depth)) { touching = false; return; }

    bool first = !touching;
    touching = true;
    if (first) { Sparks(Vector2.Lerp(a.Pose.pos, b.Pose.pos, 0.5f)); a.Hits++; b.Hits++; }

    if (a.CurrentState == SlotCar.State.OnSlot && b.CurrentState == SlotCar.State.OnSlot)
    {
        if (first) { float kick = 0.15f + 0.03f * Mathf.Max(a.Speed, b.Speed); a.AddSlip(kick); b.AddSlip(kick); }
        a.AddSlip(depth * 8f * dt); b.AddSlip(depth * 8f * dt);
        float drag = Mathf.Exp(-1.2f * dt); a.ScaleSpeed(drag); b.ScaleSpeed(drag);
    }
    else
    {
        float vRel = Vector2.Dot(b.WorldVelocity - a.WorldVelocity, n);
        if (vRel < 0)
        {
            float j = -(1f + 0.35f) * vRel / 2f;
            Vector2 half = (b.Pose.pos - a.Pose.pos) / 2f;
            a.ApplyImpulse(-j * n, half);
            b.ApplyImpulse(j * n, -half);
        }
        float share = a.CurrentState == SlotCar.State.Off && b.CurrentState == SlotCar.State.Off ? 0.5f : 1f;
        if (a.CurrentState == SlotCar.State.Off) a.Nudge(-n * depth * share);
        if (b.CurrentState == SlotCar.State.Off) b.Nudge(n * depth * share);
    }
    a.DeslotIfFull(); b.DeslotIfFull();
}
```

O `SlotCar` da seção 15.5 precisa expor:
- `Pose`: a pose atual da carroceria, a mesma que vai para o `transform`;
- `WorldVelocity`: no trilho, `direção(ângulo da pista) × Speed`; solto, `flyVel`;
- `AddSlip` e `ScaleSpeed`;
- `Nudge(Vector2)`, que soma em `flyPos`;
- `ApplyImpulse(impulso, r)`, com as regras acima;
- `DeslotIfFull()`, que chama `Deslot()` quando `Slip ≥ 1` e soma o último impulso em `flyVel`;
- o contador `Hits`.

#### Testes na simulação

| Cenário | Resultado |
|---|---|
| Os dois pilotando dentro do limite | Nenhum toque. A colisão não atrapalha quem pilota limpo. |
| T2 (dentro) derrapando 40% ou 65% ao lado de T1 | Nenhum toque. A traseira ainda não chega. |
| T2 derrapando 80% ao lado de T1 | 2 a 3 toques. T2 sai, e T1 vai a 80–92% da barra. |
| Os dois com o acelerador no fundo | 4 batidas de carrinho solto em 5 voltas. |

#### Interface

- **Faíscas** amarelas no ponto de contato por 0,35 s.
- **"Batida!"** no painel do jogador por 0,6 s.
- Coluna **Toques** no placar.
- Opção **Colisão entre carrinhos** nos ajustes, para desligar quando quiser só treinar.

---

## 16. Pontes, rampas e curvas inclinadas

O `index.html` já tem as duas coisas:
- **Ponte:** qualquer peça pode subir ou descer um nível de altura. Há também dois atalhos na paleta, as **Rampas** (teclas `U` e `D`).
- **Curvas inclinadas:** longa (90°) e curta (45°), nas teclas `7` a `0`.

O botão **Exemplo: 8 com ponte** carrega um 8 deitado de 26 peças, feito com curvas curtas inclinadas, em que uma diagonal passa por cima da outra (seção 16.8).

Na Unity isso fica até mais simples que no HTML. A altura vira **Y de verdade**, e a câmera e o depth buffer resolvem quem aparece por cima.

### 16.1 Altura das peças

Cada peça ganha `rise`: `+1` sobe um nível, `-1` desce, `0` fica plana. A altura não é guardada por peça. Ela é calculada em sequência a partir da largada, igual à posição:

```
zEntrada(peça i) = zSaída(peça i − 1)          // a largada começa no chão (0)
zSaída(peça i)   = clamp(zEntrada + rise, 0, 2)
z(t)             = zEntrada + (zSaída − zEntrada) × t
```

Por que `rise` em vez de "altura da peça"? Porque inserir ou remover uma rampa no meio levanta ou abaixa **todo o resto** sozinho, como na pista de verdade.

No `TrackBuilder` (seção 6):

```csharp
[Serializable]
public class PieceInstance
{
    public PieceDefinition def;
    public int dir;
    public int rise;                         // -1 desce, 0 plana, +1 sobe
    public PieceInstance(PieceDefinition def, int dir, int rise = 0) { this.def = def; this.dir = dir; this.rise = rise; }
}

public struct Segment { public PieceInstance piece; public Pose2 a, b; public int zIn, zOut; }

public const int MaxLevel = 2;
public const float LevelHeight = 0.5f;       // 1 nível = meia célula de altura (vão para o carrinho passar)

// dentro de Rebuild(), no laço das peças:
int z = 0;
for (int i = 0; i < pieces.Count; i++)
{
    var p = pieces[i];
    var b = TrackMath.PoseAt(conn, p.def, p.dir, 1f);
    b.heading = TrackMath.Normalize(b.heading);
    int zOut = Mathf.Clamp(z + (i == 0 ? 0 : p.rise), 0, MaxLevel);
    segments.Add(new Segment { piece = p, a = conn, b = b, zIn = z, zOut = zOut });
    conn = b; z = zOut;
}
```

**Fechamento:** o circuito só fecha se a ponta chegar na largada **e** no chão:

```csharp
IsClosed = segments.Count > 1
    && Vector2.Distance(last.pos, origin.pos) < 0.02f
    && Mathf.Abs(Mathf.DeltaAngle(last.heading, origin.heading)) < 0.5f
    && segments[^1].zOut == 0;
```

Se fechar no plano mas não na altura, avise: "A ponta chega na largada, mas no nível 1. Desça até o chão."

### 16.2 Sobreposição com altura

Duas peças só se sobrepõem se estiverem perto no plano **e** na mesma altura. Guarde a altura de cada ponto amostrado em `FindOverlaps` (seção 6) e mude o teste:

```csharp
// pontos: (x, z da mesa) + altura em níveis
static bool Touches(List<Vector3> a, List<Vector3> b, float minSqr)
{
    foreach (var p in a) foreach (var q in b)
        if (Mathf.Abs(p.z - q.z) < 0.75f && (new Vector2(p.x, p.y) - new Vector2(q.x, q.y)).sqrMagnitude < minSqr)
            return true;
    return false;
}
```

Por que 0,75 nível de folga? O meio de uma rampa fica no nível 0,5 e não pode passar sobre nada nem sob nada. Só a parte plana da ponte (nível 1) passa sobre o chão (nível 0).

### 16.3 Malha com altura

No `AddStrip` (seção 5), a altura de cada vértice deixa de ser fixa:

```csharp
// y de um vértice: altura da peça naquele ponto + inclinação da curva
float VertexY(Segment s, float t, float lateral, float layerY)
{
    float y = (s.zIn + (s.zOut - s.zIn) * t) * TrackBuilder.LevelHeight + layerY;
    if (s.piece.def.banked)
    {
        // O lado de fora sobe. Na curva à direita (dir +1), o de fora é o esquerdo (lateral < 0).
        float bank = Mathf.Tan(s.piece.def.bankAngle * Mathf.Deg2Rad) * BankWeight(s, t);
        y += -s.piece.dir * lateral * bank;
    }
    return y;
}

// Transição de 20% em cada ponta, para não fazer degrau com a peça plana vizinha.
// Se a vizinha também for inclinada para o mesmo lado, mantenha 1 naquela ponta.
float BankWeight(Segment s, float t) => Mathf.Clamp01(Mathf.Min(t, 1f - t) / 0.2f);
```

Some ao `PieceDefinition` os campos `public bool banked;` e `public float bankAngle = 15f;`. Crie os assets **Curva longa inclinada** (90°) e **Curva curta inclinada** (45°), os dois com raio 2,5.

**Pilares:** a cada 1,25 célula ao longo de uma peça com `z(t) > 0,25`, ponha um cilindro fino do chão até a pista (`altura = z(t) × LevelHeight`). Ponha também uma **mureta** dos dois lados das peças no alto: uma faixa vertical da malha, de `y` até `y + 0,08`.

**Carrinho na rampa:** a rotação passa a incluir a inclinação. Guarde a altura de cada ponto no `LanePath` e use a tangente 3D:

```csharp
Vector3 here = path.At3D(Distance), ahead = path.At3D(Distance + 0.1f);
transform.SetPositionAndRotation(here + Vector3.up * 0.05f, Quaternion.LookRotation(ahead - here) * Quaternion.Euler(0, -90f, 0));
// o "-90" é porque o modelo aponta para +X; LookRotation aponta o +Z para a frente
```

**Visual da altura:** com a câmera ortográfica de cima, a altura não aparece sozinha. Ligue sombras na Directional Light, levemente inclinada (rotação `(60, 30, 0)`, por exemplo). A sombra da ponte no chão é o que mostra que ela está no alto, como a sombra deslocada do HTML.

### 16.4 Corrida com ponte e rampa

| Situação | Regra |
|---|---|
| Rampa | `a −= slopeG × inclinação`, com `slopeG = 8` e `inclinação = (zSaída − zEntrada) / comprimento`. Subir freia e descer embala. Numa rampa de reta comprida, isso dá ±2 cél/s². |
| Sair da pista na ponte | O voo começa na altura em que estava e cai até o chão (no HTML, 3 níveis por segundo; na Unity, gravidade de verdade). |
| Colisão | Só se a diferença de altura for menor que 0,6 nível. Um carrinho passa embaixo do outro sem bater. |
| Recolocar | Volta para o trilho na altura da pista naquele ponto. |

No `LanePath`, guarde por ponto a altura, a inclinação e o multiplicador de aderência (seção 16.5), como já é feito com a curvatura.

### 16.5 Curva inclinada: por que dá para acelerar mais

Numa curva plana, só o atrito segura o carrinho de lado. Numa curva inclinada, o próprio piso empurra o carrinho para dentro da curva. A conta da física é:

```
aderência inclinada / aderência plana = (tan θ + μ) / (μ × (1 − μ × tan θ))
```

Com θ = 15° de inclinação e μ = 0,9, o multiplicador fica em **1,7**. No jogo isso vira só um número por peça (`BANK_GRIP = 1,7`), usado na conta da derrapagem:

```
excesso = v² × |curvatura| / (grip × multiplicador) − 1        // multiplicador = 1 na plana, 1,7 na inclinada
```

Como a velocidade limite é a raiz da aderência, a curva inclinada aceita √1,7 ≈ **1,30×** mais velocidade:

| Curva | Trilho de dentro | Trilho de fora |
|---|---|---|
| Plana | 8,1 cél/s (58% da máxima) | 8,9 (64%) |
| Inclinada | 10,5 (75%) | 11,6 (83%) |

Na simulação da pista com ponte, os carrinhos chegaram a **13,4 cél/s** nas inclinadas e a 10,6 nas planas.

Se o jogador abusar mesmo assim, ele derrapa e sai igual. A inclinada sobe o limite, não remove o limite.

### 16.6 Testes feitos (pista "Exemplo: 8 com ponte")

| Teste | Resultado |
|---|---|
| Pista fecha, sem sobreposição | Sim. A ponte (peça 11, nível 1) passa exatamente sobre a peça 24 (chão). |
| Mesma pista com as rampas planas | Acusa sobreposição nas peças 10, 11, 12, 23, 24 e 25. |
| Só subir, sem descer | "Fecha no plano" mas não no nível: aviso de nível 1. |
| Carrinho na ponte em cima do outro | Camadas 1 e 0, nenhum toque. |
| 1/10 s com acelerador cheio a 8 cél/s | Plano 9,82 · subindo 9,64 · descendo 9,99. |
| Corrida de 5 voltas, os dois a 115% do limite | Os dois trilhos chegam juntos (27,2 s). No 8, cada carrinho faz um lóbulo por dentro e o outro por fora, então os trilhos têm o mesmo comprimento. |
| Acelerador sempre no fundo | 11 a 13 saídas em 5 voltas. |

### 16.7 Texturas novas

| Arquivo | Uso |
|---|---|
| `pista-curva-inclinada-direita.png`, `pista-curva-inclinada-esquerda.png` | Faixa inteira da curva inclinada: listras amarelas transversais, clareia rumo ao lado de fora e tem muro no lugar da zebra |
| `icone-curva-longa-inclinada-*.png`, `icone-curva-curta-inclinada-*.png` | Ícones da paleta |
| `icone-rampa-sobe.png`, `icone-rampa-desce.png` | Ícones das rampas, com setas amarelas apontando para o lado alto |
| `icone-reta-curtinha.png` | Ícone da reta de 1 célula |

No save (seção 9), cada peça ganha o campo opcional `"altura": "sobe"` ou `"desce"`, e os tipos novos são `reta-curtinha`, `curva-longa-inclinada` e `curva-curta-inclinada`. É o mesmo formato do HTML.

### 16.8 O 8 com ponte, peça por peça

O 8 deitado é o traçado clássico de autorama com ponte. Cada lóbulo vira 270° e as duas diagonais se cruzam em ângulo reto no meio. Todas as curvas são **curtas inclinadas** (45°), seis por lóbulo.

| Trecho | Peças | Rumo |
|---|---|---|
| Largada | Base de largada | leste |
| Lóbulo direito | curta dir., reta curta, curta dir., curta dir., reta curta, curta dir., reta curta, curta dir. | de 0° até 225° |
| Diagonal de cima | reta comprida **sobe**, reta curtinha (**a ponte**), reta comprida **desce** | 225° |
| Lóbulo esquerdo | curta esq., reta curta, curta esq., reta curta, curta esq., curta esq., reta curta, curta esq., reta curta, curta esq. | de 225° até −45° |
| Diagonal do chão | reta comprida, reta curtinha (passa embaixo da ponte), reta comprida | −45° |
| Fechamento | curta dir. | de −45° até 0° |

Ocupa 22,7 × 8,8 células. O meio da ponte cai exatamente sobre o meio da reta curtinha do chão.

**Por que precisa da reta curtinha.** Com curvas de raio 2,5, a distância entre os dois lóbulos ao longo de cada diagonal sai sempre **5 + um número par** de células. É uma conta de geometria, não de ajuste. As retas de 2 e 4 só somam números pares, então o 8 nunca fecha só com elas. Por isso o autorama de verdade vende retas de 1/4. Aqui, cada diagonal tem 4 + 1 + 4 = 9 células.

As retas curtas dentro dos lóbulos, duas delas na diagonal, servem para empurrar os lóbulos para longe do centro. Elas deixam 4 células de cada lado da ponte para as rampas.


## 17. Fechar o circuito sozinho (tecla Q)

Já está no projeto: `Core/TrackCloser.cs`, chamado pelo botão **Fechar circuito** do painel e pela tecla **Q**. O HTML usa o mesmo algoritmo.

- **Conta exata.** As curvas de 45° geram coordenadas com √2. Cada coordenada é guardada como `(a + b·√2) / 8`, com `a` e `b` inteiros (struct `Q`), então "chegou na largada" é uma comparação exata, sem tolerância.
- **Busca agressiva dos dois lados.** Expande até 15 peças a partir da ponta da pista e até 15 a partir da largada, andando de ré. De ré, cada curva vira para o lado oposto (`MIRROR`). As duas buscas se encontram num mesmo estado (posição + rumo), e o fechamento tem no máximo 30 peças.
- **Critério melhorado.** Para cada estado, guarda até 8 sequências. Ordena os candidatos por: (1) número de peças, (2) menos curvas completas (muito importante para velocidade), (3) comprimento total (mais elegante), (4) menos meias-curvas, (5) mais retas. Testa até 3000 contra sobreposição e recusa o que passa por cima da pista.
- **Busca progressiva (sem travamento).** Para evitar travar a Unity em pistas complexas, a busca tenta profundidades crescentes:
  - Profundidade 6: até 200 verificações (< 50ms, pistas simples)
  - Profundidade 9: até 600 verificações (pistas normais)
  - Profundidade 12: até 1500 verificações (pistas complexas)
  - Profundidade 15: até 3000 verificações (pistas muito complexas)
  
  Se uma profundidade mais baixa encontrar solução, retorna imediatamente. Isso mantém o editor responsivo para casos comuns e garante que pistas complexas também sejam resolvidas.

- **Altura e estilo.** Se a ponta está no alto, encaixa rampas que descem: primeiro na reta comprida, depois na curta, depois em qualquer reta. Se a pista usa mais curvas inclinadas que planas, o fechamento também usa inclinadas.
- **Desfazer.** O fechamento entra como uma ação só: Ctrl+Z tira tudo.

- **Cancelável, com indicador.** A busca não roda de uma vez: ela anda aos poucos e dá para parar a qualquer hora. Em pistas sem fechamento curto, a busca de profundidade 12/15 é pesada (nos testes, 3,4 GB de memória e 37 s no Node para uma ponta longe da largada), e rodar de uma vez travava a máquina.
  - **Fatias de ~8–10 ms por quadro.** O trabalho vira um iterador (`IEnumerator` na Unity, função geradora `function*` no HTML) que dá `yield` a cada 32 estados expandidos, a cada 64 estados juntados e a cada candidato testado. Quem chama dá `Pump(ms)`/`pump(ms)`; a tela continua respondendo entre as fatias.
  - **Indicador.** Enquanto roda, o botão **Fechar circuito** dá lugar a um quadro com o texto (`Procurando com até 18 peças… testando 230 de 600`), os segundos decorridos, uma barra de progresso e o botão **Cancelar (Esc)**. No HTML o quadro flutua no canto da mesa, no lugar do aviso.
  - **Barra.** Cada profundidade tem um peso (`1, 2, 4, 8`, porque o custo cresce rápido). Dentro dela: 0–20% expandir a frente, 20–40% expandir a ré, 40–50% juntar, 50–100% testar candidatos.
  - **Cancelar.** Botão, tecla **Esc**, ou qualquer edição da pista (colocar/remover peça, desfazer, refazer, arrastar a largada, trocar de aba): a busca para na hora, nada entra na pista e aparece "Busca cancelada." Quando termina sozinha, as peças entram como uma ação só.
  - **Candidatos por grupo.** Em vez de ordenar todos os candidatos de uma vez, eles ficam em grupos por número de peças (com os critérios já calculados) e só se ordena o grupo da vez. A ordem final é a mesma; quase nunca os grupos maiores são tocados.
  - **Passou por cima da pista.** Se um fechamento curto só fecha passando por cima da pista, a busca continua em profundidades maiores (HTML e Unity iguais) e só avisa "Todo fechamento curto passa por cima da pista" se nenhuma achar.

**Implementação (C#):**
- `TrackCloser.Start(origin, frente, tras, preferBanked)`: devolve um `TrackCloser.Job` sem rodar nada. O `Job` tem `Pump(ms)`, `Cancel()`, `Done`, `Cancelled`, `Result`, `Progress` (0..1), `Status` e `Elapsed`.
- `TrackCloser.Close(...)`: a versão de uma vez só (`Start` + `Pump(long.MaxValue)`), para testes e scripts.
- `CloseAtDepth()`: iterador da busca bidirecional com profundidade e limite de verificações de uma etapa.
- `ExpandAt()`: iterador que expande até `maxDepth` peças a partir de um estado e enche o mapa (estado → lista de sequências).
- `TrackBuilder`: `BeginAutoClose()` (devolve a mensagem se não há o que buscar), `PumpAutoClose(ms)` (a cada quadro; quando acaba, entra na pista com `Commit` e avisa), `CancelAutoClose()`, `CloseJob`/`Closing`, e `AutoClose()` de uma vez só. `Commit`, `Undo`, `Redo`, `BeginEdit` e `ShowTrack` cancelam a busca.
- `BuilderPanel`: `PumpClose()` no `Update` (8 ms por quadro), `closeBox` com `meter`/`meter-fill` (classes `.close-box` no USS) e botão Cancelar; Esc cancela; trocar para Corrida/Campeonato cancela.

**Implementação (HTML):** `Closer.start(origin, frente, banked, tras)` devolve o job (`pump`, `cancel`, `done`, `result`, `progress`, `status`, `elapsed`); `Closer.close(...)` roda de uma vez. `autoClose()` bombeia o job com `setTimeout` em fatias de 10 ms e mostra `#closing`; `cancelClose()` é chamado por `commit`, `undo`, `redo`, `setMode`, pelo botão e por Esc.

**Constantes progressivas** (em `TrackCloser.cs`):
```csharp
static readonly int[] ProgressiveDepths = { 6, 9, 12, 15 };
static readonly int[] ProgressiveChecks = { 200, 600, 1500, 3000 };
static readonly float[] StageWeights = { 1f, 2f, 4f, 8f };   // peso de cada etapa na barra
```

**Testes feitos:**
- HTML (`ferramentas/teste-html.js`): mesmas respostas de antes nas 4 pistas de teste (5, 12, 9 e 17 peças). Com `performance.now` real, `pump(10)` nunca passou de ~50 ms por fatia na pista de 17 peças (31 s no total); `cancel()` para na hora. `autoClose` → `cancelClose` deixa a pista intacta; concluir fecha (5 → 10 peças) e Ctrl+Z volta; editar durante a busca cancela.
- Unity (`script eval`): a pista de 12 peças dá a mesma resposta do HTML (curva longa · direita, meia curva · direita, reta comprida, reta curtinha, …). Na pista pesada, `Pump(10)` + `Cancel()` funcionam, mas o coletor de lixo do Mono faz algumas fatias passarem de 500 ms quando a busca fica grande: é a hora de cancelar.

## 18. Cruzamento (X)

Duas peças, já no catálogo (`PieceCatalog.Cross` e `CrossS`):

| Peça | id no JSON | Tecla | Comprimento | S de cada trilho |
|---|---|---|---|---|
| Cruzamento | `cruzamento` | X | 4 células | 4,038 |
| Cruzamento curto | `cruzamento-curto` | B | 2 células | 2,075 |

- **Caminho do trilho.** Na peça, o trilho sai de um lado e chega no outro: deslocamento `offset · cos(π·t)` (`PieceDef.LaneOffsetAt`). O comprimento do S é igual para os dois trilhos (`CrossLaneLength`).
- **Troca de lado.** Depois de cada cruzamento, o sinal do deslocamento inverte. O `LanePath` do `RaceSim` faz isso ao montar o caminho.
- **Número ímpar de cruzamentos.** O carrinho só volta ao trilho de partida depois de 2 voltas. O caminho então cobre 2 voltas e tem 2 bandeiradas (`marks`). A volta é contada por `LineCount(d) − 1`. No resumo, os dois trilhos aparecem com a média: o mesmo comprimento.
- **Textura.** `pista-cruzamento.png` (256×1024) e `pista-cruzamento-curto.png` (256×512) cobrem a peça inteira (`vScale = 1 / Length`), com as fendas em X e a faixa amarela no meio. Materiais `theme.cross` e `theme.crossShort`.

## 19. Campeonato

Terceira aba, ao lado de Construtor e Corrida. No HTML ela já funciona; a seguir, como fazer igual na Unity.

### 19.1 Como funciona

- **Campeonato** = nome + pilotos (2 a 8) + etapas. **Etapa** = uma pista com um número de voltas.
- **Bateria** = uma largada com 2 carros. Cada etapa tem **N baterias** (N = número de pilotos), em **rodízio em ciclo**: na bateria k correm o piloto k no trilho 1 e o piloto k+1 no trilho 2.

| Pilotos | Baterias da etapa |
|---|---|
| 2 | A×B, B×A (a troca de lado) |
| 3 | A×B, B×C, C×A |
| 4 | A×B, B×C, C×D, D×A |

  Cada piloto corre **exatamente uma vez em cada trilho**, nunca fica sem par e não precisa de todos contra todos.
- **Resultado da etapa:** mais voltas primeiro, depois o **tempo somado** das duas baterias. É o que permite comparar pilotos que não correram juntos.
- **Fim da bateria:** quando os dois chegam, ou **30 s** depois da bandeirada do primeiro. Quem não terminou fica com as voltas que fez, e o tempo dele é o tempo da prova nesse momento.
- **Pontos:** 10 · 8 · 6 · 5 · 4 · 3 · 2 · 1 por posição, **+1 para a melhor volta da etapa**. Desempate da tabela geral: vitórias, pódios, tempo total.
- **Regras fixas:** ao começar, o campeonato copia a velocidade, a rampa do acelerador, a aderência e a colisão dos ajustes da corrida. Assim, todas as baterias correm com as mesmas regras.
- **Cópia das pistas:** o campeonato guarda uma cópia de cada pista. Editar a pista na biblioteca no meio do caminho não muda o campeonato.
- **Salvar a bateria:** depois da bandeirada, a bateria só conta quando alguém aperta **Salvar e seguir** (Enter). **Correr de novo** descarta a bateria e larga outra vez. O campeonato fica salvo depois de cada bateria, e dá para fechar e continuar outro dia.

### 19.2 Dados

`Scripts/Champ/ChampData.cs`. Os nomes dos campos são os mesmos do HTML (`localStorage`: `autorama-pilotos-v1`, `autorama-campeonatos-v1`, `autorama-recordes-v1`).

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    [Serializable] public class Pilot
    {
        public string id, name, tag; public string color;    // color: "#RRGGBB"
        public bool ai; public int skill = 50;               // piloto IA e qualidade 1–100 (seção 19.10)
    }

    [Serializable] public class Stage { public string name; public int laps = 5; public string hash; public TrackSave data; }

    [Serializable] public class HeatResult
    {
        public int laps; public float time; public bool dnf;
        public float best = -1;                  // -1: nenhuma volta completa
        public int offs, hits;
        public bool rec, raceRec, pb;            // bateu recorde de volta, de corrida, ou o melhor pessoal
    }

    [Serializable] public class Heat
    {
        public int s;                            // etapa
        public int[] p = new int[2];             // índice dos pilotos: [trilho 1, trilho 2]
        public HeatResult[] res;                 // null até salvar
        public bool Saved => res != null && res.Length == 2;
    }

    [Serializable] public class ChampRules { public float vmax, ramp, grip; public bool collide; }

    [Serializable] public class Championship
    {
        public string id, name; public long created, finished;
        public ChampRules rules;
        public List<Pilot> pilots = new List<Pilot>();
        public List<Stage> stages = new List<Stage>();
        public List<Heat> heats = new List<Heat>();
    }

    public static class ChampRulesTable
    {
        public static readonly int[] Points = { 10, 8, 6, 5, 4, 3, 2, 1 };
        public const float Cutoff = 30f;
        public const int MaxPilots = 8;
        public static readonly string[] Colors = { "#E0362C", "#2563EB", "#F2B705", "#16A34A", "#9333EA", "#F97316", "#0EA5B7", "#E04B95" };
    }
}
```

Os 8 nomes das cores, na ordem: Vermelho, Azul, Amarelo, Verde, Roxo, Laranja, Ciano, Rosa. Dois pilotos do mesmo campeonato não podem ter a mesma cor.

A **sigla** são as 3 primeiras letras do nome, sem acento e em maiúsculas (André → AND). O jogador pode trocar.

### 19.3 Rodízio

```csharp
public static List<Heat> MakeHeats(int nPilots, int nStages)
{
    var list = new List<Heat>();
    for (int s = 0; s < nStages; s++)
        for (int k = 0; k < nPilots; k++)
            list.Add(new Heat { s = s, p = new[] { k, (k + 1) % nPilots } });
    return list;
}

// A bateria da vez: a primeira sem resultado. -1 = campeonato encerrado.
public static int CurrentHeat(Championship c) => c.heats.FindIndex(h => !h.Saved);
```

**Teclas com um humano só** (Unity, `RaceController.KeySet`): o rodízio põe o mesmo piloto ora no trilho 1, ora no 2. Quando só um dos carrinhos da bateria é humano (o outro é IA), ele usa sempre **A · S**, em qualquer trilho; o controle na tela, o texto da bateria ("acelera A · recoloca S") e a leitura do teclado seguem essa regra. Com dois humanos continua A · S no trilho 1 e L · K no 2. Na corrida livre contra o robô nada muda (o humano já é o trilho 1); no online já era sempre A · S.

```csharp
int KeySet(int k)
{
    bool h0 = sim.aiSkill[0] <= 0, h1 = sim.aiSkill[1] <= 0;
    return h0 != h1 ? 0 : k;          // 0 = A·S, 1 = L·K
}
```

Teste (`script eval`): IA (0, 0) → trilho 1 A, trilho 2 L; IA (0, 70) → A, A; IA (70, 0) → A, A; IA (70, 70) → A, L (com dois robôs as teclas não são lidas).

### 19.4 Mudanças no RaceSim

**1. Regras e voltas.** Para cada bateria, o `RaceController` cria o `RaceSim` com um `RaceSettings` montado a partir de `champ.rules` e de `stage.laps`. Os ajustes salvos da aba Corrida não mudam.

**2. Tempo limite.** Um campo novo e uma linha no `Step`:

```csharp
public float? cutoff;          // null na corrida livre; 30 no campeonato
public float? bestT;           // melhor volta da bateria até agora

// no Step, logo depois de avançar t:
if (phase == Phase.Running && cutoff.HasValue && winner >= 0 && t - cars[winner].finish.Value >= cutoff.Value)
    phase = Phase.Done;
```

**3. Aviso de volta.** Um evento `LapDone(int car, float lap)`, disparado antes de atualizar `best` e `bestT`. É ele que mostra o recorde na hora (19.6).

### 19.5 Resultado da etapa e tabela geral

```csharp
public class StageRow { public int i, laps, runs, offs, hits, pts = -1; public float time, best = -1; public bool fast; }

public static (List<StageRow> rows, bool complete) StageResults(Championship c, int s)
{
    var rows = new List<StageRow>();
    for (int i = 0; i < c.pilots.Count; i++) rows.Add(new StageRow { i = i });
    bool complete = true;
    foreach (var h in c.heats)
    {
        if (h.s != s) continue;
        if (!h.Saved) { complete = false; continue; }
        for (int k = 0; k < 2; k++)
        {
            var r = h.res[k]; var row = rows[h.p[k]];
            row.laps += r.laps; row.time += r.time; row.runs++; row.offs += r.offs; row.hits += r.hits;
            if (r.best >= 0 && (row.best < 0 || r.best < row.best)) row.best = r.best;
        }
    }
    rows.RemoveAll(r => r.runs == 0);
    rows.Sort((a, b) => a.laps != b.laps ? b.laps.CompareTo(a.laps) : a.time.CompareTo(b.time));
    if (complete)
    {
        for (int pos = 0; pos < rows.Count; pos++) rows[pos].pts = pos < ChampRulesTable.Points.Length ? ChampRulesTable.Points[pos] : 0;
        StageRow fastest = null;
        foreach (var r in rows) if (r.best >= 0 && (fastest == null || r.best < fastest.best)) fastest = r;
        if (fastest != null) { fastest.fast = true; fastest.pts++; }
    }
    return (rows, complete);
}
```

A tabela geral soma só as etapas completas: pontos, vitórias (1º na etapa), pódios (até 3º), melhores voltas e tempo. A ordem é pontos, depois vitórias, depois pódios, depois menor tempo.

A setinha ▲▼ compara a posição atual com a tabela calculada sem a última etapa completa.

### 19.6 Recordes

**Identidade da pista.** O recorde fica preso à **forma da pista**, não ao nome nem à posição na mesa:
- a mesma pista salva com outro nome mantém o recorde;
- mudar uma peça faz dela outra pista.

A identidade é o FNV-1a de 32 bits do texto `largada:0:0|reta-comprida:0:0|meia-curva:-1:0|…`, com a peça escrita como `id:sentido:altura`:
- o id é o mesmo do JSON;
- o sentido é −1 para a esquerda, 1 para a direita e 0 nas retas;
- a altura é 1 para sobe, −1 para desce e 0 para plana.

O HTML calcula exatamente o mesmo número.

```csharp
public static string TrackHash(TrackSave t)
{
    var sb = new System.Text.StringBuilder("largada:0:0");
    foreach (var p in t.pecas)
    {
        int dir = p.sentido == "esquerda" ? -1 : p.sentido == "direita" ? 1 : 0;
        int rise = p.altura == "sobe" ? 1 : p.altura == "desce" ? -1 : 0;
        sb.Append('|').Append(p.tipo).Append(':').Append(dir).Append(':').Append(rise);
    }
    uint h = 0x811c9dc5;
    foreach (char ch in sb.ToString()) { h ^= ch; h *= 0x01000193; }
    return h.ToString("x8");
}
```

**O que fica guardado por pista:**
- **Volta:** o recorde absoluto, com tempo, piloto (cópia com nome, sigla e cor), data e campeonato.
- **Por trilho:** um recorde para cada trilho. Fica de fora quando a pista tem número **ímpar** de cruzamentos, porque aí cada carrinho passa pelos dois trilhos.
- **Corrida:** um recorde por número de voltas. 5 voltas só se compara com 5 voltas.
- **Pessoal:** a melhor volta e a melhor corrida de cada piloto.

**Quando gravar.** Só ao **Salvar e seguir** a bateria. Antes disso, cada volta completa só compara (evento `LapDone`):
- se a volta for menor que o recorde salvo e que a melhor volta da bateria, o controle mostra "**Recorde da pista!**" em amarelo por 2,5 s;
- se for só menor que o melhor pessoal (que já existia), mostra "**Melhor volta pessoal!**" em verde.

**Corrida livre.** Não grava recorde, mas mostra o recorde da pista como meta: "Recorde da pista: 4,03 s CAI".

### 19.7 Telas (UI Toolkit)

**Montar o campeonato:**
- Nome do campeonato.
- **Pilotos:** cadastro com marcar e desmarcar. A ordem em que marca é a ordem do rodízio. Cada piloto tem Editar e Apagar, e um piloto com cor repetida mostra o aviso "cor repetida".
- **Formulário de piloto:** nome, sigla e as 8 bolinhas de cor. As cores já usadas aparecem apagadas.
- **Etapas:** miniatura, voltas com − e +, botões para subir, descer e tirar.
- **Pistas disponíveis:** da construção, os exemplos e a biblioteca, cada uma com o recorde.
- **Regras e plano:** "3 pilotos · 2 etapas · 6 baterias · cerca de 2 min". A estimativa é, por bateria, voltas × comprimento ÷ (60% da velocidade máxima) + 10 s.
- **Começar:** fica desligado com o motivo à vista (menos de 2 pilotos, cor repetida, nenhuma etapa).
- **Campeonatos anteriores:** cada um abre a tabela final e tem "Apagar do histórico".

**Durante o campeonato:**
- **Ficha da bateria:** a etapa e as voltas, os dois pilotos com a cor deles, "Trilho 1 · fora" e "Trilho 2 · dentro" (o trilho mais curto é o de dentro; se forem iguais, não diz) e as teclas de cada um.
- **Botões:** Largar (Enter); Recomeçar bateria durante a prova; depois da bandeirada, Salvar e seguir (Enter) e Correr de novo.
- **Placar:** voltas, última, melhor, saídas e toques. O carrinho e a borda do controle ganham a **cor do piloto** (no HTML, a variável CSS `--car` e `--lane`).
- **Resultado da etapa:** a etapa da última bateria salva. Enquanto não termina, tem a coluna Bat. (1/2, 2/2) e os pontos ficam "—".
- **Tabela geral** com a setinha ▲▼.
- **Recordes** da pista em uso e o melhor de cada piloto nela.
- **Lista das etapas:** ✓ e o vencedor, ou quantas baterias faltam.
- **Faixa no meio da mesa:** quem venceu a bateria, com "RECORDE" e de quem, se houve. Na primeira bateria de uma etapa nova, quem venceu a anterior.

**No fim:**
- Pódio (2º, 1º, 3º).
- Prêmios:
  - mão mais rápida (melhor volta do campeonato);
  - rei do muro (mais saídas de pista);
  - para-choque (mais toques);
  - número de recordes batidos.
- Botão **Arquivar e montar outro**, que guarda a tabela final no histórico.

### 19.8 Onde salvar

Pasta `Application.persistentDataPath/autorama/`:
- `pilotos.json`
- `campeonatos.json`, com o rascunho, o campeonato ativo e o histórico
- `recordes.json`

O `JsonUtility` não lê dicionário. Os recordes no HTML são um mapa `{ hash: { lap, lanes, race: { voltas: … }, pb: { piloto: … } } }`. Para ter **o mesmo JSON** nos dois, use o pacote `com.unity.nuget.newtonsoft-json`. Se preferir o `JsonUtility`, troque os mapas por listas (`List<TrackRecords>` com o campo `hash`).

### 19.9 Testes feitos (HTML, simulação com pilotos-robô)

3 pilotos e 2 etapas: Oval com 3 voltas e 8 com ponte com 2 voltas. Os robôs aceleram até um limite diferente cada um: 105%, 112% e 122% do que a curva aguenta.

| Bateria | Pista | Trilho 1 | Trilho 2 |
|---|---|---|---|
| 1 | Oval | AND (fora) 13,72 s | BIA (dentro) 13,88 s |
| 2 | Oval | BIA (fora) 14,73 s | CAI (dentro) 12,32 s |
| 3 | Oval | CAI (fora) 13,02 s | AND (dentro) 12,92 s |
| 4 | 8 com ponte | AND 11,55 s | BIA 11,87 s |
| 5 | 8 com ponte | BIA 12,08 s | CAI 13,17 s, 2 saídas |
| 6 | 8 com ponte | CAI 11,68 s | AND 11,50 s |

- **Etapa 1 (Oval):** CAI 25,33 s, 11 pts com a melhor volta; AND 8; BIA 6.
- **Etapa 2 (8 com ponte):** AND 10; BIA 8; CAI 7 com a melhor volta.
- **Tabela final:** AND 18 e CAI 18, com 1 vitória e 2 pódios cada. O desempate foi pelo tempo total: AND 49,68 s contra CAI 50,18 s. BIA ficou com 14.
- **Recordes do Oval:** volta 4,03 s (CAI), 3 voltas 12,32 s (CAI).
- **Recordes do 8 com ponte:** volta 5,20 s (CAI), 2 voltas 11,50 s (AND).
- **Identidade da pista:** a mesma pista com outro nome e em outro lugar da mesa tem a mesma identidade; trocar uma peça muda.
- **Rodízio com 2, 4 e 5 pilotos:** cada piloto uma vez em cada trilho.
- **Salvar e recarregar:** o campeonato voltou com as 6 baterias.

### 19.10 Pilotos IA

No formulário de piloto há uma chave **Piloto IA** e, com ela ligada, uma régua de **qualidade de 1 a 100**. Piloto IA sem nome vira "Robô 1", "Robô 2"…

O texto embaixo da régua muda com a qualidade:

| Qualidade | Texto |
|---|---|
| até 20 | Iniciante: freia cedo e erra bastante |
| até 45 | Amador: anda seguro, às vezes passa do ponto |
| até 70 | Bom: entra forte nas curvas |
| até 90 | Experiente: quase no limite, raramente sai |
| acima | Profissional: no limite da aderência |

**Como a IA dirige.** Ela usa o mesmo botão que a gente, só segura e solta, sem trapaça. Olha 3,5 células à frente e acelera enquanto a velocidade for menor que a que ainda dá para frear antes da próxima curva:

```csharp
// sk = qualidade / 100
float Base(float sk)   => 0.82f + 0.33f * sk;                     // coragem: fração do limite da curva
float Spread(float sk) => 0.02f + 0.10f * (1 - sk);               // variação a cada sorteio
float Miss(float sk)   => 0.005f + 0.12f * Mathf.Pow(1 - sk, 1.5f); // chance de errar por sorteio
float ReslotDelay(float sk) => 1.4f - 0.9f * sk;                   // segundos até apertar Recolocar

void AiDrive(CarState c, int k, float dt, float sk)
{
    if (phase == Phase.Countdown) { c.held = true; return; }        // segura na luz vermelha, como gente
    if ((c.aiT -= dt) <= 0)                                         // sorteia de novo a cada 1,2 s
    {
        c.aiM = Random.value < Miss(sk) ? 1.3f + 0.1f * Random.value              // erro: entra forte demais
                                        : Base(sk) + (Random.value * 2 - 1) * Spread(sk);
        c.aiT = 1.2f;
    }
    if (c.mode == CarMode.Off) { c.held = false; if ((c.offT += dt) >= ReslotDelay(sk)) { c.offT = 0; Reslot(k); } return; }
    if (c.mode != CarMode.OnSlot || c.finish != null) { c.held = false; return; }
    float need = float.PositiveInfinity;
    for (float d = 0; d <= 3.5f; d += 0.1f)
    {
        var q = paths[k].At(Wrap(c.s + d, paths[k].Total));
        if (q.k == 0) continue;
        float v = Mathf.Sqrt(set.grip * q.grip / Mathf.Abs(q.k)) * c.aiM;
        need = Mathf.Min(need, Mathf.Sqrt(v * v + 2 * 20f * d));       // 20 cél/s²: quanto ele conta frear
    }
    c.held = c.v < need;
}
```

Chame `AiDrive` no começo do `Step`, antes de atualizar o acelerador, para cada carrinho com IA. `CarState` ganha `aiM`, `aiT` e `offT`, e o `RaceSim` ganha `ai[2]`, nulo para gente. O `RaceController` preenche `ai` com os pilotos da bateria.

**O que muda em volta:**
- **Controle do carrinho IA:** o botão vira só um mostrador ("IA pilotando · 85", borda tracejada) e ignora toque e tecla. O Recolocar fica desligado, porque a IA se recoloca sozinha.
- **Recordes:** a IA **não bate recorde da pista**, nem de volta, nem por trilho, nem de corrida. Recorde é coisa de gente. Ela guarda só o melhor pessoal dela. O aviso "Recorde da pista!" e a faixa "RECORDE" consideram só as voltas de gente.
- **Selo "IA 85":** aparece no cadastro e na tabela geral. A ficha da bateria mostra "IA · qualidade 85" no lugar das teclas.
- **Campeonato:** pode misturar gente e IA como quiser, ou ser só de IA, só para assistir.

**Calibração (HTML, 30 corridas de 5 voltas por qualidade, os dois carros iguais, sem colisão):**

| Qualidade | Oval: tempo médio | Oval: saídas por corrida | 8 com ponte: tempo médio | 8 com ponte: saídas por corrida |
|---|---|---|---|---|
| 1 | 28,09 s | 0,57 | 35,98 s | 0,80 |
| 25 | 26,66 s | 0,43 | 34,05 s | 0,73 |
| 50 | 24,72 s | 0,10 | 31,86 s | 0,27 |
| 75 | 23,39 s | 0,07 | 29,39 s | 0,10 |
| 90 | 22,28 s | 0,07 | 28,45 s | 0,07 |
| 100 | 21,60 s | 0,07 | 27,29 s | 0,03 |

A qualidade 100 anda perto do limite (o oval de 5 voltas a 115% dava 21,5 s). A qualidade 1 leva 30% a mais e sai da pista quase uma vez a cada corrida.

**Campeonato misto testado:** André (gente), Robô 1 (IA 100) e Robô 2 (IA 30) no Oval com 3 voltas. O recorde de volta ficou com o André (5,23 s), embora o Robô 1 tenha feito 4,10 s. A volta do Robô 1 ficou guardada como melhor pessoal dele.

### 19.11 Já implementado no projeto Unity

O campeonato e os pilotos IA já estão no projeto Unity deste repositório:

| Arquivo | O que faz |
|---|---|
| `Scripts/Champ/ChampData.cs` | Classes salvas (`Pilot`, `Stage`, `Heat`, `HeatResult`, `Championship`, `ChampStore`, `TrackRecords`). As contas em `Champ`: rodízio, resultado da etapa, tabela geral, identidade da pista, sigla, texto da qualidade e estimativa de duração. A leitura e gravação dos arquivos em `ChampFiles`. |
| `Scripts/Race/RaceSim.cs` | Ganhou `set` trocável, `cutoff`, `bestT`, `aiSkill[2]`, o evento `LapDone`, o `AiDrive` e o `HumanBest()`. |
| `Scripts/Race/RaceController.cs` | Virou `partial`. Ganhou as três abas (`OnMode(Mode)`), a IA no lugar do teclado, o aviso de recorde no controle e a faixa do campeonato. |
| `Scripts/Race/RaceController.Champ.cs` | A aba Campeonato inteira: montar, bateria, tabelas, recordes, pódio, histórico. Também pinta o carrinho na cor do piloto, com o mesmo desenho do `gen-texturas.js` gerado em tempo de execução. |
| `Scripts/Builder/BuilderPanel.cs` | Ganhou o enum `Mode { Build, Race, Champ }`, a terceira aba e o `ChampBox`. |
| `UI/Construtor.uss` | Estilos do campeonato. |

**Arquivos salvos** (`persistentDataPath/autorama/`): `pilotos.json`, `campeonatos.json` e `recordes.json`. Os três usam o `JsonUtility`, com listas no lugar dos mapas do HTML. O `JsonUtility` grava `null` como objeto vazio, por isso:
- `ChampStore.hasActive` diz se há campeonato ativo;
- um recorde com `t < 0` quer dizer "nenhum".

**Testes feitos na Unity:**
- **Identidade da pista:** igual à do HTML. Oval `a34f4cb9`, 8 com ponte `81546106`.
- **Campeonato só de IA (qualidades 30, 70 e 100), no Oval com 3 voltas e no 8 com ponte com 2 voltas:** a IA 100 venceu as duas etapas e ficou com 22 pontos; a IA 70 com 16; a IA 30 com 12. Ninguém saiu da pista.
- **JSON:** o campeonato salvo e relido voltou com as 6 baterias. Um campeonato sem bateria salva continua sem bateria salva depois da leitura.
- **Modo Play:** montar pelo formulário (gente + 2 IA), começar e largar funcionaram, com ficha da bateria, controle "IA PILOTANDO · 85" e carrinhos nas cores dos pilotos.

### 19.12 Corrida livre contra o robô

Na aba Corrida, logo abaixo de Largar, há uma chave **Jogador 2 é um robô**. Ligada, ela mostra a régua **Qualidade do robô** (1 a 100), com o mesmo texto do formulário de piloto. O robô é a mesma IA do campeonato (seção 19.10), sempre no trilho 2.

- **Salvo junto com os ajustes da corrida:** `RaceSettings` ganha `bot` (bool) e `botSkill` (int, padrão 60).
- **Vale na hora,** até no meio da prova. Ao entrar na aba Corrida, ou ao mexer na chave ou na régua:

```csharp
sim.aiSkill[0] = 0;
sim.aiSkill[1] = set.bot ? set.botSkill : 0;
if (!set.bot) sim.cars[1].held = false;     // desligou: solta o acelerador que a IA segurava
DrawPadHeads();
```

- **Controle do jogador 2:** vira "ROBÔ · TRILHO 2", com o botão de mostrador ("IA PILOTANDO · 80") e o Recolocar desligado.
- **Na Unity:** um `Toggle` e um `SliderInt` na seção CORRIDA do `RaceController.BuildUI`. O `DrawPadHeads` já trata o nome e o mostrador quando `aiSkill[k] > 0`; no nome, sem piloto do campeonato, use "ROBÔ · TRILHO 2".

**Teste (HTML):** Oval com 5 voltas, robô 80 contra o jogador 1 parado. O robô fez as 5 voltas em 22,65 s e o jogador 1 ficou no grid. Ao desligar a chave, o robô soltou o acelerador. Ao sair para o campeonato e voltar, a corrida livre continuou sem robô.

**Já está na Unity:**
- `RaceSettings.bot` e `RaceSettings.botSkill`;
- o `Toggle` e o `SliderInt` na seção CORRIDA;
- `RaceController.SetBot()`, chamado ao entrar na aba e a cada mudança. Ele só vale na aba Corrida.

No teste em modo Play (8 com ponte, robô 60), o robô fez 3 voltas sozinho, com melhor volta de 5,60 s, e o jogador 1 ficou no grid.

## 20. Celular em pé

No HTML, a mesma página vira a versão de celular quando a tela tem menos de 640 px de largura. Na Unity, trave a orientação em **Portrait** (Player Settings › Resolution and Presentation › Default Orientation) e use um segundo layout de UI para telas estreitas.

### 20.1 Construtor

| Área | O que tem |
|---|---|
| Topo | Abas (Construtor, Corrida, Campeonato), botão **Menu** e, embaixo, o aviso de circuito fechado/aberto |
| Meio | A mesa. Um dedo arrasta a vista e dois dedos dão zoom (pinça) |
| Embaixo | Desfazer / Refazer / Fechar circuito; a barra da peça tocada (Inverter, Remover, Sobe/Plana/Desce, Soltar); a faixa de peças com rolagem lateral |
| Menu | Gaveta que sobe de baixo com o resto do painel: resumo, minhas pistas, exportar, trocar o tipo da peça |

Na Unity: um `ScrollView` horizontal com os botões de peça e um `VisualElement` de gaveta animando `translate` de 100% para 0. Abrir uma pista ou largar fecha a gaveta.

### 20.2 Corrida

- O controle do jogador 2 fica em cima, **girado 180°**, e o do jogador 1 fica embaixo. Assim o celular fica deitado na mesa, com um jogador de cada lado. O botão **⇅ Frente** desvira o controle de cima (salvo no aparelho).
- Os dois aceleradores aceitam toques ao mesmo tempo. Na Unity, use `Input.touches`, ou um `PointerDown`/`PointerUp` por botão com o `pointerId`. O acelerador vale enquanto aquele dedo estiver no botão.
- Entre a mesa e o jogador 1 fica uma barra com **Largar** e o tempo da prova. No meio da prova, recomeçar pede dois toques, porque o botão fica perto do acelerador.
- **Pista girada:** se a área da mesa é mais alta que larga (mais de 15%) e a pista é mais larga que alta (mais de 15%), a câmera gira 90° e a régua some. Na Unity basta `camera.transform.rotation = Quaternion.Euler(90, 0, 90)` na câmera de cima e recalcular o enquadramento com largura e altura trocadas. Ao mudar o tamanho da tela durante a corrida, enquadre de novo.
- Teclas e atalhos ficam escondidos no celular.

## 21. A mesa cresce com a pista

Quando a pista passa da borda da mesa quadriculada, a mesa ganha células, com **1 de folga** além da pista e um máximo de 120 × 120. Ela nunca encolhe sozinha; para diminuir, use os campos de tamanho da mesa.

- **Passou da direita, ou do topo na Unity:** a mesa só aumenta naquele lado.
- **Passou da esquerda, ou de baixo na Unity (de cima no HTML):** a mesa começa na célula 0, então a pista inteira é **empurrada para dentro** por células inteiras. Assim a largada continua encaixada na grade. A vista anda junto (`TopDownCamera.Nudge`; no HTML, `view.x/y`), e nada pula na tela.
- **Quando:**
  - a cada edição (`Commit`);
  - ao abrir uma pista (`ShowTrack`, rascunho, biblioteca, exemplos, corrida, campeonato);
  - ao **soltar** a largada arrastada (`EndEdit`). Durante o arrasto a mesa espera, senão a pista fugiria do mouse.
- **Desfazer:** a mesa entra no Ctrl+Z junto com a pista (o instantâneo do desfazer já guarda `mesa`). Desfazer a peça que fez a mesa crescer devolve a mesa ao tamanho anterior.

```csharp
// TrackBuilder
const int BoardMargin = 1, BoardMax = 120;
bool GrowBoard()
{
    var b = TrackLayout.Analyze(Origin, TrackLayout.Build(Origin, pieces)).bounds;   // já inclui meia largura da pista
    int dx = b.xMin < -0.01f ? Mathf.CeilToInt(-b.xMin) + BoardMargin : 0;
    int dz = b.yMin < -0.01f ? Mathf.CeilToInt(-b.yMin) + BoardMargin : 0;
    int w = boardSize.x + dx, h = boardSize.y + dz;
    if (b.xMax + dx > w + 0.01f) w = Mathf.CeilToInt(b.xMax + dx) + BoardMargin;
    if (b.yMax + dz > h + 0.01f) h = Mathf.CeilToInt(b.yMax + dz) + BoardMargin;
    w = Mathf.Min(w, BoardMax); h = Mathf.Min(h, BoardMax);
    if (dx == 0 && dz == 0 && w == boardSize.x && h == boardSize.y) return false;
    if (dx != 0 || dz != 0) { Origin = new Pose2(Origin.pos + new Vector2(dx, dz), Origin.heading); Shifted?.Invoke(new Vector2(dx, dz)); }
    boardSize = new Vector2Int(w, h);
    return true;
}
```

**Testes:**
- **HTML, oval:** 8 retas compridas para a direita levaram a mesa de 32 × 20 para 44 × 20. Virando para cima com 5 retas, a pista desceu 17 células e a mesa foi para 46 × 37. Desfazendo, a mesa voltou para 44 × 20.
- **Unity:** 8 retas levaram a mesa de 32 para 44. Virando e passando da borda, ela foi para 46 × 37. Saindo pela borda de baixo, a pista foi empurrada para cima e o aviso `Shifted` saiu a cada empurrão (é ele que move a câmera).

## 22. As duas pontas: encaixar na frente ou atrás da largada

A pista tem duas pontas: a **da frente** (o fim da pista, onde fica a seta amarela) e a **de trás** (a entrada da peça mais afastada atrás da largada, ou da própria largada, apontando para fora). O seletor **Frente ▸ / ◂ Trás** do painel de peças (HTML: no cabeçalho; tecla **E** nos dois) diz em qual delas a próxima peça encaixa. A ponta ativa tem a seta grande (no HTML ela pisca e a outra fica apagada; na Unity a outra é uma seta menor, `arrow2`). **A largada nunca sai do lugar.**

**Como fica guardado.** A largada continua sendo a peça 0 e a origem do layout. A lista de peças segue a ordem da volta: largada, peças da frente, peças de trás. As últimas `atras` (HTML `state.atras`; Unity `Tail`) peças da lista ficam **atrás** da largada: são posicionadas de ré a partir dela, e a última da lista encosta na largada. Num circuito fechado isso é a mesma volta de sempre. Por isso, ao fechar, `atras` volta a 0 (`settleBack` / `SettleTail`, a cada edição e ao abrir): a geometria é idêntica e corrida, campeonato, recordes e a identidade da pista não mudam.

```csharp
// TrackLayout.Build(origin, pieces, tail): frente a partir da origem, trás de ré a partir dela
int front = pieces.Count - tail;
/* ... frente como sempre, de 0 a front-1 ... */
var c = new Pose2(origin.pos, TrackMath.Normalize(origin.heading));
for (int i = pieces.Count - 1; i >= front; i--)
{
    var a = TrackMath.EntryFor(c, p.Def, p.dir);          // pose de entrada da peça que termina em c
    /* segmento: a = a, b = c, no chão */
    c = a;
}

// TrackMath: a peça percorrida de ré (curva espelhada)
public static Pose2 EntryFor(Pose2 c, PieceDef d, int dir)
{
    var e = PoseAt(new Pose2(c.pos, c.heading + 180f), d, -dir, 1f);
    return new Pose2(e.pos, e.heading + 180f);
}
```

(HTML: `layout(origin, pieces, nb)` e `backOf`.)

- **Fechou?** `Analyze(origin, segs, tail)` (HTML `ends`/`analyze`): a ponta da frente tem de chegar na **entrada da peça de trás mais afastada** (na largada, se não há peças atrás), no chão. As peças vizinhas na lista não contam como sobrepostas, menos a ponta da frente com a de trás enquanto abertas; a largada e a última de trás se encostam.
- **Encaixar.** Frente: como antes, no fim da frente (`FrontEnd`) ou depois da selecionada; se a selecionada está entre as de trás, a peça nova entra ali e `atras` sobe. Trás: entra no índice `FrontEnd` (antes das de trás já postas) e `atras` sobe. A peça selecionada continua a mesma.
- **Apagar.** `Del` apaga a selecionada (`atras` desce se era de trás). `⌫` sem seleção apaga a última da ponta ativa: a de trás mais afastada, se o seletor está em Trás.
- **Rampas não entram atrás:** a largada fica no chão, então `rise` ≠ 0 em peça de trás só avisa "Rampa não encaixa atrás da largada" (encaixar, trocar por rampa e o botão Sobe/Desce).
- **Fantasma** atrás: a peça espelhada (`-dir`) a partir da ponta de trás.
- **Fechar circuito (Q)** parte da ponta da frente e vai até a ponta de trás (`TrackCloser.Close(origin, frente, tras, preferBanked)`; HTML `Closer.close(origin, frente, banked, tras)`). A busca de ré começa no destino, que é a largada andada de ré pelas peças de trás, com as curvas espelhadas, em aritmética exata. As peças achadas entram entre a frente e as de trás. A busca roda aos poucos, com barra de progresso e botão **Cancelar (Esc)** (§17).
- **JSON.** O campo opcional `atras` (número) diz quantas das últimas peças de `pecas` ficam atrás da largada. Fora isso é o mesmo formato; sem o campo vale 0.
- **Mover e girar a largada** levam a pista toda (frente e trás), como antes.

**Testes feitos.**
- **HTML** (`ferramentas/teste-html.js`), oval de exemplo com uma reta comprida atrás da largada: a largada continuou em (8; 7,5; 0), as 12 peças da frente ficaram nas mesmas posições, a nova terminou na largada (de (4; 7,5) a (8; 7,5)) e a pista ficou aberta. Depois, uma curva atrás dela e desfazer, passo a passo, até o oval fechado. Pista nova com 3 peças atrás (reta, meia, meia) e 3 na frente (reta, curva, curva): `Q` fechou e `atras` voltou a 0. Importar JSON com `atras: 2`, `trackInfo` e `sanitize`, e remover as peças de trás.
- **Unity** (`script eval`, sem cena): largada + reta comprida + curva longa na frente e reta comprida + curva longa atrás. A abertura (gap) deu 11,7047, igual à do HTML (11,705), e o fechamento achou as mesmas 5 peças nos dois: reta comprida, curva longa, reta comprida, reta curta, curva longa.

---

## 23. Campeonato online

Quarta aba, **Online**: criar ou entrar numa sala, escolher a cor, conversar, o host escolher as pistas, **todos ficarem prontos** e correr o campeonato, com a tela do campeonato offline mais o chat. Por decisão do projeto, isto existe **só na Unity**: o HTML não tem a aba Online (exceção temporária à regra de HTML e Unity iguais).

### 23.1 Como funciona

- **Servidor de salas** (`servidor/servidor.js`, Node + `ws`): só repassa mensagens. Guarda as salas, os jogadores, o chat e a lista de pistas do host. Não roda física.
- **Entrar ou criar sala exige o nome** do jogador (1 a 20 caracteres, sem repetir dentro da sala). O cliente confere e o servidor também.
- **Sala pública ou privada.** A pública aparece na lista de salas; a privada só entra pelo código de 5 letras. As duas entram pelo código. O host muda a visibilidade quando quiser.
- **No lobby o visitante só escolhe a cor e fala no chat.** O **host** também escolhe as pistas (e as voltas de cada etapa) e a visibilidade. O servidor recusa a mesma ação vinda de um visitante (`host`).
- **Cor**: as 8 do campeonato (`Champ.Colors`), uma por jogador. Quem entra recebe a primeira livre; escolher uma já usada dá `cor-usada`.
- **Host que sai**: o jogador mais antigo vira host. Ele só tem o resumo das etapas, então reencontra as pistas pela identidade (`Champ.TrackHash`) entre as que tem no computador e tira as que não achar (`AdoptStages`). Sala vazia é apagada.
- **Cheia**: 8 jogadores (`MaxPlayers`). Máximo de 12 etapas, cada uma com até 100 KB de JSON e de 1 a 30 voltas.
- **Pronto.** Cada visitante aperta **Estou pronto** (o host está sempre pronto). Mexer nas etapas zera o "pronto" de quem já tinha confirmado (o servidor avisa no chat). O host só larga com **pelo menos 2 pilotos, 1 etapa e todos prontos**; o servidor confere de novo (`poucos`, `sem-etapas`, `nao-prontos`).
- **Largar.** O host manda as regras (os ajustes da aba Corrida: velocidade, rampa, aderência, colisão). O servidor entrega a **todos** os pilotos (na ordem do rodízio), as regras e as pistas com o JSON (`started`); a sala passa a `racing` e ninguém mais entra nem troca de cor. É o mesmo campeonato do offline (`Champ.MakeHeats`: N baterias por etapa, cada piloto uma vez em cada trilho).
- **Acabou ou desistiu.** O host manda `end` e todos voltam ao lobby (`lobby`), com o "pronto" zerado. Se o **host sai** no meio, o campeonato é encerrado para todos. Se um **visitante sai**, o campeonato segue: o carrinho dele fica parado e a bateria dele vale como não terminada (e se os dois da bateria saíram, o host a grava sozinho como não terminada).

### 23.2 Protocolo

JSON de uma linha por mensagem WebSocket, com o tipo em `t`. Classes em `Online/OnlineProtocol.cs` (o `JsonUtility` não faz herança: `NetMsg` junta tudo o que o servidor manda; `NetOut`, `NetOutStages`, `NetOutSnap` e `NetOutEv` são o que o cliente manda).

| Cliente → servidor | Campos | Quem pode |
|---|---|---|
| `create` | `name`, `roomName` (opcional), `pub` | qualquer um |
| `join` | `name`, `code` | qualquer um |
| `list` | | qualquer um |
| `color` | `color` (`#RRGGBB` da paleta) | todos na sala, só no lobby |
| `chat` | `text` (até 200; 5 mensagens a cada 3 s) | todos na sala |
| `ready` | `ready` | visitantes, só no lobby |
| `stages` | `stages`: `[{ name, laps, hash, json }]` | **só o host**, só no lobby |
| `visibility` | `pub` | **só o host** |
| `start` | `name`, `vmax`, `ramp`, `grip`, `collide` | **só o host**, com todos prontos |
| `end` | `text` | **só o host**, no campeonato |
| `in` | `held`, `re` (contador de "recolocar") | visitantes, no campeonato; vai **só para o host**, até 120 por segundo |
| `snap` | `snap`: `{ hi, ph, t, w, cars[2] }` | **só o host**; repassado a todos os outros (até 16 KB) |
| `ev` | `k` (`res` ou `reset`), `hi`, `heatRes[2]` | **só o host**; repassado a todos os outros |
| `leave`, `ping` | | |

| Servidor → cliente | Campos |
|---|---|
| `hello` | `id` |
| `joined` | `you` (meu id), `room`, `chat` (últimas 50) |
| `room` | `room`: `code, name, pub, host, max, state, players[{id,name,color,ready}], stages[{name,laps,hash}]` (sem o JSON das pistas) |
| `rooms` | `rooms`: `[{ code, name, host, players, max, stages }]` (só as públicas em `lobby`) |
| `chat` | `name`, `color`, `text`, `time`; ou `sys: true` (aviso do servidor) |
| `started` | `title`, `rules`, `pilots[{id,name,color}]`, `stages[{name,laps,hash,json}]` |
| `lobby` | `text`: o campeonato acabou ou foi interrompido; todos voltam ao lobby |
| `in` | `from` (id de quem mandou), `held`, `re` (só o host recebe) |
| `snap`, `ev` | os do host, como foram mandados |
| `error` | `code`, `text` (em português, para mostrar ao jogador) |

Códigos de erro: `nome`, `sem-sala`, `cheia`, `nome-usado`, `andamento`, `cor`, `cor-usada`, `host`, `etapas`, `poucos`, `sem-etapas`, `nao-prontos`, `regras`, `rapido`, `sala`, `cheio`, `versao`, `muitas-conexoes`, `muitas-salas`, `json`, `tipo`.

### 23.3 Servidor

```
cd servidor
npm install
npm start          # ws://localhost:8080 (variável PORT muda a porta)
npm test           # 36 verificações com vários clientes simulados
```

- **Versão do protocolo.** A primeira mensagem do cliente é `hello` com `ver` (`OnlineClient.Protocol`, hoje 1; no servidor `PROTOCOL`). Versão diferente, ou qualquer outra mensagem antes do `hello`, dá o erro `versao` ("Atualize o jogo") e derruba a conexão. Mude o número nos dois lados sempre que o formato das mensagens mudar.
- **Limites por endereço:** 20 conexões abertas (`muitas-conexoes`) e 6 salas criadas por minuto (`muitas-salas`). Atrás de proxy, o endereço vem de `fly-client-ip` ou `X-Forwarded-For`; isso só vale com `TRUST_PROXY=1` (o `Dockerfile` e o `fly.toml` já ligam).

- Mensagem de até 2 MB; o servidor derruba quem não responde a um ping de 15 s e tira o jogador da sala.
- `/saude` (HTTP) responde `ok salas=N`.
- O servidor precisa estar rodando antes de criar ou entrar. Sem ele, a aba avisa (balão, barra de status e texto no topo) com o endereço usado e o comando para subi-lo.
- Sem TLS: para jogar pela internet, ponha atrás de um proxy que dê `wss://` (o campo **Servidor** da aba aceita `wss://…`). O endereço fica em `PlayerPrefs` (`autorama.online.servidor`), junto com o último nome usado (`autorama.online.nome`). Padrão: `ws://localhost:8080`.
- Limites contra abuso: 500 salas, cor só da paleta, texto do chat limpo de controles, tipos de mensagem só os da tabela.

### 23.4 Unity

- **`OnlineClient`** usa `System.Net.WebSockets.ClientWebSocket` (não funciona em WebGL). A rede roda em tarefas com `ConfigureAwait(false)` e só põe eventos numa fila; **`Pump()` entrega tudo na thread principal**, chamado a cada quadro por `RaceController.Update` (`OnlineTick`). Cada conexão tem uma geração: o que sobrou de uma conexão fechada é descartado. Um envio por vez (fila de saída + `SemaphoreSlim`), porque o `ClientWebSocket` não aceita dois `SendAsync` juntos. Conectar tem limite de 8 s.
- **Conexão preguiçosa:** só conecta quando o jogador cria, entra ou atualiza a lista (`WhenConnected`). Sair da sala fecha a conexão.
- **`BuilderPanel`:** `Mode.Online` e a aba **ONLINE** (fonte das abas 10 px, para caberem as quatro). `RaceMode` é só "os carrinhos andam": Corrida, Campeonato e a aba Online **durante o campeonato online** (`OnlineRacing`, ligado por `SetOnlineRacing`). `Locked` é "não é o construtor" (nada de editar a pista, atalho Q, célula sob o mouse), e `EditLocked` substitui o antigo `InRace` no `BuilderInput`. Com o campeonato online andando não dá para trocar de aba (aparece um aviso); só sair da sala ou terminar.
- **`RaceController.Online.cs`** monta a aba, no mesmo estilo da aba Campeonato: tela de entrada (servidor, nome, criar, código, salas públicas) e tela do lobby (sala e código com **Copiar**, pilotos com "✓ pronto", cores, etapas com a lista de pistas do host, chat, **Estou pronto** / **Começar campeonato**, sair). No lobby a pista do construtor fica só de fundo e os carrinhos parados no grid.
- **`RaceController.OnlineRace.cs`** é a corrida online (seção 23.5); **`NetSync`** (`Online/NetSync.cs`) copia o estado de um carrinho do `RaceSim` para o snapshot e de volta.
- **`RaceSim.Mirror(fase, tempo, vencedor)`:** o visitante copia esses três valores (que têm `set` privado) do host.
- **Rich text:** nomes e mensagens dos outros passam por `Safe` (`<` vira `<` + espaço de largura zero), para ninguém mudar o visual do chat com `<b>`, `<size>` etc. Vale também para o nome dos pilotos, do campeonato e das etapas que chegam em `started`.
- **USS:** `.chat-log`, `.chat-line`; as abas ficaram com `font-size: 10px`.

```csharp
// OnlineClient: o que o jogo usa
var net = new OnlineClient();
net.Opened += () => net.Send(new NetOut { t = "create", name = "Ana", roomName = "Sala da Ana", pub = true });
net.Message += m => { if (m.t == "joined") room = m.room; };
net.Closed += motivo => Debug.Log(motivo);
net.Connect("ws://localhost:8080");
// a cada quadro, na thread principal:
net.Pump();
```

### 23.5 A corrida online

**Host autoritativo.** O host roda o `RaceSim` de verdade; os visitantes mandam só o botão e rodam o mesmo `RaceSim` como predição, corrigida pelo host. A tela é a do campeonato offline (`DrawRun`: cartão da bateria com os dois trilhos, placar ao vivo, tabela da etapa, tabela geral, lista de etapas), com três diferenças: o **chat** entra logo abaixo do placar, não há **recordes** (cada computador tem os seus) e os botões são do host. Só o host vê **Largar (Enter)**, **Salvar e seguir (Enter)**, **Correr de novo** e **Voltar ao lobby**; os visitantes veem "Aguardando o host".

| Constante | Valor |
|---|---|
| Snapshot na corrida (`SnapFast`) | 1/30 s (largada e prova) |
| Snapshot parado ou encerrado (`SnapSlow`) | 0,25 s |
| Tolerância do carro próprio (`NetSync.OwnTolerance`) | 1,5 células |
| Repetição do botão do visitante (`HeldRefresh`) | 0,25 s |
| Entradas por segundo de cada visitante (servidor) | 120 |

**Fluxo de uma bateria.**
1. `started` chega a todos. Cada um monta o mesmo `Championship` (pilotos com o id da sala, regras, etapas, `Champ.MakeHeats`) e a bateria da vez é `Champ.CurrentHeat`. `UseChampTrack` carrega a pista e as regras; o carrinho de cada piloto ganha a cor dele (`CarTexture`).
2. Quem está na bateria vê só o **seu** controle (acelerar **A**, **L** ou **Espaço**; recolocar **S** ou **K**; ou o mouse no botão). Quem não está só assiste (a câmera segue o trilho do jogador se ele está na bateria).
3. **Visitante:** `sim.cars[meu].held` vale na hora (predição) e o estado do botão vai ao host (`in`) a cada mudança e a cada 0,25 s. "Recolocar" usa um contador (`re`): o host só recoloca quando o contador sobe.
4. **Host:** junta o botão de cada trilho (o dele, ou o último `in` de quem está na sala; quem saiu fica sem acelerar), roda o `Step` e manda o snapshot (`NetSync.Pack`: fase, relógio, vencedor e os dois carrinhos inteiros; cerca de 1,2 KB, ~35 KB/s por visitante).
5. **Aplicar o snapshot** (`NetSync.Apply`): fase, relógio e vencedor sempre; o carrinho do outro piloto, por inteiro; o **meu** carrinho só se a predição se afastou mais que 1,5 célula ou o modo (no trilho, fora, voltando) é outro. Se não, fica a predição (senão a latência faria o carrinho "andar para trás" a cada snapshot) e só o placar (voltas, saídas, toques, melhor volta, chegada) vem do host. Snapshot de outra bateria é ignorado (`hi`).
6. A bateria acaba (`Done`). O host aperta **Salvar e seguir**: grava o resultado (`HeatResult` sem recordes), manda `ev res` (a bateria e os dois resultados) e carrega a próxima. Os visitantes aplicam o mesmo resultado e carregam a mesma bateria. **Correr de novo** manda `ev reset`.
7. Acabadas as baterias, o cartão vira o pódio e os prêmios (`DrawPodium`) e o host manda `end`.

```csharp
// Visitante: o snapshot do host vira o estado do RaceSim local
NetSync.Apply(snap, sim, ownLane);          // ownLane: 0, 1, ou -1 se só assiste
// Host: o que mandar a cada 1/30 s
net.Send(new NetOutSnap { snap = NetSync.Pack(sim, heatIndex) });
```

**Cuidados.**
- O `RaceController` usa `ActiveChamp` (o campeonato online, se houver; senão o offline) e `ChampLike` (aba Campeonato **ou** campeonato online) no lugar de `store.Active` e `panel.ChampMode` na tela, no placar, nos controles, no status e no banner. O `OnLapDone` (avisos de recorde) continua só no offline.
- Com o campeonato online, o `Update` vai para `OnlineRunUpdate`; `OnlineExitRun` devolve a tela do campeonato à aba Campeonato (`cRun` troca de pai) e restaura a mesa.
- Os recordes da pista **não** são gravados no campeonato online.

### 23.6 Testes feitos

- **Servidor** (`npm test`, Node 22, clientes `ws`, 36 verificações; as 4 últimas são versão do protocolo e limites por endereço): criar sem nome é recusado; sala pública na lista e privada fora dela; entrar pelo código sem diferenciar maiúsculas; nome repetido recusado; cor fora da paleta e cor já usada recusadas; chat chega a todos com nome e cor; chat vazio ignorado; limite de ritmo; visitante não escolhe pistas; etapa inválida (hash, JSON grande, 13 etapas) recusada; sala cheia (8) recusa; host sai e o próximo vira host; sala vazia apagada; JSON quebrado e tipo desconhecido não derrubam o servidor. **Corrida:** começar sozinho, sem pistas ou sem todos prontos é recusado (com os nomes de quem falta); mexer nas pistas zera o "pronto" (o host é sempre pronto); visitante não começa; regras fora dos limites recusadas; `started` igual para todos, com pilotos na ordem, regras e o JSON; ninguém entra nem troca de cor com o campeonato andando; `in` vai só ao host, com o `from`, e é limitado (119 de 150); `snap` e `ev` só valem do host e vão a todos menos a ele; snapshot grande demais é descartado; só o host encerra; o fim zera o "pronto"; host que sai encerra o campeonato; visitante que sai não derruba.
- **Cliente Unity** (`OnlineSmokeTest`, no Editor, contra o servidor de verdade): as regras do lobby pelo `JsonUtility` (18 verificações), o fluxo pronto → largar → `in` → `snap` → `ev` → fim (16) e a **sincronização** (6): host e visitante (80 ms de ida e 80 ms de volta, snapshots a 30 Hz, o visitante acelera e solta o carro 0 e recoloca quando sai da pista) rodam a corrida de 3 voltas no oval até o fim: mesma fase, vencedor, voltas, saídas, toques, chegada e melhor volta nos dois carros (45,2 s, vencedor carro 1, 32 recolocações); desvio máximo 0,89 célula no carro próprio e 0,90 no outro.
- **Tela** (Play mode, com a Unity de host e um visitante-robô em Node): lobby do host com "Começar campeonato" liberado só com o robô pronto ("Todos prontos: 2 pilotos · 2 etapas · 4 baterias"); a largada para os dois; o robô recebeu os snapshots (fase 0 → 1 → 2 → 3, cerca de 26 por segundo na prova) e o `ev res` com os resultados `[5 voltas 32,00 s]` e `[5 voltas 55,88 s]`; a bateria 2 com os trilhos trocados (Hugo no trilho 2, tabela da etapa "1 de 2 baterias"); **Encerrar campeonato** levou os dois ao lobby com o "pronto" zerado. A tela do campeonato offline continua igual (cartão da bateria, botão Largar e a dica).

### 23.7 Colocar o servidor no ar

O servidor é um processo Node só, com o estado das salas na **memória**: rode **uma única instância** (reiniciar derruba as salas abertas). Para jogar pela internet ele precisa de `wss://`; o jogo aceita `wss://` no campo **Servidor**.

**Fly.io (região `gru`, São Paulo).** Já existem `servidor/Dockerfile`, `.dockerignore` e `fly.toml` (uma máquina `shared-cpu-1x` de 256 MB, sempre ligada, verificação em `/saude`, `TRUST_PROXY=1`, TLS pelo Fly).
```
cd servidor
fly auth login
fly launch --no-deploy --copy-config      # troque "app" no fly.toml por um nome livre
fly deploy
```
Depois do deploy, escreva o endereço (`wss://SEU-APP.fly.dev`) em `OnlineClient.ProductionServer`: nos builds ele vira o padrão do campo **Servidor**; no Editor o padrão continua `ws://localhost:8080`.

**VPS qualquer.** `docker build -t autorama-servidor servidor && docker run -d --restart=always -p 127.0.0.1:8080:8080 autorama-servidor`, com o Caddy na frente (`seu.dominio { reverse_proxy 127.0.0.1:8080 }`; ele cuida do certificado e do WebSocket).

**Escolha a região perto dos jogadores:** cada comando do visitante passa pelo servidor até o host, e o snapshot faz o caminho de volta.

### 23.8 Próximos passos

1. Recordes da pista e histórico no campeonato online (hoje só o offline grava).
2. Reduzir o snapshot (campos que quase nunca mudam, ou só o que mudou) se a banda for um problema.
3. Expulsar jogador, passar o host adiante sem encerrar o campeonato, reconectar no meio da prova.
4. Depois que a Unity estiver fechada, portar a aba Online para o HTML (o servidor e o protocolo são os mesmos).

---

## 24. Steam (teste com o Spacewar)

Branch `steam-spacewar`. A Steam no lugar do servidor Node: o lobby faz de sala e as mensagens diretas entre os jogadores fazem a corrida. A aba Online escolhe a rede com o botão **Jogar pela Steam (sem servidor)**; o nome do jogador é o da conta Steam. Os testes usam o app de teste da Valve (**Spacewar, AppId 480**), que roda sem pagar nada e fora da loja. O servidor Node continua valendo para quem não usa a Steam.

### 24.1 Preparação

- **Pacote:** Steamworks.NET `2025.165.0` (UPM por git, em `Packages/manifest.json`; já traz a `libsteam_api.dylib` universal, arm64 e x86_64). Para atualizar, troque o número da tag.
- **`steam_appid.txt`** na raiz do projeto, com `480`. Sem ele a Steam recusa o jogo fora da loja. Num app seu, troque pelo AppId e **não** distribua esse arquivo nos builds.
- **Cliente da Steam aberto e logado** na máquina (o `SteamBoot` avisa em português quando não está).
- **Spacewar é compartilhado:** muita gente testa nele. Por isso todo lobby nosso leva o dado `autorama=1` e a versão do protocolo, e toda busca filtra por isso.

### 24.2 Camadas (`Assets/Autorama/Scripts/Online/Steam`)

| Classe | O quê |
|---|---|
| `SteamBoot` | `Init` (`SteamAPI.InitEx`, com a causa da falha em português), `Tick` (`RunCallbacks`, a cada quadro), `Shutdown`, nome e id da conta, AppId |
| `SteamLobbyClient` | O lobby como sala do campeonato: criar, procurar, entrar, sair, dados, membros, chat |
| `SteamLoopback` | Par de conexões da Steam no mesmo processo (`CreateSocketPair`), para testar o envio e a leitura das mensagens da corrida |

**Mapa do servidor Node para a Steam**

| Servidor Node | Steam |
|---|---|
| sala pública (aparece na lista) | lobby `Public`; busca com `pub=1` |
| sala privada (só pelo código) | lobby `Invisible`; busca com `code=XXXXX` |
| código de 5 letras, nome, etapas, estado | dados do lobby (`code`, `name`, `stages`, `state`, `pub`, `ver`), só o dono escreve |
| cor e "pronto" de cada jogador | dados do membro (`color`, `ready`), cada um escreve os seus |
| chat | `SendLobbyChatMsg` (UTF-8, o autor volta no callback) |
| host | dono do lobby (`GetLobbyOwner`); se ele sai, a Steam elege outro |
| `in` / `snap` / `ev` | mensagens de conexão (`SendMessageToConnection` / `ReceiveMessagesOnConnection`), o botão confiável e o snapshot sem confiabilidade |
| nome do jogador | nome da conta (`GetPersonaName`) |

```csharp
SteamBoot.Init();
var lobby = new SteamLobbyClient();
lobby.Created += ok => { if (ok) lobby.SetMyData("color", "#2563EB"); };
lobby.Chat += (quem, texto) => Debug.Log($"{quem.name}: {texto}");
lobby.Create("Minha sala", pub: true);        // ou lobby.Search() / lobby.Search("QYQH3") e lobby.Join(id)
// a cada quadro:
SteamBoot.Tick();
```

### 24.3 Testes

**Sozinho, com uma conta** (`SteamSpacewarTest.Run`, no Editor com a Steam aberta):
```
unity command script eval 'Autorama.SteamSpacewarTest.Run(); return Autorama.SteamSpacewarTest.Last;'
```
Resultado desta etapa, com a conta real (24 verificações, todas passaram): a Steam inicia e o app é o 480; criar lobby público e o criador ser o dono; dados do lobby gravados e alterados só pelo dono; cor, pronto e nome do membro; o lobby aparece na lista (a busca pode precisar de 1 a 2 tentativas, a lista demora alguns segundos para enxergar um lobby novo) com nome, jogadores, máximo, etapas e host; chat com UTF-8 e `<b>` voltando inteiro e com o autor; lobby privado achado só pelo código e fora da lista pública; sair do lobby; e, pelo par em loopback, o botão (`in`, confiável), o snapshot (737 bytes parado, 1,2 KB na prova, não confiável) e o `ev` chegando iguais, ida e volta. Mensagens não confiáveis de 1200, 1500 e 4000 bytes chegaram inteiras **no loopback**, que não passa pela rede: na rede de verdade a Steam fragmenta mensagens grandes sem confiabilidade e perde a mensagem toda se um pedaço se perder, então vale manter o snapshot pequeno (seção 23.8, item 2) ou mandá-lo confiável.

**Com duas contas** (o que a camada ainda não prova: chegada entre jogadores de verdade):
1. Máquina A (conta 1): `Autorama.SteamSpacewarTest.HostManual("Sala de teste", true)` e depois `Autorama.SteamSpacewarTest.ManualLog` mostra o código.
2. Máquina B (conta 2, outra Steam, com este mesmo projeto): `Autorama.SteamSpacewarTest.JoinManual("")` entra na primeira sala pública, ou `JoinManual("CODIGO")`.
3. Dos dois lados: `ManualSay("oi")`, `ManualReady(true)`, e conferir `ManualLog` (jogadores com ✓, o dono marcado como host, o chat do outro lado).
4. `ManualStop()` para sair.

### 24.4 A aba Online sobre a Steam (transporte)

A aba (`RaceController.Online*.cs`) fala com a rede só por `IOnlineTransport` (`Connect`, `Send`, `Pump`, eventos `Opened`/`Closed`/`Message`), com as mesmas mensagens (`NetOut…` para enviar, `NetMsg` para receber). Duas implementações:

| | `OnlineClient` | `SteamTransport` |
|---|---|---|
| Rede | WebSocket até o servidor Node | Lobby da Steam + mensagens diretas (`SteamNetworkingMessages`, Datagram Relay) |
| "Servidor" | `servidor/servidor.js` | cada cliente, em cima do lobby (o dono do lobby é o host) |
| Nome | o jogador digita | **o da conta Steam** (o campo fica desligado) |

`SteamTransport` usa um `ILobbyBackend` (`SteamBackend` na Steam de verdade; `FakeLobbyHub`/`FakeBackend`, no Editor, para testar vários jogadores num processo só). O que o servidor Node confere, o transporte confere em quem manda, e os outros só confiam no dono do lobby.

- **Sala:** `create` cria o lobby (público: aparece na lista; privado: tipo *Invisible*, só pelo código). O código de 5 letras, o nome, o estado, as etapas (resumo em JSON em `stl`) e a visibilidade são dados do lobby, que só o dono escreve. `join` busca pelo código, confere o estado (`andamento`) e a lotação (`cheia`) e entra.
- **Cor, pronto e nome:** cor e pronto são dados do próprio membro; o nome vem de `GetPersonaName`. O "pronto" vale para uma **época**: o lobby guarda `epoch` (começa em 1; `0` é "não pronto") e o jogador grava a época em que confirmou. Mexer nas etapas sobe a época e zera todos sem precisar escrever nos dados dos outros.
- **Largar (host):** confere 2 pilotos, 1 etapa, todos prontos e as regras; manda `started` (pilotos, regras e as pistas **com o JSON**) a cada visitante por mensagem direta confiável (até 500 KB), grava `state=racing` e fecha o lobby (`SetLobbyJoinable(false)`).
- **Corrida:** `in` vai do visitante ao dono (confiável, sem *nagle*, e o id vem de quem mandou, não do texto); `snap` do dono a todos, sem confiabilidade; `ev` e `lobby` do dono a todos, confiáveis. Só se aceita `snap`/`ev`/`started`/`lobby` do dono, e conversa direta só de quem está no lobby (`AcceptSessionWithUser` confere).
- **Fim:** `end` manda `lobby` a todos, volta `state=lobby`, sobe a época e reabre o lobby. Se o **host sai** no meio, cada um percebe que o dono da corrida sumiu, mostra "O host saiu" e quem virou dono volta o lobby. Se só um visitante sai, o campeonato segue.
- **Host novo no lobby:** o novo dono perde o JSON das pistas (o lobby guarda só o resumo): a aba reencontra as pistas pelo hash e as reenvia (`AdoptStages` agora sempre chama `SendStages`, com os dois transportes).
- **Chat:** `SendLobbyChatMsg`; não há histórico para quem entra depois (o servidor Node guarda as últimas 50).
- **Botão na aba:** ligar **Jogar pela Steam** inicia a Steam (se falhar, desliga e avisa, por exemplo "O cliente da Steam não está aberto"), mostra o nome da conta e esconde o endereço do servidor. A escolha fica em `PlayerPrefs` (`autorama.online.steam`).

**Convidar amigos** (só com a Steam ligada, dentro da sala, no lobby):
- **Convidar amigos (Steam):** abre a janela de convite da Steam (`ActivateGameOverlayInviteDialog`). O overlay só existe no jogo instalado, **não no Editor**.
- **Amigos online** (lista dentro do jogo, funciona no Editor): os amigos da conta que estão online e ainda não estão na sala, com **Convidar** (`InviteUserToLobby`; vira **Convidado ✓**). **Atualizar** relê a lista. O convite aparece na Steam do amigo; ao aceitar com o jogo aberto, a Steam avisa (`GameLobbyJoinRequested`) e a aba liga a Steam, abre a aba Online e entra no lobby sozinha (`JoinById`). Com o jogo fechado, a Steam abre o jogo com `+connect_lobby <id>` na linha de comando e a aba entra assim que inicia. O lobby precisa ser do Autorama (`autorama=1`, mesma versão do protocolo) e estar em `lobby`; com o campeonato andando ele nem aceita mais ninguém.
- **A lista rola sozinha:** o título mostra quantos são (`AMIGOS ONLINE · 26`) e as linhas ficam num `ScrollView` com a classe `.friends-scroll` (altura máxima `240px`, `overflow: hidden`, borda e fundo escuro), então o que passa da caixa é cortado e se rola com a roda dentro dela. A lista mostra todos os amigos (antes eram só os 20 primeiros). Ao convidar, a lista é redesenhada e volta à mesma posição de rolagem. Teste (Play mode, 40 linhas falsas com as mesmas classes): caixa com 240 px de altura, conteúdo de 1.415 px, barra de rolagem visível.
- Quem convida pode ser qualquer um da sala (não só o host).

### 24.5 Testes do transporte

`SteamTransportTest.Run` (Editor): `script eval 'Autorama.SteamTransportTest.Run(); return Autorama.SteamTransportTest.Last;'`.

- **Mundo falso, três jogadores** (a mesma lista de regras do `npm test`): conectar; lista vazia; criar sala (host, 1ª cor, nome da conta, código); sala pública na lista; código errado e maiúsculas; cor inválida e cor usada; chat com texto limpo, nome e cor, e limite de ritmo; visitante não escolhe pistas; resumo das etapas com os hashes conhecidos; etapa inválida; sala privada fora da lista; largar sem todos prontos; pronto; mexer nas pistas zera o pronto (e o host é sempre pronto); visitante não larga; regras fora dos limites; `started` com pilotos, regras e o JSON da pista (mesmo hash); com o campeonato andando ninguém entra nem troca de cor; `in` só ao host, com o id certo; `snap` só do host; `ev`; só o host encerra; o fim zera o pronto; host que sai no meio encerra e o visitante vira host; sala vazia some. Descobriu um erro real: a época começava em `0`, igual a "não pronto" (corrigido: começa em 1).
- **Steam de verdade** (uma conta, Spacewar): o transporte abre; o nome vem da conta; criar sala; as etapas ficam no lobby; o chat volta com o nome; a cor muda; largar sozinho é recusado; a lista enxerga a sala; torná-la privada; e uma mensagem direta para si mesmo **chegou** (a Steam aceita loopback entre contas iguais).
- **Convites** (mundo falso, 3 jogadores): só os amigos online aparecem; amigo offline não recebe; o host convida o Caio, que recebe o pedido e **entra sozinho** na sala certa (privada, sem estar pronto); quem já está na sala sai da lista; o botão da janela da Steam chama o overlay; com o campeonato andando o lobby não aceita mais ninguém. Na Steam de verdade a lista de amigos foi lida sem erro (26 online); **nenhum convite real foi enviado** nos testes.
- **Tela** (Play mode, Unity de host com o botão da Steam ligado e um visitante-robô no mundo falso, `SteamTransportTest.UiSetup`/`BotTick`): o nome fica "Hugo" e o campo desligado; a sala é criada; o robô entra e fica pronto; ao mexer nas etapas o pronto dele zera e ele confirma de novo; o host larga; a corrida roda (755 snapshots entregues ao robô) e o carrinho do robô anda com o botão dele; a tela é a do campeonato offline, com o chat. Na sala, a lista **Amigos online** mostrou o robô, o clique em **Convidar** (evento de teclado/gamepad do UI Toolkit) fez o robô entrar e ficar pronto, e ele saiu da lista.

Regressão: `OnlineSmokeTest` (servidor Node, 42 verificações) e `SteamSpacewarTest` (24) continuam passando.

### 24.5b steam_appid.txt nas builds (Windows e macOS)

A Steam procura `steam_appid.txt` **ao lado do executável**. `SteamBuildPostprocessor` copia o arquivo da raiz do projeto (`480` = Spacewar) depois de cada build:

- **Windows e Linux:** para a pasta da build, ao lado do `.exe`.
- **macOS:** o executável fica **dentro do pacote**. Antes o arquivo ia só para a pasta que contém o `Autorama.app`, onde a Steam não o via ("sem AppId" no Mac). Agora vai para `Autorama.app/Contents/MacOS/` **e** para o lado do `.app`.
- Só vale para o teste com o Spacewar. Numa build enviada à Steam (SteamPipe) com o AppId próprio, a Steam informa o app ao abrir o jogo e o arquivo não é necessário (convém não enviá-lo).
- Não foi possível gerar uma build de macOS nesta máquina (Windows): a correção está conferida só pela leitura do código e pela compilação.

### 24.6 O que falta

1. **Duas contas de verdade:** nada disso provou ainda a chegada entre dois jogadores em máquinas diferentes (ver o roteiro em 24.3). A mensagem direta para si mesmo chegou, o que é um bom sinal, mas não é a rede.
2. **Tamanho do snapshot** (~1,2 KB): na rede real a Steam fragmenta mensagens grandes sem confiabilidade e perde a mensagem toda se um pedaço se perder. Se houver perda visível, reduzir o snapshot ou mandá-lo confiável.
3. **Rich Presence** (mostrar "numa sala do Autorama" para os amigos, com o botão "Entrar no jogo") e testar o convite de verdade entre duas contas, incluindo o jogo fechado (`+connect_lobby`).
4. **AppId próprio** e a taxa da Steam Direct (US$ 100) antes de publicar; o 480 serve só para teste. Num app seu, o transporte já filtra por `autorama=1` e pela versão do protocolo.

## 25. Pista de 4 carros

Uma pista nasce de **2 ou de 4 carros** e fica nesse tipo. Existe **só na Unity** (o HTML está congelado; ver a regra da memória do projeto). Construtor, corrida livre, campeonato e online suportam os dois tipos, e numa pista de 4 podem correr **2, 3 ou 4 carros** (seção 27).

### 25.1 A ideia: mesma geometria em células, escala 2 no mundo

Em vez de criar um segundo catálogo de peças, a pista de 4 usa **as mesmas peças e os mesmos ids** (`reta-curta`, `curva-longa-inclinada`…) e a mesma conta em células. O que muda é quanto vale uma célula:

| | Pista de 2 | Pista de 4 |
|---|---|---|
| 1 célula no mundo (`TrackMath.Scale`) | 1 | **2** |
| Trilhos, em células (`TrackMath.LanesOf`) | ±0,25 | **±0,125 e ±0,375** |
| Trilhos, no mundo | ±0,25 | ±0,25 e ±0,75 |
| Raio da curva no centro, no mundo | 2,5 | **5** |
| Raio do trilho de dentro / de fora (mundo) | 2,25 / 2,75 | **4,25 / 5,75** |
| Carrinho (mundo) | 0,60 × 0,32 | o mesmo |

Por isso o construtor inteiro (encaixe, sobreposição, mesa que cresce, fechar circuito com Q, frente/trás da largada) funciona sem mudar nada: tudo é em células. Quem mostra é que multiplica:

- `TrackBuilder.Faixas` (2 ou 4), `Scale`, `CellToWorld`/`WorldToCell`. A raiz das peças geradas ganha `localScale = Scale`; a mesa (`GridBoard.Build(size, scale, mesa4)`) também.
- O mouse vira célula com `WorldToCell` (célula sob o mouse, arrastar a largada). O enquadramento, as réguas e a barra de escala usam `CellToWorld`. `Shifted` (a mesa cresceu e a pista foi empurrada) já sai em unidades do mundo.
- No resumo, `cm por célula = escalaCm × Scale` (a célula da pista de 4 tem o dobro da largura).

A física fica **no mundo**: `LanePath(segs, offset, scale)` multiplica posições e alturas por `scale` e divide a curvatura (`k = dir / ((R − dir·off) · scale)`). A rampa continua em níveis por célula (o ângulo é igual nas duas pistas). O grid e a bandeirada também escalam (`GridPos = GridS × Scale`, marca em `LineS × Scale`), assim como a altura de quem sai da pista e o recuo ao recolocar (0,4 × Scale). As constantes (`ACCEL 3.5`, `BRAKE 1.8`, `SLIP_GAIN 6`…) **não mudam**; como a curva é maior, a pista de 4 é mais rápida nas curvas e a volta é mais longa.

```csharp
public static readonly float[] Lanes4 = { -0.375f, -0.125f, 0.125f, 0.375f };
public static float[] LanesOf(int faixas) => faixas == 4 ? Lanes4 : Lanes;
public static float Scale(int faixas) => faixas == 4 ? 2f : 1f;
```

**Cruzamento:** cada trilho faz o S `l·cos(π·t)`, então troca com o espelho: **1↔4 e 2↔3**. O S do 1 e do 4 é maior (comprimento com a amplitude de cada trilho: `CrossLaneLength(length, |offset|)`). Com cruzamentos em número ímpar, `Analyze` faz a média de cada par espelhado.

### 25.2 JSON e identidade

- Campo novo `"faixas": 4` no JSON da pista. Sem o campo (todas as pistas antigas e as do HTML) é **2**; qualquer valor diferente de 4 vira 2 (`TrackMath.Faixas`).
- Identidade para os recordes: a pista de 4 começa a cadeia com `f4|` antes do FNV-1a (`"f4|largada:0:0|…"`). As de 2 **não mudam**: oval = `a34f4cb9`, 8 com ponte = `81546106`. A mesma forma em 2 e em 4 são pistas diferentes, com recordes separados.
- O HTML não conhece o campo: ele abriria uma pista de 4 como se fosse de 2. Como o HTML está congelado, isso fica registrado aqui e não foi mexido lá.

### 25.3 Construtor

- Seção **TIPO DA PISTA** no painel: **2 carros · 4 carros**. Com só a largada, troca na hora (`SetFaixas`, entra no desfazer). Com peças, o segundo clique começa uma **pista nova** do outro tipo (`NewTrack(faixas)`; Ctrl+Z volta).
- **Nova pista** mantém o tipo atual; os exemplos (oval, 8) abrem no tipo atual. Título do painel: `AUTORAMA · 4 TRILHOS`.
- A biblioteca mostra o tipo na linha de cada pista (`4 carros · 12 peças · …`).
- Resumo com os 4 trilhos (cores vermelho, azul, verde e amarelo: `--trilho-a…d` no USS) e "mais longo: trilho 4 +X cm (sobre o 1)".

### 25.4 Arte própria (não é a de 2 esticada)

Tudo gerado por `ferramentas/gen-texturas.js` (bloco "PISTA DE 4 CARROS") e ligado pelo menu **Autorama > Atualizar materiais e ícones** (campos `*4` do `TrackTheme`). Estilo "circuito":

- **Asfalto** azulado e mais liso (`#2A3138`, ruído mais fino), **trilhos de cobre trançado** (listras diagonais) em vez de aço, **tracejado branco** no meio (entre os trilhos 2 e 3), acostamento cinza-azulado nas bordas e uma tinta bem leve da cor de cada trilho em volta da fenda.
- **Curva plana:** zebra **azul e branca**, 6 listras por célula. **Inclinada:** chevrons brancos subindo para o lado alto, o asfalto clareando em azul e um **muro azul** na borda de fora.
- **Cruzamento:** as 4 fendas em S e um **losango branco** no meio.
- **Largada** (`pista4-largada.png`, a peça inteira numa imagem, 1024 × 2048): um **box em U na cor de cada trilho** em volta do lugar do carrinho, o **número do trilho** (dígitos de 7 segmentos) atrás do box e a bandeirada quadriculada larga. Por isso `TrackPieceView` usa v de 0 a 1 na largada de 4 (como no cruzamento) e não desenha a faixa de bandeirada separada.
- **Peças no alto:** muro azul listrado (`mureta4.png`) mais fino e mais alto (0,02 × 0,06 célula) e, embaixo, **três pilares quadrados de aço escuro** por fileira (bordas e meio) com uma **travessa** de borda a borda, a cada 1 célula (`BuildPillars4`).
- **Mesa** em tom frio (`grade-mesa4.png`) e **ícones** próprios (`icone4-*.png`: 4 trilhos coloridos; o painel troca os ícones da paleta pelo tipo).
- **Carrinhos 3 e 4:** `carrinho-verde.png` e `carrinho-amarelo.png` (o mesmo molde), em `TrackTheme.cars34`. Sem o tema atualizado, a corrida repete o vermelho/azul.
- Texturas `pista4-*` importam até 2048 px (o dobro de detalhe por célula).

### 25.5 Corrida

- `RaceSim.SetTrack(segs, closed, faixas)`: `cars` passa a ter 2 ou 4 (`sim.N`); `paths`/`aiSkill` têm 4 lugares (`MaxCars`). Tudo que era "os dois" virou laço até `N`: passo, IA, fim da prova (acaba quando **todos** chegam ou no limite do campeonato) e `HumanBest`.
- **Colisão por pares:** 1 par na pista de 2, 6 na de 4 (`CollidePair`, com o "já estava encostado" guardado por par em `pairContact`). A conta de cada par é a mesma de antes.
- **Teclas:** os humanos ganham os conjuntos **na ordem dos trilhos**: o 1º humano **A·S** (Q meia), o 2º **L·K** (O meia), o 3º **↑·↓** (← meia), o 4º **8·5 do teclado numérico** (7 meia). Com um humano só ele usa A·S em qualquer trilho (a regra antiga é o caso particular). Os controles seguem a mesma ordem (1º controle, 2º…).
- **Corrida livre na pista de 4:** "Jogador 2 é um robô" continua; aparece **"Jogadores 3 e 4 são robôs"** (`RaceSettings.bot34`, ligado por padrão), com a mesma qualidade.
- Tela: 4 painéis de jogador mais baixos (`.pads--4`), 4 fichas no placar com a posição de cada um (`Order()`: chegada, voltas e depois a **fração da volta**, porque os trilhos têm comprimentos diferentes), resultado com as 4 posições, câmera **Atrás do carro** com Jogador 1 a 4 e a **Dinâmica** enquadrando o retângulo dos 4 (`SetRaceTargets(pos[], heading[], count, focus)`).
- Exemplos novos na lista de pistas: **oval de 4 carros** e **8 com ponte de 4 carros** (`TrackSave.Four`).

### 25.6 Campeonato

- **Carros por bateria: 2 · 4** no Novo campeonato (`ChampDraft.faixas`, `Championship.faixas`). Só aparecem as pistas do tipo escolhido; trocar o tipo tira as etapas do outro (segundo clique). **Os dois tipos nunca se misturam.**
- Campeonato de 4 aceita **a partir de 2 pilotos**: os trilhos que sobram ficam vazios (seção 27).
- **Rodízio** (`MakeHeats(n, etapas, faixas)`): na bateria k correm os pilotos `k, k+1, k+2, k+3` (módulo n) nos trilhos 1 a 4. Cada piloto corre **uma vez em cada trilho** e a etapa tem n baterias, como na de 2. `Heat.p` tem 2 ou 4 posições; `Saved` confere `res.Length == p.Length`.
- Recordes por trilho: `lane3` e `lane4` em `TrackRecords` (vazios nas pistas de 2). Os de volta e de corrida continuam de gente só.
- Arquivo antigo (`campeonatos.json` sem `faixas`) abre como de 2.

### 25.7 Testes

`script eval` (sem cena), 4 robôs de qualidade 100, 5 voltas, passo 1/60 s, `Random.InitState(7)`:

| Pista | Hash | Trilhos (células) | Chegada (s) | Saídas |
|---|---|---|---|---|
| Oval, 2 | `a34f4cb9` | 37,133 · 33,991 | 21,933 · 20,883 | 0 · 0 |
| 8 com ponte, 2 | `81546106` | 57,562 · 57,562 | 27,166 · 27,166 | 0 · 0 |
| Oval, 4 | `6bcdca3d` | 37,918 · 36,347 · 34,777 · 33,206 | 35,383 · 33,966 · 32,783 · 32,316 | 2 · 0 · 0 · 0 |
| 8 com ponte, 4 | `961b213a` | 57,562 (os 4) | 41,483 (os 4) | 0 |

- A pista de 2 deu **exatamente os mesmos números** antes e depois da mudança (física, IA e colisão por pares).
- Caminho no mundo da oval de 4: 75,834 · 72,693 · 69,551 · 66,410 (o dobro das células).
- Rodízio de 5 pilotos em 4 trilhos: 5 baterias, cada piloto **uma vez em cada trilho**. Rodízio de 2 com 3 pilotos continua `[0,1] [1,2] [2,0]`.
- JSON sem `faixas` → 2; JSON de 4 ida e volta → 4.
- **IA na pista de 4:** no primeiro teste os robôs dos trilhos de fora saíam **9 vezes** cada no oval: a coragem (até 115% do limite) enchia a derrapagem nas curvas 2× mais longas. Correção: com `Scale > 1`, a parte acima do limite é dividida pela escala, `m = √(1 + (m² − 1) / Scale)` (os erros de propósito, m ≥ 1,3, ficam). Caiu para 2 saídas em 4 carros. Na pista de 2 nada muda.
- Tela (Play mode): construtor com o 8 de 4 (mesa com células de 2, réguas certas, arte nova, 4 carrinhos no grid) e corrida no oval de 4 com 4 fichas de posição e 4 painéis.

### 25.8 O que falta

1. O HTML não conhece `faixas` (congelado).

## 26. Corrida por tempo (manches)

Segundo formato de prova, ao lado do **por voltas**. A corrida tem uma duração total e se divide em **uma manche por trilho**: 2 manches na pista de 2, 4 na de 4. No fim de cada manche os carrinhos param, cada um passa para o **trilho seguinte** (1→2, …, último→1) e uma nova contagem larga a manche. Uma prova de 1 min tem manches de 30 s na pista de 2 e de 15 s na de 4. Vence quem **andar mais**. Existe só na Unity (o HTML está congelado).

### 26.1 Regras

- **Distância** = bandeiradas + fração da volta: `Dist(k) = cross + LapFrac(s)`. Vale em trilhos de comprimentos diferentes (cada um mede a fração no próprio caminho). A ordem da prova usa a distância nos dois modos (por voltas, quem chegou vem antes).
- **Troca de trilho no mesmo ponto da volta:** o carrinho mantém a fração do caminho (`f = s / Total`) e vai para `f × Total` do trilho novo, parado. Quem estava fora da pista volta do ponto onde saiu. A contagem de bandeiradas continua (`crossBase = cross − (LineCount(s) − 1)` no caminho novo).
- A **volta que pegou a troca** (metade num trilho e metade no outro) conta na distância, mas **não vale como tempo** de volta (`lapDirty`): melhor volta e recordes só com voltas inteiras num trilho.
- O relógio de prova **para** durante a troca (`SwitchTime = 2,5 s`) e a nova contagem (`Count = 2,4 s`): `resumeT` guarda o tempo e a contagem usa `t` como antes. `RaceTime` e `TimeLeft` dão o relógio certo em qualquer fase.
- Por tempo não há chegada nem o limite de 30 s depois do primeiro (`cutoff`); a prova acaba no fim da última manche, e `winner` é quem tem a maior distância.
- Não há recorde por trilho nem recorde de corrida por tempo; o recorde de volta e o melhor pessoal continuam.

### 26.2 Código

- `RaceSettings.byTime`, `secs` (padrão 120; na aba Corrida de 1 a 10 min, de 30 em 30 s).
- `RaceSim`: fase nova `Switch`; `Manche`, `MancheLen = secs / N`, `EndManche()`, `RotateLanes()`, `PathOf(k)` (o caminho do trilho atual do carrinho k: `paths[cars[k].lane]`), `Dist(k)`, `TimeLeft`. `CarState.lane`, `crossBase`, `lapDirty`. Por voltas, `lane = k` sempre e tudo fica igual ao de antes.
- `LanePath.LapFrac(d)`: fração entre a última bandeirada e a próxima (com cruzamentos ímpares o caminho tem 2 marcas).
- `present[k]`: trilho vazio na bateria (campeonato por tempo com menos pilotos que trilhos). Carrinho ausente não anda, não colide, não aparece e não entra na ordem nem nas teclas (`KeySet` só conta os presentes).
- As teclas e os controles são **do piloto, não do trilho**: o carrinho k troca de trilho e continua com as mesmas teclas.

### 26.3 Tela

- Aba Corrida, em Ajustes: **Por voltas · Por tempo** e a duração ("2 min · 30 s por trilho").
- Relógio regressivo (o total antes da largada). Fichas: `2,24 v · T1` (distância e trilho atual). Painéis: "x voltas · trilho n" e, na troca, "Vai para o trilho n".
- Entre as manches: **TROCA DE TRILHO / manche 2 de 4** no meio da mesa (`.cd-text--switch`), e as luzes e o VAI! de novo em cada manche.
- Resultado e aviso de vitória com a distância ("13,65 voltas em 1 min").

### 26.4 Campeonato

- **Formato: Por voltas · Por tempo**, para o campeonato inteiro (`Championship.byTime`, `ChampDraft.byTime`). Cada etapa tem a duração (`Stage.secs`, 1 a 10 min) em vez das voltas.
- Por tempo, uma bateria já passa por todos os trilhos, então **cada piloto corre uma vez por etapa**. Com mais pilotos que trilhos, eles se dividem em **grupos equilibrados** na ordem do cadastro (`MakeHeats(n, etapas, faixas, byTime)`): 5 em 4 trilhos = `[0,1,2,−] [3,4,−,−]`; 6 em 2 = 3 grupos de 2; 3 em 2 = `[0,1] [2,−]`. Os trilhos que sobram ficam vazios (`−1`, mostrados como "trilho vazio").
- Mínimo de pilotos por tempo: 2, também na pista de 4 (o trilho vazio resolve).
- A etapa classifica todos os pilotos pela **distância** (`StageRow.dist`, gravada em `HeatResult.dist`); pontos e melhor volta como antes.
- Validação do arquivo: `heats.Count == HeatsPerStage(pilotos, faixas, byTime) × etapas`.

### 26.5 Online

- No lobby, seção **ETAPAS**, o host tem **FORMATO: Por voltas · Por tempo** (o mesmo ajuste `set.byTime` da aba Corrida, então largar usa o que está ali) e **PISTA: 2 carros · 4 carros** (`onlineFaixas`; a lista de pistas mostra só as do tipo escolhido, e trocar com etapas na lista pede um segundo clique e as tira). Todos leem a linha "**Por tempo · pistas de 4 carros** · com 3 jogadores, 1 trilho fica vazio em cada bateria".
- `start` leva `byTime`; cada etapa leva `secs`, `tempo` (o formato) e `faixas` (`NetStage`). O servidor Node e o transporte da Steam só validam `secs` (60 a 600) e repassam `tempo` e `faixas` no resumo da sala, que é de onde os visitantes leem o formato. Cliente antigo sem os campos: 120 s, por voltas, 2 carros.
- O servidor Node e o transporte da Steam validam `secs` entre 60 e 600 e repassam `byTime` no `started`.
- Snapshot: `mi` (manche) e `rt` (relógio guardado) na corrida; cada carro leva `lane`, `cb` (crossBase) e `ld` (lapDirty). O visitante só mantém a predição do próprio carrinho se ele estiver no mesmo trilho do host.
- Online continua só de 2 carros; por tempo com 3 pilotos, o grupo de 1 corre sozinho.

### 26.6 Testes

`script eval`, robôs de qualidade 90, 1 min, passo 1/60 s, `Random.InitState(7)`:

| Pista | Fins de manche (s) | Distância (voltas) | Trilhos de cada carro |
|---|---|---|---|
| Oval, 2 | 30 | 13,654 · 13,558 | 2 |
| 8 com ponte, 2 | 30 | 10,549 · 10,633 | 2 |
| Oval, 4 | 15 · 30 · 45 | 8,853 · 8,960 · 8,804 · 8,839 | 4 |
| 8 com ponte, 4 | 15 · 30 · 45 | 7,156 · 7,154 · 7,156 · 7,157 | 4 |

- O relógio de prova fecha em 60,000 s; com as pausas, o tempo real foi 67,4 s (pista de 2) e 77,3 s (pista de 4).
- Por voltas: os números da seção 25.7 continuam idênticos.
- Grupos: os cinco exemplos acima conferidos. Etapa por tempo com 3 pilotos em 2 trilhos: ordem pela distância (11,2 · 10,9 · 10,5), pontos 10+1 (melhor volta), 8, 6.
- `servidor`: `npm test` continua passando, com dois testes novos: `secs`, `tempo` e `faixas` chegam aos visitantes (sem os campos: 120, por voltas, 2), e duração de 30 s é recusada.
- Tela (Play mode, Editor em segundo plano, então a simulação foi avançada pelo script): oval de 4 por tempo na troca de trilho, com as fichas por distância, os painéis "Vai para o trilho n" e o aviso no meio.

### 26.7 O que falta

1. HTML (congelado).
2. Teste do online por tempo com dois jogadores de verdade.

## 27. Trilho vazio: 2 ou 3 carros numa pista de 4

Numa pista de 4 podem correr menos carros. O trilho sem carro fica **vazio**: `RaceSim.present[k] = false` (o carrinho k não anda, não colide, não aparece, não entra na ordem nem nas teclas, e a prova por voltas acaba quando **os presentes** chegam).

- **Corrida livre:** na pista de 4 aparece **2 carros · 3 carros · 4 carros** (`RaceSettings.cars4`, padrão 4). Com 3, o trilho 4 fica vazio e o ajuste vira "Jogador 3 é um robô"; com 2, só os trilhos 1 e 2.
- **Campeonato por voltas:** `MakeHeats` faz `H = max(pilotos, trilhos)` baterias por etapa; na bateria k o trilho l recebe o piloto `(k + l) mod H`, ou fica vazio se esse número passa do total. Cada piloto corre **uma vez em cada trilho** e enfrenta os outros. 3 pilotos na pista de 4: `[0,1,2,−] [1,2,−,0] [2,−,0,1] [−,0,1,2]`. Com pilotos ≥ trilhos é o rodízio de sempre. Mínimo: 2 pilotos.
- **Campeonato por tempo:** já tinha os grupos com trilho vazio (seção 26.4).
- **Online:** a aba Online aceita pistas de 4; a primeira etapa decide o tipo e as outras precisam ser iguais (o host só vê as do mesmo tipo). No `started`, o cliente monta `faixas` pela primeira etapa e recusa etapas misturadas. Com 3 jogadores numa pista de 4, o rodízio acima. O servidor não muda (ele não conhece trilhos); o snapshot leva os 4 carros (o ausente também, parado).

### 27.1 Testes

- Rodízio por voltas: 3 pilotos em 4 trilhos = 4 baterias, 4 vazios, cada um 1× por trilho; 2 em 4 = 4 baterias, 8 vazios; 5 em 4 e 3 em 2 iguais ao de antes.
- Simulação, 3 robôs (qualidade 90) na oval de 4: por voltas acaba em 33,98 s com os 3 chegando (o 4º parado no grid); por tempo, 60 s, cada presente passou pelos 4 trilhos.
- Etapa de 3 pilotos na pista de 4: completa (4/4), 4 corridas cada, pontos 11 · 8 · 6.
- **Sincronia online** sem rede (`NetSync.Pack` → JSON → `Apply` a cada 3 quadros, como o host manda): 3 carros na oval de 4, por voltas e por tempo. O visitante termina na mesma fase, com o mesmo vencedor, a mesma distância (até a 3ª casa) e o mesmo trilho em cada carro. Snapshot de 4 carros: **1,8 a 2,0 KB** (o de 2 tinha ~1,25 KB).
- `OnlineSmokeTest` contra o servidor Node: as 42 verificações continuam passando; `npm test` também.
- Falta: dois jogadores de verdade numa pista de 4, e conferir na Steam real se o snapshot de ~2 KB (sem confiabilidade) chega bem (ver 24.6).

## 28. Jogo em 3D (branch `jogo-3d`, experimento)

Estilo **brinquedo low-poly**, pensando na Steam (sem a restrição do celular). A física não muda: o 3D é só a camada visual. A interface e as cores continuam as mesmas.

### 28.1 Pista com volume (`TrackPieceView`)

A malha continua gerada por código a partir da lista de peças, agora com corpo. Medidas no mundo (na pista de 4, a malha é em células e a raiz tem escala 2, então dividem por `TrackMath.Scale`):

- **Base** com espessura `Thickness = 0,09`: laterais, fundo (só nas peças no alto) e tampas nas pontas (`AddCaps`), no submesh 3 (`TrackTheme.side` / `side4`: plástico escuro; sem eles, o material dos pilares).
- **Fendas afundadas** `SlotDepth = 0,035`: o tampo é cortado em faixas entre as fendas; cada fenda tem duas paredes e um fundo. O `u` da textura continua sendo o deslocamento lateral (`o + 0,5`), então a pintura fica no lugar; paredes e fundo usam o `u` do meio da fenda (preto na textura). Meia largura da fenda: 0,022 (2 trilhos) e 0,011 célula (4 trilhos).
- **Cruzamento:** as fendas se cruzam, então o tampo fica liso (fenda só pintada).
- **Muro com corpo** (topo e face de dentro; a face de fora emenda na lateral da base): nas peças no alto, dos dois lados (0,10 de altura; 0,12 na pista de 4), e no **lado de fora das curvas inclinadas** (0,14).
- `AddBand(tri, s, ts, a, b, vScale, y)`: faixa entre duas bordas (`Edge`: deslocamento, altura sobre a superfície e `u`). Indo de `a` para `b` para a direita a face olha para cima; para baixo, olha para a direita; para cima, para a esquerda; para a esquerda, para baixo. Cada faixa tem os próprios vértices (quinas vivas).
- Como a base desce 0,09, o **tampo da mesa fica em y = −0,10** e os carrinhos continuam correndo no nível 0. Os pilares e a célula sob o mouse acompanham.

### 28.2 Mesa (`GridBoard`)

Tampo de madeira com moldura (0,7 além da grade, 0,5 de espessura), quatro pés e um chão claro 9 unidades abaixo, feitos com cubos e cópias do material da mesa sem a textura. Só são refeitos quando a mesa muda de tamanho.

### 28.3 Carrinho 3D (`CarView`)

- **Provisório**, montado com cubos e cilindros: assoalho, capô, traseira, cabine escura, teto, faixa e bico na segunda cor, aerofólio sobre dois apoios e quatro rodas. Mesma caixa da física (0,60 × 0,32), frente em +X, chão em y = 0.
- **Duas cores:** a principal é a do trilho ou a do piloto (`SetColors`); a segunda é branca, ou escura quando a principal é clara. Substitui a pintura `CarTexture` do carrinho chapado.
- **Movimento:** rodas giram com a velocidade; o carrinho inclina na rampa (pitch), **deita na curva inclinada** (`RaceSim.CarBank`, de `LanePoint.bank`: o ângulo lateral da superfície) e **capota** ao sair da pista. A derrapagem (traseira saindo) já vinha da pose da física.
- Modelo escolhido para substituir o provisório: https://poly.pizza/m/EMcTJiXibv. O site é bloqueado no navegador do assistente: o arquivo (GLB ou FBX) precisa ser baixado à mão e colocado em `Assets/Autorama/Modelos/`.

### 28.4 Teste

Play mode, 8 com ponte, câmera perto do carrinho azul (`docs/previa-3d.png`): fendas afundadas, curva inclinada com muro, ponte com pilares, mesa com moldura e os dois carrinhos com volume e sombra. Corrida rodando normalmente (a física é a mesma).

### 28.5 Zebra em relevo e pórtico de largada

- **Zebra em relevo** no lado de fora das curvas planas, onde a textura já pinta a zebra: sobe em chanfro (0,012 de largura), fica plana a 0,022 de altura e desce na borda. Vai de 0,40 a 0,48 do centro (0,43 a 0,475 na pista de 4). Nas peças no alto o muro ocupa o lugar.
- **Pórtico** sobre a bandeirada (`BuildGantry`, na peça de largada): dois postes fora da pista (0,09 além da borda), travessa quadriculada a 0,95 de altura e um **semáforo de três luzes**. `TrackPieceView.SetStartLights(vermelhas, verde)` acende as luzes (cor e emissão) junto com as da tela: 1, 2, 3 vermelhas na contagem e as três verdes no primeiro segundo de cada largada ou manche.
- Teste (Play mode, construtor, oval): `docs/previa-3d-largada.png` (2 carros) e `docs/previa-3d-pista4.png` (4 carros). Zebra com volume nas curvas, pórtico com as três luzes, carrinhos no grid, seta de encaixe e célula sob o mouse no lugar. A pista de 4 usa o mesmo código, com as medidas divididas pela escala.

### 28.6 Próximos passos

1. Trocar o carrinho provisório pelo modelo.
2. Sala em volta da mesa (ProBuilder), luz mais quente e pós-processamento leve.
3. Ajustar as câmeras 3D e "Atrás do carro" para o novo visual (não foram vistas em movimento: o Editor estava em segundo plano).

## 29. Som (branch `jogo-3d`)

Todos os sons são **sintetizados em tempo de execução** (`RaceController.Audio.cs`), sem arquivos de áudio: `AudioClip.Create` + `SetData`, 44,1 kHz, mono. A cena não tinha `AudioListener`; `AudioStart` põe um na câmera.

| Som | Como é feito | Quando toca |
|---|---|---|
| **Motor** (um por carrinho, em laço) | Silvo agudo tipo broca de dentista: 1400 e 1409 Hz batendo entre si, mais 2800, 4200 e 5609 Hz e um chiado de ar leve; sem grave (a 1ª versão era um zumbido de 90 Hz, que não agradou) | Volume `(0,25 + 0,75·T) × min(1, 6·v/vmax + T)`; **tom** `0,55 + 1,9 · √(v/vmax)` (× 1,03 por trilho): de ~770 Hz parado a ~3,4 kHz no fundo. 0,15 por carro (0,10 na pista de 4) |
| **Pneu** (um por carrinho, em laço) | Ruído num ressonador de dois polos em 2,2 kHz, pontas cruzadas | Quando a derrapagem passa de 25%: volume `((slip − 0,25)/0,75)² × 0,5` |
| Bipe | 520 Hz, 0,2 s | A cada luz vermelha da contagem (também nas manches) |
| Largada | 1040 Hz + oitava, 0,55 s | Luz verde |
| Batida | Ruído + grave descendo, 0,22 s | `sim.Spark` (primeiro toque de cada par) |
| Saída de pista | Três estalos de "plástico quicando", 0,6 s | Quando `offs` de um carrinho sobe |
| Volta | Dois tons (880 → 1320 Hz), 0,35 s | `sim.LapDone` (mais baixo para os robôs) |
| Troca de trilho | 660 → 440 Hz, 0,5 s | Entrada na fase `Switch` (corrida por tempo) |
| Vitória | Dó, mi, sol, dó, 1,1 s | Quando `winner` aparece |

- **Volume:** `RaceSettings.volume` (0 a 100, padrão 70), controle "Som" nos Ajustes da aba Corrida. Fora das abas de corrida fica tudo mudo.
- `AudioTick` roda em todo `Update` (corrida livre, campeonato e online).
- Os laços ficam sempre tocando, com volume 0 quando não há o que ouvir; se a Unity parar algum, `AudioTick` religa.

### 29.0 Opções de motor

O timbre do motor não agradou nas duas primeiras versões (zumbido grave, depois o silvo de broca) e não dá para o assistente ouvir. Por isso o motor virou uma **escolha**: nos Ajustes da aba Corrida, o botão **Som do motor: … ▸** alterna as opções, inclusive no meio da corrida (`RaceSettings.motorSound`, `NextMotor`, `MotorSound { clip, p0, p1, vol }`; tom = `Lerp(p0, p1, √(v/vmax))`).

| Opção | Receita | Tom (parado → fundo) |
|---|---|---|
| Broca | 1400 + 1409 Hz batendo, 2800, 4200, 5609 Hz e chiado leve | 0,55 → 2,45 |
| Elétrico suave | 420 Hz quase puro, com um pouco de 840 e 1260 Hz | 0,6 → 2,6 |
| Escovas | Dente de serra de 170 Hz (9 harmônicos) modulado em 340 Hz, com "areia" | 0,6 → 2,4 |
| **Turbina** (padrão, a escolhida; volume 0,4, quase metade do primeiro ajuste) | Chiado de ar forte com silvo de 2100 + 2112 Hz | 0,6 → 2,0 |
| Ronco | Dente de serra de 70 Hz (6 harmônicos) com 35 Hz | 0,7 → 2,8 |
| Seu arquivo | `persistentDataPath/autorama/sons/motor.wav` (PCM 16 bits, mono ou estéreo), lido por `LoadWav`; só aparece se o arquivo existir | 0,6 → 2,0 |

Para a gravação: um trecho curto (1 a 3 s) de motor em rotação constante, que emende bem em laço.

Teste: as 5 opções carregam, alternam em sequência e ficam tocando (pico entre 0,62 e 0,89). O `motor.wav` não foi testado (não havia arquivo).

### 29.1 Teste

Play mode, `script eval` (os números, não o ouvido: **ninguém ouviu ainda**):

- 1 `AudioListener`; os 9 clipes existem com as durações da tabela, pico entre 0,34 e 1,00, e todos começam e terminam em 0 (sem estalo). O primeiro teste achou o som de saída de pista começando em −0,40 e a vitória terminando em 0,08: corrigidos com um ataque de 3 ms e uma queda de 50 ms.
- Corrida no oval com dois robôs (qualidade 90 e 30): motor do carro 1 com volume máximo 0,168 e tom de 0,70 a 2,74; pneu com volume máximo 0,288; 1 saída de pista; efeitos tocando no fim (vitória). No grid: motor e pneu em 0.
- Os laços não estavam tocando no primeiro teste (`isPlaying` falso); com o religar no `AudioTick`, motor e pneu aparecem tocando.

### 29.2 O que falta

1. **Ouvir e ajustar o timbre** (principalmente o motor e o pneu).
2. Som nos menus e no construtor (clique, encaixe de peça), música.
3. Som posicional (3D) seguindo cada carrinho.

## 30. Sugerir pistas ao host (online)

No lobby, qualquer visitante pode **sugerir uma das próprias pistas** ao host; o host decide. Vale no servidor Node e na Steam, com as mesmas regras.

### 30.1 Fluxo

- **Visitante** (seção "SUGERIR UMA PISTA AO HOST", em ETAPAS): lista as pistas dele que são **circuitos fechados**; se o host já escolheu o tipo (2 ou 4 carros), só as desse tipo. Clicar manda a pista e marca "enviada ✓" (cada pista só uma vez por sala).
- **Host** (seção "SUGESTÕES DOS PILOTOS"): cada sugestão mostra miniatura, nome, "de Fulano" e três botões: **Incluir** (vira etapa, com a mesma regra de tipo de qualquer etapa: se for de 4 e o campeonato é de 2, recusa e a sugestão fica), **Salvar** (guarda na biblioteca do host, sem sobrescrever: se já há uma pista com o nome, grava "Nome (de Fulano)") e **Ignorar**. Também aparece um aviso: "Beto sugeriu a pista “…”".
- As sugestões existem só no lobby, na memória do host; saem ao largar, ao sair da sala ou ao entrar numa sala.

### 30.2 Mensagens

- Visitante → host: `{t:"suggest", track, hash, faixas, json}` (`NetOut`). O servidor (ou a Steam) repassa só ao **host**, como `{t:"suggested", from, name, color, track, hash, faixas, json}` (`NetMsg`). O remetente (`from`, `name`, `color`) vem do servidor ou da Steam, **nunca do texto**.
- Servidor Node (`suggest`): só no lobby; o host não sugere para si; `hash` com 8 hexadecimais; `json` até 100 KB; **3 sugestões por minuto** por jogador; `faixas` diferente de 4 vira 2; o nome é limpo (`clean`, 60 caracteres).
- Steam (`Suggest` no visitante, `case "suggested"` no host): as mesmas regras, refeitas **dos dois lados** (o host não confia no visitante); mensagem direta confiável ao dono do lobby.

### 30.3 O que o host confere (`OnSuggested`)

Nada que veio da rede é confiado: o JSON é lido, a pista precisa ser **um circuito fechado**, `Champ.TrackHash` tem que **bater** com o `hash` dito, e valem os limites **12 sugestões no total**, **3 por jogador**, sem repetir a mesma pista do mesmo jogador e sem sugerir o que já é etapa. JSON inválido é ignorado em silêncio. O nome vem com `Safe` (sem rich text).

### 30.4 Testes

- `servidor`: `npm test` ganhou 6 verificações (chega ao host com quem mandou e o nome limpo; não volta ao visitante; host não sugere para si; identidade inválida; grande demais; tipo desconhecido vira 2; 3 por minuto).
- `SteamTransportTest` (mundo falso, 3 jogadores): as mesmas 7 verificações, passando; a parte da Steam de verdade falhou no "criar sala" (esta máquina), como antes de mudar o código.
- Play mode, host simulado (`OnSuggested`, `AcceptSuggestion`, `SaveSuggestion`): sugestão válida entra; repetida, de identidade errada, pista aberta e lixo no lugar do JSON são recusadas; 4 formas diferentes do mesmo jogador ficam em 3, e a mesma forma de outro jogador entra; a lista do host desenha uma linha por sugestão; incluir pista de 4 num campeonato de 2 é recusado e, depois de escolher 4 carros, entra como etapa e sai da lista; salvar grava na biblioteca (o arquivo de teste foi apagado).
- **Não testado:** a tela com dois jogadores reais, e a Steam de verdade.
