using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // Perfil do jogador (seção 78 do guia): por enquanto só o "Desbloquear tudo" de teste. Um por computador, em
    // persistentDataPath/autorama/perfil.json. As moedas ficam na Carteira (carteira.json, seção 94).
    [Serializable]
    public class PlayerProfile
    {
        public bool tudo;                 // "Desbloquear tudo" (só enquanto Progresso.Teste)
        // Tutorial (seção 102): 0 nunca fez, 1 terminou a construção (falta a corrida), 2 terminou, 3 pulou.
        public int tutorial;
        public string tutPista = "";      // a pista montada no tutorial (JSON), para a corrida voltar a ela
    }

    // Um grupo de peças do construtor e a prova da carreira que o abre (terminar com ★ em qualquer save).
    public class PieceUnlock
    {
        public string id, nome, pecas, no;
        public PieceUnlock(string id, string nome, string pecas, string no) { this.id = id; this.nome = nome; this.pecas = pecas; this.no = no; }
    }

    public static class Progresso
    {
        // Enquanto o jogo não sai: mostra o "Desbloquear tudo" na tela de Peças. No lançamento, false (o botão some
        // e o desbloqueio deixa de valer).
        public const bool Teste = true;

        // A prova que apresenta a peça abre a peça (seção 78): a primeira em que ela aparece.
        public static readonly PieceUnlock[] Pecas =
        {
            new PieceUnlock("curtinha", L.N("Reta curtinha"), L.N("reta-curtinha"), "garagem-oficina"),
            new PieceUnlock("inclinadas", L.N("Curvas inclinadas"), L.N("curva longa e curta inclinadas"), "clube-1"),
            new PieceUnlock("troca", L.N("Troca de faixa"), L.N("cruzamento e cruzamento curto"), "clube-2"),
            new PieceUnlock("rampas", L.N("Subida e descida"), L.N("rampas para ponte"), "clube-3"),
            new PieceUnlock("salto", L.N("Rampa de salto"), L.N("salto de vão 1"), "regional-1"),
            new PieceUnlock("curva-c", L.N("Curva em C"), L.N("curva em C plana e inclinada (1 a 3 fileiras)"), "regional-3"),
            new PieceUnlock("loop", L.N("Loop"), L.N("loop"), "nacional-1"),
            new PieceUnlock("salto-grande", L.N("Saltos grandes"), L.N("salto de vão 2 e 3"), "nacional-2"),
            // Seção 99.
            new PieceUnlock("fechada", L.N("Fechada"), L.N("os trilhos de cada par se juntam num só"), "clube-funil"),
            new PieceUnlock("meia-c", L.N("Meia curva em C"), L.N("meia curva em C plana e inclinada (1 a 3 fileiras)"), "regional-cotovelos"),
            new PieceUnlock("muro", L.N("Muro de parede"), L.N("a pista deita de lado: devagar, cai"), "nacional-parede"),
            new PieceUnlock("parafuso", L.N("Parafuso"), L.N("a pista dá uma volta inteira no eixo"), "global-parafuso"),
        };

        // Modelos de bolha (seção 80): vencer o rival ou ser campeão da região abre um modelo em todos os modos.
        // O rival corre com o modelo que ele entrega. estrelas: 1 = vencer o nó; 2 = campeão (campeonato).
        public class ModelUnlock { public int bolha, modelo, estrelas; public string no; }
        public static readonly ModelUnlock[] Modelos =
        {
            new ModelUnlock { bolha = 0, modelo = 1, no = "garagem-9", estrelas = 1 },           // Coupé GT · Tio Beto
            new ModelUnlock { bolha = 0, modelo = 2, no = "garagem-copa", estrelas = 2 },        // Rali · Copa Garagem
            new ModelUnlock { bolha = 2, modelo = 1, no = "clube-marina", estrelas = 1 },        // Flecha · Marina Faísca
            new ModelUnlock { bolha = 1, modelo = 1, no = "regional-drcurva", estrelas = 1 },    // Protótipo · Dr. Curva
            new ModelUnlock { bolha = 2, modelo = 2, no = "regional-copa", estrelas = 2 },       // Bala · Copa Regional
            new ModelUnlock { bolha = 1, modelo = 2, no = "nacional-campeao", estrelas = 1 },    // Asa dupla · O Campeão
        };
        static ModelUnlock UnlockOf(int bolha, int modelo) { foreach (var u in Modelos) if (u.bolha == bolha && u.modelo == modelo) return u; return null; }

        public static bool ModelOk(int bolha, int modelo)
        {
            if (modelo <= 0 || All) return true;
            var u = UnlockOf(bolha, modelo);
            if (u == null) return true;
            if (open == null) Scan();
            return models.Contains(u);
        }

        // "Vença Tio Beto (Garagem) na carreira" / "Seja campeão da Copa Garagem na carreira".
        public static string ModelWhere(int bolha, int modelo)
        {
            var u = UnlockOf(bolha, modelo);
            if (u == null) return "";
            foreach (var R in Career.Def.regioes) foreach (var n in R.nos)
                if (n.id == u.no) return n.tipo == "campeonato" ? L.F("Seja campeão da {0} na carreira.", n.nome) : L.F("Vença {0} ({1}) na carreira.", n.nome, R.nome);
            return L.T("Abre na carreira.");
        }

        // Modelos que o nó entrega (para o resultado da prova).
        public static IEnumerable<string> ModelsOf(string no, int estrelas)
        {
            foreach (var u in Modelos) if (u.no == no && estrelas >= u.estrelas) yield return L.F("{0} (bolha {1})", CarView.ModelName(u.bolha, u.modelo), CarSetup.BolhaNames[u.bolha].ToLowerInvariant());
            if (no == FormulaNo && estrelas >= 2) yield return L.T("bolha Fórmula");
        }

        // Peça → grupo que a libera ("" = grátis).
        public static string ItemOf(string pieceId) => pieceId switch
        {
            "reta-curtinha" => "curtinha",
            "cruzamento" or "cruzamento-curto" => "troca",
            "curva-longa-inclinada" or "curva-curta-inclinada" => "inclinadas",
            "curva-c" or "curva-c-inclinada" => "curva-c",
            "salto" => "salto",
            "loop" => "loop",
            "fechada" => "fechada",
            "meia-curva-c" or "meia-curva-c-inclinada" => "meia-c",
            "muro" => "muro",
            "parafuso" => "parafuso",
            _ => "",
        };

        static PlayerProfile p;
        public static PlayerProfile P => p ??= Load();
        public static event Action Changed;

        static string PathOf => Path.Combine(TrackLibrary.Root, "perfil.json");

        static PlayerProfile Load()
        {
            try { return File.Exists(PathOf) ? JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(PathOf)) ?? new PlayerProfile() : new PlayerProfile(); }
            catch { return new PlayerProfile(); }
        }

        public static void Save()
        {
            try { Directory.CreateDirectory(TrackLibrary.Root); File.WriteAllText(PathOf, JsonUtility.ToJson(P, true)); }
            catch (Exception e) { Debug.LogWarning("Perfil não salvo: " + e.Message); }
            Changed?.Invoke();
        }

        public static bool All => Teste && P.tudo;

        // Grupos abertos em algum save (lidos dos saves e guardados; Refresh depois de salvar a carreira).
        static HashSet<string> open;
        static readonly HashSet<ModelUnlock> models = new HashSet<ModelUnlock>();
        static int reached = -1;
        public static void Refresh() { open = null; reached = -1; Changed?.Invoke(); }
        // A bolha Fórmula (seção 93): campeão do Mundial (★★) em algum save.
        public const string FormulaNo = "global-mundial";
        static bool formula;
        // Zerou a carreira (campeão do Mundial): o construtor fica sem limite (mesa máxima, peças ×1, salvar de graça).
        public static bool Zerou => FormulaOk;
        public static bool FormulaOk { get { if (All) return true; if (open == null) Scan(); return formula; } }
        static void Scan()
        {
            open = new HashSet<string>();
            models.Clear();
            reached = 0;
            formula = false;
            int n = Career.Def.regioes.Length;
            // As melhores estrelas de sempre (Carteira, seção 94): recomeçar a carreira não fecha o que já abriu.
            var s = Career.Load(1);
            Carteira.Sync(s);
            foreach (var u in Pecas) if (Carteira.Estrelas(u.no) > 0) open.Add(u.id);
            foreach (var u in Modelos) if (Carteira.Estrelas(u.no) >= u.estrelas) models.Add(u);
            if (Carteira.Estrelas(FormulaNo) >= 2) formula = true;
            reached = Mathf.Min(Carteira.D.regiao, n - 1);
        }
        public static bool Owns(string item) { if (string.IsNullOrEmpty(item) || All) return true; if (open == null) Scan(); return open.Contains(item); }

        // A maior região aberta em algum save da carreira.
        public static int RegionReached() { if (open == null) Scan(); return reached; }
        // Região que ainda não existe no carreira.json conta como aberta.
        public static bool RegionOk(int regiao) => All || regiao >= Career.Def.regioes.Length || regiao <= RegionReached();

        public static PieceUnlock UnlockOf(string item) { foreach (var u in Pecas) if (u.id == item) return u; return null; }

        // "Clube › Troca de Faixa": a prova que abre o grupo.
        public static string WhereText(PieceUnlock u)
        {
            foreach (var R in Career.Def.regioes) foreach (var n in R.nos) if (n.id == u.no) return $"{R.nome} › {n.nome}";
            return u.no;
        }

        // ----- travas -----

        // Peça da paleta: grátis, aberta pela carreira, ou "Desbloquear tudo". Rampa precisa de "rampas"; salto de vão 2 e 3, de "salto-grande".
        public static bool EntryOk(PaletteEntry e) => Owns(ItemOf(e.def.Id)) && (e.rise == 0 || Owns("rampas"));
        public static bool PieceOk(string id) => Owns(ItemOf(id));

        // Tamanho de pista (faixas): aberto quando a região que o traz está aberta em algum save.
        public static bool FaixasOk(int faixas)
        {
            if (All || faixas <= 2) return true;
            var R = Career.Def.regioes;
            for (int i = 0; i < R.Length; i++) if (R[i].faixas == faixas) return RegionOk(i);
            return true;                       // região desse tamanho ainda não existe
        }

        public static string LockText(string pieceId)
        {
            var u = UnlockOf(ItemOf(pieceId));
            return u != null ? L.F("{0}: abre na carreira ({1}).", L.T(u.nome), WhereText(u)) : L.T("Abre na carreira.");
        }

        // Fora do construtor da carreira (que tem as regras da prova): toda peça pode ser colocada, e a que ainda não
        // abriu só custa mais (PiecePrice.Mult). O Q usa só as abertas. Mesa e pagar a pista ao salvar (seção 94).
        public static void ApplyGate()
        {
            TrackBuilder.AllowedEntry = null;
            TrackBuilder.MaxPieces = 0;
            TrackBuilder.GapOk = null;
            TrackBuilder.RiseOk = null;
            TrackCloser.Allowed = All ? null : PieceOk;
            TrackBuilder.BoardLimit = All || Zerou ? 0 : Carteira.MesaMax;
            TrackBuilder.SaveGate = All || Zerou ? null : PaySave;
        }

        static string PaySave(TrackSave s)
        {
            string key = TrackLibrary.Key(s.nome);
            int custo = PiecePrice.Total(s);
            string why = Carteira.CanPay(key, custo);
            if (why == "") Carteira.Pay(key, custo);
            return why;
        }
    }
}
