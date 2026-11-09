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

        // Pronto, largar, botão, snapshot, resultado e fim, tudo pelo cliente real e pelo JsonUtility.
        static void RunRace(string url)
        {
            Peer h = new Peer(), g = new Peer();
            try
            {
                h.c.Connect(url); g.c.Connect(url);
                Check(WaitFor(() => h.got.Exists(m => m.t == "hello") && g.got.Exists(m => m.t == "hello"), h, g), "corrida: os dois conectam");
                h.c.Send(new NetOut { t = "create", name = "Hugo", roomName = "Corrida", pub = false });
                Check(WaitFor(() => h.got.Exists(m => m.t == "joined"), h, g), "corrida: host cria a sala");
                string code = h.Take("joined").room.code;
                g.c.Send(new NetOut { t = "join", name = "Gabi", code = code });
                Check(WaitFor(() => g.got.Exists(m => m.t == "joined"), h, g), "corrida: visitante entra");
                string gid = g.Take("joined").you;

                var stages = new NetOutStages();
                stages.stages.Add(new NetStage { name = "Oval", laps = 3, hash = Champ.TrackHash(TrackSave.Oval()), json = TrackSave.Oval().ToJson(false) });
                stages.stages.Add(new NetStage { name = "Oito", laps = 2, hash = Champ.TrackHash(TrackSave.Eight()), json = TrackSave.Eight().ToJson(false) });
                h.c.Send(stages);
                h.c.Send(new NetOut { t = "start", vmax = 14f, ramp = 0.5f, grip = 29f, collide = true });
                Check(WaitFor(() => h.got.Exists(m => m.t == "error" && m.code == "nao-prontos"), h, g), "corrida: sem todos prontos o servidor recusa");

                g.c.Send(new NetOut { t = "ready", ready = true });
                Check(WaitFor(() => h.got.FindAll(m => m.t == "room").Exists(m => m.room.Player(gid)?.ready == true && m.room.stages.Count == 2), h, g), "corrida: o visitante fica pronto e o host vê");
                h.got.Clear(); g.got.Clear();
                h.c.Send(new NetOut { t = "start", name = "Copa", vmax = 14f, ramp = 0.5f, grip = 29f, collide = true });
                Check(WaitFor(() => h.got.Exists(m => m.t == "started") && g.got.Exists(m => m.t == "started"), h, g), "corrida: o campeonato começa para os dois");
                var st = g.Take("started"); h.Take("started");
                Check(st.title == "Copa" && st.pilots.Count == 2 && st.pilots[0].name == "Hugo" && st.rules.vmax == 14f && st.rules.collide && st.stages.Count == 2, "corrida: pilotos, regras e etapas chegam");
                var d = TrackSave.FromJson(st.stages[0].json);
                Check(Champ.TrackHash(d) == st.stages[0].hash && st.stages[1].hash == "81546106", "corrida: a pista chega inteira (mesmo hash)");

                g.c.Send(new NetOut { t = "in", held = true, re = 2 });
                Check(WaitFor(() => h.got.Exists(m => m.t == "in"), h, g), "corrida: o botão do visitante chega ao host");
                var inp = h.Take("in");
                Check(inp.from == gid && inp.held && inp.re == 2, "corrida: com o id, o estado e o contador de recolocar");

                var sim = MakeSim(TrackSave.Oval());
                sim.StartRace();
                for (int i = 0; i < 200; i++) sim.Step(1f / 60f);
                sim.cars[0].held = true;
                for (int i = 0; i < 60; i++) sim.Step(1f / 60f);
                h.c.Send(new NetOutSnap { snap = NetSync.Pack(sim, 4) });
                Check(WaitFor(() => g.got.Exists(m => m.t == "snap"), h, g), "corrida: o snapshot chega ao visitante");
                var sn = g.Take("snap").snap;
                Check(sn.hi == 4 && sn.ph == (int)sim.phase && sn.cars.Count == 2 && Mathf.Abs(sn.cars[0].s - sim.cars[0].s) < 1e-3f && sn.cars[0].held, "corrida: o snapshot traz fase, bateria e os dois carrinhos");

                var ev = new NetOutEv { k = "res", hi = 4 };
                ev.heatRes.Add(new HeatResult { laps = 3, time = 41.25f, best = 12.5f, offs = 1, hits = 2 });
                ev.heatRes.Add(new HeatResult { laps = 2, time = 60f, best = -1f, dnf = true });
                h.c.Send(ev);
                Check(WaitFor(() => g.got.Exists(m => m.t == "ev"), h, g), "corrida: o resultado da bateria chega");
                var e = g.Take("ev");
                Check(e.k == "res" && e.hi == 4 && e.heatRes.Count == 2 && e.heatRes[0].laps == 3 && Mathf.Abs(e.heatRes[0].time - 41.25f) < 1e-3f && e.heatRes[1].dnf && e.heatRes[1].best < 0f, "corrida: com voltas, tempo, melhor volta e não terminou");

                g.c.Send(new NetOut { t = "end" });
                Check(WaitFor(() => g.got.Exists(m => m.t == "error" && m.code == "host"), h, g), "corrida: visitante não encerra");
                h.got.Clear(); g.got.Clear();
                h.c.Send(new NetOut { t = "end", text = "Fim" });
                Check(WaitFor(() => h.got.Exists(m => m.t == "lobby") && g.got.Exists(m => m.t == "lobby"), h, g), "corrida: o host encerra e todos voltam ao lobby");
            }
            finally { h.c.Close(); g.c.Close(); }
        }

        static RaceSim MakeSim(TrackSave t)
        {
            var sim = new RaceSim(new RaceSettings { laps = 3, vmax = 14f, ramp = 0.5f, grip = 29f, collide = true });
            var segs = TrackLayout.Build(t.Origin, t.Pieces(), t.Tail);
            sim.SetTrack(segs, TrackLayout.Analyze(t.Origin, segs, t.Tail).closed);
            sim.cutoff = Champ.Cutoff;
            return sim;
        }

        // O host e um visitante (que controla o carrinho 0 com 80 ms de ida e 80 ms de volta, e só vê o outro pelos snapshots a 30 Hz)
        // rodam a mesma corrida; compara o placar e a posição dos dois no fim.
        static void RunSync()
        {
            var host = MakeSim(TrackSave.Oval());
            var guest = MakeSim(TrackSave.Oval());
            Check(host.Ready && host.closed && guest.closed, "sync: a pista oval está fechada nos dois");
            const float dt = 1f / 60f;
            const int delay = 5;                            // quadros de cada sentido (~83 ms)
            var toHost = new Queue<(bool held, bool reslot)>(); var toGuest = new Queue<string>();
            for (int i = 0; i < delay; i++) { toHost.Enqueue((false, false)); toGuest.Enqueue(null); }
            host.StartRace(); guest.StartRace();
            float maxOwn = 0f, maxOther = 0f; int snaps = 0, bytes = 0, reslots = 0;
            for (int f = 0; f < 60 * 150 && host.phase != RaceSim.Phase.Done; f++)
            {
                float time = f * dt;
                bool want0 = time % 5f < 3.3f;                                  // o visitante acelera e solta (carro 0)
                bool want1 = time % 6f < 3.9f;                                  // o host acelera e solta (carro 1)
                // Quem saiu da pista aperta Recolocar um instante depois, como um jogador faria.
                bool reslot0 = guest.cars[0].mode == CarMode.Off && f % 30 == 0;
                if (reslot0) { guest.Reslot(0); reslots++; }
                toHost.Enqueue((want0, reslot0));
                var arrivedIn = toHost.Dequeue();
                host.cars[0].held = arrivedIn.held;
                if (arrivedIn.reslot) host.Reslot(0);
                host.cars[1].held = want1;
                if (host.cars[1].mode == CarMode.Off && f % 30 == 0) { host.Reslot(1); reslots++; }
                host.Step(dt);
                if (f % 2 == 0)
                {
                    string json = JsonUtility.ToJson(new NetOutSnap { snap = NetSync.Pack(host, 0) });
                    bytes += json.Length; snaps++;
                    toGuest.Enqueue(json);
                }
                else toGuest.Enqueue(null);
                guest.cars[0].held = want0;                                     // predição: o botão vale na hora
                var arrived = toGuest.Dequeue();
                if (arrived != null) NetSync.Apply(JsonUtility.FromJson<NetMsg>(arrived).snap, guest, 0);
                guest.Step(dt);
                maxOwn = Mathf.Max(maxOwn, Mathf.Abs(guest.cars[0].s - host.cars[0].s));
                maxOther = Mathf.Max(maxOther, Mathf.Abs(guest.cars[1].s - host.cars[1].s));
            }
            Check(host.phase == RaceSim.Phase.Done, $"sync: a corrida acabou no host ({host.t:0.0} s, vencedor {host.winner})");
            for (int i = 0; i < delay * 3; i++)
            {
                host.cars[0].held = false; host.cars[1].held = false; host.Step(dt);
                var j = JsonUtility.ToJson(new NetOutSnap { snap = NetSync.Pack(host, 0) });
                NetSync.Apply(JsonUtility.FromJson<NetMsg>(j).snap, guest, 0);
            }
            Check(guest.phase == host.phase && guest.winner == host.winner, "sync: o visitante termina na mesma fase e com o mesmo vencedor");
            for (int k = 0; k < 2; k++)
            {
                var a = host.cars[k]; var b = guest.cars[k];
                Check(a.laps == b.laps && a.offs == b.offs && a.hits == b.hits && a.finish == b.finish && a.best == b.best && a.lastLap == b.lastLap, $"sync: carro {k}: voltas, saídas, toques, chegada e melhor volta iguais ({a.laps} voltas, melhor {a.best:0.00})");
            }
            report.AppendLine($"    desvio máx. do carro próprio {maxOwn:0.00} cél · do outro carro {maxOther:0.00} cél · {snaps} snapshots, {bytes / Mathf.Max(1, snaps)} bytes cada, {reslots} recolocações");
            Check(maxOwn < 1.6f && maxOther < 2.5f, $"sync: o desvio ficou pequeno (próprio {maxOwn:0.00} · outro {maxOther:0.00} células)");
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
                RunRace(url);
                RunSync();
                Debug.Log("[OnlineSmokeTest] TUDO CERTO");
            }
            catch (Exception e) { Debug.LogError("[OnlineSmokeTest] " + e.Message); report.AppendLine(e.Message); code = 1; }
            finally { a.c.Close(); b.c.Close(); Last = report.ToString(); }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
