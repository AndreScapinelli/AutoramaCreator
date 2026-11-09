using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;

namespace Autorama
{
    public class SteamLobbyInfo
    {
        public CSteamID id;
        public string code, name, host;
        public int players, max, stages;
        public bool pub;
    }

    public class SteamMember { public CSteamID id; public string name, color; public bool ready; }

    // O lobby da Steam como sala do campeonato online: o mesmo mapa do servidor Node (seção 24 do guia).
    //   sala pública   → lobby Public, achada na lista       · sala privada → lobby Invisible, achada só pelo código
    //   nome/código/etapas/estado → dados do lobby (só o dono escreve)
    //   cor e pronto   → dados do membro (cada um escreve os seus) · chat → mensagens do lobby · host → dono do lobby
    // Tudo roda na thread principal: os eventos saem de SteamBoot.Tick() (SteamAPI.RunCallbacks).
    public class SteamLobbyClient : IDisposable
    {
        public const string Tag = "autorama";                 // todo lobby nosso leva autorama=1: o Spacewar (480) é compartilhado com outros testes
        public const int MaxMembers = 8;
        const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public CSteamID Lobby { get; private set; } = CSteamID.Nil;
        public bool InLobby => Lobby.IsValid();
        public bool IsOwner => InLobby && SteamMatchmaking.GetLobbyOwner(Lobby) == SteamUser.GetSteamID();

        public event Action<bool> Created;                    // ok
        public event Action<bool> Joined;                     // ok
        public event Action<List<SteamLobbyInfo>> Listed;
        public event Action MembersChanged;                   // entrou, saiu, mudou cor ou pronto
        public event Action DataChanged;                      // dados do lobby (nome, etapas, estado)
        public event Action<SteamMember, string> Chat;        // quem, texto

        readonly CallResult<LobbyCreated_t> onCreated;
        readonly CallResult<LobbyEnter_t> onEnter;
        readonly CallResult<LobbyMatchList_t> onList;
        readonly Callback<LobbyChatMsg_t> onChat;
        readonly Callback<LobbyChatUpdate_t> onUpdate;
        readonly Callback<LobbyDataUpdate_t> onData;
        string pendingName, pendingCode;
        bool pendingPub;

        public SteamLobbyClient()
        {
            onCreated = CallResult<LobbyCreated_t>.Create(OnCreated);
            onEnter = CallResult<LobbyEnter_t>.Create(OnEnter);
            onList = CallResult<LobbyMatchList_t>.Create(OnList);
            onChat = Callback<LobbyChatMsg_t>.Create(OnChatMsg);
            onUpdate = Callback<LobbyChatUpdate_t>.Create(_ => MembersChanged?.Invoke());
            onData = Callback<LobbyDataUpdate_t>.Create(OnDataUpdate);
        }

        public static string NewCode()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 5; i++) sb.Append(CodeChars[UnityEngine.Random.Range(0, CodeChars.Length)]);
            return sb.ToString();
        }

        // ---------- Criar, procurar, entrar, sair ----------

        public void Create(string roomName, bool pub)
        {
            pendingName = roomName; pendingPub = pub; pendingCode = NewCode();
            onCreated.Set(SteamMatchmaking.CreateLobby(pub ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeInvisible, MaxMembers));
        }

        // code vazio: as salas públicas; com código: só aquela (pública ou privada).
        public void Search(string code = null)
        {
            SteamMatchmaking.AddRequestLobbyListStringFilter(Tag, "1", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter("ver", OnlineClient.Protocol.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
            if (string.IsNullOrEmpty(code)) SteamMatchmaking.AddRequestLobbyListStringFilter("pub", "1", ELobbyComparison.k_ELobbyComparisonEqual);
            else SteamMatchmaking.AddRequestLobbyListStringFilter("code", code.Trim().ToUpperInvariant(), ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            onList.Set(SteamMatchmaking.RequestLobbyList());
        }

        public void Join(CSteamID id) => onEnter.Set(SteamMatchmaking.JoinLobby(id));

        public void Leave()
        {
            if (InLobby) SteamMatchmaking.LeaveLobby(Lobby);
            Lobby = CSteamID.Nil;
        }

        // ---------- Dados ----------

        public string Data(string key) => InLobby ? SteamMatchmaking.GetLobbyData(Lobby, key) : "";
        public void SetData(string key, string value) { if (IsOwner) SteamMatchmaking.SetLobbyData(Lobby, key, value); }

        public string MemberData(CSteamID user, string key) => InLobby ? SteamMatchmaking.GetLobbyMemberData(Lobby, user, key) : "";
        public void SetMyData(string key, string value) { if (InLobby) SteamMatchmaking.SetLobbyMemberData(Lobby, key, value); }

        public CSteamID Owner => InLobby ? SteamMatchmaking.GetLobbyOwner(Lobby) : CSteamID.Nil;

        public List<SteamMember> Members()
        {
            var list = new List<SteamMember>();
            if (!InLobby) return list;
            int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < n; i++)
            {
                var id = SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i);
                list.Add(new SteamMember { id = id, name = SteamFriends.GetFriendPersonaName(id), color = MemberData(id, "color"), ready = MemberData(id, "ready") == "1" });
            }
            return list;
        }

        public bool SendChat(string text)
        {
            text = (text ?? "").Trim();
            if (!InLobby || text.Length == 0) return false;
            if (text.Length > 200) text = text.Substring(0, 200);
            var bytes = Encoding.UTF8.GetBytes(text);
            return SteamMatchmaking.SendLobbyChatMsg(Lobby, bytes, bytes.Length);
        }

        // ---------- Callbacks da Steam ----------

        void OnCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Created?.Invoke(false); return; }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, Tag, "1");
            SteamMatchmaking.SetLobbyData(Lobby, "ver", OnlineClient.Protocol.ToString());
            SteamMatchmaking.SetLobbyData(Lobby, "name", string.IsNullOrWhiteSpace(pendingName) ? $"Sala de {SteamFriends.GetPersonaName()}" : pendingName.Trim());
            SteamMatchmaking.SetLobbyData(Lobby, "code", pendingCode);
            SteamMatchmaking.SetLobbyData(Lobby, "pub", pendingPub ? "1" : "0");
            SteamMatchmaking.SetLobbyData(Lobby, "state", "lobby");
            SteamMatchmaking.SetLobbyData(Lobby, "stages", "0");
            Created?.Invoke(true);
        }

        void OnEnter(LobbyEnter_t r, bool ioFailure)
        {
            bool ok = !ioFailure && r.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
            if (ok) Lobby = new CSteamID(r.m_ulSteamIDLobby);
            Joined?.Invoke(ok);
        }

        void OnList(LobbyMatchList_t r, bool ioFailure)
        {
            var list = new List<SteamLobbyInfo>();
            if (!ioFailure)
                for (int i = 0; i < r.m_nLobbiesMatching; i++)
                {
                    var id = SteamMatchmaking.GetLobbyByIndex(i);
                    int.TryParse(SteamMatchmaking.GetLobbyData(id, "stages"), out int stages);
                    var owner = SteamMatchmaking.GetLobbyOwner(id);
                    list.Add(new SteamLobbyInfo
                    {
                        id = id, code = SteamMatchmaking.GetLobbyData(id, "code"), name = SteamMatchmaking.GetLobbyData(id, "name"),
                        host = owner.IsValid() ? SteamFriends.GetFriendPersonaName(owner) : "", pub = SteamMatchmaking.GetLobbyData(id, "pub") == "1",
                        players = SteamMatchmaking.GetNumLobbyMembers(id), max = SteamMatchmaking.GetLobbyMemberLimit(id), stages = stages,
                    });
                }
            Listed?.Invoke(list);
        }

        void OnChatMsg(LobbyChatMsg_t m)
        {
            if (m.m_ulSteamIDLobby != Lobby.m_SteamID) return;
            var buf = new byte[4096];
            int n = SteamMatchmaking.GetLobbyChatEntry(Lobby, (int)m.m_iChatID, out CSteamID user, buf, buf.Length, out EChatEntryType type);
            if (type != EChatEntryType.k_EChatEntryTypeChatMsg || n <= 0) return;
            Chat?.Invoke(new SteamMember { id = user, name = SteamFriends.GetFriendPersonaName(user), color = MemberData(user, "color") }, Encoding.UTF8.GetString(buf, 0, n));
        }

        // Um LobbyDataUpdate vale para o lobby (m_ulSteamIDMember == lobby) ou para um membro.
        void OnDataUpdate(LobbyDataUpdate_t u)
        {
            if (u.m_ulSteamIDLobby != Lobby.m_SteamID) return;
            if (u.m_ulSteamIDMember == u.m_ulSteamIDLobby) DataChanged?.Invoke(); else MembersChanged?.Invoke();
        }

        public void Dispose()
        {
            Leave();
            onCreated.Dispose(); onEnter.Dispose(); onList.Dispose(); onChat.Dispose(); onUpdate.Dispose(); onData.Dispose();
        }
    }
}
