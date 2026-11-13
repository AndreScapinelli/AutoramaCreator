# Análise: Risco de Rodar em Curva Interna vs. Externa

## TL;DR
**SIM, quem faz a curva por DENTRO tem MUITO MAIS chance de rodar.**
- Trilho interno aguenta até **8,08 cél/s** na curva
- Trilho externo aguenta até **8,93 cél/s** (10,6% a mais)
- A diferença de raio (0,5 células) é pequena, mas suficiente para uma margem significativa

---

## A Física

No Autorama Creator, a física de derrapagem se baseia em:

```
Velocidade limite = √(grip × raio)
Excesso = (v² × curvatura) / grip − 1
```

### Geometria da Curva

| Elemento | Valor |
|---|---|
| Raio da linha central | 2,5 células |
| Distância trilho → centro | 0,25 células |
| **Raio trilho INTERNO** | **2,25 células** |
| **Raio trilho EXTERNO** | **2,75 células** |
| Diferença | **0,5 células** (22% de variação) |

### Curvatura

A curvatura é o inverso do raio. Quanto menor o raio, maior a curvatura:

| Trilho | Raio | Curvatura |
|---|---|---|
| Interno | 2,25 | 0,4444 /cél |
| Externo | 2,75 | 0,3636 /cél |

---

## Limites de Velocidade

Com `grip = 29` (padrão do jogo):

```
Trilho Interno:  vLimite = √(29 × 2,25) = 8,08 cél/s
Trilho Externo:  vLimite = √(29 × 2,75) = 8,93 cél/s
```

| Trilho | Limite | % da velocidade de reta (14 cél/s) |
|---|---|---|
| Interno | 8,08 cél/s | 57,7% |
| Externo | 8,93 cél/s | 63,8% |

### Interpretação

- Uma curva de 90° limita o carrinho a pouco mais de metade da velocidade máxima
- O trilho externo permite 6,1 pontos percentuais a mais de segurança
- Para uma mesma velocidade, a força lateral no trilho interno é **10,6% maior**

---

## Simulação: A Que Velocidade Roda?

Usando os parâmetros do jogo (`slipGain = 6`, `slipDecay = 1,5`):

### Trilho INTERNO (r = 2,25)

| Velocidade | Excesso | Roda? | Tempo até rodar |
|---|---|---|---|
| 8,0 cél/s | −1% | ❌ Não | — |
| 8,2 cél/s | +2% | ❌ Não | — |
| 8,4 cél/s | +4% | ❌ Não | — |
| **8,6 cél/s** | **+6%** | **⚠️ SIM** | **1,24s** |
| 9,0 cél/s | +11% | ⚠️ SIM | 0,69s |
| 10,0 cél/s | +24% | ⚠️ SIM | 0,31s |

### Trilho EXTERNO (r = 2,75)

| Velocidade | Excesso | Roda? | Tempo até rodar |
|---|---|---|---|
| 8,0 cél/s | −10% | ❌ Não | — |
| 9,0 cél/s | +1% | ❌ Não | — |
| 9,2 cél/s | +3% | ❌ Não | — |
| **9,4 cél/s** | **+5%** | **⚠️ SIM** | **1,54s** |
| 10,0 cél/s | +12% | ⚠️ SIM | 0,65s |
| 11,0 cél/s | +23% | ⚠️ SIM | 0,32s |

### Janela de Segurança

**Do limite até rodar (aprox. 6% de excesso):**
- Trilho interno: 8,08 → 8,6 = **0,52 cél/s** de margem
- Trilho externo: 8,93 → 9,4 = **0,47 cél/s** de margem

Mesmo com margens similares em velocidade absoluta, o trilho externo se beneficia de estar começando num patamar mais alto.

---

## Por Que Isso Importa no Jogo

### Realismo

No autorama de verdade:
- Curvas naturalmente têm características de aderência diferentes
- A parte interna da curva tem um raio mais apertado
- Correr pela linha interna requer mais precisão
- A linha externa é a mais segura

### Estratégia de Corrida

**Trilho Interno (mais rápido se bem pilotado):**
- ✓ Trajetória mais curta (~1,8% menos distância)
- ✓ Permite arriscar um pouquinho mais em certos pontos
- ✗ Margem menor para erro
- ✗ Requer freios mais precisos

**Trilho Externo (mais seguro):**
- ✓ Margem maior de segurança nas curvas
- ✓ Menos propenso a rodar
- ✓ Melhor para pilotos novatos
- ✗ Trajetória ligeiramente mais longa

---

## Tabela de Referência Rápida

Para uma curva de 90° (raio 2,5):

| Condição | Trilho Interno | Trilho Externo | Diferença |
|---|---|---|---|
| Raio | 2,25 cél | 2,75 cél | −0,5 cél |
| Curvatura | 0,444 /cél | 0,364 /cél | −18% |
| Limite de v | 8,08 cél/s | 8,93 cél/s | +10,6% |
| Zona segura | até 8,2 cél/s | até 9,0 cél/s | +0,8 cél/s |
| Zona de risco | 8,2–8,6 | 9,0–9,4 | Janela maior |

---

## Conclusão

**Quem faz a curva por DENTRO tem significativamente mais chance de rodar** porque:

1. **Raio menor** → curvatura maior → força lateral maior para mesma velocidade
2. **Limite de velocidade 10,6% menor** em números absolutos
3. **Começa a rodar mais cedo** (8,6 vs 9,4 cél/s, em torno de 10% de diferença)
4. **Tempo até rodar é similar** (1,24s vs 1,54s), mas o trilho externo começa com mais folga

É uma boa implementação de física e deixa o jogo realista: pilotos novatos naturalmente escolhem o trilho externo e pilotam mais seguro, enquanto pilotos experientes exploram o trilho interno para ganhar distância com arriscadas bem calculadas.

---

---

# PARTE 2: Curvas Inclinadas

## A Inclinação Muda Tudo (um Pouco)

Nas curvas inclinadas, o próprio piso ajuda a manter o carrinho na pista. A aderência efetiva aumenta em **1,7×** graças à inclinação de 15°.

### A Fórmula

```
Aderência efetiva = grip × multiplicador
excesso = v² × |curvatura| / (grip × multiplicador) − 1
```

Com `multiplicador = 1,7` nas inclinadas:

```
v_limite_inclinada = √(grip × multiplicador × raio) = √(29 × 1,7 × raio)
```

### Novos Limites

| Trilho | Plana | Inclinada | Ganho |
|---|---|---|---|
| **Interno** | 8,08 cél/s | **10,53 cél/s** | ×1,30 (+30,4%) |
| **Externo** | 8,93 cél/s | **11,64 cél/s** | ×1,30 (+30,4%) |

O ganho é exatamente **√1,7 ≈ 1,30** para ambos os trilhos!

### % da Velocidade Máxima

| Trilho | Plana | Inclinada |
|---|---|---|
| Interno | 57,7% | **75,2%** |
| Externo | 63,8% | **83,2%** |

Agora é realista tentar fazer curvas inclinadas em alta velocidade!

---

## A Diferença Interna vs. Externa se Mantém

| Aspecto | Curva Plana | Curva Inclinada |
|---|---|---|
| Diferença absoluta | 0,85 cél/s | 1,11 cél/s |
| Diferença percentual | **10,6%** | **10,6%** |

**O trilho externo continua sendo 10,6% mais seguro** em ambas as situações.

---

## Velocidades Críticas (Quando Roda)

| Trilho | Plana | Inclinada | Ganho |
|---|---|---|---|
| **Interno** | 8,50 cél/s | **11,00 cél/s** | +29% |
| **Externo** | 9,30 cél/s | **12,20 cél/s** | +31% |

---

## Conclusão para Curvas Inclinadas

✅ **Quem faz a curva inclinada por DENTRO ainda tem mais chance de rodar**, mas agora com:

1. **Limite 30% maior** em termos absolutos (8,1 → 10,5 cél/s)
2. **Mesma diferença percentual** (10,6%) entre interno e externo
3. **Margem maior** em termos de tempo: 1,55s até rodar vs. 1,24s na plana

A inclinação "levanta" o limite de ambos igualmente. É uma boa implementação: o jogador sente que pode arriscar mais na inclinada, mas a probabilidade relativa de rodar continua a mesma.

### Estratégia na Inclinada

- **Trilho externo**: Agora aguenta 11,6 cél/s (83% da máxima) → muito mais seguro
- **Trilho interno**: Agora aguenta 10,5 cél/s (75% da máxima) → mas ainda é a opção mais arriscada

Na simulação da pista de teste ("8 com ponte"), os carrinhos chegaram a **13,4 cél/s** nas inclinadas, bem acima do limite. Isso significa que todos estavam derrapando um pouco, o que é realista e torna a pista desafiadora.

---

## Referência do Código

**Constantes da física (`Assets/Autorama/Scripts/Race/RaceSim.cs`, `Accel`/`Brake` na linha 219 e `grip` na linha 14):**
- `ACCEL = 3.5` (resposta do motor)
- `BRAKE = 1.8`, `BRAKE0 = 0.5` (freio)
- `SLIP_GAIN = 3`, `SLIP_DECAY = 3` (derrapagem)
- `GRIP = 29` (aderência padrão)

**Geometria das curvas (`GUIA-UNITY.md` seção 3):**
- Raio das curvas: `2,5` células
- Offset dos trilhos: `±0,25` células

**Curvas inclinadas (`GUIA-UNITY.md` seção 16.5):**
- Multiplicador de aderência: `BANK_GRIP = 1,7`
- Ângulo de inclinação: `15°`
- Fórmula física: `(tan θ + μ) / (μ × (1 − μ × tan θ))` ≈ 1,7

**Física de derrapagem (`RaceSim.cs` linha 365 e `GUIA-UNITY.md` seção 15.3):**
- Cálculo: `ratio = v² × |curvatura| / (grip × multiplicador)`
- Acúmulo: `slip += (ratio - 1) × SLIP_GAIN × dt`
- Limite: `if (slip >= 1) rodar()`

Ver também: [GUIA-UNITY.md](/docs/GUIA-UNITY.md) seção 16.5 (Curva inclinada: por que dá para acelerar mais)
