using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace Autorama
{
    // Notas das pistas (seção 83 do guia): um placar (leaderboard) da Steam por identidade de pista, "nota-<hash>",
    // com a nota de cada jogador, de 1 a 5 (votar de novo troca a nota). A média sai lendo as notas gravadas.
    // Um pedido de cada vez, numa fila; o resultado fica guardado por Fresh segundos. Ler nunca cria o placar
    // (FindLeaderboard); só dar a nota cria (FindOrCreateLeaderboard).
    public static class TrackRatings
    {
        public const int Max = 5;
        const int MaxRead = 2000;          // notas lidas por placar (a média é das primeiras MaxRead; o total vem da Steam)
        const float Fresh = 600f, Timeout = 15f;

        public class Info { public float avg; public int count, mine; public bool done; public float at = -1f; }

        class Job { public string hash; public int stars; public float since; }   // stars 0: ler · 1 a 5: dar a nota

        static readonly Dictionary<string, Info> cache = new Dictionary<string, Info>();
        static readonly Queue<Job> queue = new Queue<Job>();
        static Job running;
        static CallResult<LeaderboardFindResult_t> onFind;
        static CallResult<LeaderboardScoresDownloaded_t> onDown;
        static CallResult<LeaderboardScoreUploaded_t> onUp;

        public static int Version { get; private set; }      // sobe a cada nota que chega: quem desenha listas compara
        public static bool Available => SteamBoot.Ready;
        static string Board(string hash) => "nota-" + hash;

        // A nota da pista como está agora (pode estar chegando); pede à Steam se não tem ou se ficou velha.
        public static Info Get(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return null;
            if (!cache.TryGetValue(hash, out var i)) cache[hash] = i = new Info();
            bool asked = running != null && running.hash == hash;
            foreach (var j in queue) asked |= j.hash == hash;
            if (Available && !asked && (i.at < 0f || Time.realtimeSinceStartup - i.at > Fresh))
            {
                i.at = Time.realtimeSinceStartup;          // conta como pedido: não entra de novo na fila enquanto espera
                queue.Enqueue(new Job { hash = hash });
            }
            return i;
        }

        // Dá a nota (1 a 5). Na hora a média daqui já muda; depois a Steam confirma e a média é lida de novo.
        public static void Rate(string hash, int stars)
        {
            if (!Available || string.IsNullOrEmpty(hash) || stars < 1 || stars > Max) return;
            var i = Get(hash);
            // Sem a média lida ainda, não inventa uma de 1 voto: guarda só a minha e espera a leitura depois de gravar.
            if (i.done)
            {
                float sum = i.avg * i.count - (i.mine > 0 ? i.mine : 0) + stars;
                if (i.mine == 0) i.count++;
                i.avg = i.count > 0 ? sum / i.count : stars;
            }
            i.mine = stars;
            queue.Enqueue(new Job { hash = hash, stars = stars });
            Version++;
        }

        // " · ★ 4,2 (12)" para a linha das listas; vazio sem nota ou sem Steam.
        public static string Line(TrackSave s)
        {
            if (!Available || s == null) return "";
            var i = Get(Champ.TrackHash(s));
            return i != null && i.done && i.count > 0 ? L.F(" · <color=#F5C542>★</color> {0} ({1})", BuilderPanel.Num(i.avg), i.count) : "";
        }

        // A cada quadro (RaceController.Update). Só roda os callbacks da Steam quando tem pedido andando.
        public static void Tick()
        {
            if (!Available) { running = null; queue.Clear(); return; }
            if (running == null && queue.Count == 0) return;
            SteamBoot.Tick();
            if (running != null && Time.realtimeSinceStartup - running.since > Timeout) Finish();
            if (running != null || queue.Count == 0) return;
            running = queue.Dequeue();
            running.since = Time.realtimeSinceStartup;
            onFind ??= CallResult<LeaderboardFindResult_t>.Create(OnFind);
            onDown ??= CallResult<LeaderboardScoresDownloaded_t>.Create(OnDown);
            onUp ??= CallResult<LeaderboardScoreUploaded_t>.Create(OnUp);
            onFind.Set(running.stars > 0
                ? SteamUserStats.FindOrCreateLeaderboard(Board(running.hash), ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending, ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric)
                : SteamUserStats.FindLeaderboard(Board(running.hash)));
        }

        static void OnFind(LeaderboardFindResult_t r, bool fail)
        {
            if (running == null) return;
            if (fail || r.m_bLeaderboardFound == 0)
            {
                // Ninguém deu nota ainda (ler não cria o placar).
                if (running.stars == 0 && !Voting(running.hash)) { var i = cache[running.hash]; i.avg = 0f; i.count = 0; i.mine = 0; i.done = true; i.at = Time.realtimeSinceStartup; Version++; }
                Finish(); return;
            }
            if (running.stars > 0) onUp.Set(SteamUserStats.UploadLeaderboardScore(r.m_hSteamLeaderboard, ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodForceUpdate, running.stars, null, 0));
            else onDown.Set(SteamUserStats.DownloadLeaderboardEntries(r.m_hSteamLeaderboard, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobal, 1, MaxRead));
        }

        static void OnDown(LeaderboardScoresDownloaded_t r, bool fail)
        {
            if (running == null) return;
            // Leitura que saiu antes de um voto ainda na fila: é velha (sem o voto). Fica a média daqui; a leitura depois do voto acerta.
            if (!fail && !Voting(running.hash))
            {
                var me = SteamBoot.MyId;
                int n = 0, sum = 0, mine = 0;
                for (int k = 0; k < r.m_cEntryCount; k++)
                {
                    if (!SteamUserStats.GetDownloadedLeaderboardEntry(r.m_hSteamLeaderboardEntries, k, out var e, null, 0)) continue;
                    int v = Mathf.Clamp(e.m_nScore, 1, Max);
                    n++; sum += v;
                    if (e.m_steamIDUser == me) mine = v;
                }
                var i = cache[running.hash];
                i.count = Mathf.Max(n, SteamUserStats.GetLeaderboardEntryCount(r.m_hSteamLeaderboard));
                i.avg = n > 0 ? sum / (float)n : 0f;
                i.mine = mine; i.done = true; i.at = Time.realtimeSinceStartup;
                Version++;
            }
            Finish();
        }

        static void OnUp(LeaderboardScoreUploaded_t r, bool fail)
        {
            if (running == null) return;
            string hash = running.hash;
            Finish();
            // Gravou: lê de novo, para a média ser a da Steam (com as notas dos outros que chegaram nesse meio-tempo).
            if (!fail && r.m_bSuccess != 0 && cache.TryGetValue(hash, out var i)) { i.at = -1f; Get(hash); }
        }

        static void Finish() => running = null;

        static bool Voting(string hash) { foreach (var j in queue) if (j.hash == hash && j.stars > 0) return true; return false; }
    }
}
