using System;
using System.Collections.Generic;

namespace Autorama
{
    public class LobbyRoomInfo
    {
        public ulong id;
        public string code, name, host, state;
        public int players, max, stages;
        public bool pub;
    }

    public class LobbyMember { public ulong id; public string name; }
    public class LobbyFriend { public ulong id; public string name; public bool online; }

    // O que o SteamTransport precisa de um "lobby": a Steam de verdade (SteamBackend) ou um mundo falso em memória, para teste
    // (FakeLobbyHub, no Editor). Tudo na thread principal; os eventos saem de Tick().
    public interface ILobbyBackend
    {
        bool Init(out string error);
        ulong MyId { get; }
        string MyName { get; }
        bool InLobby { get; }
        ulong Owner { get; }                    // o dono do lobby é o host da corrida

        event Action MembersChanged;            // entrou, saiu ou mudou algo de um membro
        event Action DataChanged;               // dados do lobby
        event Action<ulong, string> Chat;       // quem, texto
        event Action<ulong, byte[]> Peer;       // mensagem direta de outro jogador (quem, bytes)
        event Action<ulong> JoinRequested;      // um amigo convidou (ou "entrar no jogo" na Steam): o id do lobby

        List<LobbyFriend> Friends();            // os amigos da conta
        bool Invite(ulong friend);              // convite dentro do jogo (aparece na Steam do amigo)
        void OpenInviteDialog();                // a janela de convite da Steam (só aparece em build: o overlay não existe no Editor)

        void Create(string name, bool pub, Action<bool> done);
        void Search(string code, Action<List<LobbyRoomInfo>> done);    // code vazio: as salas públicas
        void Join(ulong lobbyId, Action<bool> done);
        void Leave();

        string Data(string key);                // dados do lobby: só o dono escreve
        void SetData(string key, string value);
        string MemberData(ulong member, string key);                   // dados de cada membro: cada um escreve os seus
        void SetMyData(string key, string value);
        List<LobbyMember> Members();

        void SetPublic(bool pub);
        void SetJoinable(bool on);
        bool SendChat(string text);
        void SendTo(ulong member, byte[] data, bool reliable);

        void Tick();
        void Shutdown();
    }
}
