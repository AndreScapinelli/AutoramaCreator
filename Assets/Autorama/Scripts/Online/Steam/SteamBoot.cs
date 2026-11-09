using Steamworks;
using UnityEngine;

namespace Autorama
{
    // Liga e desliga a Steam e roda os callbacks (seção 24 do guia). Precisa do cliente da Steam aberto e logado.
    // Com steam_appid.txt na raiz do projeto (480 = Spacewar, o app de teste da Valve) a Steam aceita o jogo fora da loja.
    // Quem usa chama Tick() a cada quadro (ou, em teste sem quadros, em laço).
    public static class SteamBoot
    {
        public const uint SpacewarAppId = 480;

        public static bool Ready { get; private set; }
        public static string Error { get; private set; } = "";

        public static string PersonaName => Ready ? SteamFriends.GetPersonaName() : "";
        public static CSteamID MyId => Ready ? SteamUser.GetSteamID() : CSteamID.Nil;
        public static uint AppId => Ready ? SteamUtils.GetAppID().m_AppId : 0;

        public static bool Init()
        {
            if (Ready) return true;
            try
            {
                var r = SteamAPI.InitEx(out string msg);
                Ready = r == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
                Error = Ready ? "" : Explain(r, msg);
            }
            catch (System.Exception e) { Ready = false; Error = "Não deu para carregar a biblioteca da Steam: " + e.Message; }
            return Ready;
        }

        static string Explain(ESteamAPIInitResult r, string msg)
        {
            switch (r)
            {
                case ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient: return "O cliente da Steam não está aberto. Abra a Steam, entre na conta e tente de novo.";
                case ESteamAPIInitResult.k_ESteamAPIInitResult_VersionMismatch: return "A Steam está desatualizada para esta versão da biblioteca. Atualize o cliente da Steam.";
                default: return $"A Steam não iniciou ({r}): {msg}. Confira se o steam_appid.txt (480) está na raiz do projeto.";
            }
        }

        public static void Tick() { if (Ready) SteamAPI.RunCallbacks(); }

        public static void Shutdown() { if (Ready) { SteamAPI.Shutdown(); Ready = false; } }
    }
}
