#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Autorama
{
    // Um "mundo Steam" em memória para testar o SteamTransport com vários jogadores no mesmo processo
    // (uma conta de verdade só pode estar uma vez em cada lobby). Os eventos e os "done" saem em Tick(), como os callbacks da Steam.
    public class FakeLobbyHub
    {
        public class Lobby
        {
            public ulong id, owner;
            public bool pub, joinable = true;
            public readonly Dictionary<string, string> data = new Dictionary<string, string>();
            public readonly List<ulong> members = new List<ulong>();
            public readonly Dictionary<ulong, Dictionary<string, string>> mdata = new Dictionary<ulong, Dictionary<string, string>>();
        }

        public readonly Dictionary<ulong, Lobby> lobbies = new Dictionary<ulong, Lobby>();
        public readonly Dictionary<ulong, FakeBackend> peers = new Dictionary<ulong, FakeBackend>();
        ulong nextLobby = 9000;

        public FakeBackend NewPlayer(ulong id, string name) => peers[id] = new FakeBackend(this, id, name);

        internal Lobby Create(ulong owner, bool pub)
        {
            var l = new Lobby { id = nextLobby++, owner = owner, pub = pub };
            l.data["autorama"] = "1"; l.data["ver"] = OnlineClient.Protocol.ToString(); l.data["state"] = "lobby"; l.data["pub"] = pub ? "1" : "0";
            l.data["code"] = SteamLobbyClient.NewCode();
            lobbies[l.id] = l;
            AddMember(l, owner);
            return l;
        }

        internal void AddMember(Lobby l, ulong id) { l.members.Add(id); l.mdata[id] = new Dictionary<string, string>(); Notify(l, b => b.Enqueue(() => b.RaiseMembers())); }

        internal void RemoveMember(Lobby l, ulong id)
        {
            l.members.Remove(id); l.mdata.Remove(id);
            if (l.members.Count == 0) { lobbies.Remove(l.id); return; }
            if (l.owner == id) l.owner = l.members[0];
            Notify(l, b => b.Enqueue(() => b.RaiseMembers()));
        }

        internal void Notify(Lobby l, Action<FakeBackend> act) { foreach (var m in new List<ulong>(l.members)) act(peers[m]); }
    }

    public class FakeBackend : ILobbyBackend
    {
        readonly FakeLobbyHub hub;
        readonly Queue<Action> inbox = new Queue<Action>();
        FakeLobbyHub.Lobby lobby;
        public int sent;                                   // mensagens diretas enviadas (para os testes)

        public event Action MembersChanged, DataChanged;
        public event Action<ulong, string> Chat;
        public event Action<ulong, byte[]> Peer;
        public event Action<ulong> JoinRequested;
        public readonly List<ulong> invited = new List<ulong>();

        // Todos os outros jogadores do mundo são "amigos" (os que já estão nesta sala, também: a aba os esconde).
        public List<LobbyFriend> Friends()
        {
            var list = new List<LobbyFriend>();
            foreach (var p in hub.peers.Values) if (p.MyId != MyId) list.Add(new LobbyFriend { id = p.MyId, name = p.MyName, online = !p.offline });
            return list;
        }

        // O convite aceito na hora: o amigo recebe o pedido de entrada, como depois de clicar em "Aceitar" na Steam.
        public bool Invite(ulong friend)
        {
            if (lobby == null || !hub.peers.TryGetValue(friend, out var to) || to.offline) return false;
            var id = lobby.id; invited.Add(friend);
            to.Enqueue(() => to.JoinRequested?.Invoke(id));
            return true;
        }

        public ulong LobbyIdForTest => lobby?.id ?? 0;
        public int dialogOpened;
        public bool offline;
        public void OpenInviteDialog() => dialogOpened++;

        public FakeBackend(FakeLobbyHub hub, ulong id, string name) { this.hub = hub; MyId = id; MyName = name; }

        internal void Enqueue(Action a) => inbox.Enqueue(a);
        internal void RaiseMembers() => MembersChanged?.Invoke();

        public bool Init(out string error) { error = ""; return true; }
        public ulong MyId { get; }
        public string MyName { get; }
        public bool InLobby => lobby != null;
        public ulong Owner => lobby?.owner ?? 0;

        public void Create(string name, bool pub, Action<bool> done)
        {
            lobby = hub.Create(MyId, pub);
            lobby.data["name"] = string.IsNullOrWhiteSpace(name) ? $"Sala de {MyName}" : name.Trim();
            Enqueue(() => done(true));
        }

        public void Search(string code, Action<List<LobbyRoomInfo>> done)
        {
            var list = new List<LobbyRoomInfo>();
            foreach (var l in hub.lobbies.Values)
            {
                if (l.data["autorama"] != "1") continue;
                if (string.IsNullOrEmpty(code) ? !l.pub : l.data["code"] != code.Trim().ToUpperInvariant()) continue;
                int.TryParse(l.data.TryGetValue("stages", out var s) ? s : "0", out int stages);
                list.Add(new LobbyRoomInfo { id = l.id, code = l.data["code"], name = l.data["name"], host = hub.peers[l.owner].MyName, state = l.data["state"], players = l.members.Count, max = 8, stages = stages, pub = l.pub });
            }
            Enqueue(() => done(list));
        }

        public void Join(ulong lobbyId, Action<bool> done)
        {
            if (!hub.lobbies.TryGetValue(lobbyId, out var l) || !l.joinable || l.members.Count >= 8) { Enqueue(() => done(false)); return; }
            lobby = l; hub.AddMember(l, MyId);
            Enqueue(() => done(true));
        }

        public void Leave() { if (lobby == null) return; var l = lobby; lobby = null; hub.RemoveMember(l, MyId); }

        public string Data(string key) => lobby != null && lobby.data.TryGetValue(key, out var v) ? v : "";

        public void SetData(string key, string value)
        {
            if (lobby == null || lobby.owner != MyId) return;
            lobby.data[key] = value;
            hub.Notify(lobby, b => b.Enqueue(() => b.DataChanged?.Invoke()));
        }

        public string MemberData(ulong member, string key) => lobby != null && lobby.mdata.TryGetValue(member, out var d) && d.TryGetValue(key, out var v) ? v : "";

        public void SetMyData(string key, string value)
        {
            if (lobby == null) return;
            lobby.mdata[MyId][key] = value;
            hub.Notify(lobby, b => b.Enqueue(() => b.RaiseMembers()));
        }

        public List<LobbyMember> Members()
        {
            var list = new List<LobbyMember>();
            if (lobby != null) foreach (var id in lobby.members) list.Add(new LobbyMember { id = id, name = hub.peers[id].MyName });
            return list;
        }

        public void SetPublic(bool pub) { if (lobby != null && lobby.owner == MyId) { lobby.pub = pub; SetData("pub", pub ? "1" : "0"); } }
        public void SetJoinable(bool on) { if (lobby != null && lobby.owner == MyId) lobby.joinable = on; }

        public bool SendChat(string text)
        {
            if (lobby == null) return false;
            hub.Notify(lobby, b => b.Enqueue(() => b.Chat?.Invoke(MyId, text)));
            return true;
        }

        public void SendTo(ulong member, byte[] data, bool reliable)
        {
            sent++;
            if (lobby != null && lobby.members.Contains(member)) { var to = hub.peers[member]; var from = MyId; to.Enqueue(() => to.Peer?.Invoke(from, data)); }
        }

        public void Tick() { int n = inbox.Count; for (int i = 0; i < n && inbox.Count > 0; i++) inbox.Dequeue()(); }
        public void Shutdown() => Leave();
    }
}
#endif
