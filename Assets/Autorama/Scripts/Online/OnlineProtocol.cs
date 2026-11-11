using System;
using System.Collections.Generic;

namespace Autorama
{
    // Mensagens trocadas com o servidor de salas (servidor/servidor.js; seção "Campeonato online" do guia).
    // São JSON de uma linha com o tipo em "t". O JsonUtility não faz herança, então cada direção tem classes enxutas.

    [Serializable] public class NetPlayer { public string id, name, color; public bool ready; }
    // secs: duração por tempo (0 = cliente antigo, vale 120) · tempo: o host escolheu "por tempo" · faixas: pista de 2 ou 4 carros
    [Serializable] public class NetStage { public string name, hash, json; public int laps, secs, faixas; public bool tempo; }   // json: a pista (TrackSave); o servidor só devolve o resumo

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
    [Serializable] public class NetRules { public float vmax, ramp, grip; public bool collide, byTime; }   // byTime: campeonato por tempo (seção 26)

    // Um carrinho no snapshot: tudo o que o RaceSim guarda por carro. Os opcionais (voltas) usam −1 para "não tem".
    [Serializable]
    public class NetCar
    {
        public float s, v, hold, T, slip;
        public bool held, contact;
        public int side, mode;                              // mode: CarMode
        public int gp;                                      // nível do acelerador em % (1–100); 0 = cliente antigo, vale 100
        public float fx, fy, fvx, fvy, flyH, flyW, flyZ;    // fora da pista
        public float bx, by, backH, backY, backT;           // voltando ao trilho
        public float exitS, lapStart, hitFx;
        public int cross, laps, offs, hits;
        public float lastLap = -1f, best = -1f, finish = -1f;
        public int lane, cb;                                // corrida por tempo: trilho atual e ajuste das bandeiradas
        public bool ld;                                     // volta que pegou a troca de trilho
    }

    // hi: a bateria do snapshot (ele só vale para quem está nela) · ph: RaceSim.Phase · t: relógio · w: vencedor (−1 ninguém)
    [Serializable] public class NetSnap { public int hi, ph, w, mi; public float t, rt; public List<NetCar> cars = new List<NetCar>(); }   // mi: manche · rt: relógio guardado na troca

    // Tudo o que o servidor manda: hello, joined, left, room, rooms, chat, error, pong, started, lobby, in, snap, ev.
    [Serializable]
    public class NetMsg
    {
        public string t;
        public string you, code, text;                    // you: meu id (joined) · code/text: erro
        public bool sys;                                  // chat: aviso do servidor
        // suggested (visitante → host): uma pista sugerida (o remetente vem em from/name/color)
        public string track, hash, json;
        public int faixas;
        public string from, name, color;                  // chat · in: quem mandou
        public long time;
        public NetRoom room = new NetRoom();              // joined, room
        public List<NetListed> rooms = new List<NetListed>();
        public List<NetChat> chat = new List<NetChat>();  // joined: as últimas mensagens da sala
        // started: o campeonato (pilotos na ordem do rodízio, regras e as pistas com o JSON)
        public string title;
        public NetRules rules = new NetRules();
        public List<NetPlayer> pilots = new List<NetPlayer>();
        public List<NetStage> stages = new List<NetStage>();
        // in (visitante → host): o botão e o contador de "recolocar"
        public bool held;
        public int re;
        public int gp;                                    // in: nível do acelerador em % (0 = cliente antigo, vale 100)
        // snap / ev (host → todos)
        public NetSnap snap = new NetSnap();
        public string k;                                  // ev: res (resultado salvo) · reset (bateria de novo)
        public int hi;                                    // ev: índice da bateria
        public List<HeatResult> heatRes = new List<HeatResult>();
    }

    // O que o cliente manda.
    [Serializable]
    public class NetOut
    {
        public string t, name, roomName, code, color, text;
        public bool pub, ready, held, collide, byTime;
        public int re, ver;                               // ver: versão do protocolo (hello)
        public int gp;                                    // in: nível do acelerador em % (1–100)
        public float vmax, ramp, grip;                    // start: as regras (os ajustes da aba Corrida do host)
        public string track, hash, json;                  // suggest: a pista que o visitante sugere ao host (nome, identidade e o JSON)
        public int faixas;                                // suggest: 2 ou 4 carros
    }
    [Serializable] public class NetOutStages { public string t = "stages"; public List<NetStage> stages = new List<NetStage>(); }
    [Serializable] public class NetOutSnap { public string t = "snap"; public NetSnap snap; }
    [Serializable] public class NetOutEv { public string t = "ev"; public string k; public int hi; public List<HeatResult> heatRes = new List<HeatResult>(); }
}
