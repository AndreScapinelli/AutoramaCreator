using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // Perfil do jogador (seção 74 do guia): moedas e o que já comprou na Loja. Um por computador, em
    // persistentDataPath/autorama/perfil.json; vale para todos os saves da carreira.
    [Serializable]
    public class PlayerProfile
    {
        public int moedas;
        public List<string> compras = new List<string>();
        public bool tudo;                 // "Desbloquear tudo" (só enquanto Progresso.Teste)
        public string ultimaPista;        // contra farm: a mesma pista várias vezes seguidas paga menos
        public int seguidas;
    }

    // Um item da Loja: um grupo de peças com preço e a região da carreira que o abre para compra.
    public class ShopItem
    {
        public string id, nome, pecas;
        public int preco, regiao;         // regiao: índice em carreira.json (0 = desde o começo)
        public ShopItem(string id, string nome, string pecas, int preco, int regiao) { this.id = id; this.nome = nome; this.pecas = pecas; this.preco = preco; this.regiao = regiao; }
    }

    public static class Progresso
    {
        // Enquanto o jogo não sai: mostra o "Desbloquear tudo" nas Opções da Loja. No lançamento, false (o botão some
        // e o desbloqueio deixa de valer).
        public const bool Teste = true;

        public static readonly ShopItem[] Loja =
        {
            new ShopItem("curtinha", "Reta curtinha", "reta-curtinha", 150, 0),
            new ShopItem("rampas", "Subida e descida", "rampas para ponte", 150, 0),
            new ShopItem("troca", "Troca de faixa", "cruzamento e cruzamento curto", 250, 1),
            new ShopItem("inclinadas", "Curvas inclinadas", "curva longa e curta inclinadas", 400, 1),
            new ShopItem("curva-c", "Curva em C", "curva em C (1 a 3 fileiras)", 500, 2),
            new ShopItem("salto", "Rampa de salto", "salto de vão 1", 600, 2),
            new ShopItem("loop", "Loop", "loop", 1000, 3),
            new ShopItem("salto-grande", "Saltos grandes", "salto de vão 2 e 3", 1200, 3),
        };

        // Peça → item da Loja que a libera ("" = grátis).
        public static string ItemOf(string pieceId) => pieceId switch
        {
            "reta-curtinha" => "curtinha",
            "cruzamento" or "cruzamento-curto" => "troca",
            "curva-longa-inclinada" or "curva-curta-inclinada" => "inclinadas",
            "curva-c" => "curva-c",
            "salto" => "salto",
            "loop" => "loop",
            _ => "",
        };

        // Prêmios (seção 74).
        public static readonly int[] StarPrize = { 50, 75, 100 };            // 1ª, 2ª e 3ª estrela de um nó, só da 1ª vez
        public const int RivalPrize = 300;                                    // rival vencido, 1ª vez
        public static readonly int[] ChampPrize = { 500, 800, 1200, 2000 };   // campeão da região, 1ª vez
        public const int RacePrize = 10, PlacePrize = 5, RecordPrize = 50, Streak = 3;
        public const float OnlineBonus = 1.5f;

        static PlayerProfile p;
        public static PlayerProfile P => p ??= Load();
        public static event Action Changed;
        public static event Action<int> Earned;     // moedas ganhas agora (o selo anima)

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
        public static bool Owns(string item) => string.IsNullOrEmpty(item) || All || P.compras.Contains(item);

        // A maior região aberta em algum save da carreira. Região que ainda não existe no carreira.json conta como aberta
        // (senão nada dela daria para comprar até ela ficar pronta).
        public static int RegionReached()
        {
            int n = Career.Def.regioes.Length, best = 0;
            for (int slot = 1; slot <= Career.Slots; slot++)
            {
                var s = Career.Load(slot);
                for (int i = 0; i < n; i++) if (Career.RegionOpen(s, i)) best = Mathf.Max(best, i);
            }
            return best;
        }

        public static bool RegionOk(int regiao) => All || regiao >= Career.Def.regioes.Length || regiao <= RegionReached();

        // Pode comprar? "" se pode; senão o motivo.
        public static string CanBuy(ShopItem it)
        {
            if (Owns(it.id)) return "Já é seu.";
            if (!RegionOk(it.regiao)) return $"Abre ao chegar na região {Career.Def.regioes[it.regiao].nome} da carreira.";
            if (P.moedas < it.preco) return $"Faltam {it.preco - P.moedas} moedas.";
            return "";
        }

        public static bool Buy(ShopItem it)
        {
            if (CanBuy(it) != "") return false;
            P.moedas -= it.preco;
            P.compras.Add(it.id);
            Save();
            return true;
        }

        public static int Earn(int coins)
        {
            if (coins <= 0) return 0;
            P.moedas += coins;
            Save();
            Earned?.Invoke(coins);
            return coins;
        }

        // Corrida fora da carreira (livre, campeonato, online): 10 + 5 por carro que ficou atrás. A partir da 4ª corrida
        // seguida na mesma pista, metade.
        public static int RacePay(string trackHash, int beaten, bool online)
        {
            if (P.ultimaPista == trackHash) P.seguidas++; else { P.ultimaPista = trackHash; P.seguidas = 1; }
            float c = RacePrize + PlacePrize * Mathf.Max(0, beaten);
            if (P.seguidas > Streak) c *= 0.5f;
            if (online) c *= OnlineBonus;
            return Earn(Mathf.RoundToInt(c));
        }

        // ----- travas -----

        // Peça da paleta: grátis, comprada, ou "Desbloquear tudo". Rampa precisa de "rampas"; salto de vão 2 e 3, de "salto-grande".
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
            string id = ItemOf(pieceId);
            foreach (var it in Loja) if (it.id == id) return $"{it.nome}: compre na Loja ({it.preco} moedas).";
            return "Compre na Loja.";
        }

        // Liga as travas da Loja no construtor e no Q (fora do construtor da carreira, que tem as regras da prova).
        public static void ApplyGate()
        {
            TrackBuilder.AllowedEntry = All ? null : EntryOk;
            TrackBuilder.RuleNotice = "Esta peça é da Loja: compre com moedas (menu inicial › Loja).";
            TrackBuilder.MaxPieces = 0;
            TrackBuilder.GapOk = v => v <= 1 || Owns("salto-grande");
            TrackBuilder.RiseOk = () => Owns("rampas");
            TrackCloser.Allowed = All ? null : PieceOk;
        }
    }
}
