using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;

namespace Autorama
{
    // O lobby da Steam de verdade para o SteamTransport: SteamLobbyClient (sala, dados, chat) mais mensagens diretas entre
    // os jogadores pelo SteamNetworkingMessages (rede de retransmissão da Valve: sem IP exposto e passa por NAT).
    public class SteamBackend : ILobbyBackend
    {
        const int Channel = 0;
        readonly SteamLobbyClient lobby = new SteamLobbyClient();
        Callback<SteamNetworkingMessagesSessionRequest_t> onSession;
        Action<bool> createDone, joinDone;
        Action<List<LobbyRoomInfo>> searchDone;

        public event Action MembersChanged, DataChanged;
        public event Action<ulong, string> Chat;
        public event Action<ulong, byte[]> Peer;

        public SteamBackend()
        {
            lobby.MembersChanged += () => MembersChanged?.Invoke();
            lobby.DataChanged += () => DataChanged?.Invoke();
            lobby.Chat += (who, text) => Chat?.Invoke(who.id.m_SteamID, text);
            lobby.Created += ok => { var d = createDone; createDone = null; d?.Invoke(ok); };
            lobby.Joined += ok => { var d = joinDone; joinDone = null; d?.Invoke(ok); };
            lobby.Listed += list =>
            {
                var d = searchDone; searchDone = null;
                d?.Invoke(list.ConvertAll(l => new LobbyRoomInfo { id = l.id.m_SteamID, code = l.code, name = l.name, host = l.host, state = l.state, players = l.players, max = l.max, stages = l.stages, pub = l.pub }));
            };
        }

        public bool Init(out string error)
        {
            bool ok = SteamBoot.Init();
            error = SteamBoot.Error;
            if (ok && onSession == null)
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();
                // Só aceita conversa direta de quem está no mesmo lobby.
                onSession = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(r =>
                {
                    var id = r.m_identityRemote;
                    if (!IsMember(id.GetSteamID64())) return;
                    SteamNetworkingMessages.AcceptSessionWithUser(ref id);
                });
            }
            return ok;
        }

        bool IsMember(ulong id) => lobby.Members().Exists(m => m.id.m_SteamID == id);

        public ulong MyId => SteamBoot.MyId.m_SteamID;
        public string MyName => SteamBoot.PersonaName;
        public bool InLobby => lobby.InLobby;
        public ulong Owner => lobby.Owner.m_SteamID;

        public void Create(string name, bool pub, Action<bool> done) { createDone = done; lobby.Create(name, pub); }
        public void Search(string code, Action<List<LobbyRoomInfo>> done) { searchDone = done; lobby.Search(code); }
        public void Join(ulong lobbyId, Action<bool> done) { joinDone = done; lobby.Join(new CSteamID(lobbyId)); }
        public void Leave() => lobby.Leave();

        public string Data(string key) => lobby.Data(key);
        public void SetData(string key, string value) => lobby.SetData(key, value);
        public string MemberData(ulong member, string key) => lobby.MemberData(new CSteamID(member), key);
        public void SetMyData(string key, string value) => lobby.SetMyData(key, value);
        public List<LobbyMember> Members() => lobby.Members().ConvertAll(m => new LobbyMember { id = m.id.m_SteamID, name = m.name });

        public void SetPublic(bool pub) => lobby.SetPublic(pub);
        public void SetJoinable(bool on) => lobby.SetJoinable(on);
        public bool SendChat(string text) => lobby.SendChat(text);

        public void SendTo(ulong member, byte[] data, bool reliable)
        {
            var id = new SteamNetworkingIdentity();
            id.SetSteamID64(member);
            int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable)
                        | Constants.k_nSteamNetworkingSend_NoNagle | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { SteamNetworkingMessages.SendMessageToUser(ref id, h.AddrOfPinnedObject(), (uint)data.Length, flags, Channel); }
            finally { h.Free(); }
        }

        public void Tick()
        {
            SteamBoot.Tick();
            if (!SteamBoot.Ready) return;
            var ptrs = new IntPtr[32];
            int n;
            while ((n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, ptrs, ptrs.Length)) > 0)
                for (int i = 0; i < n; i++)
                {
                    var m = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs[i]);
                    var bytes = new byte[m.m_cbSize];
                    Marshal.Copy(m.m_pData, bytes, 0, m.m_cbSize);
                    ulong from = m.m_identityPeer.GetSteamID64();
                    SteamNetworkingMessage_t.Release(ptrs[i]);
                    Peer?.Invoke(from, bytes);
                }
        }

        public void Shutdown()
        {
            lobby.Leave();
            SteamBoot.Tick();
            onSession?.Dispose(); onSession = null;
            SteamBoot.Shutdown();
        }
    }
}
