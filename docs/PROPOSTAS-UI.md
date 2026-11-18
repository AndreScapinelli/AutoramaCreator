# Propostas para a interface

> **Situação (18/11/2026, branch `melhorias-ui`):**
> - **Feitas:** A, C, G; em D, o kit de ícones, a dica própria e o "?"; B em versão de painel em tela cheia com cartões; F com a torre, os painéis compactos e o minimapa justo; E com a paleta em abas embaixo.
> - **Faltam:** a fonte de display própria, a mesa de madeira, o resultado com pódio animado e a navegação completa por controle.
> - Detalhes na seção 84 do `GUIA-UNITY.md`.

Levantamento de 18/11/2026, feito no código (`UI/Construtor.uss`, `BuilderPanel.cs`, `RaceController*.cs`) e em capturas do Play a 1280×720. Os protótipos visuais das propostas estão em `docs/propostas-ui.html` (abre no navegador).

## 1. O que temos hoje

| Item | Situação |
|---|---|
| Tecnologia | UI Toolkit, **100% montada em C#** (nenhum UXML). Uma folha só, `Construtor.uss` (707 linhas, ~180 classes), e o tema padrão da Unity (`TemaRuntime.tss`). |
| Escala | `PainelConstrutor.asset`: escala pela tela, referência 1600×900, casando pela altura. Em 720p tudo sai a 80%: o texto de 10 px vira 8 px. |
| Cores | 27 variáveis em `:root`, mas ~40 `rgb(...)` soltos no meio das regras e ~280 `style.*` direto no C#. Cada cor de trilho (a–h) se repete em 5 grupos de classes. |
| Navegação | Menu inicial (cartão por cima do construtor) → barra lateral de 340 px com 4 abas (Construtor · Corrida · Campeonato · Online). A carreira tem telas próprias, em tela cheia, com o painel escondido. |
| Telas | Menu inicial, Opções/teclas, Peças; barra lateral do construtor (8 seções); aba Corrida (6 seções + 2 sanfonas); Campeonato; Online (lobby, partida rápida, chat, amigos); HUD da corrida (fichas, relógio, narração, painéis dos jogadores, minimapa, câmera, etiquetas F1, contagem, resultado, pausa); carreira (saves, mapa com pinos, cartão da prova, objetivos, resultado, garagem, moedas). |
| Ícones | Glifos Unicode no texto (`▦ ⚑ ▶ ★ ◉ ⌂ ◀ ☰ ■`), além dos ícones das peças (texturas). |
| Dicas | 39 `tooltip` no código. |

## 2. O que está atrapalhando

Em ordem do que mais pesa para quem joga.

1. **Parece um editor, não um jogo.** Correr, campeonato e online são configurados dentro da barra do construtor, embaixo de "CONSTRUTOR DE PISTA", em 4 abas apertadas (texto de 10 px). O menu inicial abre por cima do construtor, com a paleta e as réguas visíveis atrás. A carreira, que já tem telas próprias, parece outro jogo.
2. **Texto pequeno e demais.** Quase toda seção abre com um parágrafo de explicação (`hint`), e os atalhos são um bloco de texto. Boa parte do texto fica entre 10 e 12 px, que a 720p cai para 8–10 px. A barra lateral rola várias telas.
3. **Sem hierarquia.** Toda seção tem o mesmo título amarelo e a mesma linha. O amarelo marca título, botão escolhido, botão de ação, líder, estrela, moeda e recorde, e por isso não diz mais nada. Há dois "botões principais": o branco (`btn--primary`) e o amarelo (`btn--accent`).
4. **Controles misturados.** Os nossos (`.btn`, segmentados) convivem com os padrão da Unity sem estilo: lista suspensa branca, caixa de seleção branca, slider, sanfona e barra de rolagem cinza (dá para ver na aba Online). A barra lateral tem barra de rolagem horizontal sem motivo.
5. **Ícones que não dizem nada.** `⚑` (Carreira) sai como um quadrado: a fonte não tem o glifo. A barra de ações da mesa (`▶ ↶ ↷ ≠ □ ✕ □ 3D − +`) não se explica. As 39 dicas (`tooltip`) não aparecem no jogo: o UI Toolkit só as desenha no Editor (confirmar num build).
6. **Erros visuais que se notam.**
   - Na corrida, a régua de cima do construtor aparece no canto superior direito, com os números encavalados. `BuilderPanel.LateUpdate` mostra a régua quando `cam.IsTopView`, sem olhar se está correndo.
   - O título "CONSTRUTOR DE PISTA" quebra em duas linhas.
   - O selo "Online: crie uma sala ou entre numa" sai cortado na borda do painel.
   - No menu, 5 cartões em grade de 2 deixam um buraco.
   - O minimapa tem uma área vazia grande e fica em cima da pista.
7. **HUD pesado.** Os painéis dos jogadores ocupam o quarto de baixo da tela e misturam estado e tecla ("ACELERAR A · 60% S"). A lista de pilotos do minimapa repete as fichas do topo.
8. **Nada se mexe.** Nenhuma `transition` na USS: o hover só troca a cor da borda, as telas aparecem e somem de uma vez, sem som de clique.
9. **Sem identidade.** A paleta verde-escura e a mesa cinza-clara lembram um programa de escritório. Não tem nada de autorama (pista, bandeira quadriculada, pintura de carro, cores de brinquedo).

## 3. Propostas

Cada uma diz o quê, por quê e como fazer na Unity. Esforço: **P** (até 1 dia), **M** (2–4 dias), **G** (1 semana ou mais).

### A. Consertos rápidos (P, fazer antes de tudo)

- Esconder as réguas, o selo de célula e a escala fora do construtor (`LateUpdate`: `top && !chromeHidden && mode == Mode.Build`).
- Trocar `⚑` por um glifo que a fonte tem, ou por ícone (ver D).
- Tirar a rolagem horizontal da barra lateral (`horizontalScrollerVisibility = Hidden`).
- Título da barra pelo modo ("CONSTRUTOR", "CORRIDA"…), numa linha só; selo de status com quebra de linha.
- Menu com 6 cartões (Peças entra na grade) ou 3 grandes + 2 menores.
- Corpo de texto com **13 px no mínimo**, e 11 px só para rótulos em maiúsculas.
- Estilizar no TSS os controles padrão que aparecem: `DropdownField`, `Toggle`, `Slider`, `Foldout`, `Scroller`.

### B. Separar o jogo do editor (G, a maior mudança)

Hoje o jogo inteiro mora na barra do construtor. Proposta:

- **Menu inicial em tela cheia**, sem o construtor atrás: a pista atual girando devagar em 3D, desfocada (câmera orbital que já existe), o logo e 6 cartões grandes.
- **Cada modo vira uma tela de preparação em tela cheia**, no mesmo molde das telas da carreira (`k-panel`), em passos: *Pista → Pilotos e carros → Regras → Largar*. A coluna da esquerda mostra os passos; o centro, o passo aberto; a direita, um resumo fixo com o botão **LARGAR**.
- A barra lateral fica **só para o construtor**. Saem as 4 abas; o caminho é menu → modo → voltar (Esc). O construtor ganha "Testar" e "Correr nesta pista", que levam à preparação da corrida.
- O que já existe se reaproveita: os blocos `raceBox`, `champBox` e `onlineBox` mudam de pai (saem da barra e vão para a tela da preparação) sem refazer a lógica.

### C. Sistema visual: tipo, cor e superfícies (M)

- **Escala de tipo com 5 tamanhos:** 40 (logo), 24 (título de tela), 17 (título de seção), 14 (corpo), 12 (rótulo em maiúsculas com espaçamento). Uma fonte de display condensada e itálica (estilo placar de corrida) para títulos e números do HUD, com algarismos tabulares nos tempos.
- **Papéis de cor:**
  - amarelo só para a ação principal e o foco;
  - branco para títulos;
  - verde para ok, laranja para alerta, roxo para a melhor volta;
  - dourado (outro tom) para estrela e moeda.
  O botão branco `btn--primary` sai: fica um primário só.
- **Três níveis de superfície** (`--sup-1/2/3`) no lugar das 6 variações de fundo e das 5 de borda. Os `rgb` soltos viram variáveis.
- **Cor de trilho pelo código:** uma propriedade customizada (`--cor-trilho`) posta no elemento pelo C#. As 40 regras `--a … --h` viram 5.
- **Mesa com mais personalidade:** fundo de madeira ou carpete escuro em vez do cinza-claro, para a pista colorida se destacar e a UI escura combinar.

### D. Componentes e ícones (M)

- Uma biblioteca pequena em C# (`UI/Kit.cs`): `Card`, `Segmented`, `Stepper`, `ListItem` (miniatura, título, sub, ações), `Section` (título e "?" que abre a explicação), `IconButton` (ícone e rótulo).
- **Ícones de verdade:** um conjunto de SVG (o UI Toolkit da Unity 6 lê vetor) ou um atlas de PNG gerado pelo `gen-texturas.js`, com o mesmo traço dos ícones das peças.
- **Dica própria:** um manipulador que mostra um balão depois de 400 ms parado em cima, já que o `tooltip` não aparece no jogo. As 39 dicas existentes passam a aparecer sem reescrever.
- Os parágrafos de `hint` vão para o "?" de cada seção. A lista de atalhos sai da barra e vira um painel na tecla **H** (os dados já estão em `AllKeys`).
- **Navegação por controle:** anel de foco visível (`:focus`) e ordem de foco em todas as telas, já que a corrida já aceita controle.

### E. Construtor (M)

- **Paleta embaixo, horizontal**, com ícones grandes e grupos em abas (Retas · Curvas · Inclinadas · Especiais), como em jogos de construção. Os números 1–0 aparecem no canto do ícone.
- **Painel da direita só com o contexto:** peça escolhida (girar, inverter, altura, remover) e, sem peça escolhida, o resumo da pista.
- Topo da mesa: nome da pista, status, **Fechar circuito (Q)**, **Testar** e **Correr**. "Minhas pistas" vira uma tela de biblioteca com miniaturas grandes.
- A barra de ações da mesa ganha rótulos ou dicas (desfazer, refazer, enquadrar, 3D, zoom).

### F. HUD da corrida (M)

- **Torre de posições à esquerda**, no estilo das transmissões: posição, cor, sigla, diferença. Substitui as fichas do topo e a lista do minimapa.
- **Topo central:** volta ou tempo grande, e a narração embaixo.
- **Painel do jogador compacto** num canto: acelerador como barra vertical com a marca da meia, turbo como anel ou barra curta, velocidade em número. A tecla só aparece no grid e durante a pausa.
- Minimapa no tamanho da pista (sem a faixa vazia), canto superior direito, sem a lista.
- Resultado em tela cheia com pódio animado em vez do banner.

### G. Movimento e som (P a M)

- `transition` nos botões e cartões: cor e `scale: 1.03` em 120 ms; telas entram com fade e um leve deslize (`opacity`, `translate`).
- Som curto de clique e de confirmação na UI (já existe `RaceController.Audio`).
- Números que contam (moedas, pontos, tempo no resultado).

## 4. Ordem sugerida

1. **Semana 1:** A (consertos) e C (tipo e cor) na USS atual. É barato e muda a primeira impressão.
2. **Semana 2:** D (kit, ícones, dica própria, "?") e G (movimento).
3. **Semanas 3–4:** B (menu e telas de preparação em tela cheia) e F (HUD).
4. **Depois:** E (construtor com a paleta embaixo).

Antes de começar, vale juntar as reclamações recebidas e marcar cada uma num item acima, para confirmar a ordem.
