# Autorama Creator

Construtor de pista de autorama (slot car), com corrida para 2 jogadores e campeonato. Existe em duas versões que precisam ficar iguais:

- **`html/index.html`**: a versão de referência, num arquivo só (SVG, sem dependências). Toda ideia nova nasce aqui.
- **Projeto Unity** (`Assets/Autorama`): Unity 6000.0.66f2, URP 17, Input System, UI Toolkit. É a mesma coisa portada.
- **`docs/GUIA-UNITY.md`**: o guia de como cada recurso funciona e de como fazer na Unity. Tem medidas, constantes, algoritmos e os testes feitos. Leia a seção do assunto antes de mexer.

O usuário escreve em português, e o projeto inteiro também: textos da interface, comentários, nomes de ids no JSON e o guia.

## Regras

- **HTML e Unity iguais.** Mesmas regras, constantes e física. Mesmos ids no JSON de pista (`reta-curta`, `curva-longa-inclinada`, `cruzamento-curto`…), então uma pista exportada num abre no outro. Mesma identidade de pista para os recordes (FNV-1a de `id:sentido:altura`; oval = `a34f4cb9`, 8 com ponte = `81546106`).
- **Sempre atualize `docs/GUIA-UNITY.md`** junto com qualquer recurso novo ou mudado: o que faz, constantes, código C# e os números do teste. Isso faz parte de terminar a tarefa.
- **Coordenadas.** No HTML, y cresce para baixo e o ângulo cresce no sentido horário. Na Unity, o plano é XZ, com z = altura da mesa − y e ângulo = −h. 1 célula = largura da pista (2 trilhos, ±0,25). Raio das curvas: 2,5.
- **Física da corrida** (os dois iguais): `ACCEL 3.5`, `BRAKE 1.8`, `BRAKE0 0.5`, `SLOPE_G 8`, `SLIP_GAIN 6`, `SLIP_DECAY 1.5`, curva inclinada × 1,7 de aderência. Mudou uma, mude na outra versão e no guia.
- O piloto IA não bate recorde da pista; só o melhor pessoal dele.

## Mapa da Unity (`Assets/Autorama/Scripts`)

| Pasta | O quê |
|---|---|
| `Core/` | Peças (`Pieces.cs`), geometria (`TrackMath`), montagem e análise (`TrackLayout`), JSON e biblioteca (`TrackSave`), fechar circuito (`TrackCloser`) |
| `Builder/` | `TrackBuilder` (lista de peças, desfazer, mesa que cresce), `BuilderPanel` (painel UI Toolkit e as 4 abas), `BuilderInput` |
| `Race/` | `RaceSim` (física pura, sem cena, mais a IA), `RaceController` (aba Corrida), `RaceController.Champ.cs` (aba Campeonato), `RaceController.Online.cs` (aba Online: lobby), `RaceController.OnlineRace.cs` (corrida online) |
| `Online/` | `OnlineProtocol.cs` (mensagens), `OnlineClient.cs` (WebSocket, fila de eventos), `NetSync.cs` (carrinho ↔ snapshot) |
| `Champ/` | `ChampData.cs`: dados, rodízio, tabela, recordes, arquivos |
| `View/` | Malha das peças, grade da mesa, câmera |
| `Editor/AutoramaSetup.cs` | Menus **Autorama > Montar cena do construtor** e **Atualizar materiais e ícones** |
| `Editor/OnlineSmokeTest.cs` | Teste do cliente online contra o servidor de verdade (`script eval`) |

O servidor de salas do campeonato online fica em `servidor/` (Node, fora do `Assets`). O campeonato online existe **só na Unity** por enquanto: o HTML não tem a aba Online (seção 23 do guia).

A cena é `Assets/Autorama/Cenas/Construtor.unity`. Os dados do jogo ficam em `Application.persistentDataPath/autorama/`: `pistas/`, `rascunho.json`, `pilotos.json`, `campeonatos.json` e `recordes.json`. No HTML ficam no `localStorage`, nas chaves `autorama-*-v1`.

## Como testar

- **HTML sem navegador:** `node ferramentas/teste-html.js meu-teste.js` roda a página no Node com um DOM falso. O teste vê todas as funções (`setMode`, `startRace`, `step`, `buildPaths`, `champBegin`…). Bom para física, regras e contas.
- **HTML no navegador:** abra `html/index.html`. O `localStorage` pode estar bloqueado em `file://`/`data:`, e a página precisa funcionar assim mesmo.
- **Unity:** com o Editor aberto, use o `unity` CLI (pacote `com.unity.pipeline`):
  - Online: `cd servidor && npm test` (servidor, sem Unity) e, com `node servidor.js` rodando, `script eval 'Autorama.OnlineSmokeTest.Run(); return Autorama.OnlineSmokeTest.Last;'` (cliente Unity + servidor). Com o Editor em segundo plano o Play mode não avança quadros: use `isPaused = true` + `EditorApplication.Step()` antes de capturar a tela.
  - `unity command script recompile`, depois `script recompile_status` até "completed";
  - `script eval '<C#>'` para rodar lógica sem cena: `RaceSim`, `Champ` e `TrackLayout` não dependem de cena;
  - `editor.playmode start|stop` e `capture game_view --save_path Assets/Temp/x.png` para ver a tela. Apague `Assets/Temp` depois.
- **No modo Play,** testes que gravam dados mexem nos arquivos reais do jogador: faça backup e restaure, ou avise.
- **Texturas:** são geradas por `node ferramentas/gen-texturas.js Assets/Autorama/Texturas`. Depois, use o menu **Autorama > Atualizar materiais e ícones**.

## Cuidados

- `JsonUtility` grava `null` como objeto vazio. Por isso `ChampStore.hasActive` e `Rec.t < 0` querem dizer "não tem".
- O `TrackBuilder` é `[ExecuteAlways]`. Fora do Play ele só mostra a pista; o rascunho só é salvo no Play e fora da corrida.
- Os carrinhos do campeonato usam uma pintura gerada em tempo de execução (`CarTexture`), o mesmo desenho de `gen-texturas.js`.
