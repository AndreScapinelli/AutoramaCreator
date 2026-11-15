using System.Collections.Generic;

namespace Autorama
{
    // O que o narrador vê de cada carro num quadro (seção 60 do guia).
    public struct NarraCarro
    {
        public bool presente, fora, turbo, chegou;
        public int pos, voltas, saidas;
        public int onde;          // onde saiu da pista: 0 pista, 1 loop, 2 salto
        public float dist;        // voltas andadas com a fração da volta (RaceSim.Dist)
        public float? melhor;     // melhor volta da prova
        public string aviso;      // o aviso da ficha ("Recorde da pista! …", "Melhor volta pessoal! …") ou null
        public string nome;
        public string cor;        // "#RRGGBB" da ficha do carro: o nome sai nessa cor (robôs com o mesmo nome têm cores diferentes)
    }

    // O quadro inteiro: a prova, os carros e, no campeonato, a tabela projetada.
    public class NarraQuadro
    {
        public RaceSim.Phase fase;
        public int n, voltasTotal;
        public bool porTempo;
        public float tempoFalta;
        public readonly NarraCarro[] c = new NarraCarro[RaceSim.MaxCars];
        // Campeonato: nomes na ordem da tabela geral se a prova acabasse agora (só quando esta bateria fecha a etapa;
        // senão null), o líder antes da bateria (null: ninguém ainda ou empate) e se é a última etapa.
        public List<string> proj;
        public string campLider;
        public bool ultimaEtapa;
        // Cor de cada nome ("#RRGGBB"): o nome aparece na fala com a cor do carro ou do piloto.
        public readonly Dictionary<string, string> cores = new Dictionary<string, string>();
    }

    // Narração da corrida em tom de TV com bom humor. Compara cada quadro com o anterior, gera falas numa fila com
    // prioridade e mostra uma por vez (Atual). Puro: sem cena, testável por script eval.
    public class Narrador
    {
        public const float Duracao = 3.6f;        // segundos que cada fala fica na tela
        public const float Intervalo = 0.6f;      // pausa entre uma fala e a próxima
        public const float Velho = 4f;            // fala que esperou mais que isso na fila é descartada (prioridade 3: o dobro)
        public const int FilaMax = 3;
        public const float Firme = 1f;            // a posição tem de durar isso para valer ultrapassagem
        public const float Largada = 2.5f;        // depois do "vai", a confusão da largada não é narrada
        public const float CampFirme = 3f;        // a projeção do campeonato tem de durar isso
        public const float CampIntervalo = 25f;   // no mínimo entre duas falas de campeonato
        public const float DisputaGap = 0.06f;    // voltas entre dois carros para contar como disputa
        public const float DisputaTempo = 15f;    // segundos colados para narrar a disputa
        public const float TurboIntervalo = 15f;  // por carro
        public const float LiderForaTempo = 20f;  // segundos de prova antes de reparar no líder do campeonato fora do pódio

        public string Atual { get; private set; } = "";
        public int AtualPrio { get; private set; }

        struct Fala { public string txt; public int prio; public float t; }
        readonly List<Fala> fila = new List<Fala>();
        readonly System.Random rng;
        readonly Dictionary<string, int[]> usadas = new Dictionary<string, int[]>();
        float relogio, mostra, espera, inicio;
        RaceSim.Phase faseAnt = RaceSim.Phase.Idle;
        readonly int[] firme = new int[RaceSim.MaxCars], posAnt = new int[RaceSim.MaxCars];
        readonly float[] desde = new float[RaceSim.MaxCars], turboT = new float[RaceSim.MaxCars];
        readonly bool[] foraAnt = new bool[RaceSim.MaxCars], turboAnt = new bool[RaceSim.MaxCars], chegouAnt = new bool[RaceSim.MaxCars];
        readonly float?[] melhorAnt = new float?[RaceSim.MaxCars];
        readonly string[] avisoAnt = new string[RaceSim.MaxCars];
        readonly Dictionary<int, float> colados = new Dictionary<int, float>(), disputaT = new Dictionary<int, float>();
        bool ultimaDita, vitoriaDita, liderForaDito, tituloDito;
        string projFirme, projCand; float projDesde, campT;
        float liderForaDesde;
        public static readonly int[] Contagem = { 15, 10, 5, 3, 2, 1 };
        int contagem;
        float rapida = float.PositiveInfinity;   // a volta mais rápida da prova até agora
        int marcoLider, marcoUlt;
        readonly int[] voltasAnt = new int[RaceSim.MaxCars];
        Dictionary<string, string> cores;

        public Narrador(int? seed = null) { rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random(); Reset(); }

        public void Reset()
        {
            fila.Clear(); Atual = ""; AtualPrio = 0; mostra = espera = 0f;
            faseAnt = RaceSim.Phase.Idle;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                firme[k] = posAnt[k] = -1; desde[k] = 0f; turboT[k] = -99f;
                foraAnt[k] = turboAnt[k] = chegouAnt[k] = false; melhorAnt[k] = null; avisoAnt[k] = null;
            }
            colados.Clear(); disputaT.Clear();
            rapida = float.PositiveInfinity; marcoLider = marcoUlt = 0;
            for (int k = 0; k < RaceSim.MaxCars; k++) voltasAnt[k] = 0;
            contagem = 0;
            ultimaDita = vitoriaDita = liderForaDito = tituloDito = largou = false;
            projFirme = projCand = null; projDesde = 0f; campT = -99f; liderForaDesde = -1f;
        }

        // dt: 0 na pausa (o relógio do narrador para junto).
        public void Tick(NarraQuadro q, float dt)
        {
            cores = q.cores;
            relogio += dt;
            if (q.fase == RaceSim.Phase.Idle || (q.fase == RaceSim.Phase.Countdown && (faseAnt == RaceSim.Phase.Running || faseAnt == RaceSim.Phase.Done)))
                if (faseAnt != RaceSim.Phase.Idle) Reset();
            if (q.fase == RaceSim.Phase.Idle) { faseAnt = q.fase; Mostrar(dt); return; }
            if (q.fase == RaceSim.Phase.Running && !largou)
            {
                largou = true; inicio = relogio;
                if (faseAnt == RaceSim.Phase.Countdown) Diz("largada", 2);
            }
            if (largou && (q.fase == RaceSim.Phase.Running || q.fase == RaceSim.Phase.Done)) Corrida(q);
            faseAnt = q.fase;
            Mostrar(dt);
        }

        bool largou;

        void Corrida(NarraQuadro q)
        {
            bool cedo = relogio - inicio < Largada;
            int lider = -1;
            for (int k = 0; k < q.n; k++) if (q.c[k].presente && q.c[k].pos == 0) lider = k;

            for (int k = 0; k < q.n; k++)
            {
                var c = q.c[k];
                if (!c.presente) continue;
                // Saída de pista.
                if (c.fora && !foraAnt[k] && !c.chegou)
                {
                    string chave = c.onde == 1 ? "loop" : c.onde == 2 ? "salto" : c.saidas >= 5 ? "saida5" : c.saidas == 3 ? "saida3" : "saida";
                    Diz(chave, c.onde > 0 || c.pos == 0 ? 2 : 1, Nm(c));
                }
                // Turbo.
                if (c.turbo && !turboAnt[k] && relogio - turboT[k] > TurboIntervalo) { turboT[k] = relogio; Diz("turbo", 1, Nm(c)); }
                // Recorde e melhor volta.
                // Recorde da pista (do aviso da ficha) > volta mais rápida da prova > melhor pessoal. A primeira volta
                // rápida da prova só marca o tempo; a partir daí, quem baixa o tempo é narrado.
                bool novoAviso = c.aviso != null && c.aviso != avisoAnt[k];
                bool baixou = c.melhor.HasValue && c.melhor != melhorAnt[k] && c.melhor.Value < rapida;
                if (novoAviso && c.aviso.StartsWith("Recorde")) Diz("recorde", 3, Nm(c), "", Tempo(c.melhor));
                else if (baixou && !float.IsPositiveInfinity(rapida)) Diz("melhorvolta", 2, Nm(c), "", Tempo(c.melhor));
                else if (novoAviso) Diz("pessoal", 1, Nm(c), "", Tempo(c.melhor));
                if (baixou) rapida = c.melhor.Value;
                // Ultrapassagem: a posição nova durou Firme segundos.
                if (c.pos != posAnt[k]) desde[k] = relogio;
                if (firme[k] < 0 || cedo || c.chegou) firme[k] = c.pos;
                else if (c.pos != firme[k] && relogio - desde[k] >= Firme)
                {
                    if (c.pos < firme[k] && !c.fora && q.fase == RaceSim.Phase.Running)
                    {
                        int atras = PorPos(q, c.pos + 1);
                        if (atras >= 0 && !q.c[atras].fora)
                            Diz(c.pos == 0 ? "lider" : "passa", c.pos == 0 ? 3 : 2, Nm(c), Nm(q.c[atras]));
                    }
                    firme[k] = c.pos;
                }
                posAnt[k] = c.pos;
                foraAnt[k] = c.fora; turboAnt[k] = c.turbo; melhorAnt[k] = c.melhor; avisoAnt[k] = c.aviso;
            }

            // Disputa: dois carros seguidos colados por DisputaTempo segundos.
            if (q.fase == RaceSim.Phase.Running && !cedo)
                for (int p = 0; p + 1 < q.n; p++)
                {
                    int a = PorPos(q, p), b = PorPos(q, p + 1);
                    if (a < 0 || b < 0) continue;
                    int key = a * 16 + b;
                    bool junto = !q.c[a].fora && !q.c[b].fora && !q.c[a].chegou && q.c[a].dist - q.c[b].dist < DisputaGap;
                    if (!junto) { colados.Remove(key); continue; }
                    if (!colados.ContainsKey(key)) colados[key] = relogio;
                    if (relogio - colados[key] >= DisputaTempo && (!disputaT.TryGetValue(key, out float dt0) || relogio - dt0 > 40f))
                    { disputaT[key] = relogio; colados.Remove(key); Diz("disputa", 1, Nm(q.c[a]), Nm(q.c[b])); }
                }

            // Última volta ou 15 s finais.
            if (!ultimaDita && lider >= 0 && !q.porTempo && q.voltasTotal > 1 && q.c[lider].voltas == q.voltasTotal - 1 && q.fase == RaceSim.Phase.Running)
            {
                ultimaDita = true; Diz("ultima", 3);
                if (q.proj != null && q.ultimaEtapa && q.proj.Count > 0 && !tituloDito) { tituloDito = true; Diz("titulo", 3, q.proj[0]); }
            }
            // Corrida por tempo: 15, 10, 5, 3, 2, 1. Entram na hora, por cima do que estiver na tela.
            if (q.porTempo && q.fase == RaceSim.Phase.Running && q.tempoFalta > 0f)
                while (contagem < Contagem.Length && q.tempoFalta <= Contagem[contagem])
                {
                    int seg = Contagem[contagem++];
                    if (contagem < Contagem.Length && q.tempoFalta <= Contagem[contagem]) continue;   // pulou (entrou atrasado): só o último
                    Ja(seg == 15 ? "quinze" : seg == 10 ? "dez" : seg == 5 ? "cinco" : seg.ToString(), seg > 3 ? 2.5f : 0.9f);
                }

            Marcos(q, lider);

            // Chegada.
            for (int k = 0; k < q.n; k++)
            {
                var c = q.c[k];
                if (!c.presente) continue;
                if (c.chegou && !chegouAnt[k] && !vitoriaDita && c.pos == 0) { vitoriaDita = true; Diz("vitoria", 3, Nm(c)); }
                chegouAnt[k] = c.chegou;
            }
            if (q.fase == RaceSim.Phase.Done && faseAnt == RaceSim.Phase.Running)
            {
                if (!vitoriaDita && lider >= 0) { vitoriaDita = true; Diz("vitoria", 3, Nm(q.c[lider])); }
                int ult = PorPos(q, q.n - 1);
                if (q.n >= 3 && ult >= 0 && q.c[ult].chegou) Diz("ultimo", 1, Nm(q.c[ult]));
            }

            Campeonato(q);
        }

        // A cada Marco voltas: o líder é exaltado ao completar e o último a completar é zoado (com 3 carros ou mais).
        // A volta que começa a última e a chegada já têm fala própria, então os marcos param antes.
        public static int Marco(NarraQuadro q) => q.porTempo ? 3 : System.Math.Max(2, q.voltasTotal / 3);

        void Marcos(NarraQuadro q, int lider)
        {
            int M = Marco(q);
            bool vale(int m) => m > 0 && (q.porTempo || m < q.voltasTotal - 1);
            if (q.fase == RaceSim.Phase.Running && lider >= 0)
            {
                int m = q.c[lider].voltas / M * M;
                if (m > marcoLider && vale(m)) Diz("marcolider", 2, Nm(q.c[lider]), m.ToString());
                if (m > marcoLider) marcoLider = m;
                // O último a completar: quando a menor contagem entre os presentes chega ao marco, é quem acabou de chegar nele.
                int min = int.MaxValue;
                for (int k = 0; k < q.n; k++) if (q.c[k].presente) min = System.Math.Min(min, q.c[k].voltas);
                int mu = min == int.MaxValue ? 0 : min / M * M;
                if (mu > marcoUlt && vale(mu) && q.n >= 3)
                {
                    int quem = -1;
                    for (int k = 0; k < q.n; k++)
                        if (q.c[k].presente && q.c[k].voltas >= mu && voltasAnt[k] < mu && (quem < 0 || q.c[k].pos > q.c[quem].pos)) quem = k;
                    if (quem >= 0) Diz("marcoultimo", 2, Nm(q.c[quem]), mu.ToString());
                }
                if (mu > marcoUlt) marcoUlt = mu;
            }
            for (int k = 0; k < q.n; k++) voltasAnt[k] = q.c[k].voltas;
        }

        static string Tempo(float? t) => t.HasValue ? RaceController.Fmt(t) : "";

        void Campeonato(NarraQuadro q)
        {
            if (q.fase != RaceSim.Phase.Running) return;
            bool cedo = relogio - inicio < Largada;
            // Líder do campeonato fora do pódio, uma vez por bateria.
            if (!liderForaDito && q.campLider != null && q.n >= 4 && relogio - inicio > LiderForaTempo)
            {
                int k = PorNome(q, q.campLider);
                if (k >= 0 && q.c[k].pos >= 3 && !q.c[k].fora)
                {
                    if (liderForaDesde < 0f) liderForaDesde = relogio;
                    else if (relogio - liderForaDesde > 5f) { liderForaDito = true; Diz("liderfora", 1, Nm(q.c[k]), Ordinal(q.c[k].pos + 1)); }
                }
                else liderForaDesde = -1f;
            }
            // Troca de líder na tabela projetada.
            if (q.proj == null || q.proj.Count == 0) return;
            string cand = q.proj[0];
            if (cedo || projFirme == null) { projFirme = projCand = cand; projDesde = relogio; return; }
            if (cand != projCand) { projCand = cand; projDesde = relogio; }
            if (projCand != projFirme && relogio - projDesde >= CampFirme && relogio - campT >= CampIntervalo)
            {
                campT = relogio;
                string antes = projFirme; projFirme = projCand;
                Diz(projCand == q.campLider ? "camprecupera" : "campassume", 2, projCand, antes);
            }
        }

        static int PorPos(NarraQuadro q, int p) { for (int k = 0; k < q.n; k++) if (q.c[k].presente && q.c[k].pos == p) return k; return -1; }
        static int PorNome(NarraQuadro q, string nm) { for (int k = 0; k < q.n; k++) if (q.c[k].presente && q.c[k].nome == nm) return k; return -1; }
        static string Ordinal(int p) => p + "º";

        // ---------- Fila e tela ----------

        // Mostra já, por dur segundos, sem passar pela fila (a contagem final não pode atrasar).
        void Ja(string chave, float dur)
        {
            if (!Frases.TryGetValue(chave, out var lista)) return;
            Atual = Sorteia(chave, lista); AtualPrio = 3; mostra = dur; espera = 0f;
        }

        void Diz(string chave, int prio, string a = "", string b = "", string t = "")
        {
            if (!Frases.TryGetValue(chave, out var lista)) return;
            string txt = Sorteia(chave, lista).Replace("{A}", Pinta(a)).Replace("{B}", Pinta(b)).Replace("{T}", t);
            if (fila.Count >= FilaMax)
            {
                int pior = 0;
                for (int i = 1; i < fila.Count; i++) if (fila[i].prio < fila[pior].prio) pior = i;
                if (fila[pior].prio > prio) return;
                fila.RemoveAt(pior);
            }
            fila.Add(new Fala { txt = txt, prio = prio, t = relogio });
        }

        static string Nm(NarraCarro c) => string.IsNullOrEmpty(c.cor) ? c.nome : $"<color={c.cor}>{c.nome}</color>";
        string Pinta(string nome) => nome.StartsWith("<color") ? nome : cores != null && !string.IsNullOrEmpty(nome) && cores.TryGetValue(nome, out var c) ? $"<color={c}>{nome}</color>" : nome;

        // Sorteia sem repetir as duas últimas desta chave.
        string Sorteia(string chave, string[] lista)
        {
            if (!usadas.TryGetValue(chave, out var u)) usadas[chave] = u = new[] { -1, -1 };
            int i;
            int tent = 0;
            do i = rng.Next(lista.Length); while ((i == u[0] || i == u[1]) && lista.Length > 2 && ++tent < 20);
            u[1] = u[0]; u[0] = i;
            return lista[i];
        }

        void Mostrar(float dt)
        {
            if (mostra > 0f) { mostra -= dt; if (mostra <= 0f) { Atual = ""; AtualPrio = 0; espera = Intervalo; } return; }
            if (espera > 0f) { espera -= dt; return; }
            fila.RemoveAll(f => relogio - f.t > (f.prio >= 3 ? 2f * Velho : Velho));
            if (fila.Count == 0) return;
            int m = 0;
            for (int i = 1; i < fila.Count; i++) if (fila[i].prio > fila[m].prio) m = i;
            Atual = fila[m].txt; AtualPrio = fila[m].prio; mostra = Duracao;
            fila.RemoveAt(m);
        }

        // ---------- Frases ----------
        // {A}: o piloto do evento; {B}: o outro (quem foi passado, o líder anterior, a posição, o número de voltas); {T}: o tempo da volta.
        public static readonly Dictionary<string, string[]> Frases = new Dictionary<string, string[]>
        {
            ["largada"] = new[] { "Apagaram as luzes! E lá vão eles!", "Largada! Todo mundo de pé no fundo!", "Valendo! Que vença o melhor... ou o mais sortudo.", "Largada limpa... por enquanto.", "E foi dada a largada! O carpete que se cuide." },
            ["passa"] = new[] { "{A} passa {B}! Não pediu licença, não!", "{A} por dentro, e {B} só viu a placa do chassi.", "Ultrapassagem! {A} deixa {B} para trás.", "{A} pra cima de {B}... e passou!", "{B} piscou e {A} foi embora." },
            ["lider"] = new[] { "{A} assume a liderança! {B} fica olhando a traseira.", "Tem líder novo: {A}!", "{A} passa {B} e pula para a ponta!", "Mudou tudo! {A} é o novo líder." },
            ["saida"] = new[] { "{A} saiu da pista! O trilho estava ali, viu?", "Lá vai {A} conhecer o carpete da sala.", "{A} voou! Alguém chama o fiscal.", "Escapou! {A} foi dar uma volta fora da pista.", "{A} errou o ponto de freada. Acontece nas melhores famílias." },
            ["saida3"] = new[] { "Terceira vez, {A}. O fiscal já sabe seu nome.", "{A} sai de novo! Já é freguês do carpete.", "Três saídas para {A}. Coleção bonita." },
            ["saida5"] = new[] { "{A} fora outra vez! A essa altura é estratégia.", "Mais uma de {A}. Alguém segura esse acelerador!", "{A} já conhece cada canto da mesa." },
            ["loop"] = new[] { "{A} não segurou no loop! A gravidade manda lembranças.", "Caiu no loop! {A} esqueceu de acelerar lá em cima.", "{A} deu meia volta no loop... e despencou." },
            ["salto"] = new[] { "{A} pulou e não chegou! Faltou coragem!", "{A} ficou no vão do salto. Aterrissagem cancelada.", "Saltou, voou... e caiu! {A} fora." },
            ["turbo"] = new[] { "{A} soltou o turbo! Segura!", "Turbo ligado para {A}!", "{A} apertou o botão mágico!", "Lá vai {A} de turbo. Cuidado na curva!" },
            ["recorde"] = new[] { "RECORDE DA PISTA! {A} escreve o nome na história... desta mesa. {T}!", "Recorde! {A} faz {T}, a volta mais rápida que essa pista já viu!", "Que volta, {A}! {T}, recorde da pista!", "Segura essa: {T}! {A} quebra o recorde da pista!", "Podem emoldurar: {A}, {T}, novo recorde da pista!" },
            ["pessoal"] = new[] { "Melhor volta pessoal de {A}!", "{A} se superou nessa volta.", "Volta caprichada de {A}: a melhor da carreira nesta pista." },
            ["melhorvolta"] = new[] { "{A} crava {T}, a volta mais rápida da prova!", "Melhor volta da corrida para {A}: {T}!", "{A} acelerou forte: {T}, ninguém andou tão rápido hoje.", "Cronômetro em chamas! {T} para {A}.", "{A} baixa o tempo para {T}. Que mão!" },
            ["disputa"] = new[] { "{A} e {B} colados há um tempão. Ninguém respira!", "Que briga entre {A} e {B}!", "{B} está no cangote de {A}!", "{A} e {B} dividem até o mesmo pensamento." },
            ["ultima"] = new[] { "Última volta!", "Sino tocando: última volta!", "É agora ou nunca: última volta!" },
            ["dez"] = new[] { "Dez segundos! Quem quer, vai agora!", "Faltam dez! Ninguém tira o pé!", "Dez segundinhos e acabou!" },
            ["cinco"] = new[] { "Cinco segundos!", "Cinco! Segura o coração!", "Cinco segundos e é bandeira!" },
            ["3"] = new[] { "3!" },
            ["2"] = new[] { "2!" },
            ["1"] = new[] { "1!" },
            ["quinze"] = new[] { "Quinze segundos! Tudo ou nada!", "Faltam quinze segundos! Pé no fundo!", "Reta final: quinze segundos!" },
            ["vitoria"] = new[] { "Bandeirada! {A} vence!", "{A} cruza na frente! Que corrida!", "Vitória de {A}! Pode chamar a família!", "É de {A}! Bandeira quadriculada!" },
            ["marcolider"] = new[] { "{A} completa {B} voltas na frente de todo mundo!", "{B} voltas e {A} segue mandando na corrida!", "{A} na ponta depois de {B} voltas. Que pilotagem!", "Passadas {B} voltas, quem dita o ritmo é {A}!", "{B} voltas, e {A} ainda não viu ninguém pelo retrovisor." },
            ["marcoultimo"] = new[] { "{A} fecha a volta {B} em último. Está apreciando a paisagem?", "{B} voltas e {A} lá atrás. Alguém avisa que já começou!", "{A} completa {B} voltas por último. Sem pressa, a mesa não vai fugir.", "Volta {B}: {A} na lanterna. Pelo menos o carro está limpinho.", "{A} chega à volta {B} por último. Tá de passeio, {A}?" },
            ["ultimo"] = new[] { "E {A} chega em último, mas chega!", "{A} fecha o pelotão. O importante é competir.", "{A} chegou! Atrasado, mas com estilo." },
            ["campassume"] = new[] { "Se terminar assim, {A} assume a liderança do campeonato!", "Do jeito que está, {A} vira líder do campeonato!", "Atenção na tabela: {A} passa {B} no campeonato!" },
            ["camprecupera"] = new[] { "{A} recupera a liderança do campeonato!", "Do jeito que está, {A} volta para a ponta do campeonato.", "{A} reage e retoma o topo da tabela!" },
            ["liderfora"] = new[] { "{A}, líder do campeonato, está em {B}. Hoje não está fácil.", "O líder do campeonato, {A}, só em {B}! A tabela está tremendo.", "{A} lidera o campeonato, mas aqui está em {B}." },
            ["titulo"] = new[] { "O título é de {A} se terminar assim!", "O título está nas mãos de {A}! Só não pode errar.", "Uma volta separa {A} da taça!" },
        };
    }
}
