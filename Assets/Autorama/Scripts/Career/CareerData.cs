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
        public int ia = 50;               // qualidade da IA, 1–100
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

        // Rival (seção 71): melhor de "baterias", trocando de trilho a cada uma. Nome, cor (#RRGGBB), carro e as falas.
        public string rival, rivalCor, rivalFrase, rivalVitoria, rivalDerrota;
        public CarSetup rivalCarro;
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

        public bool Has(string regra) => regras != null && System.Array.IndexOf(regras, regra) >= 0;
    }

    [Serializable] public class PilotoDef { public string nome, cor; public int ia = 50; public CarSetup carro; }

    // tipo: posicao (chegar até a posição valor) · semSair (no máximo valor saídas) · semTurbo · volta (melhor volta até valor s)
    //       · tempo (prova inteira até valor s)
    [Serializable] public class Objetivo { public string tipo; public float valor; }

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
    }

    [Serializable] public class NodeSave { public string id; public int estrelas; public int melhorPos; public float melhorVolta = -1f; public string pista; }   // pista: a montada no nó de construtor (JSON)
    [Serializable]
    public class CareerSave
    {
        public List<NodeSave> nos = new List<NodeSave>();
        public Championship campeonato;   // o campeonato da região em andamento (seção 73); sem baterias = nenhum
        public string campNo;             // o nó dele
        // Moedas e garagem do carro (seção 78): cada save tem as suas.
        public int moedas;
        public CarGarage garagem = new CarGarage();
        public string ultimoNo;           // contra farm: a mesma prova várias vezes seguidas paga menos
        public int seguidas;
        public int versao;                // 1: moedas no save (os antigos recebem as moedas das estrelas que já têm); 2: níveis por opção
    }

    public static class Career
    {
        public const int Slots = 3;

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
                default: return false;
            }
        }

        // A 1ª estrela é concluir; sem ela não vale nenhuma. As outras duas valem cada uma por si.
        public static int Avaliar(NodeDef n, ResultadoCorrida r)
        {
            if (n.estrelas == null || n.estrelas.Length == 0 || !Cumpre(n.estrelas[0], r)) return 0;
            int s = 1;
            for (int i = 1; i < n.estrelas.Length && i < 3; i++) if (Cumpre(n.estrelas[i], r)) s++;
            return s;
        }

        public static string Texto(Objetivo o)
        {
            switch (o.tipo)
            {
                case "posicao": return o.valor <= 1 ? L.T("Vencer") : L.F("Chegar até o {0:0}º", o.valor);
                case "semSair": return o.valor <= 0 ? L.T("Sem sair da pista") : L.F("No máximo {0:0} saídas", o.valor);
                case "semTurbo": return L.T("Sem usar o turbo");
                case "volta": return L.F("Volta abaixo de {0}", RaceController.Fmt(o.valor));
                case "tempo": return L.F("Prova abaixo de {0}", RaceController.Fmt(o.valor));
                case "serie": return L.T("Vencer a série");
                case "invicto": return L.T("Vencer sem perder bateria");
                case "todasEtapas": return L.T("Vencer todas as etapas");
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

        public static int RegionStars(CareerSave s, RegionDef r)
        {
            int n = 0;
            foreach (var x in r.nos) n += Stars(s, x.id);
            return n;
        }

        // Região aberta: a primeira, ou a anterior com o último nó concluído; se ele é o campeonato, só sendo campeão (★★).
        public static bool RegionOpen(CareerSave s, int i)
        {
            if (i <= 0) return true;
            var prev = Def.regioes[i - 1];
            if (!RegionOpen(s, i - 1) || prev.nos.Length == 0) return false;
            var last = prev.nos[prev.nos.Length - 1];
            return Stars(s, last.id) >= (last.tipo == "campeonato" ? 2 : 1);
        }

        // O maior tamanho de pista (faixas) já conquistado num save: o da região mais longe aberta (para a corrida livre, depois).
        public static int FaixasAbertas(CareerSave s)
        {
            int f = 2;
            for (int i = 0; i < Def.regioes.Length; i++) if (RegionOpen(s, i)) f = Mathf.Max(f, Def.regioes[i].faixas);
            return f;
        }

        public static bool NodeOpen(CareerSave s, RegionDef r, NodeDef n)
        {
            if (n.tipo == "rival" && RegionStars(s, r) < r.estrelasRival) return false;
            if (n.depois == null || n.depois.Length == 0) return true;
            foreach (var d in n.depois) if (Stars(s, d) > 0) return true;
            return false;
        }

        // Guarda o melhor de cada coisa; devolve quantas estrelas NOVAS a corrida deu.
        public static int Record(CareerSave s, NodeDef n, ResultadoCorrida r)
        {
            int got = Avaliar(n, r);
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
        public static readonly int[] StarPrize = { 50, 75, 100 };
        public const int RivalPrize = 300, RacePrize = 10, PlacePrize = 5, Streak = 3;
        public static readonly int[] ChampPrize = { 500, 800, 1200, 2000 };
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
                    if (n.tipo == "campeonato" && st >= 2) s.moedas += ChampPrize[Mathf.Clamp(r, 0, ChampPrize.Length - 1)];
                }
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
            try { Directory.CreateDirectory(TrackLibrary.Root); File.WriteAllText(PathOf(slot), JsonUtility.ToJson(s, true)); Progresso.Refresh(); return true; }
            catch (Exception e) { Debug.LogWarning("Carreira não salva: " + e.Message); return false; }
        }
    }
}
