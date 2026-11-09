using System;
using System.Collections.Generic;

namespace Autorama
{
    // Mensagens trocadas com o servidor de salas (servidor/servidor.js; seção "Campeonato online" do guia).
    // São JSON de uma linha com o tipo em "t". O JsonUtility não faz herança, então cada direção tem classes enxutas.

    [Serializable] public class NetPlayer { public string id, name, color; }
    [Serializable] public class NetStage { public string name, hash, json; public int laps; }   // json: a pista (TrackSave); o servidor só devolve o resumo

    [Serializable]
    public class NetRoom
    {
        public string code, name, host, state;            // host: id do jogador que manda na sala
        public bool pub;
        public int max;
        public List<NetPlayer> players = new List<NetPlayer>();
        public List<NetStage> stages = new List<NetStage>();
        public NetPlayer Player(string id) => players.Find(p => p.id == id);
    }

    [Serializable] public class NetListed { public string code, name, host; public int players, max, stages; }
    [Serializable] public class NetChat { public bool sys; public string from, name, color, text; public long time; }

    // Tudo o que o servidor manda: hello, joined, left, room, rooms, chat, error, pong.
    [Serializable]
    public class NetMsg
    {
        public string t;
        public string you, code, text;                    // you: meu id (joined) · code/text: erro
        public bool sys;                                  // chat: aviso do servidor
        public string from, name, color;                  // chat
        public long time;
        public NetRoom room = new NetRoom();              // joined, room
        public List<NetListed> rooms = new List<NetListed>();
        public List<NetChat> chat = new List<NetChat>();  // joined: as últimas mensagens da sala
    }

    // O que o cliente manda.
    [Serializable] public class NetOut { public string t, name, roomName, code, color, text; public bool pub; }
    [Serializable] public class NetOutStages { public string t = "stages"; public List<NetStage> stages = new List<NetStage>(); }
}
