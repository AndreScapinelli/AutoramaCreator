# Enredo da carreira: "A caixa do Tio Fê"

Roteiro para revisão antes de entrar no jogo. Acompanhamento da fase em `docs/carreira.html` (fase H).

## Premissa

Você acha na garagem da família uma caixa de sapato com um carrinho velho, um controle de lata e um bilhete: *"Para quando você tiver idade. — Fê"*. O Tio Fê sumiu quando você era pequeno e ninguém fala dele. Embaixo do carro, na placa do chassi, alguém riscou **F.B.**

O Tio Fê é **Felix Badar**, a lenda que ninguém passa há trinta anos. Ele saiu da cidade depois de brigar com o irmão, o Tio Beto, e correu o mundo com o nome de piloto. A carreira é o caminho até ele.

**Regra de ouro:** cada coisa que o jogo abre aos poucos chega como um objeto da história, entregue por alguém. Os controles travados valem **só na carreira**. Fora dela (corrida livre, campeonato, online, tutorial) tudo continua aberto.

## Elenco

| Personagem | Região | Quem é | Como fala |
|---|---|---|---|
| **Tio Fê / Felix Badar** | Global | O tio sumido; a lenda | Pouco e seco. Só fala do relógio. |
| **Tio Beto** | Garagem | Irmão do Fê. Ficou na cidade, ficou amargo | Resmunga, mas cuida. |
| **Zé da Oficina** | Garagem | O mecânico do bairro; amigo dos dois irmãos | Simples, prático, piadista. |
| **Lu Parafuso** | Garagem | Ajudante do Zé, da sua idade | Animada. |
| **Dona Célia** | Clube | A antiga melhor piloto do Clube; dupla do Fê nos anos 90 | Calma, de poucas palavras, afiada. |
| **Marina Faísca** | Clube → Nacional | A rival da sua geração | Esquentada, provoca, depois respeita. |
| **Rafa Turbo**, **Nina Pistão** | Clube → Nacional | Pilotos do Clube | Figurantes com uma frase cada. |
| **Dr. Curva** | Regional | O engenheiro; mede tudo | Professor, cita geometria. |
| **O Campeão** | Nacional | Perdeu para o Fê há décadas e nunca esqueceu | Orgulhoso. |
| **Ianzin** | Global | O único que anda junto com o Felix; ninguém sabe quem ganha entre os dois | Tranquilo, sorri pouco. |
| **Rios** | Global | O segundo melhor do mundo, cansado de ser o segundo | Competitivo, direto. |
| **SquadFun, Bilboque, Kojiro, Leector, Devair, Nebrown, Peripato** | Global | A turma do circuito mundial | Uma frase cada, em ceninhas de largada. |
| **Sofia, Mateo, Aiko** | Global | Lendas estrangeiras | Uma frase cada. |

## Objetos (o que abre)

| Id | Objeto | Quem dá | Onde | Abre na carreira |
|---|---|---|---|---|
| `controle-lata` | Controle de lata do Fê | (a caixa) | início | Acelerar e recolocar |
| `gatilho-duplo` | Gatilho de dois estágios | Zé da Oficina | `garagem-oficina` (1★) | **Meia aceleração** |
| `lata-nitro` | A lata de nitro do Fê | Lu Parafuso | `garagem-8` (1★) | **Turbo** |
| `caixa-grande` | A caixa grande do Fê | Tio Beto | `garagem-9` (vencer) | Carta 1 aberta; Coupé GT (já existe) |
| `controle-celia` | Controle da Dona Célia | Dona Célia | `clube-taca` (★★, campeã) | **Ajuste fino** (meia − e meia +) |
| `caderno-curva` | Caderno do Dr. Curva | Dr. Curva | `regional-drcurva` (vencer) | **Parciais e fantasma** dentro da carreira |
| `foto-96` | A foto de 1996 | O Campeão | `nacional-campeao` (vencer) | A revelação; nada de controle |
| `carro-fe` | O carro do Fê | Felix Badar | `global-mundial` (★★) | Bolha Fórmula e mesa sem limite (já existe) |

A garagem do carro ganha o mesmo motivo, sem mudar a regra: o Zé abre motor e pneu (Garagem), a Dona Célia abre bolha e chassi (Clube), o Dr. Curva abre o turbo (Regional).

**Provas que pedem um objeto.** Hoje `clube-chuva` e as provas seguintes não exigem nada. Com o enredo, nenhuma prova pode exigir um controle que o jogador ainda não ganhou: o ajuste fino só chega no fim do Clube, então nenhuma estrela do Clube pode depender dele (as notas atuais já não dependem).

## Cenas

Formato: quadrinho por cima do mapa 3D, retrato e balão, 2 a 4 falas, Enter avança, "Pular" fecha. Cada cena aparece uma vez por save (`vistas` no save); o Álbum mostra de novo.

Gatilhos: `regiao` (entrar na região pela 1ª vez), `antes` (abrir o cartão do nó pela 1ª vez), `depois` (1ª vez que o nó chega às estrelas pedidas), `item` (junto do objeto).

### Garagem

**G0 · A caixa** (`regiao`, garagem)
> *(narração)* A caixa estava atrás das latas de tinta, com o seu nome escrito a caneta.
> *(narração)* Dentro: um carrinho vermelho, um controle de lata e um bilhete. "Para quando você tiver idade. — Fê."
> **Lu Parafuso:** Fê? O seu tio que sumiu? O Zé conhece ele. Vamos lá na oficina!

**G1 · Primeira volta** (`antes`, garagem-1)
> **Zé da Oficina:** Esse controle aí só tem acelerar. Ou vai, ou para. Solta antes da curva, não dentro.
> **Zé da Oficina:** Saiu da pista? Recoloca e segue. Todo mundo sai.

**G2 · O gatilho** (`item`, gatilho-duplo · garagem-oficina)
> **Zé da Oficina:** Abri o seu controle. O Fê tinha soldado um segundo estágio aqui dentro e desligado. Liguei.
> **Zé da Oficina:** Agora tem **meia aceleração**: segura no meio e o carro passa na curva sem soltar.

**G3 · A lata** (`item`, lata-nitro · garagem-8)
> **Lu Parafuso:** Achei isso no fundo da caixa. Tá escrito "só nas retas".
> **Lu Parafuso:** É **turbo**! Enche um pouco a cada volta. Quem está atrás ganha mais.

**G4 · Tio Beto** (`antes`, garagem-9; substitui a `rivalFrase`)
> **Tio Beto:** Esse carro é do Fê. Onde você achou isso?
> **Tio Beto:** Ele largou tudo aqui e foi embora. Monto autorama desde antes de você nascer. Mostra o que sabe.

**G5 · A caixa grande** (`depois`, garagem-9)
> **Tio Beto:** ...Você freia igualzinho a ele.
> **Tio Beto:** Tem mais uma caixa dele no sótão. Nunca tive coragem de abrir. Fica com ela.
> *(narração)* Na caixa grande: cartas, um carro azul de turismo e um recorte de jornal rasgado no nome.

**G6 · Campeão da Garagem** (`depois`, garagem-copa)
> **Zé da Oficina:** A garagem ficou pequena. O Clube do centro tem pista de quatro carros.
> **Zé da Oficina:** Procura a Dona Célia. Se alguém sabe do Fê, é ela.

### Clube

**C0 · O salão** (`regiao`, clube)
> **Rafa Turbo:** Novato com carro velho? A Marina vai te engolir.
> **Marina Faísca:** Turbo é para usar. Vem que eu largo na sua frente.

**C1 · Dona Célia** (`antes`, clube-1)
> *(narração)* No fundo do salão, uma senhora lustra um controle preto com fita no gatilho.
> **Dona Célia:** Esse chassi... Faz muito tempo que não vejo esse carro.
> **Dona Célia:** Corre primeiro. Depois a gente conversa.

**C2 · Marina** (`depois`, clube-marina)
> **Marina Faísca:** Ok, ok... você freia melhor que eu.
> **Marina Faísca:** Na Taça Clube eu te pego. E a Dona Célia só dá o controle dela pra quem ganha a Taça.

**C3 · O controle da Dona Célia** (`item`, controle-celia · clube-taca)
> **Dona Célia:** Seu tio e eu corremos em dupla seis anos. Ele era rápido; eu era precisa.
> **Dona Célia:** Este controle tem o **ajuste fino**: muda a meia aceleração no meio da volta. Para cada curva, um ponto.
> **Dona Célia:** Ele foi embora numa noite e nunca mais escreveu. Se você achar ele, diz que eu guardei o lugar dele.

### Regional

**R0 · O ginásio** (`regiao`, regional)
> **Dr. Curva:** Seis faixas, salto, curva em C. Aqui sorte não vale nada.
> **Dr. Curva:** Curva inclinada não é sorte, é geometria.

**R1 · A foto na parede** (`antes`, regional-meio)
> *(narração)* No corredor do ginásio, uma foto: Dona Célia jovem, e ao lado, um piloto de boné com o rosto riscado.
> **Marina Faísca:** Quem risca a cara de alguém numa foto?

**R2 · O caderno** (`item`, caderno-curva · regional-drcurva)
> **Dr. Curva:** Brilhante. A geometria está do seu lado.
> **Dr. Curva:** Fica com o meu caderno: **parciais** de cada trecho e o **fantasma** da sua melhor volta.
> **Dr. Curva:** Eu medi um piloto assim uma vez. Ele também freava tarde. Também era um "F.B.".

### Nacional

**N0 · O estádio** (`regiao`, nacional)
> **Marina Faísca:** Oito carros, loop, salto grande. A gente chegou.
> **Nina Pistão:** O Campeão não perde aqui há quinze anos.

**N1 · O Campeão** (`antes`, nacional-campeao)
> **O Campeão:** Muitos chegaram até aqui. Nenhum passou.
> **O Campeão:** Esse carro... eu perdi pra um carro igual a esse. A única vez.

**N2 · A foto de 1996** (`item`, foto-96 · nacional-campeao)
> **O Campeão:** Então é você. A mesa é sua.
> **O Campeão:** Final de 96. Ele me passou na última curva e nunca mais correu aqui. Foi correr lá fora.
> *(narração)* Na foto inteira, sem risco: o piloto de boné segura a taça. Embaixo, a assinatura: **Felix Badar**.
> **O Campeão:** Felix Badar. F.B. Não sabia que ele tinha família.

**N3 · O convite** (`regiao` do convite; `antes`, nacional-convite)
> *(narração)* Um envelope sem remetente: "Convite: Felix Badar". Dentro, só uma frase.
> **Felix Badar:** *(bilhete)* Eu não corro contra você. Corro contra o relógio.

### Global

**W0 · O mundo** (`regiao`, global)
> **Rios:** Mais um que veio atrás do Felix. Entra na fila: eu estou nela há dez anos.
> **SquadFun:** Ninguém passa ele há trinta anos. Ninguém nem sabe onde ele mora.
> **Ianzin:** Eu sei. E empato com ele quase sempre. Quase.

**W1 · Frente a frente** (`antes`, global-felix)
> *(narração)* Ele tira o boné. É o rosto da foto, trinta anos depois.
> **Felix Badar:** Achou a caixa, então.
> **Felix Badar:** Não vim conversar. Vim correr. Vence a série e a gente conversa.

**W2 · Depois da série** (`depois`, global-felix)
> **Felix Badar:** Você solta antes da curva. Eu te ensinei isso numa carta, não foi?
> **Felix Badar:** Fui embora porque o Beto queria a garagem e eu queria o mundo. Fiquei tempo demais.
> **Felix Badar:** Falta o Mundial. Lá eu não vou te deixar passar.

**W3 · O carro do Fê** (`item`, carro-fe · global-mundial)
> **Felix Badar:** Faz trinta anos que ninguém me passa. A mesa agora é sua.
> **Felix Badar:** Leva o meu carro. E leva um recado pra Dona Célia e pro Beto: eu volto pro Natal.
> *(narração)* Construtor sem limite. Bolha Fórmula aberta em todos os modos.

**Epílogo** (`depois`, global-mundial, depois de W3)
> *(narração)* Na Garagem, o Tio Beto pendurou a foto de 96 na parede. Sem risco.
> **Tio Beto:** Pista nova no Natal. Grande. Monta você.

## Cartas do Fê

Uma por região, ganha com **★★★** na prova de relógio do meio (a volta perfeita é a obsessão dele). Cada carta tem uma dica real e uma pista do mistério. Ficam no Álbum.

| Carta | Prova | Dica | Pista |
|---|---|---|---|
| 1 · "Solta antes" | `garagem-6` Serpentina | Solte o gatilho antes da curva, não dentro dela. | "O Beto nunca entendeu isso." |
| 2 · "Meia é meia" | `clube-6` Inclinado Duplo | Na inclinada, a meia aceleração segura mais que o gatilho todo. | "A Célia ganhava assim." |
| 3 · "O ar" | `regional-6` Salto e Ponte | Chegue no salto rápido o bastante para voar, não para pousar mal. | Carimbo do correio: Ginásio Regional, 1994. |
| 4 · "Turbo é resto" | `nacional-6` Torre | Guarde o turbo para a última reta. | "Ganhei a final de 96 com meio tanque." |
| 5 · "O relógio" | `global-6` Viaduto Infinito | Compita contra a sua melhor volta, não contra os outros. | Assinada: "Felix — o seu Tio Fê." |

## Telas

1. **Cena** (`.k-cena`): quadrinho por cima do mapa, retrato à esquerda, balão, nome em cima, Enter avança, Esc pula a cena. Retratos: um desenho simples por personagem (cor do piloto no `carreira.json` + silhueta), sem arte nova no começo.
2. **Álbum** (botão na tela do save e no mapa): objetos (os que faltam em silhueta), cartas e cenas vistas, para reler.
3. **Resultado:** a linha "Abriu: …" ganha "Ganhou: controle da Dona Célia (ajuste fino)".
4. **Cartão do nó:** quando a prova entrega objeto ou carta, mostra "Entrega: …".
5. **Narrador (F2):** frases de contexto na carreira ("A Dona Célia está na arquibancada hoje").

## No código

- **`carreira.json`:** `"cenas"` na raiz: `{ "id", "quando": "regiao|antes|depois|item", "no", "falas": [{ "quem", "texto" }] }`, e `"item"` e `"carta"` nos nós.
- **`Progresso.Itens`** no molde de `Progresso.Pecas` e `Progresso.Modelos`: lidos dos saves, guardados, `Refresh` ao salvar.
- **Controles na carreira:** sem `gatilho-duplo`, a tecla Meia não faz nada; sem `controle-celia`, Meia − e Meia + não fazem nada e a meia fica no ajuste "Meia aceleração"; sem `lata-nitro`, o turbo fica desligado na prova. No ponto onde a entrada é lida (`GasLevel`, `HalfKeys`), só quando a corrida é da carreira.
- **Parciais e fantasma na carreira** só depois do `caderno-curva`.
- **Save:** `CareerSave.vistas` (ids das cenas já vistas). Save antigo: o que ele já ganhou conta (o objeto vem junto do nó já concluído); as cenas passadas não aparecem de novo, ficam no Álbum.
- **Idiomas:** as falas passam por `L.T`; escrever em português primeiro e traduzir no fim (`node ferramentas/idiomas.js`).
- **Rival:** a `rivalFrase`, `rivalVitoria` e `rivalDerrota` continuam valendo quando não há cena.

## Ordem sugerida

1. Objetos e trava dos controles na carreira (sem tela de cena): já muda o jogo.
2. Tela de cena e as cenas principais (G0, G4, G5, C3, N2, W1, W3).
3. Resto das cenas, cartas e Álbum.
4. Retratos e tradução.
