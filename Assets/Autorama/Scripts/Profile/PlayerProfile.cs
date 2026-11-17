using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // Perfil do jogador (seção 78 do guia): por enquanto só o "Desbloquear tudo" de teste. Um por computador, em
    // persistentDataPath/autorama/perfil.json. As moedas ficam no save da carreira (CareerSave.moedas).
    [Serializable]
    public class PlayerProfile
    {
        public bool tudo;                 // "Desbloquear tudo" (só enquanto Progresso.Teste)
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
            new PieceUnlock("curtinha", "Reta curtinha", "reta-curtinha", "garagem-oficina"),
            new PieceUnlock("inclinadas", "Curvas inclinadas", "curva longa e curta inclinadas", "clube-1"),
            new PieceUnlock("troca", "Troca de faixa", "cruzamento e cruzamento curto", "clube-2"),
            new PieceUnlock("rampas", "Subida e descida", "rampas para ponte", "clube-3"),
            new PieceUnlock("salto", "Rampa de salto", "salto de vão 1", "regional-1"),
            new PieceUnlock("curva-c", "Curva em C", "curva em C (1 a 3 fileiras)", "regional-3"),
            new PieceUnlock("loop", "Loop", "loop", "nacional-1"),
            new PieceUnlock("salto-grande", "Saltos grandes", "salto de vão 2 e 3", "nacional-2"),
        };

        // Peça → grupo que a libera ("" = grátis).
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
        static int reached = -1;
        public static void Refresh() { open = null; reached = -1; Changed?.Invoke(); }
        static void Scan()
        {
            open = new HashSet<string>();
            reached = 0;
            int n = Career.Def.regioes.Length;
            for (int slot = 1; slot <= Career.Slots; slot++)
            {
                var s = Career.Load(slot);
                foreach (var u in Pecas) if (Career.Stars(s, u.no) > 0) open.Add(u.id);
                for (int i = 0; i < n; i++) if (Career.RegionOpen(s, i)) reached = Mathf.Max(reached, i);
            }
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
            return u != null ? $"{u.nome}: abre na carreira ({WhereText(u)})." : "Abre na carreira.";
        }

        // Liga as travas da carreira no construtor e no Q (fora do construtor da carreira, que tem as regras da prova).
        public static void ApplyGate()
        {
            TrackBuilder.AllowedEntry = All ? null : EntryOk;
            TrackBuilder.RuleNotice = "Esta peça abre na carreira (menu inicial › Peças mostra em que prova).";
            TrackBuilder.MaxPieces = 0;
            TrackBuilder.GapOk = v => v <= 1 || Owns("salto-grande");
            TrackBuilder.RiseOk = () => Owns("rampas");
            TrackCloser.Allowed = All ? null : PieceOk;
        }
    }
}
