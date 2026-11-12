using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Autorama
{
    // O "servidor" do campeonato online feito dentro de cada cliente, em cima de um lobby (Steam, ou um mundo falso nos testes).
    // Fala a mesma língua do servidor Node (OnlineClient): recebe NetOut… e devolve NetMsg, então a aba Online não muda.
    // Mapa (seção 24 do guia):
    //   create/join/list/leave/visibility → lobby (dados do lobby escritos pelo dono; busca por código)
    //   color/ready → dados do membro · chat → chat do lobby · host → dono do lobby
    //   start/end → mensagens diretas do host aos outros (started, lobby) + dado "state" do lobby
    //   in → visitante → host · snap/ev → host → visitantes (mensagens diretas)
    // O que o servidor Node confere (host, prontos, limites, regras) é conferido aqui por quem manda; os outros só confiam em quem é o dono.
    public class SteamTransport : IOnlineTransport
    {
        public static Func<ILobbyBackend> BackendFactory = () => new SteamBackend();

        const int MaxStages = 12, MaxTrackJson = 100 * 1024, MaxChat = 200, MaxReliable = 500 * 1024;

        [Serializable] class StageSummary { public List<NetStage> s = new List<NetStage>(); }

        readonly ILobbyBackend b;
        public OnlineClient.State state { get; private set; }
        public event Action Opened;
        public event Action<string> Closed;
        public event Action<NetMsg> Message;
        public event Action<ulong> JoinRequested;                    // um amigo convidou: a aba decide como entrar (JoinById)

        bool joined, racing;
        ulong raceHost, lastOwner;
        float lobbyFlipSince = -1f;
        HashSet<ulong> known = new HashSet<ulong>();
        readonly Dictionary<ulong, string> names = new Dictionary<ulong, string>();
        readonly List<NetStage> stageData = new List<NetStage>();     // do host: com o JSON (o lobby guarda só o resumo)
        readonly List<float> chatTimes = new List<float>();
        readonly Dictionary<ulong, float> syncAt = new Dictionary<ulong, float>();   // host: último pedido de sync de cada um

        public SteamTransport(ILobbyBackend backend = null)
        {
            b = backend ?? BackendFactory();
            b.MembersChanged += OnMembers;
            b.DataChanged += () => { if (joined) EmitRoom(); };
            b.Chat += OnChat;
            b.Peer += OnPeer;
            b.JoinRequested += id => JoinRequested?.Invoke(id);
        }

        // ---------- Convites ----------

        // Amigos online que ainda não estão na sala.
        public List<LobbyFriend> InvitableFriends()
        {
            var inRoom = new HashSet<ulong>(); foreach (var m in b.Members()) inRoom.Add(m.id);
            return b.Friends().FindAll(f => f.online && !inRoom.Contains(f.id));
        }

        public bool Invite(ulong friend) => joined && state == OnlineClient.State.Open && b.Invite(friend);
        public void OpenInviteDialog() { if (joined) b.OpenInviteDialog(); }

        // Entrar no lobby de um amigo (convite aceito, ou "+connect_lobby" na linha de comando).
        public void JoinById(ulong lobbyId)
        {
            if (state != OnlineClient.State.Open) return;
            if (joined) { Err("sala", "Você já está numa sala."); return; }
            b.Join(lobbyId, ok =>
            {
                if (state != OnlineClient.State.Open) { b.Leave(); return; }
                if (!ok) { Err("sem-sala", "Não deu para entrar na sala do amigo."); return; }
                if (b.Data(SteamLobbyClient.Tag) != "1" || b.Data("ver") != OnlineClient.Protocol.ToString()) { b.Leave(); Err("sem-sala", "Esse lobby não é de uma sala do Autorama (ou é de outra versão do jogo)."); return; }
                b.SetMyData("ready", "0");
                AssignColor();
                Enter();
            });
        }

        // O nome do jogador é o da conta (a aba mostra e não deixa editar).
        public string LocalName => b.MyName;
        public bool Prepare(out string error) => b.Init(out error);
        public void Shutdown() { Close(); b.Shutdown(); }

        string Me => b.MyId.ToString();
        bool IsHost => joined && b.Owner == b.MyId;

        // ---------- IOnlineTransport ----------

        public void Connect(string address)
        {
            if (state != OnlineClient.State.Closed) return;
            state = OnlineClient.State.Connecting;
            if (!b.Init(out string err)) { state = OnlineClient.State.Closed; Closed?.Invoke(err); return; }
            state = OnlineClient.State.Open;
            Opened?.Invoke();
        }

        public void Close()
        {
            if (joined) b.Leave();
            joined = racing = false;
            stageData.Clear();
            state = OnlineClient.State.Closed;
        }

        public void Pump()
        {
            if (state != OnlineClient.State.Open) return;
            b.Tick();
            if (!joined) return;
            if (b.Owner != lastOwner && b.Owner != 0)
            {
                lastOwner = b.Owner;
                if (!racing) Say($"{NameOf(lastOwner)} agora é o host.");
                AdoptRaceHost();
                EmitRoom();
            }
            // Rede de segurança: o host voltou o lobby e a mensagem direta se perdeu.
            if (racing && b.Data("state") == "lobby")
            {
                if (lobbyFlipSince < 0f) lobbyFlipSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - lobbyFlipSince > 2f) { racing = false; lobbyFlipSince = -1f; Emit(new NetMsg { t = "lobby", text = "O campeonato terminou." }); EmitRoom(); }
            }
            else lobbyFlipSince = -1f;
        }

        public void Send(object msg)
        {
            if (state != OnlineClient.State.Open) return;
            switch (msg)
            {
                case NetOut o: HandleOut(o); break;
                case NetOutStages s: HandleStages(s); break;
                case NetOutSnap s: if (IsHost && racing) Broadcast(JsonUtility.ToJson(s), false); break;
                case NetOutEv e:
                    if (!IsHost || !racing) break;
                    if (string.IsNullOrEmpty(e.to)) { Broadcast(JsonUtility.ToJson(e), true); break; }
                    // Resposta a um sync: só para quem pediu (leva as pistas, então confere o tamanho como na largada).
                    var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(e));
                    if (ulong.TryParse(e.to, out ulong to) && known.Contains(to) && bytes.Length <= MaxReliable) b.SendTo(to, bytes, true);
                    break;
            }
        }

        // ---------- Comandos do jogador ----------

        void HandleOut(NetOut o)
        {
            switch (o.t)
            {
                case "list": b.Search(null, list => Emit(new NetMsg { t = "rooms", rooms = list.FindAll(r => r.state == "lobby").ConvertAll(r => new NetListed { code = r.code, name = r.name, host = r.host, players = r.players, max = r.max, stages = r.stages }) })); break;
                case "create": Create(o); break;
                case "join": Join(o); break;
                case "leave": if (joined) { b.Leave(); joined = racing = false; stageData.Clear(); Emit(new NetMsg { t = "left" }); } break;
                case "color": SetColor(o.color); break;
                case "ready": if (joined && !IsHost) { if (racing) Err("andamento", "O campeonato já começou."); else b.SetMyData("ready", o.ready ? EpochNow() : "0"); } break;
                case "chat": Chat(o.text); break;
                case "visibility": if (RequireHost()) b.SetPublic(o.pub); break;
                case "start": Start(o); break;
                case "end": End(o.text); break;
                case "in": if (joined && racing && !IsHost) b.SendTo(b.Owner, Encoding.UTF8.GetBytes(JsonUtility.ToJson(o)), true); break;
                case "suggest": Suggest(o); break;
                case "sync": if (joined && racing && !IsHost) b.SendTo(b.Owner, Encoding.UTF8.GetBytes(JsonUtility.ToJson(o)), true); break;
            }
        }

        bool RequireHost()
        {
            if (!joined) return false;
            if (!IsHost) { Err("host", "Só o host pode fazer isso."); return false; }
            return true;
        }

        void Create(NetOut o)
        {
            if (joined) { Err("sala", "Você já está numa sala."); return; }
            b.Create(o.roomName, o.pub, ok =>
            {
                if (state != OnlineClient.State.Open) { b.Leave(); return; }
                if (!ok) { Err("erro", "Não deu para criar a sala na Steam."); return; }
                b.SetData("epoch", "1");                // "0" é "não pronto": a época começa em 1
                SaveSummaries();
                b.SetMyData("ready", "0");
                AssignColor();
                Enter();
            });
        }

        void Join(NetOut o)
        {
            if (joined) { Err("sala", "Você já está numa sala."); return; }
            string code = (o.code ?? "").Trim().ToUpperInvariant();
            b.Search(code, list =>
            {
                var r = list.Count > 0 ? list[0] : null;
                if (r == null) { Err("sem-sala", "Sala não encontrada. Confira o código."); return; }
                if (r.players >= r.max) { Err("cheia", "A sala está cheia."); return; }
                b.Join(r.id, ok =>
                {
                    if (state != OnlineClient.State.Open) { b.Leave(); return; }
                    if (!ok) { Err("sem-sala", "Não deu para entrar na sala."); return; }
                    b.SetMyData("ready", "0");
                    AssignColor();
                    Enter();
                });
            });
        }

        // Campeonato andando: quem entra (de volta de uma queda, ou para assistir) já aceita o que vem do host e pede o sync.
        void Enter()
        {
            joined = true; racing = b.Data("state") == "racing"; raceHost = b.Owner; lobbyFlipSince = -1f;
            known = new HashSet<ulong>(); foreach (var m in b.Members()) { known.Add(m.id); names[m.id] = m.name; }
            lastOwner = b.Owner;
            Emit(new NetMsg { t = "joined", you = Me, room = BuildRoom() });
        }

        void AssignColor()
        {
            foreach (var c in Champ.Colors) if (!ColorTaken(c)) { b.SetMyData("color", c); return; }
        }

        bool ColorTaken(string col)
        {
            foreach (var m in b.Members()) if (m.id != b.MyId && string.Equals(b.MemberData(m.id, "color"), col, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void SetColor(string color)
        {
            if (!joined) return;
            if (racing) { Err("andamento", "O campeonato já começou."); return; }
            string col = Array.Find(Champ.Colors, c => string.Equals(c, color, StringComparison.OrdinalIgnoreCase));
            if (col == null) { Err("cor", "Cor inválida."); return; }
            if (ColorTaken(col)) { Err("cor-usada", "Essa cor já é de outro piloto."); return; }
            b.SetMyData("color", col);
            EmitRoom();
        }

        // Visitante: sugere uma pista ao host (só no lobby), com o mesmo limite de tamanho e de ritmo do servidor Node.
        readonly List<float> suggTimes = new List<float>();
        readonly Dictionary<ulong, List<float>> suggFrom = new Dictionary<ulong, List<float>>();    // host: ritmo de cada remetente
        const int MaxSuggPerMin = 3;

        static bool SuggestionOk(string hash, string json) =>
            hash != null && hash.Length == 8 && !string.IsNullOrEmpty(json) && json.Length <= MaxTrackJson;

        static bool Rate(List<float> times)
        {
            float now = Time.realtimeSinceStartup;
            times.RemoveAll(t => now - t > 60f);
            if (times.Count >= MaxSuggPerMin) return false;
            times.Add(now);
            return true;
        }

        void Suggest(NetOut o)
        {
            if (!joined || IsHost) return;
            if (racing) { Err("andamento", "O campeonato já começou."); return; }
            if (!SuggestionOk(o.hash, o.json)) { Err("sugestao", "Pista inválida ou grande demais."); return; }
            if (!Rate(suggTimes)) { Err("rapido", "Você sugeriu pistas demais em pouco tempo. Espere um minuto."); return; }
            o.t = "suggested"; o.track = Clean(o.track, 60); o.faixas = TrackMath.Faixas(o.faixas);
            b.SendTo(b.Owner, Encoding.UTF8.GetBytes(JsonUtility.ToJson(o)), true);
        }

        void Chat(string text)
        {
            if (!joined) return;
            text = Clean(text, MaxChat);
            if (text.Length == 0) return;
            float now = Time.realtimeSinceStartup;
            chatTimes.RemoveAll(t => now - t > 3f);
            if (chatTimes.Count >= 5) { Err("rapido", "Calma, você está mandando mensagens rápido demais."); return; }
            chatTimes.Add(now);
            b.SendChat(text);
        }

        // Só o host. Os prontos de quem já tinha confirmado valem só para a "época" anterior: subir a época zera todos.
        void HandleStages(NetOutStages s)
        {
            if (!RequireHost()) return;
            if (racing) { Err("andamento", "O campeonato já começou."); return; }
            if (s.stages == null || s.stages.Count > MaxStages) { Err("etapas", $"No máximo {MaxStages} etapas."); return; }
            var list = new List<NetStage>();
            foreach (var st in s.stages)
            {
                if (st != null && st.secs == 0) st.secs = 120;
                if (st == null || string.IsNullOrEmpty(st.json) || st.json.Length > MaxTrackJson || st.laps < 1 || st.laps > 30 || st.secs < 60 || st.secs > 600 || st.hash == null || st.hash.Length != 8)
                { Err("etapas", "Etapa inválida ou pista grande demais."); return; }
                list.Add(new NetStage { name = Clean(st.name, 60) is var n && n.Length > 0 ? n : "Pista", laps = st.laps, secs = st.secs, tempo = st.tempo, faixas = TrackMath.Faixas(st.faixas), hash = st.hash, json = st.json });
            }
            bool changed = list.Count != stageData.Count;
            for (int i = 0; !changed && i < list.Count; i++) changed = list[i].hash != stageData[i].hash || list[i].laps != stageData[i].laps || list[i].secs != stageData[i].secs || list[i].tempo != stageData[i].tempo;
            stageData.Clear(); stageData.AddRange(list);
            SaveSummaries();
            if (changed && b.Members().Exists(m => m.id != b.MyId && IsReady(m.id)))
            {
                BumpEpoch();
                Say("O host mudou as etapas: confirmem \"pronto\" de novo.");
            }
            EmitRoom();
        }

        void Start(NetOut o)
        {
            if (!RequireHost()) return;
            if (racing || b.Data("state") != "lobby") { Err("andamento", "O campeonato já começou."); return; }
            var room = BuildRoom();
            if (room.players.Count < 2) { Err("poucos", "Precisa de pelo menos 2 pilotos."); return; }
            if (stageData.Count == 0) { Err("sem-etapas", "Escolha pelo menos uma pista."); return; }
            var wait = room.players.FindAll(p => p.id != Me && !p.ready);
            if (wait.Count > 0) { Err("nao-prontos", "Falta ficar pronto: " + string.Join(", ", wait.ConvertAll(p => p.name)) + "."); return; }
            if (!(o.vmax >= 8 && o.vmax <= 20 && o.ramp >= 0.2f && o.ramp <= 1.5f && o.grip >= 15 && o.grip <= 45)) { Err("regras", "Regras inválidas."); return; }

            var m = new NetMsg { t = "started", title = Clean(o.name, 40) is var t && t.Length > 0 ? t : $"Campeonato de {NameOf(b.MyId)}", rules = new NetRules { vmax = o.vmax, ramp = o.ramp, grip = o.grip, collide = o.collide, byTime = o.byTime, turbo = o.turbo } };
            foreach (var p in room.players) m.pilots.Add(new NetPlayer { id = p.id, name = p.name, color = p.color, uid = p.uid });
            foreach (var s in stageData) m.stages.Add(new NetStage { name = s.name, laps = s.laps, secs = s.secs, tempo = s.tempo, faixas = s.faixas, hash = s.hash, json = s.json });
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(m));
            if (bytes.Length > MaxReliable) { Err("etapas", "As pistas são grandes demais para a Steam. Tire alguma etapa."); return; }

            foreach (var p in room.players) if (p.id != Me) b.SendTo(ulong.Parse(p.id), bytes, true);
            b.SetData("state", "racing");                // o lobby continua aberto: quem cair volta (o SteamID é o mesmo) e outros entram para assistir
            racing = true; raceHost = b.MyId; lobbyFlipSince = -1f;
            Say($"{NameOf(b.MyId)} largou o campeonato \"{m.title}\".");
            Emit(m);
            EmitRoom();
        }

        void End(string text)
        {
            if (!RequireHost()) return;
            if (!racing) return;
            EndRace(Clean(text, 100) is var t && t.Length > 0 ? t : "O campeonato terminou.", true);
        }

        // Volta ao lobby. Quem manda (o host, ou o novo dono se o host saiu) grava o estado e zera os prontos.
        void EndRace(string text, bool announce)
        {
            racing = false; lobbyFlipSince = -1f;
            if (IsHost)
            {
                b.SetData("state", "lobby");
                BumpEpoch();
                b.SetJoinable(true);
                if (announce)
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new NetMsg { t = "lobby", text = text }));
                    foreach (var m in b.Members()) if (m.id != b.MyId) b.SendTo(m.id, bytes, true);
                }
            }
            Emit(new NetMsg { t = "lobby", text = text });
            EmitRoom();
        }

        // ---------- Mensagens diretas ----------

        void Broadcast(string json, bool reliable)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            foreach (var m in b.Members()) if (m.id != b.MyId) b.SendTo(m.id, bytes, reliable);
        }

        void OnPeer(ulong from, byte[] data)
        {
            if (!joined || !known.Contains(from)) return;
            NetMsg m;
            try { m = JsonUtility.FromJson<NetMsg>(Encoding.UTF8.GetString(data)); } catch { return; }
            if (m == null || string.IsNullOrEmpty(m.t)) return;
            bool fromHost = from == b.Owner && !IsHost;
            switch (m.t)
            {
                case "in": if (IsHost && racing) { m.from = from.ToString(); Emit(m); } break;      // o id vem de quem mandou, não do texto
                case "suggested":
                    if (!IsHost || racing || !SuggestionOk(m.hash, m.json)) break;
                    if (!suggFrom.TryGetValue(from, out var times)) suggFrom[from] = times = new List<float>();
                    if (!Rate(times)) break;
                    m.from = from.ToString(); m.name = Clean(NameOf(from), 20); m.color = ColorOf(from);   // quem mandou vem da Steam, não do texto
                    m.track = Clean(m.track, 60); m.faixas = TrackMath.Faixas(m.faixas);
                    Emit(m); break;
                case "sync":                                                                  // visitante pede o campeonato (um por segundo)
                    if (!IsHost || !racing) break;
                    if (syncAt.TryGetValue(from, out float last) && Time.realtimeSinceStartup - last < 1f) break;
                    syncAt[from] = Time.realtimeSinceStartup;
                    Emit(new NetMsg { t = "sync", from = from.ToString() }); break;
                case "started": if (fromHost && !racing) { racing = true; raceHost = from; lobbyFlipSince = -1f; Emit(m); } break;
                case "lobby": if (fromHost && racing) { racing = false; lobbyFlipSince = -1f; Emit(m); EmitRoom(); } break;
                case "snap": case "ev": if (fromHost && racing) Emit(m); break;
            }
        }

        // ---------- Lobby → mensagens ----------

        void OnMembers()
        {
            if (!joined) return;
            var cur = new HashSet<ulong>();
            foreach (var m in b.Members())
            {
                cur.Add(m.id);
                if (!known.Contains(m.id)) { names[m.id] = m.name; Say($"{m.name} entrou na sala."); }
                else names[m.id] = m.name;
            }
            foreach (var id in known) if (!cur.Contains(id)) Say($"{NameOf(id)} saiu da sala.");
            known = cur;
            AdoptRaceHost();
            EmitRoom();
        }

        // O host da corrida saiu: o novo dono do lobby (a Steam escolhe) assume, e a aba recomeça a bateria da vez.
        void AdoptRaceHost()
        {
            if (!racing || known.Contains(raceHost) || b.Owner == 0 || !known.Contains(b.Owner)) return;
            raceHost = b.Owner;
            Say($"{NameOf(raceHost)} agora é o host: a bateria da vez recomeça.");
        }

        void OnChat(ulong from, string text)
        {
            if (!joined) return;
            Emit(new NetMsg { t = "chat", from = from.ToString(), name = Clean(NameOf(from), 20), color = ColorOf(from), text = Clean(text, MaxChat), time = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
        }

        void Say(string text) => Emit(new NetMsg { t = "chat", sys = true, text = text, time = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
        void Err(string code, string text) => Emit(new NetMsg { t = "error", code = code, text = text });
        void Emit(NetMsg m) => Message?.Invoke(m);
        void EmitRoom() { if (joined) Emit(new NetMsg { t = "room", room = BuildRoom() }); }

        string NameOf(ulong id)
        {
            foreach (var m in b.Members()) if (m.id == id) return m.name;
            return names.TryGetValue(id, out var n) ? n : "Jogador";
        }

        string ColorOf(ulong id) { var c = b.MemberData(id, "color"); return string.IsNullOrEmpty(c) ? "#8A948C" : c; }

        string EpochNow() { var e = b.Data("epoch"); return string.IsNullOrEmpty(e) ? "1" : e; }
        bool IsReady(ulong id) { var r = b.MemberData(id, "ready"); return id == b.Owner || (!string.IsNullOrEmpty(r) && r != "0" && r == EpochNow()); }
        void BumpEpoch() { int.TryParse(EpochNow(), out int e); b.SetData("epoch", (e + 1).ToString()); }

        void SaveSummaries()
        {
            var sum = new StageSummary();
            foreach (var s in stageData) sum.s.Add(new NetStage { name = s.name, laps = s.laps, secs = s.secs, tempo = s.tempo, faixas = s.faixas, hash = s.hash });
            b.SetData("stages", stageData.Count.ToString());
            b.SetData("stl", JsonUtility.ToJson(sum));
        }

        NetRoom BuildRoom()
        {
            var r = new NetRoom { code = b.Data("code"), name = b.Data("name"), pub = b.Data("pub") == "1", host = b.Owner.ToString(), max = SteamLobbyClient.MaxMembers, state = b.Data("state") };
            foreach (var m in b.Members())
                r.players.Add(new NetPlayer { id = m.id.ToString(), name = Clean(m.name, 20), color = ColorOf(m.id), ready = IsReady(m.id), uid = "steam:" + m.id });
            try { var sum = JsonUtility.FromJson<StageSummary>(b.Data("stl")); if (sum != null) r.stages = sum.s; } catch { }
            return r;
        }

        // Tira controle e espaços repetidos (o mesmo que o servidor Node).
        static string Clean(string s, int max)
        {
            var sb = new StringBuilder();
            bool space = false;
            foreach (char c in s ?? "")
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            return sb.Length > max ? sb.ToString(0, max) : sb.ToString();
        }
    }
}
