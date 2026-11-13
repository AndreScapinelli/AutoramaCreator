using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // A aba Online: criar ou entrar numa sala (pública ou privada), lobby com cor, chat e escolha das pistas pelo host.
    // Só o lobby por enquanto; a largada online vem depois. O servidor é servidor/servidor.js (seção "Campeonato online" do guia).
    // Visitante só escolhe a cor e fala no chat. O host também escolhe as pistas e se a sala é pública. O servidor confere tudo isso.
    public partial class RaceController
    {
        const string PrefServer = "autorama.online.servidor", PrefName = "autorama.online.nome", PrefSteam = "autorama.online.steam";
        const int MaxOnlineStages = 12, MaxTrackJson = 100 * 1024, ChatKeep = 100;
        static string DefaultServer => OnlineClient.DefaultServer;

        IOnlineTransport net;                           // o servidor Node (wsNet) ou a Steam (steamNet): a aba não sabe a diferença
        OnlineClient wsNet;
        SteamTransport steamNet;
        NetRoom room;                                   // null fora da sala
        string myId;
        bool wasHost;
        Action pendingNet;                              // o que fazer assim que a conexão abrir
        // A conexão caiu no meio do campeonato (servidor Node): tenta voltar à mesma sala como o mesmo piloto (uid).
        string rejoinCode, rejoinName;
        int rejoinTries;
        float rejoinAt = -1f;
        const int RejoinMax = 5;
        List<NetListed> listed = new List<NetListed>();
        readonly List<Stage> onlineStages = new List<Stage>();    // do host: com os dados completos (o servidor guarda e devolve só o resumo)

        // Pistas que os visitantes sugeriram ao host (seção 30 do guia). Só o host as guarda, e só no lobby.
        class Suggestion { public string from, name, track, hash; public TrackSave save; }
        readonly List<Suggestion> suggestions = new List<Suggestion>();
        readonly HashSet<string> sentSuggestions = new HashSet<string>();    // visitante: hashes que já mandei nesta sala
        const int MaxSuggestions = 12, MaxSuggestionsPerPlayer = 3;
        VisualElement oSuggestBox, oSuggest, oSendBox, oSend;

        VisualElement oEntry, oLobby, oRooms, oPlayers, oColors, oStages, oPick, oHostPick, oRun, oRunChatSec, oFormat;
        Button oByLaps, oByTime, oKind2, oKind4, oKind6, oKind8;
        Label oFormatText;
        int onlineFaixas = 2;          // tipo de pista que o host está escolhendo (vale enquanto não há etapas)
        Label oMsg, oRoomTitle, oRoomCode, oStageHint, oLobbyMsg, oRulesHint, oStartHint;
        TextField oServer, oName, oRoomName, oCode, oChatInput, oRunChatInput;
        Toggle oPublic, oVisibility, oSteam;
        VisualElement oInviteBox, oFriends;
        ScrollView friendsScroll;                                    // a lista de amigos, com rolagem própria
        readonly HashSet<ulong> invited = new HashSet<ulong>();      // amigos já convidados nesta sala
        ulong launchLobby;
        ScrollView oChat, oRunChat;
        Button oStartBtn, oReadyBtn, oRunQuit, oGuestBtn;
        VisualElement oColors2;
        Label oColors2Title;
        readonly List<NetChat> chatLog = new List<NetChat>();

        // Tira "<" de nomes e mensagens dos outros: o rótulo aceita rich text e não pode deixar ninguém mudar o visual.
        static string Safe(string s) => (s ?? "").Replace("<", "<\u200B");
        bool IsHost => room != null && room.host == myId;

        // ---------- Montagem ----------

        void BuildOnlineUI()
        {
            wsNet = new OnlineClient();
            SetTransport(wsNet);
            launchLobby = LaunchLobby();
            var box = panel.OnlineBox;

            // Antes de entrar numa sala
            oEntry = El(null, box);
            var head = Section(oEntry, "CAMPEONATO ONLINE");
            Text("Um jogador cria a sala e escolhe as pistas. Os outros entram pelo código ou pela lista de salas públicas.", "hint", head);
            oSteam = new Toggle("Jogar pela Steam (sem servidor)") { value = PlayerPrefs.GetInt(PrefSteam, 0) == 1 };
            oSteam.AddToClassList("setting");
            oSteam.tooltip = "Usa o lobby e a rede da Steam: precisa da Steam aberta e logada. O nome é o da sua conta.";
            oSteam.RegisterValueChangedCallback(e => SteamToggled(e.newValue));
            head.Add(oSteam);
            oServer = Field("Servidor (ex.: ws://localhost:8080)", 120, head);
            oServer.SetValueWithoutNotify(PlayerPrefs.GetString(PrefServer, DefaultServer));
            oServer.RegisterValueChangedCallback(e => { PlayerPrefs.SetString(PrefServer, e.newValue); });
            oName = Field("Seu nome", 20, head);
            oName.style.marginTop = 6;
            oName.SetValueWithoutNotify(PlayerPrefs.GetString(PrefName, ""));
            oName.RegisterValueChangedCallback(e => { PlayerPrefs.SetString(PrefName, e.newValue); });
            oMsg = Text("", "small bad", head);
            oMsg.style.marginTop = 6;

            var create = Section(oEntry, "CRIAR SALA");
            oRoomName = Field("Nome da sala (opcional)", 30, create);
            oPublic = new Toggle("Sala pública (aparece na lista)") { value = true };
            oPublic.AddToClassList("setting");
            oPublic.style.marginTop = 6;
            create.Add(oPublic);
            Btn("Criar sala", CreateRoom, "btn btn--primary", create);

            var join = Section(oEntry, "ENTRAR COM UM CÓDIGO");
            Text("Salas privadas só entram assim.", "hint", join);
            oCode = Field("Código da sala", 8, join);
            oCode.RegisterValueChangedCallback(e => oCode.SetValueWithoutNotify(e.newValue.ToUpperInvariant()));
            oCode.RegisterCallback<KeyDownEvent>(ev => { if (IsEnter(ev)) JoinByCode(); }, TrickleDown.TrickleDown);
            var jb = Btn("Entrar", JoinByCode, "btn btn--primary", join);
            jb.style.marginTop = 6;

            var pubs = Section(oEntry, "SALAS PÚBLICAS");
            Btn("Atualizar lista", RefreshRooms, "btn", pubs);
            oRooms = El(null, pubs);
            oRooms.style.marginTop = 8;

            // Dentro da sala
            oLobby = El(null, box);
            var sala = Section(oLobby, "SALA");
            oRoomTitle = Text("", "lib-name", sala);
            var codeRow = El("row", sala);
            codeRow.style.alignItems = Align.Center;
            Text("Código", "small", codeRow).style.marginRight = 8;
            oRoomCode = Text("", "clock", codeRow);
            var copy = Btn("Copiar", () => { GUIUtility.systemCopyBuffer = room?.code ?? ""; panel.Toast("Código copiado."); }, "btn", codeRow);
            copy.style.marginLeft = 8;
            oVisibility = new Toggle("Sala pública (aparece na lista)");
            oVisibility.AddToClassList("setting");
            oVisibility.style.marginTop = 6;
            oVisibility.RegisterValueChangedCallback(e => { if (IsHost && room != null && e.newValue != room.pub) net.Send(new NetOut { t = "visibility", pub = e.newValue }); });
            sala.Add(oVisibility);

            // Só com a Steam: convidar amigos para a sala.
            oInviteBox = El(null, sala);
            oInviteBox.style.marginTop = 8;
            var invRow = El("row", oInviteBox);
            Btn("Convidar amigos (Steam)", () => steamNet?.OpenInviteDialog(), "btn btn--primary", invRow).tooltip =
                "Abre a janela de convite da Steam. Ela só aparece no jogo instalado (não no Editor): lá, use a lista abaixo.";
            var refresh = Btn("Atualizar", DrawFriends, "btn", invRow);
            refresh.style.marginLeft = 6;
            oFriends = El(null, oInviteBox);
            oFriends.style.marginTop = 6;

            var pl = Section(oLobby, "PILOTOS");
            oPlayers = El(null, pl);
            Text("SUA COR", "h3", pl);
            oColors = El("swatches", pl);
            // O segundo jogador neste computador (seção 44 do guia): entra com o meu nome e um 2, e joga com o 2º conjunto de teclas ou o 2º controle.
            oGuestBtn = Btn("", () => net.Send(new NetOut { t = "guest", on = room?.Player(NetGuest.Id(myId)) == null }), "btn", pl);
            oGuestBtn.style.marginTop = 8;
            oGuestBtn.tooltip = "Mais um piloto jogando neste computador, com o seu nome e um 2. Ele usa o 2º conjunto de teclas (Opções) ou o 2º controle.";
            oColors2Title = Text("COR DO JOGADOR 2", "h3", pl);
            oColors2 = El("swatches", pl);

            var st = Section(oLobby, "ETAPAS");
            // Formato e tipo de pista à vista de todos: o host escolhe, os visitantes leem.
            oFormatText = Text("", "body", st);
            oFormat = El(null, st);
            Text("FORMATO", "h3", oFormat);
            var fmt = El("segmented", oFormat);
            oByLaps = Btn("Por voltas", () => OnlineFormat(false), "btn", fmt);
            oByTime = Btn("Por tempo", () => OnlineFormat(true), "btn", fmt);
            oByTime.tooltip = "Cada bateria tem uma duração e se divide em uma manche por trilho; vence quem andar mais.";
            Text("TAMANHO DA PISTA", "h3", oFormat);
            var kind = El("segmented", oFormat);
            oKind2 = Btn("2 trilhos", () => OnlineKind(2), "btn", kind);
            oKind4 = Btn("4 trilhos", () => OnlineKind(4), "btn", kind);
            oKind6 = Btn("6 trilhos", () => OnlineKind(6), "btn", kind);
            oKind6.tooltip = "Pista de 6 trilhos. Com menos de 6 jogadores, sobra trilho vazio em cada bateria.";
            oKind8 = Btn("8 trilhos", () => OnlineKind(8), "btn", kind);
            oKind8.tooltip = "Pista de 8 trilhos. Com menos de 8 jogadores, sobra trilho vazio em cada bateria.";
            oKind4.tooltip = "Pista de 4 trilhos. Com menos de 4 jogadores, sobra trilho vazio em cada bateria.";
            oStageHint = Text("", "hint", st);
            oStages = El(null, Caixa(st));
            oHostPick = El(null, st);
            Text("PISTAS DISPONÍVEIS", "h3", oHostPick);
            oPick = El(null, Caixa(oHostPick));
            // Host: o que os visitantes sugeriram. Visitante: as próprias pistas, para sugerir ao host.
            oSuggestBox = El(null, st);
            Text("SUGESTÕES DOS PILOTOS", "h3", oSuggestBox);
            Text("Pistas que os outros pilotos mandaram. Você decide: inclui como etapa, salva na sua biblioteca ou ignora.", "hint", oSuggestBox);
            oSuggest = El(null, Caixa(oSuggestBox));
            oSendBox = El(null, st);
            Text("SUGERIR UMA PISTA AO HOST", "h3", oSendBox);
            Text("Escolha uma das suas pistas (fechada) para o host incluir. Até 3 por minuto.", "hint", oSendBox);
            oSend = El(null, oSendBox);

            var chat = Section(oLobby, "CHAT");
            BuildChatBox(chat, out oChat, out oChatInput);

            var acts = Section(oLobby, "CAMPEONATO");
            oRulesHint = Text("", "small", acts);
            oRulesHint.style.marginBottom = 6;
            oLobbyMsg = Text("", "small bad", acts);
            oReadyBtn = Btn("Estou pronto", ToggleReady, "btn btn--primary", acts);
            oStartBtn = Btn("Começar campeonato", OnlineStartChamp, "btn btn--primary", acts);
            oStartHint = Text("", "small", acts);
            oStartHint.style.marginTop = 4;
            var leave = Btn("Sair da sala", LeaveRoom, "btn btn--danger", acts);
            leave.style.marginTop = 8;

            // Campeonato em andamento: a tela do campeonato offline (cRun) com o chat no meio (BeginOnlineRun).
            oRun = El(null, box);
            oRunChatSec = El("section");
            Text("CHAT", "h2", oRunChatSec);
            BuildChatBox(oRunChatSec, out oRunChat, out oRunChatInput);
            var runActs = El("section", oRun);
            oRunQuit = Btn("", null, "btn btn--danger", runActs);
            oRunQuit.clicked += OnlineQuit;

            DrawOnline();
        }

        void BuildChatBox(VisualElement parent, out ScrollView log, out TextField input)
        {
            var l = log = new ScrollView(ScrollViewMode.Vertical);
            l.AddToClassList("chat-log");
            parent.Add(l);
            var row = El("row", parent);
            row.style.flexWrap = Wrap.NoWrap;
            row.style.marginTop = 6;
            var f = input = Field("Escreva uma mensagem", 200, row);
            f.RegisterCallback<KeyDownEvent>(ev => { if (IsEnter(ev)) { SendChat(f); ev.StopPropagation(); } }, TrickleDown.TrickleDown);
            Btn("Enviar", () => SendChat(f), "btn", row);
        }

        static bool IsEnter(KeyDownEvent ev) => ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter;

        // ---------- Quadro a quadro e ciclo de vida ----------

        void OnlineTick()
        {
            net?.Pump();
            if (rejoinAt >= 0f && Time.realtimeSinceStartup >= rejoinAt) { rejoinAt = -1f; JoinRoom(rejoinName, rejoinCode); }
            if (launchLobby != 0) { ulong id = launchLobby; launchLobby = 0; OnSteamJoinRequested(id); }     // o jogo foi aberto pelo convite de um amigo
        }

        // Um amigo convidou (ou usou "Entrar no jogo"): liga a Steam, abre a aba e entra no lobby dele.
        void OnSteamJoinRequested(ulong lobbyId)
        {
            if (room != null) { panel.Toast("Você já está numa sala. Saia dela para entrar na do amigo."); return; }
            oSteam.SetValueWithoutNotify(true);
            SteamToggled(true);
            if (!oSteam.value) return;                                   // a Steam não iniciou: SteamToggled já avisou
            panel.SetMode(BuilderPanel.Mode.Online);
            WhenConnected(() => steamNet.JoinById(lobbyId));
        }

        // A Steam abre o jogo com "+connect_lobby <id>" quando o convite é aceito com o jogo fechado.
        static ulong LaunchLobby()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id)) return id;
            return 0;
        }

        // ---------- Convites (Steam) ----------

        bool SteamRoom => room != null && oSteam.value && steamNet != null && net == steamNet;

        void DrawFriends()
        {
            oFriends.Clear();
            if (!SteamRoom) return;
            var list = steamNet.InvitableFriends();
            if (list.Count == 0) { Text("Nenhum amigo online agora. Use o botão acima para convidar quem está offline ou em outro jogo.", "small", oFriends); return; }
            Text($"AMIGOS ONLINE · {list.Count}", "h3", oFriends);
            // A lista pode ser grande: fica numa caixa com altura máxima e rolagem própria (o que passa é cortado).
            // Redesenhar (depois de convidar) mantém a posição da rolagem.
            float keep = friendsScroll != null ? friendsScroll.scrollOffset.y : 0f;
            friendsScroll = new ScrollView(ScrollViewMode.Vertical);
            friendsScroll.AddToClassList("friends-scroll");
            oFriends.Add(friendsScroll);
            foreach (var f in list)
            {
                var friend = f;
                var row = El("lib-item", friendsScroll.contentContainer);
                Text(Safe(f.name), "lib-name", El("lib-texts", row));
                bool sent = invited.Contains(f.id);
                var b = Btn(sent ? "Convidado ✓" : "Convidar", () =>
                {
                    if (steamNet.Invite(friend.id)) { invited.Add(friend.id); panel.Toast($"Convite enviado a {Safe(friend.name)}."); }
                    else panel.Toast("Não deu para convidar. Confira se a Steam está aberta.");
                    DrawFriends();
                }, "btn", row);
                b.SetEnabled(!sent);
            }
            if (keep > 0f) friendsScroll.schedule.Execute(() => friendsScroll.scrollOffset = new Vector2(0f, keep));
        }

        void OnlineDestroy()
        {
            wsNet?.Close();
            steamNet?.Shutdown();
            net = null;
        }

        void OnlineShown()
        {
            if (oSteam.value && (steamNet == null || steamNet.LocalName == "")) SteamToggled(true);     // a escolha da última vez
            DrawOnline(); OnlineStatus();
        }

        void SetTransport(IOnlineTransport t)
        {
            if (net == t) return;
            if (net != null) { net.Opened -= OnNetOpen; net.Closed -= OnNetClosed; net.Message -= OnNetMessage; }
            net = t;
            net.Opened += OnNetOpen; net.Closed += OnNetClosed; net.Message += OnNetMessage;
        }

        // Steam ligada: o nome é o da conta, não há endereço de servidor. Desligada: volta ao servidor Node.
        void SteamToggled(bool on)
        {
            if (room == null && net.state != OnlineClient.State.Closed) { net.Close(); pendingNet = null; }     // a próxima ação conecta pela rede escolhida
            if (on)
            {
                if (steamNet == null) { steamNet = new SteamTransport(); steamNet.JoinRequested += OnSteamJoinRequested; }
                if (!steamNet.Prepare(out string err))
                {
                    oSteam.SetValueWithoutNotify(false);
                    SetOnlineMsg(err);
                    on = false;
                }
            }
            PlayerPrefs.SetInt(PrefSteam, on ? 1 : 0);
            oServer.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            oName.SetEnabled(!on);
            oName.SetValueWithoutNotify(on ? steamNet.LocalName : PlayerPrefs.GetString(PrefName, ""));
        }

        void OnlineStatus()
        {
            if (!panel.OnlineMode || OnlineRun) return;
            if (room != null) panel.SetStatus($"Sala {room.code} · {room.players.Count}/{room.max} pilotos", "status--ok");
            else if (net.state == OnlineClient.State.Connecting) panel.SetStatus(oSteam.value ? "Conectando à Steam…" : "Conectando ao servidor…", "");
            else panel.SetStatus("Online: crie uma sala ou entre numa", "");
        }

        // ---------- Ações do jogador ----------

        void SetOnlineMsg(string text)
        {
            if (oMsg != null) oMsg.text = text ?? "";
            if (oLobbyMsg != null) oLobbyMsg.text = room != null ? text ?? "" : "";
            // O aviso fixo fica no topo da aba, longe dos botões: o balão chama a atenção onde o jogador está olhando.
            if (!string.IsNullOrEmpty(text) && room == null) panel.Toast(text);
        }

        // O nome é obrigatório para criar e para entrar.
        string RequireName()
        {
            if (oSteam.value && steamNet != null) return steamNet.LocalName;
            string n = (oName.value ?? "").Trim();
            if (n.Length == 0) { SetOnlineMsg("Digite seu nome primeiro."); oName.Focus(); return null; }
            return n;
        }

        // Conecta (se preciso) e só então roda a ação.
        void WhenConnected(Action act)
        {
            if (net.state == OnlineClient.State.Closed) SetTransport(oSteam.value && steamNet != null ? (IOnlineTransport)steamNet : wsNet);
            if (net.state == OnlineClient.State.Open) { act(); return; }
            pendingNet = act;
            if (net.state == OnlineClient.State.Closed) { SetOnlineMsg(""); net.Connect(oServer.value); }
            OnlineStatus();
        }

        void CreateRoom()
        {
            string name = RequireName(); if (name == null) return;
            bool pub = oPublic.value;
            string roomName = (oRoomName.value ?? "").Trim();
            WhenConnected(() => net.Send(new NetOut { t = "create", name = name, roomName = roomName, pub = pub, uid = ChampFiles.PlayerUid() }));
        }

        void JoinByCode()
        {
            string name = RequireName(); if (name == null) return;
            string code = (oCode.value ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) { SetOnlineMsg("Digite o código da sala."); oCode.Focus(); return; }
            JoinRoom(name, code);
        }

        void JoinRoom(string name, string code) => WhenConnected(() => net.Send(new NetOut { t = "join", name = name, code = code, uid = ChampFiles.PlayerUid() }));

        void RefreshRooms() => WhenConnected(() => net.Send(new NetOut { t = "list" }));

        void LeaveRoom()
        {
            rejoinCode = null; rejoinAt = -1f;
            net.Close();                                 // o servidor tira o jogador da sala ao ver a conexão cair
            ResetOnlineState();
            DrawOnline(); OnlineStatus();
        }

        void ResetOnlineState()
        {
            OnlineExitRun();
            room = null; myId = null; wasHost = false; pendingNet = null;
            onlineStages.Clear();
            suggestions.Clear(); sentSuggestions.Clear();
            invited.Clear();
            chatLog.Clear();
            oChat.Clear(); oRunChat.Clear();
        }

        void SendChat(TextField input)
        {
            string text = (input.value ?? "").Trim();
            if (text.Length == 0 || room == null) return;
            net.Send(new NetOut { t = "chat", text = text });
            input.SetValueWithoutNotify("");
        }

        void ToggleReady()
        {
            var me = room?.Player(myId);
            if (me != null && !IsHost) net.Send(new NetOut { t = "ready", ready = !me.ready });
        }

        // Só o host, com todos prontos. As regras são os ajustes da aba Corrida, como no campeonato offline.
        void OnlineStartChamp()
        {
            if (!IsHost || LobbyBlocker() != "") return;
            net.Send(new NetOut { t = "start", name = room.name, vmax = set.vmax, ramp = set.ramp, grip = set.grip, collide = set.collide, turbo = set.turbo, byTime = set.byTime });
        }

        string LobbyBlocker()
        {
            if (room.players.Count < 2) return "Falta gente: são precisos pelo menos 2 pilotos.";
            if (room.stages.Count == 0) return "Escolha pelo menos uma pista.";
            var wait = room.players.FindAll(p => p.id != room.host && !p.ready);
            return wait.Count > 0 ? "Aguardando ficar pronto: " + string.Join(", ", wait.ConvertAll(p => Safe(p.name))) + "." : "";
        }

        // ---------- Rede ----------

        void OnNetOpen()
        {
            var act = pendingNet; pendingNet = null;
            OnlineStatus();
            act?.Invoke();
        }

        void OnNetClosed(string reason)
        {
            bool wasInRoom = room != null, wasConnecting = pendingNet != null;
            if (wasInRoom && OnlineRun && !oSteam.value)
            {
                rejoinCode = room.code; rejoinTries = 0;
                rejoinName = room.Player(myId)?.name is string n && n != "" ? n : PlayerPrefs.GetString(PrefName, "");
            }
            ResetOnlineState();
            if (rejoinCode != null && rejoinTries < RejoinMax)
            {
                rejoinAt = Time.realtimeSinceStartup + (rejoinTries == 0 ? 1f : 3f);
                rejoinTries++;
                panel.Toast($"A conexão caiu. Tentando voltar à sala ({rejoinTries} de {RejoinMax})…");
                DrawOnline(); OnlineStatus();
                return;
            }
            if (rejoinCode != null) { rejoinCode = null; reason = "Não deu para voltar à sala: " + reason; }
            if (wasConnecting && !oSteam.value) reason = reason.TrimEnd('.', ' ') + $". Confira o endereço ({oServer.value}) e se o servidor está rodando (cd servidor && npm start).";
            SetOnlineMsg(reason);
            if (wasInRoom) panel.Toast("Você saiu da sala: " + reason);
            DrawOnline(); OnlineStatus();
            if (!wasInRoom && panel.OnlineMode) panel.SetStatus(oSteam.value ? "Sem conexão com a Steam" : "Sem conexão com o servidor", "status--warn");
        }

        void OnNetMessage(NetMsg m)
        {
            switch (m.t)
            {
                case "joined":
                    myId = m.you; room = m.room; wasHost = IsHost;
                    onlineStages.Clear(); suggestions.Clear(); sentSuggestions.Clear();
                    chatLog.Clear(); oChat.Clear(); oRunChat.Clear();
                    foreach (var c in m.chat) AddChat(c);
                    SetOnlineMsg("");
                    if (rejoinCode != null) { rejoinCode = null; panel.Toast("Você voltou à sala."); }
                    // Campeonato andando (voltei de uma queda, ou entrei para assistir): o host manda o campeonato.
                    if (room.state == "racing" && !IsHost) { syncAskedAt = -10f; AskSync(); }
                    DrawOnline(); OnlineStatus();
                    break;
                case "room":
                    if (room == null) break;
                    string oldHost = room.host;
                    room = m.room;
                    if (OnlineRun && oldHost != room.host) { if (IsHost) OnlineTakeOver(); else OnlineHostChanged(); }
                    else if (IsHost && !wasHost && !OnlineRun) AdoptStages();
                    wasHost = IsHost;
                    DrawOnline(); OnlineStatus();
                    break;
                case "rooms":
                    listed = m.rooms;
                    DrawRooms();
                    break;
                case "chat":
                    AddChat(new NetChat { sys = m.sys, name = m.name, color = m.color, text = m.text, from = m.from, time = m.time });
                    break;
                case "error":
                    if (rejoinCode != null && room == null) { rejoinCode = null; rejoinAt = -1f; }    // a sala acabou: desiste de voltar
                    SetOnlineMsg(m.text);
                    if (room != null) panel.Toast(m.text);
                    break;
                case "started": if (room != null) BeginOnlineRun(m); break;
                case "lobby":
                    bool sendStages = takeOverStages && IsHost;
                    takeOverStages = false;
                    OnlineExitRun();
                    if (sendStages) SendStages();          // virei host no meio: as etapas do campeonato voltam para a sala
                    if (!string.IsNullOrEmpty(m.text)) { panel.Toast(m.text); AddChat(new NetChat { sys = true, text = m.text }); }
                    DrawOnline(); OnlineStatus();
                    break;
                case "in": OnlineInput(m); break;
                case "suggested": OnSuggested(m); break;
                case "snap": if (OnlineRun && !IsHost) OnlineApplySnap(m.b); break;
                case "ev": if (m.k == "sync") OnlineApplySync(m); else OnlineEvent(m); break;
                case "sync": OnlineSyncRequest(m.from); break;
            }
        }

        // O host que assume no meio do caminho só tem o resumo das etapas: reencontra as pistas pela identidade
        // (hash) entre as que tem neste computador e tira as que não achar.
        void AdoptStages()
        {
            onlineStages.Clear();
            var opts = Options();
            int lost = 0;
            foreach (var s in room.stages)
            {
                // A etapa foi adaptada ao tamanho do campeonato: procura a pista daqui que, nesse tamanho, tem a mesma identidade.
                int f = TrackMath.Faixas(s.faixas);
                var o = opts.Find(x => x.closed && Champ.TrackHash(Adapted(x.save, f)) == s.hash);
                if (o.save == null) { lost++; continue; }
                var data = Adapted(o.save, f);
                onlineStages.Add(new Stage { name = s.name, laps = s.laps, secs = s.secs > 0 ? s.secs : 120, data = data, hash = s.hash });
            }
            if (lost > 0) panel.Toast($"{lost} {(lost == 1 ? "pista não está" : "pistas não estão")} neste computador e saiu da lista.");
            SendStages();                                // o novo host passa a ser quem guarda o JSON das pistas
        }

        // ---------- Etapas (host) ----------

        void AddOnlineStage(Option o)
        {
            if (!IsHost) return;
            if (onlineStages.Count >= MaxOnlineStages) { panel.Toast($"No máximo {MaxOnlineStages} etapas."); return; }
            // Qualquer pista fechada serve: ela entra adaptada ao tamanho do campeonato.
            if (!o.closed) return;
            int want = onlineStages.Count > 0 ? onlineStages[0].data.Faixas : onlineFaixas;
            var data = Adapted(o.save, want);
            if (data.ToJson(false).Length > MaxTrackJson) { panel.Toast("Essa pista é grande demais para o servidor."); return; }
            onlineStages.Add(new Stage { name = data.nome, laps = set.laps, secs = Mathf.Clamp(set.secs, 60, 600), data = data, hash = Champ.TrackHash(data) });
            SendStages(); DrawLobby();
        }

        // Host: formato do campeonato. É o mesmo ajuste da aba Corrida (set.byTime), então largar usa o que está aqui.
        void OnlineFormat(bool byTime)
        {
            if (!IsHost || set.byTime == byTime) return;
            set.byTime = byTime; set.Save();
            SendStages(); DrawLobby();
        }

        // Host: tipo de pista. Com etapas do outro tipo, elas saem (segundo clique).
        void OnlineKind(int faixas)
        {
            if (!IsHost) return;
            int now = onlineStages.Count > 0 ? onlineStages[0].data.Faixas : onlineFaixas;
            if (now == faixas) return;
            // O tamanho é livre até largar: as etapas já escolhidas são adaptadas (a identidade muda com o tamanho).
            onlineFaixas = TrackMath.Faixas(faixas);
            foreach (var s in onlineStages) { s.data.faixas = onlineFaixas; s.hash = Champ.TrackHash(s.data); }
            SendStages(); DrawLobby();
        }

        // ---------- Pistas sugeridas (seção 30) ----------

        // Host: chegou uma sugestão. Nada que veio da rede é confiado: a pista é lida e conferida aqui (fechada, com a
        // identidade que o remetente disse) e os limites são refeitos.
        void OnSuggested(NetMsg m)
        {
            if (!IsHost || room == null || OnlineRun || string.IsNullOrEmpty(m.json)) return;
            TrackSave save;
            try
            {
                save = TrackSave.FromJson(m.json);
                var segs = TrackLayout.Build(save.Origin, save.Pieces(), save.Tail);
                if (!TrackLayout.Analyze(save.Origin, segs, save.Tail, save.Faixas).closed) return;      // só circuito fechado
            }
            catch { return; }
            string hash = Champ.TrackHash(save);
            if (hash != m.hash) return;                                                              // a identidade tem que bater
            if (suggestions.Count >= MaxSuggestions) return;
            int mine = 0;
            foreach (var s in suggestions) { if (s.hash == hash && s.from == m.from) return; if (s.from == m.from) mine++; }
            if (mine >= MaxSuggestionsPerPlayer) return;
            if (onlineStages.Exists(s => s.hash == hash)) return;                                    // já é uma etapa
            string track = string.IsNullOrWhiteSpace(m.track) ? (string.IsNullOrWhiteSpace(save.nome) ? "Pista" : save.nome) : m.track;
            suggestions.Add(new Suggestion { from = m.from, name = Safe(m.name), track = Safe(track), hash = hash, save = save });
            panel.Toast($"{Safe(m.name)} sugeriu a pista “{Safe(track)}”.");
            DrawOnline();
        }

        // Host: inclui a pista sugerida como etapa (vale a mesma regra de tipo de pista de qualquer etapa).
        void AcceptSuggestion(Suggestion s)
        {
            var data = TrackSave.FromJson(s.save.ToJson(false));
            data.nome = s.track;
            AddOnlineStage(new Option { key = "sug:" + s.hash, name = s.track, save = data, closed = true });
            if (onlineStages.Exists(x => x.hash == s.hash)) suggestions.Remove(s);
            DrawOnline();
        }

        // Host: guarda a pista sugerida na própria biblioteca (sem sobrescrever uma de mesmo nome).
        void SaveSuggestion(Suggestion s)
        {
            var data = TrackSave.FromJson(s.save.ToJson(false));
            data.nome = s.track;
            if (TrackLibrary.List().Exists(e => e.save.nome == data.nome)) data.nome = $"{s.track} (de {s.name})";
            TrackLibrary.Save(data);
            panel.Toast($"“{data.nome}” salva na sua biblioteca.");
        }

        // Visitante: manda uma das minhas pistas ao host.
        void SendSuggestion(Option o)
        {
            if (IsHost || room == null || !o.closed) return;
            string json = o.save.ToJson(false);
            if (json.Length > MaxTrackJson) { panel.Toast("Essa pista é grande demais para sugerir."); return; }
            string hash = Champ.TrackHash(o.save);
            if (sentSuggestions.Contains(hash)) return;
            net.Send(new NetOut { t = "suggest", track = string.IsNullOrWhiteSpace(o.save.nome) ? o.name : o.save.nome, hash = hash, faixas = o.save.Faixas, json = json });
            sentSuggestions.Add(hash);
            panel.Toast("Pista enviada ao host.");
            DrawOnline();
        }

        void DrawSuggestions(bool host)
        {
            oSuggestBox.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oSendBox.style.display = !host && room != null ? DisplayStyle.Flex : DisplayStyle.None;
            oSuggest.Clear(); oSend.Clear();
            if (host)
            {
                if (suggestions.Count == 0) Text("Nenhuma sugestão ainda.", "small", oSuggest);
                foreach (var sg in suggestions.ToArray())
                {
                    var s = sg;
                    var item = El("lib-item", oSuggest);
                    item.Add(new TrackThumb(s.save));
                    var texts = El("lib-texts", item);
                    Text(s.track, "lib-name", texts);
                    Text($"de {s.name} · {TrackLine(s.save)}", "small", texts);
                    var row = El("row", item);
                    Btn("Incluir", () => AcceptSuggestion(s), "btn btn--primary", row);
                    Btn("Salvar", () => SaveSuggestion(s), "btn", row).tooltip = "Guarda na sua biblioteca de pistas";
                    Btn("Ignorar", () => { suggestions.Remove(s); DrawOnline(); }, "btn", row);
                }
                return;
            }
            if (room == null) return;
            // Se o host já escolheu o tipo (2 ou 4 carros), só as pistas desse tipo fazem sentido.
            int want = room.stages.Count > 0 ? TrackMath.Faixas(room.stages[0].faixas) : 0;
            int shown = 0;
            foreach (var o in Options())
            {
                if (!o.closed) continue;                         // qualquer tamanho serve: o host adapta
                var opt = o;
                bool sent = sentSuggestions.Contains(Champ.TrackHash(o.save));
                var item = new Button(() => SendSuggestion(opt));
                item.AddToClassList("lib-item"); item.AddToClassList("pick");
                item.SetEnabled(!sent);
                item.Add(new TrackThumb(o.save));
                var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                Text(string.IsNullOrWhiteSpace(o.save.nome) ? o.name : o.save.nome, "lib-name", texts).pickingMode = PickingMode.Ignore;
                Text(TrackLine(o.save), "small", texts).pickingMode = PickingMode.Ignore;
                Text(sent ? "enviada ✓" : "enviar", "pick-plus", item).pickingMode = PickingMode.Ignore;
                oSend.Add(item);
                shown++;
            }
            if (shown == 0) Text(want != 0 ? $"Você não tem pistas fechadas de {want} carros." : "Você não tem pistas fechadas para sugerir.", "small", oSend);
        }

        void SendStages()
        {
            var o = new NetOutStages();
            foreach (var s in onlineStages) o.stages.Add(new NetStage { name = s.name, laps = s.laps, secs = s.secs, tempo = set.byTime, faixas = s.data.Faixas, hash = s.hash, json = s.data.ToJson(false) });
            net.Send(o);
        }

        // ---------- Desenho ----------

        void DrawOnline()
        {
            if (oEntry == null) return;
            oEntry.style.display = room == null ? DisplayStyle.Flex : DisplayStyle.None;
            oLobby.style.display = room != null && !OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
            oRun.style.display = OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
            if (room == null) DrawRooms();
            else if (OnlineRun) champDirty = true;               // quem entrou ou saiu muda os nomes na tela da corrida
            else DrawLobby();
        }

        void DrawRooms()
        {
            oRooms.Clear();
            if (listed.Count == 0) { Text("Nenhuma sala pública agora. Toque em Atualizar, ou crie a sua.", "small", oRooms); return; }
            foreach (var r in listed)
            {
                var entry = r;
                var item = new Button(() => { string n = RequireName(); if (n != null) JoinRoom(n, entry.code); });
                item.AddToClassList("lib-item"); item.AddToClassList("pick");
                item.SetEnabled(r.players < r.max);
                var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                Text(Safe(r.name), "lib-name", texts).pickingMode = PickingMode.Ignore;
                Text($"{Safe(r.host)} · {r.players}/{r.max} pilotos · {r.stages} {(r.stages == 1 ? "etapa" : "etapas")} · {r.code}", "small", texts).pickingMode = PickingMode.Ignore;
                Text("▸", "pick-plus", item).pickingMode = PickingMode.Ignore;
                oRooms.Add(item);
            }
        }

        void DrawSwatches(VisualElement box, string id, bool g)
        {
            box.Clear();
            for (int i = 0; i < Champ.Colors.Length; i++)
            {
                string hex = Champ.Colors[i];
                var owner = room.players.Find(p => string.Equals(p.color, hex, StringComparison.OrdinalIgnoreCase));
                bool mine = owner != null && owner.id == id, taken = owner != null && !mine;
                var sw = Btn("", () => net.Send(new NetOut { t = "color", color = hex, g = g }), "swatch", box);
                ColorUtility.TryParseHtmlString(hex, out var col);
                sw.style.backgroundColor = col;
                sw.tooltip = Champ.ColorNames[i] + (taken ? $" (de {Safe(owner.name)})" : "");
                sw.EnableInClassList("swatch--on", mine);
                sw.EnableInClassList("swatch--used", taken);
                sw.SetEnabled(!taken);
            }
        }

        void DrawLobby()
        {
            if (room == null) return;
            bool host = IsHost;
            oRoomTitle.text = Safe(room.name);
            oRoomCode.text = room.code;
            oVisibility.SetEnabled(host);
            oVisibility.SetValueWithoutNotify(room.pub);
            oVisibility.label = host ? "Sala pública (aparece na lista)" : room.pub ? "Sala pública" : "Sala privada (só entra com o código)";
            bool steamRoom = SteamRoom && room.state == "lobby";
            oInviteBox.style.display = steamRoom ? DisplayStyle.Flex : DisplayStyle.None;
            if (steamRoom) DrawFriends();

            // Pilotos
            oPlayers.Clear();
            foreach (var p in room.players)
            {
                var row = El("lib-item", oPlayers);
                var dot = El("pdot", row);
                dot.style.marginRight = 8;
                ColorUtility.TryParseHtmlString(p.color, out var col);
                dot.style.backgroundColor = col;
                var texts = El("lib-texts", row);
                Text(Safe(p.name), "lib-name", texts);
                string tag = (p.id == room.host ? "host" : "") + (p.id == myId ? (p.id == room.host ? " · você" : "você") : p.id == NetGuest.Id(myId) ? "jogador 2 deste computador" : "");
                if (tag != "") Text(tag, "small", texts);
                bool ok = p.ready || p.id == room.host;
                Text(ok ? "✓ pronto" : "aguardando", ok ? "small ok" : "small", row);
            }

            // Cores (a minha e, se tiver, a do segundo jogador)
            var me = room.Player(myId);
            DrawSwatches(oColors, myId, false);
            bool guest = room.Player(NetGuest.Id(myId)) != null, lobbyNow = room.state == "lobby";
            oGuestBtn.text = guest ? "Tirar o jogador 2" : "+ Jogador 2 neste computador";
            oGuestBtn.style.display = lobbyNow ? DisplayStyle.Flex : DisplayStyle.None;
            oGuestBtn.SetEnabled(guest || room.players.Count < room.max);
            oColors2Title.style.display = oColors2.style.display = guest ? DisplayStyle.Flex : DisplayStyle.None;
            if (guest) DrawSwatches(oColors2, NetGuest.Id(myId), true);

            // Formato e tipo: o host vê os botões; todos leem a linha (os visitantes pelo que o host mandou nas etapas).
            if (host && onlineStages.Count > 0) onlineFaixas = onlineStages[0].data.Faixas;
            bool byTimeNow = host ? set.byTime : room.stages.Count > 0 && room.stages[0].tempo;
            int faixasNow = host ? onlineFaixas : room.stages.Count > 0 ? TrackMath.Faixas(room.stages[0].faixas) : 2;
            oFormat.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oByLaps.EnableInClassList("btn--on", !byTimeNow); oByTime.EnableInClassList("btn--on", byTimeNow);
            oKind2.EnableInClassList("btn--on", faixasNow == 2); oKind4.EnableInClassList("btn--on", faixasNow == 4); oKind6.EnableInClassList("btn--on", faixasNow == 6); oKind8.EnableInClassList("btn--on", faixasNow == 8);
            int n = room.players.Count, gaps = faixasNow - Mathf.Min(n, faixasNow);
            oFormatText.text = (host || room.stages.Count > 0)
                ? $"<b>{(byTimeNow ? "Por tempo" : "Por voltas")}</b> · <b>pistas de {faixasNow} trilhos</b>" + (faixasNow >= 4 && gaps > 0 ? $" · com {n} {(n == 1 ? "jogador" : "jogadores")}, {gaps} {(gaps == 1 ? "trilho fica vazio" : "trilhos ficam vazios")} em cada bateria" : "")
                : "Formato e tipo de pista: o host ainda não escolheu.";

            // Etapas
            oStageHint.text = host
                ? $"Escolha as pistas e {(set.byTime ? "a duração" : "as voltas")} de cada etapa. As outras regras (velocidade, aderência, colisão) são as da aba Corrida. Os visitantes veem a lista, mas só você mexe nela."
                : "O host escolhe as pistas. Você escolhe a cor do seu carrinho e conversa no chat.";
            oStages.Clear();
            var list = room.stages;
            if (list.Count == 0) Text(host ? "Nenhuma etapa ainda. Clique numa pista abaixo." : "O host ainda não escolheu as pistas.", "small", oStages);
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var s = list[i];
                var item = El("lib-item", oStages);
                var local = host && i < onlineStages.Count && onlineStages[i].hash == s.hash ? onlineStages[i] : null;
                if (local != null) item.Add(new TrackThumb(local.data));
                var texts = El("lib-texts", item);
                Text($"{i + 1}. {Safe(s.name)}", "lib-name", texts);
                Text(local != null ? TrackLine(local.data) : s.tempo ? Dur(s.secs > 0 ? s.secs : 120) : $"{s.laps} {(s.laps == 1 ? "volta" : "voltas")}", "small", texts);
                if (!host || local == null) continue;
                var ctl = El("stage-ctl", item);
                var step = El("stepper", ctl);
                // O formato (voltas ou tempo) é o da aba Corrida do host, como as outras regras.
                if (set.byTime)
                {
                    Btn("−", () => { local.secs = Mathf.Max(60, local.secs - 30); SendStages(); }, "btn step-btn", step);
                    Text(Dur(local.secs), "step-val", step);
                    Btn("+", () => { local.secs = Mathf.Min(600, local.secs + 30); SendStages(); }, "btn step-btn", step);
                }
                else
                {
                    Btn("−", () => { local.laps = Mathf.Max(1, local.laps - 1); SendStages(); }, "btn step-btn", step);
                    Text($"{local.laps} {(local.laps == 1 ? "volta" : "voltas")}", "step-val", step);
                    Btn("+", () => { local.laps = Mathf.Min(30, local.laps + 1); SendStages(); }, "btn step-btn", step);
                }
                var mv = El("row", ctl);
                Btn("▲", () => { (onlineStages[idx - 1], onlineStages[idx]) = (onlineStages[idx], onlineStages[idx - 1]); SendStages(); }, "btn step-btn", mv).SetEnabled(i > 0);
                Btn("▼", () => { (onlineStages[idx + 1], onlineStages[idx]) = (onlineStages[idx], onlineStages[idx + 1]); SendStages(); }, "btn step-btn", mv).SetEnabled(i < list.Count - 1);
                Btn("✕", () => { onlineStages.RemoveAt(idx); SendStages(); }, "btn step-btn", mv).tooltip = "Tirar etapa";
            }

            oHostPick.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oPick.Clear();
            if (host)
                foreach (var o in Options())
                {
                    if (!o.closed) continue;                     // pista aberta não corre
                    var opt = o;
                    var item = new Button(() => AddOnlineStage(opt));
                    item.AddToClassList("lib-item"); item.AddToClassList("pick");
                    item.SetEnabled(o.closed);
                    item.Add(new TrackThumb(o.save));
                    var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                    Text(o.name, "lib-name", texts).pickingMode = PickingMode.Ignore;
                    Text((o.sub != null ? o.sub + " · " : "") + TrackLine(o.save), "small", texts).pickingMode = PickingMode.Ignore;
                    Text("+", "pick-plus", item).pickingMode = PickingMode.Ignore;
                    oPick.Add(item);
                }

            DrawSuggestions(host);

            // Pronto e largada
            string blocker = LobbyBlocker();
            oRulesHint.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oRulesHint.text = $"Regras: os ajustes da aba Corrida (velocidade {Num(set.vmax)} cél/s, até 100% em {Num(set.ramp, 2)} s, aderência {Mathf.RoundToInt(set.grip)}, colisão {(set.collide ? "ligada" : "desligada")}, turbo {(set.turbo ? "ligado" : "desligado")}). Ficam fixos até o fim.";
            oReadyBtn.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
            oReadyBtn.text = me != null && me.ready ? "Não estou pronto" : "Estou pronto";
            oReadyBtn.EnableInClassList("btn--primary", !(me != null && me.ready));
            oStartBtn.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oStartBtn.SetEnabled(host && blocker == "");
            oStartHint.text = host ? (blocker != "" ? blocker : $"Todos prontos: {room.players.Count} pilotos · {room.stages.Count} {(room.stages.Count == 1 ? "etapa" : "etapas")} · {room.players.Count * room.stages.Count} baterias.")
                : me != null && me.ready ? "Você está pronto. Aguardando o host largar." : "Confirme quando estiver pronto: o host só larga com todos prontos.";
        }

        // ---------- Chat ----------

        void AddChat(NetChat c)
        {
            chatLog.Add(c);
            if (chatLog.Count > ChatKeep) chatLog.RemoveAt(0);
            foreach (var log in new[] { oChat, oRunChat }) AppendChatLine(log, c);
        }

        static void AppendChatLine(ScrollView log, NetChat c)
        {
            string text = Safe(c.text);
            string line = c.sys ? $"<i><color=#94A197>{text}</color></i>"
                : $"<b><color={(ColorUtility.TryParseHtmlString(c.color, out _) ? c.color : "#E5EBE3")}>{Safe(c.name)}</color></b>: {text}";
            log.Add(Text(line, "chat-line"));
            while (log.contentContainer.childCount > ChatKeep) log.contentContainer.RemoveAt(0);
            log.schedule.Execute(() => log.scrollOffset = new Vector2(0f, log.contentContainer.layout.height)).ExecuteLater(30);
        }
    }
}
