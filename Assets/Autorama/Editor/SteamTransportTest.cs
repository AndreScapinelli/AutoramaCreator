#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Autorama
{
    // Testa o SteamTransport: primeiro com um mundo falso e três jogadores (a mesma lista de regras do servidor Node), depois
    // na Steam de verdade (Spacewar, uma conta): o host cria a sala, conversa, escolhe pistas e a lista a enxerga.
    //   script eval 'Autorama.SteamTransportTest.Run(); return Autorama.SteamTransportTest.Last;'
    public static class SteamTransportTest
    {
        public static string Last = "";
        static readonly StringBuilder report = new StringBuilder();

        class P
        {
            public readonly SteamTransport t;
            public readonly List<NetMsg> got = new List<NetMsg>();
            public P(ILobbyBackend b) { t = new SteamTransport(b); t.Message += got.Add; }
            public NetMsg Take(string type, Func<NetMsg, bool> where = null)
            {
                int i = got.FindIndex(m => m.t == type && (where == null || where(m)));
                if (i < 0) return null; var m2 = got[i]; got.RemoveAt(i); return m2;
            }
            public bool Has(string type, Func<NetMsg, bool> where = null) => got.Exists(m => m.t == type && (where == null || where(m)));
        }

        static bool WaitFor(Func<bool> done, P[] ps, float seconds = 5f)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                foreach (var p in ps) p.t.Pump();
                if (done()) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        // Dá uns ciclos para tudo chegar e confere que NÃO chegou.
        static bool Quiet(Func<bool> arrived, P[] ps) { WaitFor(arrived, ps, 0.25f); return !arrived(); }

        static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception("FALHOU: " + what);
            report.AppendLine("ok  " + what);
            Debug.Log("[SteamTransportTest] ok  " + what);
        }

        static NetStage Stage(string name, TrackSave t, int laps = 3) => new NetStage { name = name, laps = laps, hash = Champ.TrackHash(t), json = t.ToJson(false) };

        public static void Run()
        {
            report.Clear();
            try { RunFake(); RunReal(); Debug.Log("[SteamTransportTest] TUDO CERTO"); }
            catch (Exception e) { Debug.LogError("[SteamTransportTest] " + e.Message); report.AppendLine(e.Message); }
            finally { Last = report.ToString(); }
        }

        static void RunFake()
        {
            var hub = new FakeLobbyHub();
            P a = new P(hub.NewPlayer(1001, "Ana")), b = new P(hub.NewPlayer(1002, "Beto")), c = new P(hub.NewPlayer(1003, "Caio"));
            var all = new[] { a, b, c };
            foreach (var p in all) { int opened = 0; p.t.Opened += () => opened++; p.t.Connect(""); if (opened != 1 || p.t.state != OnlineClient.State.Open) throw new Exception("não abriu"); }
            Check(true, "fake: os três jogadores conectam");

            b.t.Send(new NetOut { t = "list" });
            Check(WaitFor(() => b.Has("rooms"), all) && b.Take("rooms").rooms.Count == 0, "fake: lista vazia no começo");

            a.t.Send(new NetOut { t = "create", roomName = "Sala da Ana", pub = true });
            Check(WaitFor(() => a.Has("joined"), all), "fake: criar sala devolve joined");
            var j = a.Take("joined");
            Check(j.you == "1001" && j.room.host == "1001" && j.room.players.Count == 1 && j.room.players[0].name == "Ana" && j.room.players[0].color == Champ.Colors[0] && j.room.players[0].ready && j.room.code.Length == 5 && j.room.pub && j.room.name == "Sala da Ana", "fake: o criador é o host, com a primeira cor e o nome da conta");
            string code = j.room.code;

            b.t.Send(new NetOut { t = "list" });
            Check(WaitFor(() => b.Has("rooms"), all), "fake: lista chega");
            var rooms = b.Take("rooms").rooms;
            Check(rooms.Count == 1 && rooms[0].code == code && rooms[0].host == "Ana" && rooms[0].players == 1, "fake: a sala pública está na lista");

            b.t.Send(new NetOut { t = "join", code = "ZZZZZ" });
            Check(WaitFor(() => b.Has("error"), all) && b.Take("error").code == "sem-sala", "fake: código errado é recusado");
            b.t.Send(new NetOut { t = "join", code = code.ToLowerInvariant() });
            Check(WaitFor(() => b.Has("joined"), all), "fake: entrar pelo código (sem diferenciar maiúsculas)");
            var jb = b.Take("joined");
            Check(jb.room.players.Count == 2 && jb.room.host == "1001" && jb.room.players[1].name == "Beto" && jb.room.players[1].color == Champ.Colors[1] && !jb.room.players[1].ready, "fake: o visitante entra com a 2ª cor e sem estar pronto");
            Check(WaitFor(() => a.Has("room", m => m.room.players.Count == 2) && a.Has("chat", m => m.sys && m.text.Contains("Beto entrou")), all), "fake: o host vê a sala com 2 e o aviso no chat");

            b.t.Send(new NetOut { t = "color", color = "#123456" });
            Check(WaitFor(() => b.Has("error"), all) && b.Take("error").code == "cor", "fake: cor fora da paleta é recusada");
            b.t.Send(new NetOut { t = "color", color = Champ.Colors[0] });
            Check(WaitFor(() => b.Has("error"), all) && b.Take("error").code == "cor-usada", "fake: cor já usada é recusada");
            a.got.Clear();
            b.t.Send(new NetOut { t = "color", color = Champ.Colors[5].ToLowerInvariant() });
            Check(WaitFor(() => a.Has("room", m => m.room.Player("1002")?.color == Champ.Colors[5]), all), "fake: o visitante escolhe a cor e o host vê");

            a.got.Clear(); b.got.Clear();
            b.t.Send(new NetOut { t = "chat", text = "  oi   <b>pessoal</b> " });
            Check(WaitFor(() => a.Has("chat", m => !m.sys) && b.Has("chat", m => !m.sys), all), "fake: o chat chega a todos");
            var chat = a.Take("chat", m => !m.sys);
            Check(chat.text == "oi <b>pessoal</b>" && chat.name == "Beto" && chat.color == Champ.Colors[5] && chat.from == "1002", "fake: com texto limpo, nome da conta e cor");
            for (int i = 0; i < 6; i++) b.t.Send(new NetOut { t = "chat", text = "spam " + i });
            Check(WaitFor(() => b.Has("error", m => m.code == "rapido"), all), "fake: o chat limita o ritmo");

            // pistas
            b.t.Send(new NetOutStages { stages = { Stage("Oval", TrackSave.Oval()) } });
            Check(WaitFor(() => b.Has("error", m => m.code == "host"), all), "fake: visitante não escolhe pistas");
            a.got.Clear(); b.got.Clear();
            a.t.Send(new NetOutStages { stages = { Stage("Oval", TrackSave.Oval(), 3), Stage("Oito", TrackSave.Eight(), 5) } });
            Check(WaitFor(() => b.Has("room", m => m.room.stages.Count == 2), all), "fake: o host escolhe pistas e o visitante vê o resumo");
            var rs = b.got.FindLast(m => m.t == "room").room.stages;
            Check(rs[0].name == "Oval" && rs[0].laps == 3 && rs[0].hash == "a34f4cb9" && rs[1].hash == "81546106" && string.IsNullOrEmpty(rs[0].json), "fake: resumo com os hashes conhecidos e sem o JSON");
            a.t.Send(new NetOutStages { stages = { new NetStage { name = "x", laps = 3, hash = "xyz", json = "{}" } } });
            Check(WaitFor(() => a.Has("error", m => m.code == "etapas"), all), "fake: etapa inválida é recusada");

            // visibilidade
            a.t.Send(new NetOut { t = "visibility", pub = false });
            c.t.Send(new NetOut { t = "list" });
            Check(WaitFor(() => c.Has("rooms"), all) && c.Take("rooms").rooms.Count == 0, "fake: a sala privada some da lista");
            a.t.Send(new NetOut { t = "visibility", pub = true });

            // pronto e largada
            a.got.Clear(); b.got.Clear();
            a.t.Send(new NetOut { t = "start", vmax = 14, ramp = .5f, grip = 29, collide = true });
            Check(WaitFor(() => a.Has("error", m => m.code == "nao-prontos" && m.text.Contains("Beto")), all), "fake: sem todos prontos o host não larga");
            b.t.Send(new NetOut { t = "ready", ready = true });
            Check(WaitFor(() => a.Has("room", m => m.room.Player("1002")?.ready == true), all), "fake: o visitante fica pronto e o host vê");
            a.t.Send(new NetOutStages { stages = { Stage("Oval", TrackSave.Oval(), 3), Stage("Oito", TrackSave.Eight(), 5), Stage("Oval", TrackSave.Oval(), 2) } });
            Check(WaitFor(() => a.Has("room", m => m.room.stages.Count == 3 && m.room.Player("1002")?.ready == false), all), "fake: mexer nas pistas zera o pronto");
            b.t.Send(new NetOut { t = "ready", ready = true });
            WaitFor(() => a.Has("room", m => m.room.Player("1002")?.ready == true), all);
            a.t.Send(new NetOut { t = "ready", ready = false });                       // o host está sempre pronto
            a.got.Clear(); b.got.Clear();
            b.t.Send(new NetOut { t = "start", vmax = 14, ramp = .5f, grip = 29, collide = true });
            Check(WaitFor(() => b.Has("error", m => m.code == "host"), all), "fake: visitante não larga");
            a.t.Send(new NetOut { t = "start", vmax = 99, ramp = .5f, grip = 29, collide = true });
            Check(WaitFor(() => a.Has("error", m => m.code == "regras"), all), "fake: regras fora dos limites são recusadas");
            a.got.Clear(); b.got.Clear();
            a.t.Send(new NetOut { t = "start", name = "Copa  Fake", vmax = 14, ramp = .5f, grip = 29, collide = true });
            Check(WaitFor(() => a.Has("started") && b.Has("started"), all), "fake: todos prontos: o campeonato começa para o host e o visitante");
            var sa = a.Take("started"); var sb = b.Take("started");
            Check(sb.title == "Copa Fake" && sb.pilots.Count == 2 && sb.pilots[0].name == "Ana" && sb.rules.vmax == 14f && sb.rules.collide && sb.stages.Count == 3, "fake: pilotos, regras e etapas chegam");
            var d = TrackSave.FromJson(sb.stages[1].json);
            Check(Champ.TrackHash(d) == "81546106" && sa.stages[1].json == sb.stages[1].json, "fake: a pista chega inteira (mesmo hash)");

            c.t.Send(new NetOut { t = "join", code = code });
            Check(WaitFor(() => c.Has("error"), all) && c.Take("error").code == "andamento", "fake: com o campeonato andando ninguém entra");
            b.got.Clear();
            b.t.Send(new NetOut { t = "color", color = Champ.Colors[6] });
            Check(WaitFor(() => b.Has("error", m => m.code == "andamento"), all), "fake: nem troca de cor");

            // corrida: in → host, snap/ev → visitante
            a.got.Clear(); b.got.Clear();
            b.t.Send(new NetOut { t = "in", held = true, re = 2 });
            Check(WaitFor(() => a.Has("in"), all), "fake: o botão do visitante chega ao host");
            var inp = a.Take("in");
            Check(inp.from == "1002" && inp.held && inp.re == 2, "fake: com o id de quem mandou, o estado e o contador");
            a.t.Send(new NetOut { t = "in", held = true });
            Check(Quiet(() => b.Has("in") || a.Has("in"), all), "fake: o host não manda botão a ninguém");
            var sim = new RaceSim(new RaceSettings());
            a.t.Send(new NetOutSnap { snap = NetSync.Pack(sim, 1) });
            Check(WaitFor(() => b.Has("snap"), all) && b.Take("snap").snap.hi == 1, "fake: o snapshot do host chega ao visitante");
            b.t.Send(new NetOutSnap { snap = NetSync.Pack(sim, 9) });
            Check(Quiet(() => a.Has("snap"), all), "fake: snapshot de visitante é ignorado");
            var ev = new NetOutEv { k = "res", hi = 1 }; ev.heatRes.Add(new HeatResult { laps = 3, time = 41.25f }); ev.heatRes.Add(new HeatResult { dnf = true });
            a.t.Send(ev);
            Check(WaitFor(() => b.Has("ev"), all), "fake: o resultado da bateria chega");
            var evb = b.Take("ev");
            Check(evb.k == "res" && evb.hi == 1 && evb.heatRes[0].laps == 3 && evb.heatRes[1].dnf, "fake: com os dois resultados");

            // fim
            b.t.Send(new NetOut { t = "end" });
            Check(WaitFor(() => b.Has("error", m => m.code == "host"), all), "fake: visitante não encerra");
            a.got.Clear(); b.got.Clear();
            a.t.Send(new NetOut { t = "end", text = "Fim de papo" });
            Check(WaitFor(() => a.Has("lobby") && b.Has("lobby"), all), "fake: o host encerra e todos voltam ao lobby");
            Check(b.Take("lobby").text == "Fim de papo", "fake: com a mensagem do host");
            Check(WaitFor(() => b.Has("room", m => m.room.state == "lobby" && m.room.Player("1002").ready == false && m.room.Player("1001").ready), all), "fake: o pronto zera (o host continua pronto)");

            // host sai no meio: encerra
            b.t.Send(new NetOut { t = "ready", ready = true });
            WaitFor(() => a.Has("room", m => m.room.Player("1002")?.ready == true), all);
            a.t.Send(new NetOut { t = "start", vmax = 14, ramp = .5f, grip = 29, collide = true });
            WaitFor(() => b.Has("started"), all);
            b.got.Clear();
            a.t.Send(new NetOut { t = "leave" });
            Check(WaitFor(() => b.Has("lobby", m => m.text.Contains("host saiu")), all), "fake: host que sai no meio da corrida encerra o campeonato");
            Check(WaitFor(() => b.Has("room", m => m.room.host == "1002" && m.room.state == "lobby" && m.room.players.Count == 1), all), "fake: o visitante vira o host e o lobby volta");
            Check(b.t.state == OnlineClient.State.Open, "fake: a conexão segue aberta");
            foreach (var p in all) p.t.Close();
            Check(hub.lobbies.Count == 0, "fake: sala vazia some do mundo");
        }

        // ---------- UI com o transporte Steam, no Play mode (host na tela, visitante-robô no mundo falso) ----------

        static FakeLobbyHub uiHub;
        static P uiBot;
        static bool botJoined, botReady, botHeld;
        public static int BotSnaps, BotEvents;

        public static void UiSetup()
        {
            uiHub = new FakeLobbyHub();
            var host = uiHub.NewPlayer(2001, "Hugo");
            var bot = uiHub.NewPlayer(2002, "Bia");
            SteamTransport.BackendFactory = () => host;
            uiBot = new P(bot);
            uiBot.t.Connect("");
            botJoined = botReady = botHeld = false; BotSnaps = BotEvents = 0;
        }

        // Chamar a cada quadro: o robô entra na sala, fica pronto e, na corrida, acelera e solta.
        public static string BotTick(int frame)
        {
            if (uiBot == null) return "sem robô";
            uiBot.t.Pump();
            if (!botJoined && uiHub.lobbies.Count > 0)
            {
                foreach (var l in uiHub.lobbies.Values) { uiBot.t.Send(new NetOut { t = "join", code = l.data["code"] }); break; }
                botJoined = uiBot.Has("joined") || true;
            }
            // Fica pronto de novo toda vez que o host mexe nas etapas (o servidor zera o "pronto").
            var lastRoom = uiBot.got.FindLast(m => m.t == "room");
            if (uiBot.Has("joined") && !uiBot.Has("started") && (!botReady || (lastRoom != null && lastRoom.room.Player("2002") is var me && me != null && !me.ready && lastRoom.room.state == "lobby")))
            { uiBot.t.Send(new NetOut { t = "ready", ready = true }); botReady = true; }
            BotSnaps += uiBot.got.RemoveAll(m => m.t == "snap");
            BotEvents += uiBot.got.RemoveAll(m => m.t == "ev");
            if (uiBot.Has("started"))
            {
                bool want = frame % 300 < 200;
                if (want != botHeld) { botHeld = want; uiBot.t.Send(new NetOut { t = "in", held = want, re = 0 }); }
            }
            uiBot.got.RemoveAll(m => m.t == "chat" || m.t == "room" || m.t == "in");
            return $"sala={(uiHub.lobbies.Count > 0 ? "sim" : "não")} bot: entrou={uiBot.Has("joined")} pronto={botReady} largou={uiBot.Has("started")} snaps={BotSnaps} ev={BotEvents}";
        }

        public static void BotSay(string text) => uiBot?.t.Send(new NetOut { t = "chat", text = text });

        public static void UiTeardown()
        {
            uiBot?.t.Close(); uiBot = null; uiHub = null;
            SteamTransport.BackendFactory = () => new SteamBackend();
        }

        // Steam de verdade, uma conta só: o que dá para provar sem uma segunda pessoa.
        static void RunReal()
        {
            SteamTransport.BackendFactory = () => new SteamBackend();
            var real = new P(new SteamBackend());
            var ps = new[] { real };
            try
            {
                string err = null; bool opened = false;
                real.t.Opened += () => opened = true; real.t.Closed += r => err = r;
                real.t.Connect("");
                Check(opened && err == null, "steam: o transporte abre (Steam iniciada)" + (err != null ? ": " + err : ""));
                string me = real.t.LocalName;
                Check(!string.IsNullOrEmpty(me), $"steam: o nome vem da conta ({me})");

                real.t.Send(new NetOut { t = "create", roomName = "Transporte Autorama", pub = true });
                Check(WaitFor(() => real.Has("joined"), ps, 10f), "steam: criar sala devolve joined");
                var j = real.Take("joined");
                Check(j.room.players.Count == 1 && j.room.players[0].name == me && j.room.host == j.you && j.room.code.Length == 5 && j.room.name == "Transporte Autorama" && j.room.state == "lobby" && j.room.pub, "steam: sala com a conta como host, código, nome e estado");

                real.got.Clear();
                real.t.Send(new NetOutStages { stages = { Stage("Oval", TrackSave.Oval(), 4), Stage("Oito", TrackSave.Eight(), 2) } });
                Check(WaitFor(() => real.Has("room", m => m.room.stages.Count == 2), ps), "steam: as etapas ficam no lobby (resumo)");
                var rs = real.got.FindLast(m => m.t == "room").room.stages;
                Check(rs[0].hash == "a34f4cb9" && rs[0].laps == 4 && rs[1].hash == "81546106", "steam: com os hashes conhecidos");

                real.got.Clear();
                real.t.Send(new NetOut { t = "chat", text = "oi pela Steam" });
                Check(WaitFor(() => real.Has("chat", m => !m.sys && m.text == "oi pela Steam"), ps), "steam: o chat volta com o nome da conta");

                real.t.Send(new NetOut { t = "color", color = Champ.Colors[3] });
                Check(WaitFor(() => real.got.Exists(m => m.t == "room" && m.room.Player(j.you)?.color == Champ.Colors[3]), ps), "steam: a cor muda");

                real.t.Send(new NetOut { t = "start", vmax = 14, ramp = .5f, grip = 29, collide = true });
                Check(WaitFor(() => real.Has("error", m => m.code == "poucos"), ps), "steam: largar sozinho é recusado (precisa de 2)");

                // A lista pode demorar uns segundos para enxergar um lobby novo.
                NetMsg rooms = null;
                for (int i = 0; i < 6 && (rooms == null || !rooms.rooms.Exists(r => r.code == j.room.code)); i++)
                {
                    real.t.Send(new NetOut { t = "list" });
                    WaitFor(() => real.Has("rooms"), ps, 5f);
                    rooms = real.Take("rooms");
                    if (rooms == null || !rooms.rooms.Exists(r => r.code == j.room.code)) WaitFor(() => false, ps, 1f);
                }
                var mine = rooms?.rooms.Find(r => r.code == j.room.code);
                Check(mine != null && mine.name == "Transporte Autorama" && mine.stages == 2 && mine.host == me, "steam: a lista de salas enxerga a sala");

                real.t.Send(new NetOut { t = "visibility", pub = false });
                Check(WaitFor(() => real.got.Exists(m => m.t == "room" && !m.room.pub), ps), "steam: o host torna a sala privada");

                // Mensagem direta para si mesmo: a Steam aceita? (só informação; o teste de verdade é com duas contas)
                var be = new SteamBackend(); be.Init(out _);
                ulong from = 0; byte[] rx = null; be.Peer += (f, d) => { from = f; rx = d; };
                be.SendTo(be.MyId, Encoding.UTF8.GetBytes("{\"t\":\"ev\"}"), true);
                for (int i = 0; i < 100 && rx == null; i++) { be.Tick(); Thread.Sleep(20); }
                report.AppendLine($"    mensagem direta para si mesmo: {(rx != null ? "chegou" : "não chegou (esperado: só entre jogadores diferentes)")}");

                real.t.Send(new NetOut { t = "leave" });
                Check(WaitFor(() => real.Has("left"), ps), "steam: sair da sala");
            }
            finally { real.t.Shutdown(); }
        }
    }
}
#endif
