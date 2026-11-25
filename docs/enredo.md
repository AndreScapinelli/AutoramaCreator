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

Gatilhos: `regiao` (entrar na região pela 1ª vez), `antes` (abrir o cartão do nó pela 1ª vez), `depois` (1ª vez que o nó chega às estrelas pedidas), `derrota` (a prova não deu estrela e o nó segue sem nenhuma), `item` (junto do objeto).

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

## Cenas da 2ª leva

Falas curtas entre as cenas principais: peça ou regra nova, pistas do Fê no meio das regiões, campeonatos, derrotas para os rivais (`derrota`) e a turma do Global. Ficam no `cenas.json` com as outras.

### Garagem

**g7-relogio** (`antes`, garagem-2)
> **Zé da Oficina:** Prova de relógio: ninguém na pista e nada de turbo. É você contra o cronômetro.
> **Zé da Oficina:** O Fê passava a tarde nessa bancada, batendo a própria volta.

**g8-chuva** (`antes`, garagem-4)
> **Lu Parafuso:** O Zé lavou o chão e a pista molhou junto. Hoje o pneu escorrega.
> **Lu Parafuso:** Freia mais cedo. E o carro é o da oficina: hoje não dá para mexer no ajuste.

**g9-atrasada** (`antes`, garagem-7)
> **Zé da Oficina:** Hoje você sai depois de todo mundo. Paciência: a corrida é longa.
> **Zé da Oficina:** O Beto fazia isso com o Fê de castigo. E o Fê ganhava assim mesmo.

**g10-copinha** (`antes`, garagem-copinha)
> **Zé da Oficina:** Campeonato: várias baterias, e cada um larga de um trilho. Vale o total, não uma corrida só.
> **Lu Parafuso:** Eu vou correr também. E não vou facilitar!

**g11-rei** (`depois`, garagem-rei)
> **Lu Parafuso:** Rei da Garagem! Vou pintar uma coroa no seu carro.
> **Zé da Oficina:** Pinta não, que o Fê me mata. ...Se ele voltar um dia.

**g12-beto-perde** (`derrota`, garagem-9)
> **Tio Beto:** Carro do Fê não faz piloto. Volta quando souber frear.
> **Lu Parafuso:** Ele está resmungando, mas ficou um tempão olhando o seu carro.

### Clube

**c4-viaduto** (`antes`, clube-3)
> **Nina Pistão:** Primeira vez no viaduto? Na subida o carro perde força; na descida, embala.
> **Nina Pistão:** Segura o gatilho na descida, ou a curva lá embaixo te joga fora.

**c5-chuva** (`antes`, clube-chuva)
> **Dona Célia:** Choveu no telhado do salão. Goteira na pista é tradição aqui.
> **Dona Célia:** Na pista molhada, quem freia cedo chega primeiro.

**c6-funil** (`antes`, clube-funil)
> **Rafa Turbo:** Viu a fechada? Duas faixas viram uma. Quem chega depois espera.
> **Rafa Turbo:** Ou bate. Eu prefiro que você bata.

**c7-elim** (`antes`, clube-elim)
> **Marina Faísca:** Sobrevivência: de tempos em tempos, o último sai. Até sobrar um.
> **Marina Faísca:** E quem sobra sou eu.

**c8-celia-copa** (`depois`, clube-copa)
> **Dona Célia:** Você solta antes da curva. Quem te ensinou isso?
> **Dona Célia:** ...Ele dizia a mesma coisa. Com essas palavras.

**c9-marina-perde** (`derrota`, clube-marina)
> **Marina Faísca:** Viu? Carro velho, piloto novo. Treina mais.
> **Dona Célia:** Ela freia tarde demais. Na próxima, espera ela errar.

**c10-convite** (`antes`, clube-convite)
> **Rafa Turbo:** O Convite é só para quem a Dona Célia chama. Ela chamou você?
> **Rafa Turbo:** Então tá. Aqui ninguém faz corpo mole.

### Regional

**r3-c** (`antes`, regional-3)
> **Dr. Curva:** Curva em C: raio pequeno, volta grande. A velocidade segura cai com o raio.
> **Dr. Curva:** Traduzindo: entra devagar e sai acelerando.

**r4-chuva** (`antes`, regional-chuva)
> **Marina Faísca:** Ziguezague molhado e carro emprestado. Quem inventou essa prova?
> **Dr. Curva:** Eu. Para medir quem pensa antes de acelerar.

**r5-salto** (`antes`, regional-6)
> **Dr. Curva:** Salto: devagar, você cai no vão. Rápido demais, o pouso rouba velocidade.
> **Dr. Curva:** Existe um número certo. Ache.

**r6-resist** (`antes`, regional-resist)
> **Nina Pistão:** Quinze voltas. Quem sai da pista toda volta não chega no fim.
> **Nina Pistão:** Guarda o turbo: vai ter muita reta.

**r7-adesivo** (`depois`, regional-mirante)
> *(narração)* Na mesa do mirante, um adesivo velho, quase apagado: "Recorde da pista — F.B., 1994".
> **Marina Faísca:** F.B. de novo. O mesmo da placa do seu carro. Esse cara passou por todo lugar.

**r8-curva-perde** (`derrota`, regional-drcurva)
> **Dr. Curva:** Você entrou rápido demais na terceira curva. Eu medi.
> **Dr. Curva:** Volte com outro número.

**r9-copa** (`depois`, regional-copa)
> **Marina Faísca:** Você ganhou a região. Vou para o Nacional também: nem pense que se livrou de mim.
> **Dr. Curva:** No estádio são oito faixas. Leve o meu caderno, e leve a cabeça.

### Nacional

**n4-loop** (`antes`, nacional-3)
> **Marina Faísca:** Um loop. De cabeça para baixo, quem segura o carro é a velocidade.
> **Marina Faísca:** Chega devagar e ele cai. Já aconteceu comigo.

**n5-parede** (`antes`, nacional-parede)
> **Nina Pistão:** Parede da Morte: a pista deita de lado. Devagar demais, o carro descola.
> **Nina Pistão:** E aqui cada saída conta. Poucas e você está fora.

**n6-serra** (`antes`, nacional-serra)
> **O Campeão:** A Serra molhada é a minha pista preferida. Ninguém aqui gosta de chuva.
> **O Campeão:** Por isso eu gosto.

**n7-resist** (`antes`, nacional-resist)
> **Zé da Oficina:** Vim de ônibus só para ver isso. Vinte voltas! Não queima tudo no começo.
> **Zé da Oficina:** A Lu mandou dizer que está torcendo pela tevê. E o Beto fingiu que não ouviu.

**n8-campeao-perde** (`derrota`, nacional-campeao)
> **O Campeão:** Quinze anos. Ninguém passa.
> **Marina Faísca:** Ele freia na mesma marca toda volta. Dá para usar isso.

**n9-gp** (`depois`, nacional-gp)
> *(narração)* No jornal do dia seguinte: "Piloto novo vence o GP com o carro de um campeão sumido".
> **Tio Beto:** Recortei a notícia. Não conta para o Zé que eu fiquei feliz.

### Global

**w5-autodromo** (`antes`, global-1)
> **Kojiro:** Primeira corrida no Mundial? Aqui ninguém tira o pé.
> **Leector:** Eu tiro. Na curva. Por isso ainda estou na pista.

**w6-recordes** (`antes`, global-2)
> **Devair:** Reta dos Recordes. A melhor volta daqui é do Felix faz vinte anos.
> **Devair:** Dizem que ele treina sozinho de madrugada. Só ele e o cronômetro.

**w7-elim** (`antes`, global-elim)
> **Bilboque:** Sobrevivência Mundial! O último sai, de tempos em tempos. Eu sempre sobro.
> **Nebrown:** Ele sobra porque ninguém passa ele na reta. Fica atrás e espera a curva.

**w8-tempestade** (`antes`, global-chuva)
> **Sofia Lindqvist:** Tempestade. Na minha terra, a gente corre assim o inverno inteiro.
> **Mateo Ruiz:** Na minha, a gente fica em casa.

**w9-parafuso** (`antes`, global-parafuso)
> **Peripato:** O parafuso gira a pista inteira. Lento demais, o carro solta no meio.
> **Peripato:** Rápido demais também. Boa sorte!

**w10-rios** (`antes`, global-perseg)
> **Rios:** Perseguição: você larga depois de todo mundo. Bem-vindo à minha vida: dez anos vendo o Felix de costas.
> **Rios:** Segundo lugar eu já sei ser. Terceiro, não vou virar agora.

**w11-rios-depois** (`depois`, global-perseg)
> **Rios:** ...Tá. Você anda muito.
> **Rios:** Se for passar o Felix, passa de vez. Não deixa pela metade.

**w12-resist** (`antes`, global-resist)
> **Aiko Tanabe:** Vinte voltas no Mundial. A cabeça cansa antes do carro.
> **SquadFun:** Por isso eu trouxe lanche.

**w13-ianzin** (`antes`, global-rei)
> **Ianzin:** Rei do Mundo. Eu ganhei três vezes. O Felix também.
> **Ianzin:** Quero ver se você desempata.

**w14-copa** (`depois`, global-copa)
> **Ianzin:** Você corre parecido com ele. Solta antes da curva.
> **Ianzin:** Ele está te esperando. Não fala, mas está.

**w15-felix-perde** (`derrota`, global-felix)
> **Felix Badar:** Mais cedo. Você soltou tarde na última curva.
> **Felix Badar:** De novo. O relógio não tem pressa.

**w16-lendas** (`antes`, global-convite)
> *(narração)* Lendas: os três melhores do mundo na mesma largada. E você.
> **Rios:** Eu, o Felix e o Ianzin. Sobrou pouco espaço no pódio.

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

1. ~~Objetos e trava dos controles na carreira (sem tela de cena): já muda o jogo.~~ Pronto (seção 104 do guia; os objetos ficam em `Career.Itens`, lidos das estrelas, sem campo novo no save). Parciais e fantasma do caderno ficam para depois.
2. ~~Tela de cena e as cenas principais.~~ Pronto, com todas as cenas deste roteiro (seção 106 do guia; `Resources/Carreira/cenas.json`). O caderno do Dr. Curva ainda não abre parciais nem fantasma, então a fala dele diz só que anotou as voltas do "F.B.".
3. Resto das cenas, cartas e Álbum.
4. Retratos e tradução.
