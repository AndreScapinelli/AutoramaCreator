#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Autorama
{
    // Teste do cliente online contra um servidor de verdade (servidor/servidor.js), sem cena:
    //   node servidor/servidor.js &
    //   Unity -batchmode -nographics -projectPath . -executeMethod Autorama.OnlineSmokeTest.Run -logFile -
    // A URL vem de AUTORAMA_URL (padrão ws://localhost:8080). Sai com código 0 se tudo passou.
    public static class OnlineSmokeTest
    {
        public static string Last = "";          // o relatório da última rodada (para ler pelo unity CLI: script eval)
        static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();

        class Peer
        {
            public readonly OnlineClient c = new OnlineClient();
            public readonly List<NetMsg> got = new List<NetMsg>();
            public string closed;
            public Peer() { c.Message += got.Add; c.Closed += r => closed = r; }
            public NetMsg Take(string t) { var i = got.FindIndex(m => m.t == t); if (i < 0) return null; var m = got[i]; got.RemoveAt(i); return m; }
        }

        static bool WaitFor(Func<bool> done, params Peer[] peers)
        {
            var end = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < end)
            {
                foreach (var p in peers) p.c.Pump();
                if (done()) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception("FALHOU: " + what);
            report.AppendLine("ok  " + what);
            Debug.Log("[OnlineSmokeTest] ok  " + what);
        }

        public static void Run()
        {
            int code = 0;
            report.Clear();
            Peer a = new Peer(), b = new Peer();
            try
            {
                string url = Environment.GetEnvironmentVariable("AUTORAMA_URL") ?? "ws://localhost:8080";
                a.c.Connect(url); b.c.Connect(url);
                Check(WaitFor(() => a.got.Exists(m => m.t == "hello") && b.got.Exists(m => m.t == "hello"), a, b), "os dois conectam e recebem hello");

                a.c.Send(new NetOut { t = "create", name = "Ana", roomName = "Sala de teste", pub = true });
                Check(WaitFor(() => a.got.Exists(m => m.t == "joined"), a, b), "criar sala devolve joined");
                var j = a.Take("joined");
                Check(j.room.players.Count == 1 && j.room.players[0].name == "Ana" && j.room.host == j.you && j.room.pub && j.room.code.Length == 5, "a sala tem a Ana como host e código de 5 letras");

                b.c.Send(new NetOut { t = "join", name = "", code = j.room.code });
                Check(WaitFor(() => b.got.Exists(m => m.t == "error"), a, b) && b.Take("error").code == "nome", "entrar sem nome é recusado");

                b.c.Send(new NetOut { t = "list" });
                Check(WaitFor(() => b.got.Exists(m => m.t == "rooms"), a, b), "lista de salas chega");
                var rooms = b.Take("rooms").rooms;
                Check(rooms.Count == 1 && rooms[0].code == j.room.code && rooms[0].host == "Ana" && rooms[0].players == 1, "a sala pública está na lista");

                b.c.Send(new NetOut { t = "join", name = "Beto", code = j.room.code });
                Check(WaitFor(() => b.got.Exists(m => m.t == "joined"), a, b), "visitante entra");
                var jb = b.Take("joined");
                Check(jb.room.players.Count == 2 && jb.room.host != jb.you && jb.chat.Count >= 1, "visitante vê 2 pilotos e o histórico do chat");

                b.c.Send(new NetOut { t = "color", color = "#F97316" });
                Check(WaitFor(() => a.got.FindAll(m => m.t == "room").Exists(m => m.room.Player(jb.you)?.color == "#F97316"), a, b), "visitante escolhe a cor e o host vê");

                b.c.Send(new NetOut { t = "chat", text = "oi <b>pessoal</b>" });
                Check(WaitFor(() => a.got.Exists(m => m.t == "chat" && m.name == "Beto"), a, b), "chat chega ao host");
                Check(a.got.Find(m => m.t == "chat" && m.name == "Beto").text == "oi <b>pessoal</b>", "o texto do chat chega inteiro");

                var stages = new NetOutStages();
                stages.stages.Add(new NetStage { name = "Oval", laps = 3, hash = Champ.TrackHash(TrackSave.Oval()), json = TrackSave.Oval().ToJson(false) });
                stages.stages.Add(new NetStage { name = "Oito", laps = 5, hash = Champ.TrackHash(TrackSave.Eight()), json = TrackSave.Eight().ToJson(false) });
                b.got.Clear();
                b.c.Send(stages);
                Check(WaitFor(() => b.got.Exists(m => m.t == "error"), a, b) && b.Take("error").code == "host", "visitante não escolhe pistas");
                a.got.Clear(); b.got.Clear();
                a.c.Send(stages);
                Check(WaitFor(() => b.got.Exists(m => m.t == "room" && m.room.stages.Count == 2), a, b), "host escolhe as pistas e o visitante vê");
                var rs = b.got.FindAll(m => m.t == "room").Find(m => m.room.stages.Count == 2).room.stages;
                Check(rs[0].name == "Oval" && rs[0].laps == 3 && rs[0].hash == "a34f4cb9" && rs[1].hash == "81546106" && string.IsNullOrEmpty(rs[0].json), "resumo das etapas sem o JSON, com os hashes conhecidos");

                a.c.Send(new NetOut { t = "visibility", pub = false });
                Check(WaitFor(() => b.got.FindAll(m => m.t == "room").Exists(m => !m.room.pub), a, b), "host torna a sala privada");

                a.c.Close();
                Check(WaitFor(() => b.got.FindAll(m => m.t == "room").Exists(m => m.room.host == jb.you), a, b), "host sai e o visitante vira host");

                var dead = new Peer();
                dead.c.Connect("ws://127.0.0.1:1");
                Check(WaitFor(() => dead.closed != null, dead) && dead.c.state == OnlineClient.State.Closed, "servidor fora do ar avisa em português: " + dead.closed);
                Check(!OnlineClient.TryParseUrl("http://x", out _) && OnlineClient.TryParseUrl("localhost:8080", out var u) && u.Scheme == "ws", "endereço do servidor é validado");
                Debug.Log("[OnlineSmokeTest] TUDO CERTO");
            }
            catch (Exception e) { Debug.LogError("[OnlineSmokeTest] " + e.Message); report.AppendLine(e.Message); code = 1; }
            finally { a.c.Close(); b.c.Close(); Last = report.ToString(); }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
