using Steamworks;
using UnityEngine;

namespace Autorama
{
    // O Elo da partida rápida no placar "elo" da Steam (seção 89 do guia): a pontuação é o Elo e o detalhe 0 é o
    // número de partidas. Cada jogador só grava o próprio (ForceUpdate: o Elo também desce). Ler traz a posição
    // no ranking e o total de jogadores. Na Steam o placar é a fonte (seção 89.1): o próprio Elo vem dele, e o dos
    // adversários da partida também (Users), no lugar do que cada um diz na fila. Um pedido de cada vez; o último Push vale.
    public static class EloBoard
    {
        const string Name = "elo";
        const float Timeout = 15f;

        public static bool Available => SteamBoot.Ready;
        public static int Rank { get; private set; }          // 0: sem posição (ainda não leu ou nunca jogou)
        public static int Total { get; private set; }
        public static int Version { get; private set; }       // sobe quando chega algo: quem desenha compara
        // Leu a própria linha: found = está no placar (elo, partidas); não está = nunca gravou.
        public static System.Action<bool, int, int> Mine;

        static CSteamID[] users;
        static System.Action<System.Collections.Generic.Dictionary<ulong, (int elo, int jogos)>> usersDone;
        static CallResult<LeaderboardScoresDownloaded_t> onUsers;

        // O Elo de cada um destes jogadores no placar; quem não está fica de fora do dicionário (nunca jogou: 1000).
        public static void Users(ulong[] ids, System.Action<System.Collections.Generic.Dictionary<ulong, (int elo, int jogos)>> done)
        {
            if (!Available || ids.Length == 0) return;
            users = System.Array.ConvertAll(ids, i => new CSteamID(i)); usersDone = done;
        }

        static bool busy, wantPull, wantPush;
        static int pushElo, pushGames;
        static float since;
        static SteamLeaderboard_t board;
        static bool haveBoard;
        static CallResult<LeaderboardFindResult_t> onFind;
        static CallResult<LeaderboardScoresDownloaded_t> onDown;
        static CallResult<LeaderboardScoreUploaded_t> onUp;

        public static void Pull() { if (Available) wantPull = true; }
        public static void Push(int elo, int games) { if (!Available) return; pushElo = elo; pushGames = games; wantPush = true; }

        // A cada quadro (RaceController.Update).
        public static void Tick()
        {
            if (!Available) { busy = wantPull = wantPush = false; haveBoard = false; return; }
            if (!busy && !wantPull && !wantPush && users == null) return;
            SteamBoot.Tick();
            if (busy && Time.realtimeSinceStartup - since > Timeout) busy = false;
            if (busy) return;
            onFind ??= CallResult<LeaderboardFindResult_t>.Create(OnFind);
            onDown ??= CallResult<LeaderboardScoresDownloaded_t>.Create(OnDown);
            onUp ??= CallResult<LeaderboardScoreUploaded_t>.Create(OnUp);
            onUsers ??= CallResult<LeaderboardScoresDownloaded_t>.Create(OnUsers);
            busy = true; since = Time.realtimeSinceStartup;
            if (!haveBoard)
            {
                onFind.Set(SteamUserStats.FindOrCreateLeaderboard(Name, ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending, ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric));
                return;
            }
            if (wantPush)
            {
                wantPush = false;
                onUp.Set(SteamUserStats.UploadLeaderboardScore(board, ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodForceUpdate, pushElo, new[] { pushGames }, 1));
                return;
            }
            if (users != null)
            {
                onUsers.Set(SteamUserStats.DownloadLeaderboardEntriesForUsers(board, users, users.Length));
                users = null;
                return;
            }
            if (!wantPull) { busy = false; return; }
            wantPull = false;
            onDown.Set(SteamUserStats.DownloadLeaderboardEntries(board, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobalAroundUser, 0, 0));
        }

        static void OnFind(LeaderboardFindResult_t r, bool fail)
        {
            busy = false;
            if (fail || r.m_bLeaderboardFound == 0) { wantPull = wantPush = false; users = null; return; }
            board = r.m_hSteamLeaderboard; haveBoard = true;
        }

        static void OnDown(LeaderboardScoresDownloaded_t r, bool fail)
        {
            busy = false;
            if (fail) return;
            Total = SteamUserStats.GetLeaderboardEntryCount(board);
            var details = new int[1];
            LeaderboardEntry_t e = default;
            bool found = r.m_cEntryCount > 0 && SteamUserStats.GetDownloadedLeaderboardEntry(r.m_hSteamLeaderboardEntries, 0, out e, details, 1)
                && e.m_steamIDUser == SteamBoot.MyId && e.m_nScore >= Elo.Min;
            if (found) { Rank = e.m_nGlobalRank; Mine?.Invoke(true, e.m_nScore, e.m_cDetails > 0 ? details[0] : 0); }
            else Mine?.Invoke(false, 0, 0);
            Version++;
        }

        static void OnUsers(LeaderboardScoresDownloaded_t r, bool fail)
        {
            busy = false;
            var done = usersDone; usersDone = null;
            if (fail || done == null) return;
            var res = new System.Collections.Generic.Dictionary<ulong, (int, int)>();
            var details = new int[1];
            for (int k = 0; k < r.m_cEntryCount; k++)
                if (SteamUserStats.GetDownloadedLeaderboardEntry(r.m_hSteamLeaderboardEntries, k, out var e, details, 1) && e.m_nScore >= Elo.Min)
                    res[e.m_steamIDUser.m_SteamID] = (e.m_nScore, e.m_cDetails > 0 ? Mathf.Max(0, details[0]) : 0);
            done(res);
        }

        static void OnUp(LeaderboardScoreUploaded_t r, bool fail)
        {
            busy = false;
            if (!fail && r.m_bSuccess != 0) { Rank = r.m_nGlobalRankNew; wantPull = true; }   // lê de novo: o total também muda
        }
    }
}
