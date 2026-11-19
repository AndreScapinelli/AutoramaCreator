using System.Collections.Generic;

namespace Autorama
{
    // O que o narrador vê de cada carro num quadro (seção 60 do guia).
    public struct NarraCarro
    {
        public bool presente, fora, turbo, chegou;
        public int pos, voltas, saidas, batidas;   // batidas: toques em outro carro (CarState.hits)
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
        public const float BatidaJanela = 1.5f;   // toque em outro carro até isso antes da saída: a saída foi por batida
        public const float RepeteJanela = 12f;    // saiu de novo antes disso: "de novo?"
        public const int PerdeuMin = 2;           // posições perdidas na saída para narrar a volta à pista

        public string Atual { get; private set; } = "";
        public int AtualPrio { get; private set; }

        struct Fala { public string txt; public int prio; public float t; }
        readonly List<Fala> fila = new List<Fala>();
        readonly System.Random rng;
        readonly Dictionary<string, List<int>> usadas = new Dictionary<string, List<int>>();
        float relogio, mostra, espera, inicio;
        RaceSim.Phase faseAnt = RaceSim.Phase.Idle;
        readonly int[] firme0 = new int[RaceSim.MaxCars];   // firme no começo do quadro
        readonly int[] firme = new int[RaceSim.MaxCars], posAnt = new int[RaceSim.MaxCars];
        readonly float[] desde = new float[RaceSim.MaxCars], turboT = new float[RaceSim.MaxCars];
        readonly float[] batidaT = new float[RaceSim.MaxCars], saidaT = new float[RaceSim.MaxCars];
        readonly int[] batidasAnt = new int[RaceSim.MaxCars], posSaida = new int[RaceSim.MaxCars];
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
                batidaT[k] = saidaT[k] = -99f; batidasAnt[k] = 0; posSaida[k] = -1;
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
            saiuJa = -1; pendente = null;
            System.Array.Copy(firme, firme0, firme.Length);

            for (int k = 0; k < q.n; k++)
            {
                var c = q.c[k];
                if (!c.presente) continue;
                if (c.batidas > batidasAnt[k]) batidaT[k] = relogio;
                batidasAnt[k] = c.batidas;
                // Saída de pista e a volta para o trilho.
                if (c.fora && !foraAnt[k] && !c.chegou) Saida(q, k);
                if (!c.fora && foraAnt[k] && !c.chegou && q.fase == RaceSim.Phase.Running) Voltou(q, k);
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
                        // Quem foi passado: estava firme na frente e agora está atrás. Se algum deles está fora da
                        // pista, a posição veio da saída dele (a volta conta em Voltou), não de ultrapassagem.
                        int atras = -1; bool deFora = false;
                        for (int j = 0; j < q.n; j++)
                            if (j != k && q.c[j].presente && firme0[j] >= 0 && firme0[j] < firme0[k] && q.c[j].pos > c.pos)
                            {
                                if (q.c[j].fora) deFora = true;
                                else if (atras < 0 || q.c[j].pos < q.c[atras].pos) atras = j;
                            }
                        if (atras >= 0 && !deFora)
                            Diz(c.pos == 0 ? "lider" : "passa", c.pos == 0 ? 3 : 2, Nm(c), Nm(q.c[atras]));
                    }
                    firme[k] = c.pos;
                }
                posAnt[k] = c.pos;
                foraAnt[k] = c.fora; turboAnt[k] = c.turbo; melhorAnt[k] = c.melhor; avisoAnt[k] = c.aviso;
            }

            if (pendente.HasValue) { Enfila(pendente.Value); pendente = null; }

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

        int saiuJa;      // quem já saiu da pista neste quadro (-2: a dupla já foi dita)
        Fala? pendente;  // a saída fica para o fim do quadro: se outro sair junto, vira a fala da dupla

        // Saída de pista com o contexto: onde, por quê, quem e quando. Do mais especial para o comum.
        void Saida(NarraQuadro q, int k)
        {
            var c = q.c[k];
            posSaida[k] = c.pos;
            bool repete = relogio - saidaT[k] < RepeteJanela;
            saidaT[k] = relogio;
            if (saiuJa == -2) return;
            if (saiuJa >= 0) { pendente = null; saiuJa = -2; Diz("saidadupla", 2, Nm(q.c[saiuJa0]), Nm(c)); return; }
            saiuJa = saiuJa0 = k;
            bool final = q.porTempo ? q.tempoFalta > 0f && q.tempoFalta <= 15f : ultimaDita;
            string chave, b = ""; int prio = 1;
            if (c.onde == 1) { chave = "loop"; prio = 2; }
            else if (c.onde == 2) { chave = "salto"; prio = 2; }
            else if (relogio - batidaT[k] < BatidaJanela) { chave = "batida"; prio = 2; }
            else if (c.pos == 0 && q.n > 1) { chave = "saidalider"; prio = 3; }
            else if (final) { chave = "saidafinal"; prio = 2; }
            else if (c.turbo) chave = "saidaturbo";
            else if (repete) chave = "saidarepete";
            else if (c.saidas == 3) chave = "saida3";
            else if (c.saidas == 5) chave = "saida5";
            else if (c.saidas >= 7 && c.saidas % 3 == 1) { chave = "saidamuitas"; b = c.saidas.ToString(); }
            else chave = "saida";
            pendente = new Fala { txt = Texto(chave, Nm(c), b, ""), prio = prio, t = relogio };
        }
        int saiuJa0;

        // De volta ao trilho: se a saída custou posições, conta quantas (e, se era o líder, quem herdou a ponta).
        void Voltou(NarraQuadro q, int k)
        {
            var c = q.c[k];
            int antes = posSaida[k]; posSaida[k] = -1;
            if (antes < 0) return;
            int novo = PorPos(q, 0);
            if (antes == 0 && c.pos > 0 && novo >= 0) { firme[novo] = 0; Diz("perdeulider", 3, Nm(c), Nm(q.c[novo])); }   // a ponta herdada não vira "líder novo"
            else if (c.pos - antes >= PerdeuMin) Diz("perdeu", 1, Nm(c), Ordinal(c.pos + 1), (c.pos - antes).ToString());
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
        static string Ordinal(int p) => L.F("{0}º", p);

        // ---------- Fila e tela ----------

        // Mostra já, por dur segundos, sem passar pela fila (a contagem final não pode atrasar).
        void Ja(string chave, float dur)
        {
            if (!Frases.TryGetValue(chave, out var lista)) return;
            Atual = Sorteia(chave, lista); AtualPrio = 3; mostra = dur; espera = 0f;
        }

        string Texto(string chave, string a, string b, string t) =>
            Frases.TryGetValue(chave, out var lista) ? L.T(Sorteia(chave, lista)).Replace("{A}", Pinta(a)).Replace("{B}", Pinta(b)).Replace("{T}", t) : null;

        void Diz(string chave, int prio, string a = "", string b = "", string t = "") =>
            Enfila(new Fala { txt = Texto(chave, a, b, t), prio = prio, t = relogio });

        void Enfila(Fala f)
        {
            if (f.txt == null) return;
            int prio = f.prio;
            if (fila.Count >= FilaMax)
            {
                int pior = 0;
                for (int i = 1; i < fila.Count; i++) if (fila[i].prio < fila[pior].prio) pior = i;
                if (fila[pior].prio > prio) return;
                fila.RemoveAt(pior);
            }
            fila.Add(f);
        }

        static string Nm(NarraCarro c) => string.IsNullOrEmpty(c.cor) ? c.nome : $"<color={c.cor}>{c.nome}</color>";
        string Pinta(string nome) => nome.StartsWith("<color") ? nome : cores != null && !string.IsNullOrEmpty(nome) && cores.TryGetValue(nome, out var c) ? $"<color={c}>{nome}</color>" : nome;

        // Sorteia sem repetir as mais recentes desta chave: uma frase só volta depois de sair mais da metade da lista.
        string Sorteia(string chave, string[] lista)
        {
            if (!usadas.TryGetValue(chave, out var u)) usadas[chave] = u = new List<int>();
            int guarda = System.Math.Min(lista.Length - 1, (lista.Length + 1) / 2);
            while (u.Count > guarda) u.RemoveAt(0);
            int i, tent = 0;
            do i = rng.Next(lista.Length); while (u.Contains(i) && ++tent < 50);
            u.Add(i);
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
            ["largada"] = new[] { L.N("Apagaram as luzes! E lá vão eles!"), L.N("Largada! Todo mundo de pé no fundo!"), L.N("Valendo! Que vença o melhor... ou o mais sortudo."), L.N("Largada limpa... por enquanto."), L.N("E foi dada a largada! O carpete que se cuide."), L.N("Bandeira verde! Os trilhos estão quentes!"), L.N("Saíram! Agora é gatilho e coragem."), L.N("Começou! Quem frear primeiro perde."), L.N("E lá se vão eles, cheios de esperança e de pilha.") },
            ["passa"] = new[] { L.N("{A} passa {B}! Não pediu licença, não!"), L.N("{A} por dentro, e {B} só viu a placa do chassi."), L.N("Ultrapassagem! {A} deixa {B} para trás."), L.N("{A} pra cima de {B}... e passou!"), L.N("{B} piscou e {A} foi embora."), L.N("{A} mergulha e engole {B}!"), L.N("Tchau, {B}! {A} sobe uma posição."), L.N("{A} passou {B} como se fosse cone de treino."), L.N("Manobra bonita de {A} em cima de {B}!"), L.N("{B} abriu a porta e {A} entrou sem bater.") },
            ["lider"] = new[] { L.N("{A} assume a liderança! {B} fica olhando a traseira."), L.N("Tem líder novo: {A}!"), L.N("{A} passa {B} e pula para a ponta!"), L.N("Mudou tudo! {A} é o novo líder."), L.N("{A} toma a ponta de {B}! A mesa vai abaixo!"), L.N("Liderança trocada! {A} na frente, {B} no prejuízo."), L.N("{A} chega lá na frente. {B}, a vista agora é outra.") },
            ["saida"] = new[] { L.N("{A} saiu da pista! O trilho estava ali, viu?"), L.N("Lá vai {A} conhecer o carpete da sala."), L.N("{A} voou! Alguém chama o fiscal."), L.N("Escapou! {A} foi dar uma volta fora da pista."), L.N("{A} errou o ponto de freada. Acontece nas melhores famílias."), L.N("{A} rodou! O carrinho foi parar longe."), L.N("Rodopio de {A}! Bonito de ver, péssimo para o cronômetro."), L.N("{A} entrou rápido demais e a curva não perdoou."), L.N("Fiscal! {A} está deitado no carpete."), L.N("{A} decolou! Ainda bem que não tem multa por voo baixo."), L.N("{A} confundiu a curva com uma reta."), L.N("Que pião de {A}! Faltou só a música."), L.N("{A} perdeu o trilho. O trilho não perdeu {A}: ele está ali esperando."), L.N("Lá se foi {A}! A física cobrou a conta."), L.N("{A} sai da pista e vai fazer turismo na mesa.") },
            ["saida3"] = new[] { L.N("Terceira vez, {A}. O fiscal já sabe seu nome."), L.N("{A} sai de novo! Já é freguês do carpete."), L.N("Três saídas para {A}. Coleção bonita."), L.N("Terceira escapada de {A}. Já pode pedir música no Fantástico."), L.N("{A} completa o trio de saídas. Ganha brinde?") },
            ["saida5"] = new[] { L.N("{A} fora outra vez! A essa altura é estratégia."), L.N("Mais uma de {A}. Alguém segura esse acelerador!"), L.N("{A} já conhece cada canto da mesa."), L.N("Quinta saída de {A}! O fiscal pediu aumento."), L.N("{A} de novo no carpete. Cinco vezes! Já tem cadeira cativa.") },
            ["saidamuitas"] = new[] { L.N("{A} sai pela {B}ª vez! A gente já parou de contar... não, não parou: {B}."), L.N("{B} saídas para {A}. Isso já é um recorde, só que do tipo errado."), L.N("{A}, {B} saídas! O carpete vai cobrar aluguel."), L.N("Saída número {B} de {A}. O fiscal já está almoçando ali do lado."), L.N("{A} chega a {B} saídas. Alguém dá um manual para esse piloto!") },
            ["saidarepete"] = new[] { L.N("De novo, {A}?! Mal voltou e já saiu!"), L.N("{A} voltou para a pista só para sair outra vez."), L.N("Nem esquentou o trilho e {A} já está fora de novo!"), L.N("{A} gostou tanto do carpete que voltou lá."), L.N("O fiscal nem largou o carrinho e {A} já saiu de novo."), L.N("{A} repete a dose! Calma nesse gatilho!") },
            ["saidalider"] = new[] { L.N("O líder rodou! {A} sai da pista!"), L.N("Que susto! {A}, na ponta, vai para o carpete!"), L.N("{A} jogou fora a liderança na curva!"), L.N("Liderança no chão! {A} saiu da pista!"), L.N("Abriu a corrida! O líder {A} escapou!"), L.N("{A} estava voando na frente... e voou mesmo. Fora da pista!") },
            ["saidafinal"] = new[] { L.N("Agora não, {A}! Saída na hora mais cara!"), L.N("{A} sai da pista na reta final! Que drama!"), L.N("Logo agora! {A} vai para o carpete no fim da prova!"), L.N("{A} escolheu o pior momento para rodar."), L.N("Pressão demais! {A} escapa no finalzinho!") },
            ["saidaturbo"] = new[] { L.N("{A} ligou o turbo e esqueceu de fazer a curva!"), L.N("Turbo demais, curva de menos! {A} fora!"), L.N("{A} foi de turbo direto para o carpete!"), L.N("O turbo levou {A} para fora da pista. Tinha que tirar o pé!"), L.N("{A} usou o turbo para voar. Literalmente.") },
            ["saidadupla"] = new[] { L.N("{A} e {B} saem juntos! Que confusão!"), L.N("Dois de uma vez! {A} e {B} no carpete!"), L.N("Saída dupla: {A} e {B} foram passear juntos."), L.N("{A} e {B} se enroscaram e foram os dois para fora!"), L.N("Strike! {A} e {B} fora da pista!") },
            ["batida"] = new[] { L.N("Encostou e voou! {A} sai da pista depois do toque!"), L.N("Batida! {A} levou a pior e foi para fora!"), L.N("Teve contato e {A} rodou!"), L.N("{A} tomou uma pancada e saiu da pista!"), L.N("Toque, rodada, carpete! {A} fora!"), L.N("Ninguém quis tirar o pé, e {A} pagou o pato.") },
            ["perdeu"] = new[] { L.N("{A} volta à pista em {B}. Foram {T} posições no carpete."), L.N("A saída custou caro: {A} cai para {B}."), L.N("{A} volta, mas agora em {B}. Perdeu {T} posições."), L.N("{A} está de volta, só que em {B}. Hora de recuperar!"), L.N("{T} posições a menos para {A}, que volta em {B}.") },
            ["perdeulider"] = new[] { L.N("{A} volta à pista, mas a ponta agora é de {B}!"), L.N("{B} herda a liderança da saída de {A}!"), L.N("Presente para {B}: a liderança caiu no colo depois do erro de {A}."), L.N("{A} perdeu a ponta no carpete. {B} agradece!"), L.N("{B} não precisou passar ninguém: {A} saiu e {B} assumiu.") },
            ["loop"] = new[] { L.N("{A} não segurou no loop! A gravidade manda lembranças."), L.N("Caiu no loop! {A} esqueceu de acelerar lá em cima."), L.N("{A} deu meia volta no loop... e despencou."), L.N("{A} ficou de cabeça para baixo e a gravidade venceu."), L.N("Loop sem fôlego! {A} despenca lá de cima."), L.N("{A} subiu o loop devagar demais. Newton agradece.") },
            ["salto"] = new[] { L.N("{A} pulou e não chegou! Faltou coragem!"), L.N("{A} ficou no vão do salto. Aterrissagem cancelada."), L.N("Saltou, voou... e caiu! {A} fora."), L.N("{A} achou que sabia voar. Não sabia."), L.N("Faltou embalo! {A} cai no vão."), L.N("{A} pulou com o freio de mão puxado!") },
            ["turbo"] = new[] { L.N("{A} soltou o turbo! Segura!"), L.N("Turbo ligado para {A}!"), L.N("{A} apertou o botão mágico!"), L.N("Lá vai {A} de turbo. Cuidado na curva!"), L.N("{A} acende o turbo! Lá vai foguete!"), L.N("Turbo de {A}! Quem está na frente que se cuide."), L.N("{A} no turbo! Agora é segurar o carrinho no trilho.") },
            ["recorde"] = new[] { L.N("RECORDE DA PISTA! {A} escreve o nome na história... desta mesa. {T}!"), L.N("Recorde! {A} faz {T}, a volta mais rápida que essa pista já viu!"), L.N("Que volta, {A}! {T}, recorde da pista!"), L.N("Segura essa: {T}! {A} quebra o recorde da pista!"), L.N("Podem emoldurar: {A}, {T}, novo recorde da pista!"), L.N("Inacreditável! {T}! {A} é o dono do recorde!") },
            ["pessoal"] = new[] { L.N("Melhor volta pessoal de {A}!"), L.N("{A} se superou nessa volta."), L.N("Volta caprichada de {A}: a melhor da carreira nesta pista."), L.N("{A} bate o próprio tempo. Evolução!"), L.N("{A} nunca andou tão rápido aqui.") },
            ["melhorvolta"] = new[] { L.N("{A} crava {T}, a volta mais rápida da prova!"), L.N("Melhor volta da corrida para {A}: {T}!"), L.N("{A} acelerou forte: {T}, ninguém andou tão rápido hoje."), L.N("Cronômetro em chamas! {T} para {A}."), L.N("{A} baixa o tempo para {T}. Que mão!"), L.N("{T}! {A} manda a volta mais rápida da corrida."), L.N("{A} acha mais um décimo: {T}, a melhor da prova!") },
            ["disputa"] = new[] { L.N("{A} e {B} colados há um tempão. Ninguém respira!"), L.N("Que briga entre {A} e {B}!"), L.N("{B} está no cangote de {A}!"), L.N("{A} e {B} dividem até o mesmo pensamento."), L.N("{B} não desgruda de {A}! Vai ter troca?"), L.N("{A} olha o retrovisor e só vê {B}."), L.N("Duelo! {A} segura, {B} pressiona.") },
            ["ultima"] = new[] { L.N("Última volta!"), L.N("Sino tocando: última volta!"), L.N("É agora ou nunca: última volta!"), L.N("Última volta! Quem guardou turbo, a hora é essa!"), L.N("Última volta! Coração na mão!") },
            ["dez"] = new[] { L.N("Dez segundos! Quem quer, vai agora!"), L.N("Faltam dez! Ninguém tira o pé!"), L.N("Dez segundinhos e acabou!") },
            ["cinco"] = new[] { L.N("Cinco segundos!"), L.N("Cinco! Segura o coração!"), L.N("Cinco segundos e é bandeira!") },
            ["3"] = new[] { "3!" },
            ["2"] = new[] { "2!" },
            ["1"] = new[] { "1!" },
            ["quinze"] = new[] { L.N("Quinze segundos! Tudo ou nada!"), L.N("Faltam quinze segundos! Pé no fundo!"), L.N("Reta final: quinze segundos!") },
            ["vitoria"] = new[] { L.N("Bandeirada! {A} vence!"), L.N("{A} cruza na frente! Que corrida!"), L.N("Vitória de {A}! Pode chamar a família!"), L.N("É de {A}! Bandeira quadriculada!"), L.N("{A} vence! Pode estourar o refrigerante!"), L.N("Acabou! {A} é o grande vencedor!"), L.N("Quadriculada para {A}! Vitória com sabor de pódio!") },
            ["marcolider"] = new[] { L.N("{A} completa {B} voltas na frente de todo mundo!"), L.N("{B} voltas e {A} segue mandando na corrida!"), L.N("{A} na ponta depois de {B} voltas. Que pilotagem!"), L.N("Passadas {B} voltas, quem dita o ritmo é {A}!"), L.N("{B} voltas, e {A} ainda não viu ninguém pelo retrovisor."), L.N("{A} lidera com {B} voltas. Os outros que corram atrás."), L.N("Volta {B}: {A} continua com o nariz na frente.") },
            ["marcoultimo"] = new[] { L.N("{A} fecha a volta {B} em último. Está apreciando a paisagem?"), L.N("{B} voltas e {A} lá atrás. Alguém avisa que já começou!"), L.N("{A} completa {B} voltas por último. Sem pressa, a mesa não vai fugir."), L.N("Volta {B}: {A} na lanterna. Pelo menos o carro está limpinho."), L.N("{A} chega à volta {B} por último. Tá de passeio, {A}?"), L.N("{A} na lanterna depois de {B} voltas. Ainda dá tempo... talvez.") },
            ["ultimo"] = new[] { L.N("E {A} chega em último, mas chega!"), L.N("{A} fecha o pelotão. O importante é competir."), L.N("{A} chegou! Atrasado, mas com estilo."), L.N("{A} cruza por último. Na próxima vai!"), L.N("Lá vem {A}, fechando a porta e apagando a luz.") },
            ["campassume"] = new[] { L.N("Se terminar assim, {A} assume a liderança do campeonato!"), L.N("Do jeito que está, {A} vira líder do campeonato!"), L.N("Atenção na tabela: {A} passa {B} no campeonato!"), L.N("A tabela está virando: {A} na frente de {B}!") },
            ["camprecupera"] = new[] { L.N("{A} recupera a liderança do campeonato!"), L.N("Do jeito que está, {A} volta para a ponta do campeonato."), L.N("{A} reage e retoma o topo da tabela!"), L.N("{A} devolve o troco e volta ao topo do campeonato!") },
            ["liderfora"] = new[] { L.N("{A}, líder do campeonato, está em {B}. Hoje não está fácil."), L.N("O líder do campeonato, {A}, só em {B}! A tabela está tremendo."), L.N("{A} lidera o campeonato, mas aqui está em {B}."), L.N("Dia difícil para {A}: líder do campeonato e só em {B}.") },
            ["titulo"] = new[] { L.N("O título é de {A} se terminar assim!"), L.N("O título está nas mãos de {A}! Só não pode errar."), L.N("Uma volta separa {A} da taça!"), L.N("{A} já sente o cheiro da taça!") },
        };
    }
}
