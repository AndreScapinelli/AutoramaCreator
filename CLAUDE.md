# Autorama Creator

Construtor de pista de autorama (slot car), com corrida, campeonato e campeonato online.

- **Projeto Unity** (`Assets/Autorama`): Unity 6000.3.6f1, URP 17, Input System, UI Toolkit. É a única versão do jogo.
- **`docs/GUIA-UNITY.md`**: o guia de como cada recurso funciona na Unity. Tem medidas, constantes, algoritmos e os testes feitos. Leia a seção do assunto antes de mexer.

O usuário escreve em português, e o projeto inteiro também: textos da interface, comentários, nomes de ids no JSON e o guia.

## Regras

- **A versão em HTML acabou.** O `html/index.html` foi removido; a última versão dele está na tag `html-final`. Não há mais nada a manter igual nem a portar para lá. Os textos antigos do guia que falam do HTML são histórico.
- **Formato e identidade não mudam.** Os ids do JSON de pista (`reta-curta`, `curva-longa-inclinada`, `cruzamento-curto`…) e a identidade de pista dos recordes (FNV-1a de `id:sentido:altura`; oval = `a34f4cb9`, 8 com ponte = `81546106`) continuam iguais, para não perder as pistas salvas e os recordes.
- **Sempre atualize `docs/GUIA-UNITY.md`** junto com qualquer recurso novo ou mudado: o que faz, constantes, código C# e os números do teste. Isso faz parte de terminar a tarefa.
- **Coordenadas.** No JSON de pista (formato herdado do HTML), y cresce para baixo e o ângulo cresce no sentido horário. Na Unity, o plano é XZ, com z = altura da mesa − y e ângulo = −h (a conversão fica no `TrackSave`). 1 célula = largura da pista (2 trilhos, ±0,25). Raio das curvas: 2,5.
- **Pistas de 4, 6 e 8 carros** (seções 25, 31 e 43 do guia): mesmas peças e ids, `"faixas": 4`, `6` ou `8` no JSON, geometria igual em células e **escala 2, 3 ou 4 no mundo** (`TrackMath.Scale`; o espaço entre trilhos é sempre 0,5 no mundo). Identidade com prefixo `f4|`, `f6|` ou `f8|`. Campeonato (offline e online) nunca mistura pistas de 2 e de 4; numa pista larga podem correr de 2 até o número de trilhos (trilho vazio, seção 27). Teclado só para 4 humanos: do 5º ao 8º jogam só de controle.
- **Física da corrida** (`RaceSim`): `ACCEL 3.5`, `BRAKE 1.8`, `BRAKE0 0.5`, `SLOPE_G 8`, `SLIP_GAIN 6`, `SLIP_DECAY 1.5`, curva inclinada × 1,7 de aderência. Mudou uma, mude no guia.
- **Turbo** (seção 37 do guia): tanque que enche a cada saída de pista (de 0,2 para o líder até 0,5 para o último e longe: metade pela posição, metade pela distância até o líder, cheia a meia volta), +25% de velocidade máxima, 3 s de tanque cheio. Volta com turbo não vale como melhor volta nem recorde. Teclas em `Race/Controles.cs` (A · S · Espaço · D), trocáveis em Opções no menu inicial (seção 36).
- **Loop e rampa de salto** (seção 42 do guia): ids `loop` (com `sentido`) e `salto`, só no chão. No loop o carrinho cai se `v²/raio` não segura o peso na parte de cima; no salto ele voa (`CarState.air`, gravidade `JumpG 29`), cai no vão se vier devagar e capota no pouso se vier rápido demais (`JumpHard 5`).
- O piloto IA não bate recorde da pista; só o melhor pessoal dele.

## Mapa da Unity (`Assets/Autorama/Scripts`)

| Pasta | O quê |
|---|---|
| `Core/` | Peças (`Pieces.cs`), geometria (`TrackMath`), montagem e análise (`TrackLayout`), JSON e biblioteca (`TrackSave`), fechar circuito (`TrackCloser`) |
| `Builder/` | `TrackBuilder` (lista de peças, desfazer, mesa que cresce), `BuilderPanel` (painel UI Toolkit e as 4 abas), `BuilderInput` |
| `Race/` | `RaceSim` (física pura, sem cena, mais a IA), `RaceController` (aba Corrida), `RaceController.Champ.cs` (aba Campeonato), `RaceController.Online.cs` (aba Online: lobby), `RaceController.OnlineRace.cs` (corrida online) |
| `Online/` | `OnlineProtocol.cs` (mensagens), `OnlineClient.cs` (WebSocket, fila de eventos), `NetSync.cs` (carrinho ↔ snapshot), `IOnlineTransport.cs`/`ILobbyBackend.cs` (as interfaces da rede), `Steam/` (Steamworks.NET: `SteamBoot`, `SteamLobbyClient`, `SteamBackend`, `SteamTransport`, `SteamLoopback`; seção 24) |
| `Champ/` | `ChampData.cs`: dados, rodízio, tabela, recordes, arquivos |
| `View/` | Malha das peças, grade da mesa, câmera |
| `Editor/AutoramaSetup.cs` | Menus **Autorama > Montar cena do construtor** e **Atualizar materiais e ícones** |
| `Editor/OnlineSmokeTest.cs` | Teste do cliente online contra o servidor de verdade (`script eval`) |
| `Editor/SteamSpacewarTest.cs`, `SteamTransportTest.cs`, `FakeLobbyBackend.cs` | Testes da camada Steam com o Spacewar (AppId 480; precisam da Steam aberta e logada) e do transporte com um mundo falso |

O servidor de salas do campeonato online fica em `servidor/` (Node, fora do `Assets`). Campeonato online: seções 23 e 41 do guia (protocolo 2: snapshot binário, sync, host que passa adiante, voltar de uma queda).

A cena é `Assets/Autorama/Cenas/Construtor.unity`. Os dados do jogo ficam em `Application.persistentDataPath/autorama/`: `pistas/`, `rascunho.json`, `pilotos.json`, `campeonatos.json`, `recordes.json` e `jogador.json` (o id fixo do jogador no online, seção 39 do guia).

## Como testar

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

## Economia de tokens

- **Arquivos:** leia antes de escrever; não releia o que não mudou. Prefira edições pontuais a reescrever arquivos. Evite ler arquivos grandes inteiros (cenas `.unity`, `.mat`, `docs/GUIA-UNITY.md`): procure com Grep e leia só a seção.
- **Respostas:** raciocínio completo, resposta curta. Sem abertura elogiosa, sem fecho de cortesia, sem emojis. Código primeiro; explique só o que o código não deixa claro.
- **Código:** a solução mais simples que funciona. Sem abstração para uso único, sem recurso especulativo, sem tratar erro que não acontece. Não acrescente comentários nem anotações em código que não está sendo mexido.
- **Depuração e revisão:** leia o código antes de formar teoria. Diga o problema, onde está e a correção, de uma vez. Se a causa não estiver clara, diga isso em vez de chutar. Sem sugestões fora do escopo.
- **Precisão:** não invente APIs, versões, flags, SHAs ou nomes de pacote; confira no código ou na documentação.
- **Terminar:** teste antes de dizer que acabou (veja "Como testar").
