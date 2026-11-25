using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // Modo carreira (seção 68 do guia). A definição vem no jogo (Resources/Carreira/carreira.json e as pistas em
    // Resources/Carreira/pistas); o progresso do jogador fica em persistentDataPath/autorama/carreira-N.json (3 slots).

    [Serializable] public class CareerDef { public RegionDef[] regioes; }

    [Serializable]
    public class RegionDef
    {
        public string id, nome;
        public int faixas = 2;
        public int estrelasRival;   // estrelas da região para abrir o nó "rival"
        public int estrelasAbrir;   // estrelas somadas das regiões anteriores para abrir esta (seção 92; 0 = só ser campeão da anterior)
        public int nivelIA;         // nível da garagem dos carros da IA (0–10, seção 92): todas as trilhas da opção que eles usam
        public PilotoDef[] pilotos; // quem corre nas provas comuns da região (nome e cor; o carro e o jeito seguem a regra da IA)
        public NodeDef[] nos;
    }

    [Serializable]
    public class NodeDef
    {
        public string id, nome;
        public string tipo = "corrida";   // corrida · relogio · desafio · rival (depois: construtor, campeonato)
        public string[] depois;           // abre quando QUALQUER um destes tem 1 estrela (vazio: aberto desde o começo)
        public string pista;              // arquivo em Resources/Carreira/pistas, sem .json
        public int voltas = 3;
        public int ia = 50;               // qualidade da IA, 1–200 (101–150 mestres, 151–200 lendas; seção 92)
        public int adversarios = 1;       // carros da IA (no relógio: 0)
        public bool turbo = true;         // no relógio fica sempre desligado
        public Objetivo[] estrelas;       // 3: a 1ª é concluir; sem ela, nenhuma estrela

        // Desafio (seção 71): regras que se somam. molhada (aderência × Career.Wet) · carro (o ajuste "carro" imposto)
        // · sobrevivencia (mais de "limite" saídas e está fora) · atrasada (larga "atraso" s depois da luz verde)
        // · turboLivre (tanque sempre cheio, para todos).
        public string[] regras;
        public CarSetup carro;
        public int limite = 3;
        public float atraso = 2f;
        public float intervalo = 20f;     // eliminacao (seção 92): a cada "intervalo" s o último sai, até sobrar um
        public int minEstrelas;           // convite (seção 92): só abre com essas estrelas na região

        // Rival (seção 71): melhor de "baterias", trocando de trilho a cada uma. Nome, cor (#RRGGBB), carro e as falas.
        public string rival, rivalCor, rivalFrase, rivalVitoria, rivalDerrota;
        public CarSetup rivalCarro;
        public int rivalNivel;            // nível da garagem só do rival (0: o da região; acima de 10 só o Felix, seção 92)
        public string rivalEstilo;        // jeito do rival (seção 92): agressivo · constante · nervoso (vazio: normal)
        public int baterias = 3;

        // Construtor (seção 72): o jogador monta a pista e corre nela. pecas = ids permitidos; rampas = pode subir e
        // descer; maxPecas = limite (sem a largada); exigir = ids que precisam aparecer pelo menos uma vez.
        public string[] pecas, exigir;
        public bool rampas;
        public int maxPecas;

        // Campeonato da região (seção 73): etapas (ids de pista), os pilotos IA e o rodízio do campeonato offline.
        public string[] etapas;
        public PilotoDef[] pilotos;
        // Por tempo (seção 75): uma bateria por etapa com todos, passando por todos os trilhos em manches de segundos/N.
        public bool porTempo;
        public int segundos = 120;
        public int premio;                // prêmio em moedas do 1º lugar (seção 92); os outros, a fração de Career.PursePart

        public bool Has(string regra) => regras != null && System.Array.IndexOf(regras, regra) >= 0;
    }

    [Serializable] public class PilotoDef { public string nome, cor, estilo; public int ia = 50, nivel; public CarSetup carro; }   // nivel: 0 = o da região

    // tipo: posicao (chegar até a posição valor) · semSair (no máximo valor saídas) · semTurbo · volta (melhor volta até valor s)
    //       · tempo (prova inteira até valor s) · lider (liderar valor s no total)
    // dono: de quem é a marca (seção 92), só no texto: "Bater o recorde de Felix Badar (8,10)".
    [Serializable] public class Objetivo { public string tipo, dono; public float valor; }

    // O que a corrida devolve ao fim, do ponto de vista do jogador (carro 0).
    public struct ResultadoCorrida
    {
        public bool terminou;
        public int posicao, carros, saidas;
        public bool turbo;
        public float melhorVolta;   // < 0: nenhuma volta completa
        public float tempo;
        public int vitorias, derrotas;   // só no rival: baterias ganhas e perdidas
        public int etapas, etapasVencidas;   // só no campeonato: posicao é a da tabela final
        public float lider;                  // segundos na liderança (seção 92)
    }

    [Serializable] public class NodeSave { public string id; public int estrelas; public int melhorPos; public float melhorVolta = -1f; public string pista; }   // pista: a montada no nó de construtor (JSON)
    [Serializable]
    public class CareerSave
    {
        public List<NodeSave> nos = new List<NodeSave>();
        public Championship campeonato;   // o campeonato da região em andamento (seção 73); sem baterias = nenhum
        public string campNo;             // o nó dele
        // Moedas e garagem de antes da carteira (seção 94): só lidas uma vez, para passar para a Carteira.
        public int moedas;
        public CarGarage garagem = new CarGarage();
        public string ultimoNo;           // contra farm: a mesma prova várias vezes seguidas paga menos
        public int seguidas;
        public int versao;                // 1: moedas no save (os antigos recebem as moedas das estrelas que já têm); 2: níveis por opção
    }

    public static class Career
    {
        public const int Slots = 1;     // um save só (seção 94); recomeçar apaga ele

        static CareerDef def;
        public static CareerDef Def => def ??= Traduz(JsonUtility.FromJson<CareerDef>(Resources.Load<TextAsset>("Carreira/carreira").text));

        // Nomes das regiões e das provas e as falas do rival no idioma do jogo (seção 88); os ids não mudam.
        static CareerDef Traduz(CareerDef d)
        {
            foreach (var r in d.regioes)
            {
                r.nome = L.T(r.nome);
                foreach (var n in r.nos) { n.nome = L.T(n.nome); n.rivalFrase = L.T(n.rivalFrase); n.rivalVitoria = L.T(n.rivalVitoria); n.rivalDerrota = L.T(n.rivalDerrota); }
            }
            return d;
        }
        public static void Reload() => def = null;

        public static TrackSave Track(string id)
        {
            var t = Resources.Load<TextAsset>("Carreira/pistas/" + id);
            return t != null ? TrackSave.FromJson(t.text) : null;
        }

        // ----- estrelas -----

        public static bool Cumpre(Objetivo o, ResultadoCorrida r)
        {
            switch (o.tipo)
            {
                case "posicao": return r.terminou && r.posicao <= o.valor;
                case "semSair": return r.terminou && r.saidas <= o.valor;
                case "semTurbo": return r.terminou && !r.turbo;
                case "volta": return r.melhorVolta >= 0f && r.melhorVolta <= o.valor;
                case "tempo": return r.terminou && r.tempo <= o.valor;
                case "serie": return r.terminou && r.vitorias > r.derrotas;
                case "invicto": return r.terminou && r.vitorias > 0 && r.derrotas == 0;
                case "todasEtapas": return r.terminou && r.etapas > 0 && r.etapasVencidas == r.etapas;
                case "lider": return r.terminou && r.lider >= o.valor;
                default: return false;
            }
        }

        // A 1ª estrela é concluir; sem ela não vale nenhuma. As outras duas valem cada uma por si.
        public static int Avaliar(NodeDef n, ResultadoCorrida r, CareerSave sv = null)
        {
            if (n.estrelas == null || n.estrelas.Length == 0 || !Cumpre(Meta(sv, n, 0), r)) return 0;
            int s = 1;
            for (int i = 1; i < n.estrelas.Length && i < 3; i++) if (Cumpre(Meta(sv, n, i), r)) s++;
            return s;
        }

        // A prova tem turbo, mas o jogador ainda não tem a lata de nitro (seção 104): ninguém usa turbo nela.
        public static bool SemLata(CareerSave s, NodeDef n) => s != null && n.tipo != "relogio" && n.turbo && !n.Has("turboLivre") && !Has(s, ItemTurbo);
        // O objetivo i da prova. Sem a lata, "Sem usar o turbo" sairia de graça: vira "Sem sair da pista".
        static readonly Objetivo SemSairMeta = new Objetivo { tipo = "semSair", valor = 0f };
        public static Objetivo Meta(CareerSave s, NodeDef n, int i) => n.estrelas[i].tipo == "semTurbo" && SemLata(s, n) ? SemSairMeta : n.estrelas[i];

        public static string Texto(Objetivo o)
        {
            switch (o.tipo)
            {
                case "posicao": return o.valor <= 1 ? L.T("Vencer") : L.F("Chegar até o {0:0}º", o.valor);
                case "semSair": return o.valor <= 0 ? L.T("Sem sair da pista") : L.F("No máximo {0:0} saídas", o.valor);
                case "semTurbo": return L.T("Sem usar o turbo");
                case "volta": return string.IsNullOrEmpty(o.dono) ? L.F("Volta abaixo de {0}", RaceController.Fmt(o.valor)) : L.F("Bater o recorde de {0} ({1})", o.dono, RaceController.Fmt(o.valor));
                case "tempo": return L.F("Prova abaixo de {0}", RaceController.Fmt(o.valor));
                case "serie": return L.T("Vencer a série");
                case "invicto": return L.T("Vencer sem perder bateria");
                case "todasEtapas": return L.T("Vencer todas as etapas");
                case "lider": return L.F("Liderar por {0:0} s", o.valor);
                default: return o.tipo;
            }
        }

        public const float Wet = 0.8f;   // pista molhada: aderência × 0,8

        public static string RegraTexto(NodeDef n, string regra)
        {
            switch (regra)
            {
                case "molhada": return L.T("Pista molhada: 20% menos aderência");
                case "carro": return L.F("Carro imposto: {0}", (n.carro.Padrao ? L.T("padrão") : n.carro.Resumo));
                case "sobrevivencia": return L.F("Mais de {0} {1} e você está fora", n.limite, (n.limite == 1 ? L.T("saída") : L.T("saídas")));
                case "atrasada": return L.F("Você larga {0:0.#} s depois", n.atraso);
                case "turboLivre": return L.T("Turbo sempre cheio, para todos");
                case "eliminacao": return L.F("A cada {0:0} s o último sai da pista, até sobrar um", n.intervalo);
                default: return regra;
            }
        }

        // ----- construtor (seção 72) -----

        // Vale a peça da prova ou qualquer peça que o jogador já abriu (Progresso); rampa, se a prova deixa ou se já abriu as rampas.
        public static bool PieceOk(NodeDef n, string id, int rise) =>
            id == PieceCatalog.Start.Id || ((n.pecas != null && Array.IndexOf(n.pecas, id) >= 0 || Progresso.PieceOk(id)) && (rise == 0 || RampasOk(n)));
        public static bool RampasOk(NodeDef n) => n.rampas || Progresso.Owns("rampas");

        public static string PieceName(string id) => PieceCatalog.ById(id)?.Name ?? id;

        // A pista montada vale? Devolve "" se vale; senão, o motivo.
        public static string CheckBuild(NodeDef n, TrackSave t)
        {
            var ps = t.Pieces();
            if (ps.Count < 3) return L.T("Monte a pista primeiro.");
            if (n.maxPecas > 0 && ps.Count - 1 > n.maxPecas) return L.F("Passou do limite: {0} de {1} peças.", ps.Count - 1, n.maxPecas);
            foreach (var p in ps) if (!PieceOk(n, p.id, p.rise)) return L.F("{0}{1} não vale nesta prova.", PieceName(p.id), (p.rise != 0 ? L.T(" com rampa") : ""));
            if (n.exigir != null) foreach (var id in n.exigir) if (!ps.Exists(p => p.id == id)) return L.F("Falta usar: {0}.", PieceName(id));
            var a = TrackLayout.Analyze(t.Origin, TrackLayout.Build(t.Origin, ps, t.Tail, t.StartZ), t.Tail, t.Faixas);
            if (!a.closed) return L.T("Feche o circuito (Q fecha sozinho).");
            if (a.overlaps.Count > 0) return L.T("A pista passa por cima dela mesma.");
            return "";
        }

        // ----- progresso -----

        public static NodeSave Of(CareerSave s, string id) => s.nos.Find(x => x.id == id);
        public static int Stars(CareerSave s, string id) => Of(s, id)?.estrelas ?? 0;

        // Objetos do enredo (seção 104, docs/enredo.md): cada um vem de um nó quando ele chega às estrelas pedidas
        // (2 = campeão). Os que abrem um controle (meia, ajuste fino, turbo) só valem na carreira; fora dela tudo é livre.
        public class ItemDef
        {
            public string id, nome, no, abre; public int estrelas;
            public ItemDef(string id, string nome, string no, int estrelas, string abre) { this.id = id; this.nome = nome; this.no = no; this.estrelas = estrelas; this.abre = abre; }
        }
        public const string ItemMeia = "gatilho-duplo", ItemTurbo = "lata-nitro", ItemFino = "controle-celia";
        public static readonly ItemDef[] Itens =
        {
            new ItemDef(ItemMeia, L.N("Gatilho de dois estágios"), "garagem-oficina", 1, L.N("meia aceleração")),
            new ItemDef(ItemTurbo, L.N("Lata de nitro do Fê"), "garagem-8", 1, L.N("turbo")),
            new ItemDef("caixa-grande", L.N("Caixa grande do Fê"), "garagem-9", 1, ""),
            new ItemDef(ItemFino, L.N("Controle da Dona Célia"), "clube-taca", 2, L.N("ajuste fino da meia aceleração")),
            new ItemDef("caderno-curva", L.N("Caderno do Dr. Curva"), "regional-drcurva", 1, ""),
            new ItemDef("foto-96", L.N("Foto de 1996"), "nacional-campeao", 1, ""),
            new ItemDef("carro-fe", L.N("Carro do Fê"), "global-mundial", 2, L.N("bolha Fórmula")),
        };
        public static bool Has(CareerSave s, string id)
        {
            if (Progresso.All || s == null) return true;
            foreach (var i in Itens) if (i.id == id) return Stars(s, i.no) >= i.estrelas;
            return true;
        }
        // Objetos que o nó entrega com essas estrelas (para o cartão e o resultado).
        public static IEnumerable<ItemDef> ItemsOf(string no, int estrelas)
        {
            foreach (var i in Itens) if (i.no == no && estrelas >= i.estrelas) yield return i;
        }
        public static string ItemTexto(ItemDef i) => string.IsNullOrEmpty(i.abre) ? L.T(i.nome) : L.F("{0} ({1})", L.T(i.nome), L.T(i.abre));

        public static int RegionStars(CareerSave s, RegionDef r)
        {
            int n = 0;
            foreach (var x in r.nos) n += Stars(s, x.id);
            return n;
        }

        // Região aberta: a primeira, ou a anterior com o último nó concluído; se ele é o campeonato, só sendo campeão (★★).
        // Com estrelasAbrir (seção 92), também as estrelas somadas das regiões anteriores.
        // A região do nó (-1 se não existe).
        public static int RegionOf(string no)
        {
            for (int r = 0; r < Def.regioes.Length; r++) foreach (var n in Def.regioes[r].nos) if (n.id == no) return r;
            return -1;
        }

        public static bool RegionOpen(CareerSave s, int i)
        {
            if (i <= 0) return true;
            var prev = Def.regioes[i - 1];
            if (!RegionOpen(s, i - 1) || prev.nos.Length == 0) return false;
            var last = prev.nos[prev.nos.Length - 1];
            return Stars(s, last.id) >= (last.tipo == "campeonato" ? 2 : 1) && StarsBefore(s, i) >= Def.regioes[i].estrelasAbrir;
        }

        public static int StarsBefore(CareerSave s, int i)
        {
            int n = 0;
            for (int r = 0; r < i && r < Def.regioes.Length; r++) n += RegionStars(s, Def.regioes[r]);
            return n;
        }

        // O último nó da região (o campeonato que abre a próxima).
        public static bool IsFinal(RegionDef r, NodeDef n) => r.nos.Length > 0 && r.nos[r.nos.Length - 1] == n;

        // O maior tamanho de pista (faixas) já conquistado num save: o da região mais longe aberta (para a corrida livre, depois).
        public static int FaixasAbertas(CareerSave s)
        {
            int f = 2;
            for (int i = 0; i < Def.regioes.Length; i++) if (RegionOpen(s, i)) f = Mathf.Max(f, Def.regioes[i].faixas);
            return f;
        }

        // Pilotos dos carros 1..ais de uma prova comum: os da região (RegionDef.pilotos) embaralhados pela prova, sem o
        // rival dela (o rival é o carro 1). Mesma prova, mesmos pilotos nos mesmos carros. Faltando gente, fica null.
        public static void Grid(RegionDef r, NodeDef n, int ais, PilotoDef[] grid)
        {
            System.Array.Clear(grid, 0, grid.Length);
            if (r?.pilotos == null) return;
            var pool = new List<PilotoDef>();
            foreach (var p in r.pilotos) if (p != null && !string.IsNullOrEmpty(p.nome) && p.nome != n.rival) pool.Add(p);
            var rnd = new System.Random(RaceController.SeedOf(n.id, 0));
            for (int i = pool.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            int first = n.tipo == "rival" ? 2 : 1;
            for (int k = first, i = 0; k <= ais && k < grid.Length && i < pool.Count; k++, i++) grid[k] = pool[i];
        }

        public static bool NodeOpen(CareerSave s, RegionDef r, NodeDef n)
        {
            if (n.tipo == "rival" && RegionStars(s, r) < r.estrelasRival) return false;
            if (n.minEstrelas > 0 && RegionStars(s, r) < n.minEstrelas) return false;
            if (n.depois == null || n.depois.Length == 0) return true;
            foreach (var d in n.depois) if (Stars(s, d) > 0) return true;
            return false;
        }

        // Guarda o melhor de cada coisa; devolve quantas estrelas NOVAS a corrida deu.
        public static int Record(CareerSave s, NodeDef n, ResultadoCorrida r)
        {
            int got = Avaliar(n, r, s);
            var ns = Of(s, n.id);
            if (ns == null) { ns = new NodeSave { id = n.id }; s.nos.Add(ns); }
            int novas = Mathf.Max(0, got - ns.estrelas);
            ns.estrelas = Mathf.Max(ns.estrelas, got);
            if (r.terminou && (ns.melhorPos == 0 || r.posicao < ns.melhorPos)) ns.melhorPos = r.posicao;
            if (r.melhorVolta >= 0f && (ns.melhorVolta < 0f || r.melhorVolta < ns.melhorVolta)) ns.melhorVolta = r.melhorVolta;
            return novas;
        }

        // ----- arquivos -----

        public static string PathOf(int slot) => Path.Combine(TrackLibrary.Root, $"carreira-{Mathf.Clamp(slot, 1, Slots)}.json");

        // Prêmios em moedas (seção 78): cada estrela nova de um nó paga uma vez; rival vencido e campeão da região, uma
        // vez; a corrida em si, 10 + 5 por carro que ficou atrás (metade a partir da 4ª vez seguida na mesma prova).
        public static readonly int[] StarPrize = { 75, 100, 150 };
        public const int RivalPrize = 300, RacePrize = 10, PlacePrize = 5, Streak = 3;
        public static readonly int[] ChampPrize = { 800, 1200, 2000, 3000, 5000 };   // campeão da região pela 1ª vez (só o último nó)
        // Prêmio do campeonato por posição (seção 92): o 1º leva NodeDef.premio; os outros, esta fração dele. Paga toda vez.
        public static readonly float[] PursePart = { 1f, 0.6f, 0.4f, 0.25f, 0.15f, 0.1f, 0.06f, 0.04f };
        public static int Purse(NodeDef n, int pos) => n.premio <= 0 || pos < 1 || pos > PursePart.Length ? 0 : Mathf.RoundToInt(n.premio * PursePart[pos - 1] / 10f) * 10;

        // Jeito do piloto IA (seção 92), do texto do carreira.json para RaceSim.aiStyle.
        public static int Style(string e) => e == "agressivo" ? RaceSim.StyleAgressivo : e == "constante" ? RaceSim.StyleConstante : e == "nervoso" ? RaceSim.StyleNervoso : RaceSim.StyleNormal;
        // Adversários sem nome: os jeitos se revezam pelo número do carro.
        public static int StyleOf(int car) => (car - 1) % 4;

        // Carro da IA com os níveis da garagem da região (seção 92): todas as trilhas no nível nivelIA.
        public const int MaxNivelIA = 15;
        public static CarSetup AiCar(CarSetup c, RegionDef r, int nivel = 0)
        {
            c = c.Clamped;
            int lv = Mathf.Clamp(nivel > 0 ? nivel : r != null ? r.nivelIA : 0, 0, MaxNivelIA);
            c.lvMotor = c.lvBolha = c.lvPneu = c.lvChassi = c.lvNitro = lv;
            return c;
        }
        static readonly int[] OldPrep = { 300, 600, 1000 };

        // Save de antes das moedas no save: as estrelas, rivais e títulos que ele já tem, pagos de uma vez.
        static void Upgrade(CareerSave s)
        {
            s.garagem ??= new CarGarage();
            CarShop.Fix(s.garagem);
            if (s.versao == 1)
            {
                // A preparação de 3 níveis (300, 600, 1000 por sistema) virou níveis por opção: devolve o que foi gasto.
                var g = s.garagem;
                foreach (int lv in new[] { g.motor, g.pneu, g.bolha, g.nitro })
                    for (int i = 0; i < lv && i < OldPrep.Length; i++) s.moedas += OldPrep[i];
                g.motor = g.pneu = g.bolha = g.nitro = 0;
            }
            if (s.versao >= 1) { s.versao = 2; return; }
            s.versao = 2;
            var R = Def.regioes;
            for (int r = 0; r < R.Length; r++)
                foreach (var n in R[r].nos)
                {
                    int st = Stars(s, n.id);
                    for (int i = 0; i < st && i < StarPrize.Length; i++) s.moedas += StarPrize[i];
                    if (n.tipo == "rival" && st > 0) s.moedas += RivalPrize;
                    if (n.tipo == "campeonato" && st >= 2 && IsFinal(R[r], n)) s.moedas += ChampPrize[Mathf.Clamp(r, 0, ChampPrize.Length - 1)];
                }
        }

        // O save como está no arquivo (a Carteira lê assim ao migrar, antes de existir).
        public static CareerSave LoadRaw(int slot) => Load(slot);

        // Recomeçar (seção 94): o save vira .bak e a carreira começa do zero. A carteira fica.
        public static void Delete(int slot)
        {
            try { var p = PathOf(slot); if (File.Exists(p)) { File.Copy(p, p + ".bak", true); File.Delete(p); } }
            catch (Exception e) { Debug.LogWarning("Carreira não apagada: " + e.Message); }
            Progresso.Refresh();
        }

        public static CareerSave Load(int slot)
        {
            try
            {
                var p = PathOf(slot);
                if (!File.Exists(p)) return new CareerSave { versao = 2 };
                var s = JsonUtility.FromJson<CareerSave>(File.ReadAllText(p)) ?? new CareerSave();
                Upgrade(s);
                return s;
            }
            catch { return new CareerSave(); }
        }

        public static bool Save(int slot, CareerSave s)
        {
            try { Directory.CreateDirectory(TrackLibrary.Root); File.WriteAllText(PathOf(slot), JsonUtility.ToJson(s, true)); Carteira.Sync(s); Carteira.Save(); Progresso.Refresh(); return true; }
            catch (Exception e) { Debug.LogWarning("Carreira não salva: " + e.Message); return false; }
        }
    }
}
