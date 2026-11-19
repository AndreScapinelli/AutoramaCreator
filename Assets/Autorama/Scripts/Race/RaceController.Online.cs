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
        // Visitante: o desenho das etapas por identidade (seção 23.4). Vem da biblioteca daqui ou do pedido "pistas".
        readonly Dictionary<string, TrackSave> stageTracks = new Dictionary<string, TrackSave>();
        string pistasAsked; float pistasAt = -10f;

        // Pistas que os visitantes sugeriram ao host (seção 30 do guia). Só o host as guarda, e só no lobby.
        class Suggestion { public string from, name, track, hash; public TrackSave save; }
        readonly List<Suggestion> suggestions = new List<Suggestion>();
        readonly HashSet<string> sentSuggestions = new HashSet<string>();    // visitante: hashes que já mandei nesta sala
        const int MaxSuggestions = 12, MaxSuggestionsPerPlayer = 3;
        VisualElement oSuggestBox, oSuggest, oSendBox, oSend;

        VisualElement oRunTracks;                       // as pistas do campeonato, para guardar na biblioteca (seção 64)
        VisualElement oEntry, oLobby, oRooms, oPlayers, oColors, oStages, oPick, oHostPick, oRun, oRunChatSec, oFormat;
        Button oByLaps, oByTime, oKind2, oKind4, oKind6, oKind8;
        Label oFormatText;
        int onlineFaixas = 2;          // tipo de pista que o host está escolhendo (vale enquanto não há etapas)
        Label oMsg, oRoomTitle, oRoomCode, oStageHint, oLobbyMsg, oRulesHint, oStartHint;
        TextField oServer, oName, oRoomName, oCode, oChatInput, oRunChatInput;
        Toggle oPublic, oVisibility, oSteam;
        Label oVia;
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
            var head = Section(oEntry, L.T("CAMPEONATO ONLINE"));
            Text(L.T("Um jogador cria a sala e escolhe as pistas. Os outros entram pelo código ou pela lista de salas públicas."), "hint", head);
            oSteam = new Toggle(L.T("Jogar pela Steam")) { value = PlayerPrefs.GetInt(PrefSteam, 1) == 1 };   // a Steam é o padrão; o servidor fica nas Opções
            oSteam.AddToClassList("setting");
            oSteam.tooltip = L.T("Usa o lobby e a rede da Steam: precisa da Steam aberta e logada. O nome é o da sua conta.");
            oSteam.RegisterValueChangedCallback(e => SteamToggled(e.newValue));
            oVia = Text("", "small", head);
            oServer = Field(L.T("Servidor (ex.: ws://localhost:8080)"), 120, head);
            netOpts.Add(oSteam); netOpts.Add(oServer);                   // o toggle e o endereço ficam na tela de Opções (BuildKeysUI)
            oServer.SetValueWithoutNotify(PlayerPrefs.GetString(PrefServer, DefaultServer));
            oServer.RegisterValueChangedCallback(e => { PlayerPrefs.SetString(PrefServer, e.newValue); SteamToggled(oSteam.value, false); });
            oName = Field(L.T("Seu nome"), 20, head);
            oName.style.marginTop = 6;
            oName.SetValueWithoutNotify(PlayerPrefs.GetString(PrefName, ""));
            oName.RegisterValueChangedCallback(e => { PlayerPrefs.SetString(PrefName, e.newValue); });
            oMsg = Text("", "small bad", head);
            oMsg.style.marginTop = 6;

            BuildMatchUI(oEntry);
            var create = Section(oEntry, L.T("CRIAR SALA"));
            oRoomName = Field(L.T("Nome da sala (opcional)"), 30, create);
            oPublic = new Toggle(L.T("Sala pública (aparece na lista)")) { value = true };
            oPublic.AddToClassList("setting");
            oPublic.style.marginTop = 6;
            create.Add(oPublic);
            Btn(L.T("Criar sala"), CreateRoom, "btn btn--primary", create);

            var join = Section(oEntry, L.T("ENTRAR COM UM CÓDIGO"));
            Text(L.T("Salas privadas só entram assim."), "hint", join);
            oCode = Field(L.T("Código da sala"), 8, join);
            oCode.RegisterValueChangedCallback(e => oCode.SetValueWithoutNotify(e.newValue.ToUpperInvariant()));
            oCode.RegisterCallback<KeyDownEvent>(ev => { if (IsEnter(ev)) JoinByCode(); }, TrickleDown.TrickleDown);
            var jb = Btn(L.T("Entrar"), JoinByCode, "btn btn--primary", join);
            jb.style.marginTop = 6;

            var pubs = Section(oEntry, L.T("SALAS PÚBLICAS"));
            Btn(L.T("Atualizar lista"), RefreshRooms, "btn", pubs);
            oRooms = El(null, Caixa(pubs));
            oRooms.style.marginTop = 8;
            mmHide.Add(create); mmHide.Add(join); mmHide.Add(pubs);

            // Dentro da sala
            oLobby = El(null, box);
            var sala = Section(oLobby, L.T("SALA"));
            oRoomTitle = Text("", "lib-name", sala);
            var codeRow = El("row", sala);
            codeRow.style.alignItems = Align.Center;
            Text(L.T("Código"), "small", codeRow).style.marginRight = 8;
            oRoomCode = Text("", "clock", codeRow);
            var copy = Btn(L.T("Copiar"), () => { GUIUtility.systemCopyBuffer = room?.code ?? ""; panel.Toast(L.T("Código copiado.")); }, "btn", codeRow);
            copy.style.marginLeft = 8;
            oVisibility = new Toggle(L.T("Sala pública (aparece na lista)"));
            oVisibility.AddToClassList("setting");
            oVisibility.style.marginTop = 6;
            oVisibility.RegisterValueChangedCallback(e => { if (IsHost && room != null && e.newValue != room.pub) net.Send(new NetOut { t = "visibility", pub = e.newValue }); });
            sala.Add(oVisibility);

            // Só com a Steam: convidar amigos para a sala.
            oInviteBox = El(null, sala);
            oInviteBox.style.marginTop = 8;
            var invRow = El("row", oInviteBox);
            Btn(L.T("Convidar amigos (Steam)"), () => steamNet?.OpenInviteDialog(), "btn btn--primary", invRow).tooltip =
                L.T("Abre a janela de convite da Steam. Ela só aparece no jogo instalado (não no Editor): lá, use a lista abaixo.");
            var refresh = Btn(L.T("Atualizar"), DrawFriends, "btn", invRow);
            refresh.style.marginLeft = 6;
            oFriends = El(null, oInviteBox);
            oFriends.style.marginTop = 6;

            var pl = Section(oLobby, L.T("PILOTOS"));
            oPlayers = El(null, pl);
            Text(L.T("SUA COR"), "h3", pl);
            oColors = El("swatches", pl);
            // O segundo jogador neste computador (seção 44 do guia): entra com o meu nome e um 2, e joga com o 2º conjunto de teclas ou o 2º controle.
            oGuestBtn = Btn("", () => net.Send(new NetOut { t = "guest", on = room?.Player(NetGuest.Id(myId)) == null }), "btn", pl);
            oGuestBtn.style.marginTop = 8;
            oGuestBtn.tooltip = L.T("Mais um piloto jogando neste computador, com o seu nome e um 2. Ele usa o 2º conjunto de teclas (Opções) ou o 2º controle.");
            oColors2Title = Text(L.T("COR DO JOGADOR 2"), "h3", pl);
            oColors2 = El("swatches", pl);

            var st = Section(oLobby, L.T("ETAPAS"));
            // Formato e tipo de pista à vista de todos: o host escolhe, os visitantes leem.
            oFormatText = Text("", "body", st);
            oFormat = El(null, st);
            Text(L.T("FORMATO"), "h3", oFormat);
            var fmt = El("segmented", oFormat);
            oByLaps = Btn(L.T("Por voltas"), () => OnlineFormat(false), "btn", fmt);
            oByTime = Btn(L.T("Por tempo"), () => OnlineFormat(true), "btn", fmt);
            oByTime.tooltip = L.T("Cada bateria tem uma duração e se divide em uma manche por trilho; vence quem andar mais.");
            Text(L.T("TAMANHO DA PISTA"), "h3", oFormat);
            var kind = El("segmented", oFormat);
            oKind2 = Btn(L.T("2 trilhos"), () => OnlineKind(2), "btn", kind);
            oKind4 = Btn(L.T("4 trilhos"), () => OnlineKind(4), "btn", kind);
            oKind6 = Btn(L.T("6 trilhos"), () => OnlineKind(6), "btn", kind);
            oKind6.tooltip = L.T("Pista de 6 trilhos. Com menos de 6 jogadores, sobra trilho vazio em cada bateria.");
            oKind8 = Btn(L.T("8 trilhos"), () => OnlineKind(8), "btn", kind);
            oKind8.tooltip = L.T("Pista de 8 trilhos. Com menos de 8 jogadores, sobra trilho vazio em cada bateria.");
            oKind4.tooltip = L.T("Pista de 4 trilhos. Com menos de 4 jogadores, sobra trilho vazio em cada bateria.");
            oStageHint = Text("", "hint", st);
            oStages = El(null, Caixa(st));
            oHostPick = El(null, st);
            Text(L.T("PISTAS DISPONÍVEIS"), "h3", oHostPick);
            oPickFilter = new TrackFilter(oHostPick, DrawLobby);
            oPick = El(null, Caixa(oHostPick));
            // Host: o que os visitantes sugeriram. Visitante: as próprias pistas, para sugerir ao host.
            oSuggestBox = El(null, st);
            Text(L.T("SUGESTÕES DOS PILOTOS"), "h3", oSuggestBox);
            Text(L.T("Pistas que os outros pilotos mandaram. Você decide: inclui como etapa, salva na sua biblioteca ou ignora."), "hint", oSuggestBox);
            oSuggest = El(null, Caixa(oSuggestBox));
            oSendBox = El(null, st);
            Text(L.T("SUGERIR UMA PISTA AO HOST"), "h3", oSendBox);
            Text(L.T("Escolha uma das suas pistas (fechada) para o host incluir. Até 3 por minuto."), "hint", oSendBox);
            oSendFilter = new TrackFilter(oSendBox, DrawLobby);
            oSend = El(null, Caixa(oSendBox));

            var chat = Section(oLobby, L.T("CHAT"));
            BuildChatBox(chat, out oChat, out oChatInput);

            var acts = Section(oLobby, L.T("CAMPEONATO"));
            oRulesHint = Text("", "small", acts);
            oRulesHint.style.marginBottom = 6;
            oLobbyMsg = Text("", "small bad", acts);
            oReadyBtn = Btn(L.T("Estou pronto"), ToggleReady, "btn btn--primary", acts);
            oStartBtn = Btn(L.T("Começar campeonato"), OnlineStartChamp, "btn btn--primary", acts);
            oStartHint = Text("", "small", acts);
            oStartHint.style.marginTop = 4;
            var leave = Btn(L.T("Sair da sala"), LeaveRoom, "btn btn--danger", acts);
            leave.style.marginTop = 8;

            // Campeonato em andamento: a tela do campeonato offline (cRun) com o chat no meio (BeginOnlineRun).
            oRun = El(null, box);
            oRunChatSec = El("section");
            Text(L.T("CHAT"), "h2", oRunChatSec);
            BuildChatBox(oRunChatSec, out oRunChat, out oRunChatInput);
            oRunTracks = El("section", oRun);
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
            var f = input = Field(L.T("Escreva uma mensagem"), 200, row);
            f.RegisterCallback<KeyDownEvent>(ev => { if (IsEnter(ev)) { SendChat(f); ev.StopPropagation(); } }, TrickleDown.TrickleDown);
            Btn(L.T("Enviar"), () => SendChat(f), "btn", row);
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
            if (room != null) { panel.Toast(L.T("Você já está numa sala. Saia dela para entrar na do amigo.")); return; }
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
            if (list.Count == 0) { Text(L.T("Nenhum amigo online agora. Use o botão acima para convidar quem está offline ou em outro jogo."), "small", oFriends); return; }
            Text(L.F("AMIGOS ONLINE · {0}", list.Count), "h3", oFriends);
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
                var b = Btn(sent ? L.T("Convidado ✓") : L.T("Convidar"), () =>
                {
                    if (steamNet.Invite(friend.id)) { invited.Add(friend.id); panel.Toast(L.F("Convite enviado a {0}.", Safe(friend.name))); }
                    else panel.Toast(L.T("Não deu para convidar. Confira se a Steam está aberta."));
                    DrawFriends();
                }, "btn", row);
                b.SetEnabled(!sent);
            }
            if (keep > 0f) friendsScroll.schedule.Execute(() => friendsScroll.scrollOffset = new Vector2(0f, keep));
        }

        // Ao abrir o jogo: liga a Steam (o padrão) e já conecta, sem esperar a aba Online.
        void OnlineBoot()
        {
            SteamToggled(oSteam.value, false);
            if (oSteam.value) WhenConnected(() => { });
        }

        void OnlineDestroy()
        {
            wsNet?.Close();
            steamNet?.Shutdown();
            net = null;
        }

        void OnlineShown()
        {
            if (oSteam.value && (steamNet == null || steamNet.LocalName == "")) SteamToggled(true, false);     // a escolha da última vez
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
        void SteamToggled(bool on) => SteamToggled(on, true);

        // remember=false (ao abrir o jogo): a Steam fechada não troca a escolha salva.
        void SteamToggled(bool on, bool remember)
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
            if (remember || on) PlayerPrefs.SetInt(PrefSteam, on ? 1 : 0);
            oServer.SetEnabled(!on);
            oVia.text = on ? L.T("Pela Steam, com o nome da sua conta. Para jogar pelo servidor, desligue a Steam em Opções, no menu inicial.")
                           : L.F("Pelo servidor {0}. Mude em Opções, no menu inicial.", oServer.value);
            oName.SetEnabled(!on);
            DrawMatch();
            oName.SetValueWithoutNotify(on ? steamNet.LocalName : PlayerPrefs.GetString(PrefName, ""));
        }

        void OnlineStatus()
        {
            if (!panel.OnlineMode || OnlineRun) return;
            if (room != null) panel.SetStatus(L.F("Sala {0} · {1}/{2} pilotos", room.code, room.players.Count, room.max), "status--ok");
            else if (net.state == OnlineClient.State.Connecting) panel.SetStatus(oSteam.value ? L.T("Conectando à Steam…") : L.T("Conectando ao servidor…"), "");
            else panel.SetStatus(L.T("Online: crie uma sala ou entre numa"), "");
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
            if (n.Length == 0) { SetOnlineMsg(L.T("Digite seu nome primeiro.")); oName.Focus(); return null; }
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
            if (code.Length == 0) { SetOnlineMsg(L.T("Digite o código da sala.")); oCode.Focus(); return; }
            JoinRoom(name, code);
        }

        void JoinRoom(string name, string code) => WhenConnected(() => net.Send(new NetOut { t = "join", name = name, code = code, uid = ChampFiles.PlayerUid() }));

        void RefreshRooms() => WhenConnected(() => net.Send(new NetOut { t = "list" }));

        void LeaveRoom()
        {
            rejoinCode = null; rejoinAt = -1f;
            if (MmRoom && OnlineRun && sim.phase != RaceSim.Phase.Idle) EloApply(sim.phase != RaceSim.Phase.Done);   // sair no meio: último
            net.Close();                                 // o servidor tira o jogador da sala ao ver a conexão cair
            ResetOnlineState();
            DrawOnline(); OnlineStatus();
        }

        void ResetOnlineState()
        {
            OnlineExitRun();
            room = null; myId = null; wasHost = false; pendingNet = null; mmIn = 0;
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
            net.Send(new NetOut { t = "start", name = room.name, vmax = set.vmax, ramp = set.ramp, grip = set.grip, collide = set.collide, turbo = set.turbo, reslot = set.reslotTime, byTime = set.byTime, sorteio = UnityEngine.Random.Range(1, int.MaxValue) });
        }

        string LobbyBlocker()
        {
            if (room.players.Count < 2) return L.T("Falta gente: são precisos pelo menos 2 pilotos.");
            if (room.stages.Count == 0) return L.T("Escolha pelo menos uma pista.");
            var wait = room.players.FindAll(p => p.id != room.host && !p.ready);
            return wait.Count > 0 ? L.T("Aguardando ficar pronto: ") + string.Join(", ", wait.ConvertAll(p => Safe(p.name))) + "." : "";
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
                panel.Toast(L.F("A conexão caiu. Tentando voltar à sala ({0} de {1})…", rejoinTries, RejoinMax));
                DrawOnline(); OnlineStatus();
                return;
            }
            if (rejoinCode != null) { rejoinCode = null; reason = L.T("Não deu para voltar à sala: ") + reason; }
            if (wasConnecting && !oSteam.value) reason = reason.TrimEnd('.', ' ') + L.F(". Confira o endereço ({0}) e se o servidor está rodando (cd servidor && npm start).", oServer.value);
            SetOnlineMsg(reason);
            if (wasInRoom) panel.Toast(L.T("Você saiu da sala: ") + reason);
            DrawOnline(); OnlineStatus();
            if (!wasInRoom && panel.OnlineMode) panel.SetStatus(oSteam.value ? L.T("Sem conexão com a Steam") : L.T("Sem conexão com o servidor"), "status--warn");
        }

        void OnNetMessage(NetMsg m)
        {
            switch (m.t)
            {
                case "joined":
                    myId = m.you; room = m.room; wasHost = IsHost; mmIn = 0;
                    onlineStages.Clear(); suggestions.Clear(); sentSuggestions.Clear();
                    chatLog.Clear(); oChat.Clear(); oRunChat.Clear();
                    foreach (var c in m.chat) AddChat(c);
                    SetOnlineMsg("");
                    if (rejoinCode != null) { rejoinCode = null; panel.Toast(L.T("Você voltou à sala.")); }
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
                case "mm": OnMatchStatus(m); break;
                case "mmq": if (m.mmq.Count == 4) { mmq = m.mmq.ToArray(); DrawMatch(); } break;   // Steam: a contagem das filas vem depois da lista
                case "rooms":
                    listed = m.rooms;
                    if (m.mmq.Count == 4) { mmq = m.mmq.ToArray(); DrawMatch(); }
                    DrawRooms();
                    break;
                case "chat":
                    AddChat(new NetChat { sys = m.sys, name = m.name, color = m.color, text = m.sys ? L.T(m.text) : m.text, from = m.from, time = m.time });
                    break;
                case "error":
                    if (rejoinCode != null && room == null) { rejoinCode = null; rejoinAt = -1f; }    // a sala acabou: desiste de voltar
                    if (room == null && mmIn > 0 && (m.code == "mm" || m.code == "nome-usado" || m.code == "sala" || m.code == "nome")) { mmIn = 0; DrawMatch(); }   // o servidor tirou da fila
                    SetOnlineMsg(L.T(m.text));             // o servidor fala português; o que é fixo tem tradução
                    if (room != null) panel.Toast(L.T(m.text));
                    break;
                case "started": if (room != null) { BeginOnlineRun(m); if (OnlineRun) ShowSorteio(m.mm); } break;
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
                case "pistas": OnPistas(m); break;
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
            if (lost > 0) panel.Toast(L.F("{0} {1} neste computador e saiu da lista.", lost, (lost == 1 ? L.T("pista não está") : L.T("pistas não estão"))));
            SendStages();                                // o novo host passa a ser quem guarda o JSON das pistas
        }

        // ---------- Etapas (host) ----------

        void AddOnlineStage(Option o)
        {
            if (!IsHost) return;
            if (onlineStages.Count >= MaxOnlineStages) { panel.Toast(L.F("No máximo {0} etapas.", MaxOnlineStages)); return; }
            // Qualquer pista fechada serve: ela entra adaptada ao tamanho do campeonato.
            if (!o.closed) return;
            int want = onlineStages.Count > 0 ? onlineStages[0].data.Faixas : onlineFaixas;
            var data = Adapted(o.save, want);
            if (data.ToJson(false).Length > MaxTrackJson) { panel.Toast(L.T("Essa pista é grande demais para o servidor.")); return; }
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
                var segs = TrackLayout.Build(save.Origin, save.Pieces(), save.Tail, save.StartZ);
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
            string track = string.IsNullOrWhiteSpace(m.track) ? (string.IsNullOrWhiteSpace(save.nome) ? L.T("Pista") : save.nome) : m.track;
            suggestions.Add(new Suggestion { from = m.from, name = Safe(m.name), track = Safe(track), hash = hash, save = save });
            panel.Toast(L.F("{0} sugeriu a pista “{1}”.", Safe(m.name), Safe(track)));
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
            data.baixada = true;
            if (TrackLibrary.List().Exists(e => e.save.nome == data.nome)) data.nome = L.F("{0} (de {1})", s.track, s.name);
            TrackLibrary.Save(data);
            panel.Toast(L.F("“{0}” salva na sua biblioteca.", data.nome));
        }

        // Campeonato online e partida rápida: as pistas que chegaram na largada vão sozinhas para a biblioteca. A que já está lá
        // (mesma identidade no tamanho do campeonato) não é gravada de novo; os recordes vão pela identidade (recordes.json),
        // então só mudam quando alguém bate o tempo.
        void DrawRunTracks()
        {
            oRunTracks.Clear();
            var C = onlineChamp; if (C == null) return;
            Text(L.T("PISTAS DO CAMPEONATO"), "h2", oRunTracks);
            Text(L.T("As pistas da corrida ficam guardadas na sua biblioteca."), "hint", oRunTracks);
            var box = El(null, Caixa(oRunTracks));
            var lib = TrackLibrary.List();
            var seen = new HashSet<string>();
            int novas = 0;
            foreach (var st in C.stages)
            {
                if (!seen.Add(st.hash)) continue;
                int i = lib.FindIndex(e => Champ.TrackHash(Adapted(e.save, C.faixas)) == st.hash);
                bool nova = i < 0;
                string nome = nova ? null : lib[i].save.nome;
                if (nova) { nome = SaveRunTrack(st, lib); novas++; lib = TrackLibrary.List(); }
                var row = El("lib-item", box);
                var texts = El("lib-texts", row);
                Text(nome, "lib-name", texts);
                Text(nova ? L.T("guardada agora na sua biblioteca") : L.T("já estava na sua biblioteca"), "small", texts);
            }
            if (novas > 0) panel.Toast(novas == 1 ? L.T("1 pista nova na sua biblioteca.") : L.F("{0} pistas novas na sua biblioteca.", novas));
        }

        string SaveRunTrack(Stage st, List<TrackLibrary.Entry> lib)
        {
            var data = TrackSave.FromJson(st.data.ToJson(false));
            string nome = st.name.Replace("​", "").Trim();   // tira o Safe do nome da etapa
            string bas = nome.Length > 0 ? nome : L.T("Pista");
            data.nome = bas;
            data.baixada = true;
            for (int n = 2; lib.Exists(e => e.save.nome == data.nome); n++)   // outra pista com o mesmo nome: não sobrescreve
                data.nome = n == 2 ? bas + L.T(" (online)") : L.F("{0} (online {1})", bas, n - 1);
            TrackLibrary.Save(data);
            return data.nome;
        }

        // Visitante: manda uma das minhas pistas ao host.
        void SendSuggestion(Option o)
        {
            if (IsHost || room == null || !o.closed) return;
            string json = o.save.ToJson(false);
            if (json.Length > MaxTrackJson) { panel.Toast(L.T("Essa pista é grande demais para sugerir.")); return; }
            string hash = Champ.TrackHash(o.save);
            if (sentSuggestions.Contains(hash)) return;
            net.Send(new NetOut { t = "suggest", track = string.IsNullOrWhiteSpace(o.save.nome) ? o.name : o.save.nome, hash = hash, faixas = o.save.Faixas, json = json });
            sentSuggestions.Add(hash);
            panel.Toast(L.T("Pista enviada ao host."));
            DrawOnline();
        }

        void DrawSuggestions(bool host)
        {
            oSuggestBox.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oSendBox.style.display = !host && room != null ? DisplayStyle.Flex : DisplayStyle.None;
            oSuggest.Clear(); oSend.Clear();
            if (host)
            {
                if (suggestions.Count == 0) Text(L.T("Nenhuma sugestão ainda."), "small", oSuggest);
                foreach (var sg in suggestions.ToArray())
                {
                    var s = sg;
                    var item = El("lib-item", oSuggest);
                    item.Add(new TrackThumb(s.save));
                    var texts = El("lib-texts", item);
                    Text(s.track, "lib-name", texts);
                    Text(L.F("de {0} · {1}", s.name, TrackLine(s.save)), "small", texts);
                    var row = El("row", item);
                    Btn(L.T("Incluir"), () => AcceptSuggestion(s), "btn btn--primary", row);
                    Btn(L.T("Salvar"), () => SaveSuggestion(s), "btn", row).tooltip = L.T("Guarda na sua biblioteca de pistas");
                    Btn(L.T("Ignorar"), () => { suggestions.Remove(s); DrawOnline(); }, "btn", row);
                }
                return;
            }
            if (room == null) return;
            // Se o host já escolheu o tipo (2 ou 4 carros), só as pistas desse tipo fazem sentido.
            int want = room.stages.Count > 0 ? TrackMath.Faixas(room.stages[0].faixas) : 0;
            int shown = 0;
            var sendOpts = Options();
            CountFilter(oSendFilter, sendOpts);
            foreach (var o in sendOpts)
            {
                if (!o.closed) continue;                         // qualquer tamanho serve: o host adapta
                if (!oSendFilter.Shows(o.save)) continue;
                var opt = o;
                bool sent = sentSuggestions.Contains(Champ.TrackHash(o.save));
                var item = new Button(() => SendSuggestion(opt));
                item.AddToClassList("lib-item"); item.AddToClassList("pick");
                item.SetEnabled(!sent);
                item.Add(new TrackThumb(o.save));
                var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                Text(string.IsNullOrWhiteSpace(o.save.nome) ? o.name : o.save.nome, "lib-name", texts).pickingMode = PickingMode.Ignore;
                Text(TrackLine(o.save), "small", texts).pickingMode = PickingMode.Ignore;
                Text(sent ? L.T("enviada ✓") : L.T("enviar"), "pick-plus", item).pickingMode = PickingMode.Ignore;
                oSend.Add(item);
                shown++;
            }
            if (shown == 0) Text(want != 0 ? L.F("Você não tem pistas fechadas de {0} carros.", want) : L.T("Você não tem pistas fechadas para sugerir."), "small", oSend);
        }

        // ---------- Desenho das etapas (visitante, seção 23.4) ----------

        // A pista da etapa: a do host, a do cache ou a da biblioteca daqui com a mesma identidade no tamanho da etapa.
        TrackSave StageTrack(NetStage s, List<Option> opts)
        {
            if (s.hash == null) return null;
            if (stageTracks.TryGetValue(s.hash, out var t)) return t;
            int f = TrackMath.Faixas(s.faixas);
            var o = opts.Find(x => x.closed && Champ.TrackHash(Adapted(x.save, f)) == s.hash);
            if (o.save == null) return null;
            return stageTracks[s.hash] = Adapted(o.save, f);
        }

        // Pede ao host (ou ao servidor) as pistas que faltam: de novo só se a falta mudou ou depois de 5 s.
        void AskPistas(string missing)
        {
            if (missing == "" || IsHost || room == null || room.state != "lobby") return;
            if (missing == pistasAsked && Time.realtimeSinceStartup - pistasAt < 5f) return;
            pistasAsked = missing; pistasAt = Time.realtimeSinceStartup;
            net.Send(new NetOut { t = "pistas" });
        }

        // Chegou o desenho: nada que veio da rede é confiado, a identidade tem que bater com a pista.
        void OnPistas(NetMsg m)
        {
            if (room == null || IsHost) return;
            bool any = false;
            foreach (var st in m.stages)
            {
                if (st == null || string.IsNullOrEmpty(st.json) || st.hash == null || stageTracks.ContainsKey(st.hash)) continue;
                try
                {
                    var save = TrackSave.FromJson(st.json);
                    if (Champ.TrackHash(save) != st.hash) continue;
                    stageTracks[st.hash] = save; any = true;
                }
                catch { }
            }
            if (any && !OnlineRun) DrawLobby();
        }

        // Clicar numa etapa no lobby abre a pista na mesa (Ctrl+Z volta o que estava lá).
        void OpenStageOnTable(TrackSave save, string name)
        {
            if (OnlineRun || save == null) return;
            var data = TrackSave.FromJson(save.ToJson(false));
            data.nome = name.Replace("​", "").Trim();
            builder.Load(data);
            panel.FrameTrack();
            panel.Toast(L.F("“{0}” na mesa. Ctrl+Z volta a pista de antes.", data.nome));
        }

        void SendStages()
        {
            var o = new NetOutStages();
            foreach (var s in onlineStages) o.stages.Add(new NetStage { name = s.name, laps = s.laps, secs = s.secs, tempo = set.byTime, faixas = s.data.Faixas, hash = s.hash, json = s.data.ToJson(false) });
            net.Send(o);
        }

        // ---------- Desenho ----------

        TrackFilter oPickFilter, oSendFilter;

        void DrawOnline()
        {
            if (oEntry == null) return;
            oEntry.style.display = room == null ? DisplayStyle.Flex : DisplayStyle.None;
            oLobby.style.display = room != null && !OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
            oRun.style.display = OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
            if (room == null) { DrawRooms(); DrawMatch(); }
            else if (OnlineRun) champDirty = true;               // quem entrou ou saiu muda os nomes na tela da corrida
            else DrawLobby();
        }

        void DrawRooms()
        {
            oRooms.Clear();
            if (listed.Count == 0) { Text(L.T("Nenhuma sala pública agora. Toque em Atualizar, ou crie a sua."), "small", oRooms); return; }
            foreach (var r in listed)
            {
                var entry = r;
                var item = new Button(() => { string n = RequireName(); if (n != null) JoinRoom(n, entry.code); });
                item.AddToClassList("lib-item"); item.AddToClassList("pick");
                item.SetEnabled(r.players < r.max);
                var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                Text(Safe(r.name), "lib-name", texts).pickingMode = PickingMode.Ignore;
                Text(L.F("{0} · {1}/{2} pilotos · {3} {4} · {5}", Safe(r.host), r.players, r.max, r.stages, (r.stages == 1 ? L.T("etapa") : L.T("etapas")), r.code), "small", texts).pickingMode = PickingMode.Ignore;
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
                sw.tooltip = Champ.ColorNames[i] + (taken ? L.F(" (de {0})", Safe(owner.name)) : "");
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
            oVisibility.label = host ? L.T("Sala pública (aparece na lista)") : room.pub ? L.T("Sala pública") : L.T("Sala privada (só entra com o código)");
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
                string tag = (p.id == room.host ? "host" : "") + (p.id == myId ? (p.id == room.host ? L.T(" · você") : L.T("você")) : p.id == NetGuest.Id(myId) ? L.T("jogador 2 deste computador") : "");
                if (tag != "") Text(tag, "small", texts);
                bool ok = p.ready || p.id == room.host;
                Text(ok ? L.T("✓ pronto") : L.T("aguardando"), ok ? "small ok" : "small", row);
            }

            // Cores (a minha e, se tiver, a do segundo jogador)
            var me = room.Player(myId);
            DrawSwatches(oColors, myId, false);
            bool guest = room.Player(NetGuest.Id(myId)) != null, lobbyNow = room.state == "lobby";
            oGuestBtn.text = guest ? L.T("Tirar o jogador 2") : L.T("+ Jogador 2 neste computador");
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
                ? L.F("<b>{0}</b> · <b>pistas de {1} trilhos</b>", (byTimeNow ? L.T("Por tempo") : L.T("Por voltas")), faixasNow) + (faixasNow >= 4 && gaps > 0 ? L.F(" · com {0} {1}, {2} {3} em cada bateria", n, (n == 1 ? L.T("jogador") : L.T("jogadores")), gaps, (gaps == 1 ? L.T("trilho fica vazio") : L.T("trilhos ficam vazios"))) : "")
                : L.T("Formato e tipo de pista: o host ainda não escolheu.");

            // Etapas
            oStageHint.text = host
                ? L.F("Escolha as pistas e {0} de cada etapa. As outras regras (velocidade, aderência, colisão) são as de Opções, no menu inicial. Os visitantes veem a lista, mas só você mexe nela.", (set.byTime ? L.T("a duração") : L.T("as voltas")))
                : L.T("O host escolhe as pistas. Você escolhe a cor do seu carrinho e conversa no chat.");
            oStages.Clear();
            var list = room.stages;
            if (list.Count == 0) Text(host ? L.T("Nenhuma etapa ainda. Clique numa pista abaixo.") : L.T("O host ainda não escolheu as pistas."), "small", oStages);
            var opts = host ? null : Options();
            string missing = "";
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var s = list[i];
                var item = El("lib-item", oStages);
                var local = host && i < onlineStages.Count && onlineStages[i].hash == s.hash ? onlineStages[i] : null;
                var draw = local != null ? local.data : host ? null : StageTrack(s, opts);
                if (draw == null && !host) missing += s.hash;
                var texts = El("lib-texts", item);
                if (draw != null)
                {
                    // O desenho e o nome abrem a pista na mesa (os botões do host ficam fora disso).
                    var thumb = new TrackThumb(draw);
                    item.Insert(0, thumb);
                    string nm = s.name;
                    EventCallback<ClickEvent> open = _ => OpenStageOnTable(draw, nm);
                    thumb.RegisterCallback(open); texts.RegisterCallback(open);
                    thumb.tooltip = texts.tooltip = L.T("Clique para ver a pista na mesa");
                }
                Text($"{i + 1}. {Safe(s.name)}", "lib-name", texts);
                string fmt = s.tempo ? Dur(s.secs > 0 ? s.secs : 120) : (s.laps == 1 ? L.F("{0} volta", s.laps) : L.F("{0} voltas", s.laps));
                Text(local != null ? TrackLine(local.data) : draw != null ? fmt + " · " + TrackLine(draw) : fmt, "small", texts);
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
                    Text((local.laps == 1 ? L.F("{0} volta", local.laps) : L.F("{0} voltas", local.laps)), "step-val", step);
                    Btn("+", () => { local.laps = Mathf.Min(30, local.laps + 1); SendStages(); }, "btn step-btn", step);
                }
                var mv = El("row", ctl);
                Btn("▲", () => { (onlineStages[idx - 1], onlineStages[idx]) = (onlineStages[idx], onlineStages[idx - 1]); SendStages(); }, "btn step-btn", mv).SetEnabled(i > 0);
                Btn("▼", () => { (onlineStages[idx + 1], onlineStages[idx]) = (onlineStages[idx], onlineStages[idx + 1]); SendStages(); }, "btn step-btn", mv).SetEnabled(i < list.Count - 1);
                Btn("✕", () => { onlineStages.RemoveAt(idx); SendStages(); }, "btn step-btn", mv).tooltip = L.T("Tirar etapa");
            }
            AskPistas(missing);

            oHostPick.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oPick.Clear();
            var hostOpts = host ? Options() : null;
            if (host) CountFilter(oPickFilter, hostOpts);
            if (host)
                foreach (var o in hostOpts)
                {
                    if (!o.closed) continue;                     // pista aberta não corre
                    if (!oPickFilter.Shows(o.save)) continue;
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
            oRulesHint.text = L.F("Regras da corrida, em Opções no menu inicial (velocidade {0} cél/s, até 100% em {1} s, aderência {2}, colisão {3}, turbo {4}, recolocar em {5} s). Ficam fixos até o fim.", Num(set.vmax), Num(set.ramp, 2), Mathf.RoundToInt(set.grip), (set.collide ? L.T("ligada") : L.T("desligada")), (set.turbo ? L.T("ligado") : L.T("desligado")), Num(set.reslotTime, 2));
            oReadyBtn.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
            oReadyBtn.text = me != null && me.ready ? L.T("Não estou pronto") : L.T("Estou pronto");
            oReadyBtn.EnableInClassList("btn--primary", !(me != null && me.ready));
            oStartBtn.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oStartBtn.SetEnabled(host && blocker == "");
            oStartHint.text = host ? (blocker != "" ? blocker : L.F("Todos prontos: {0} pilotos · {1} {2} · {3} baterias.", room.players.Count, room.stages.Count, (room.stages.Count == 1 ? L.T("etapa") : L.T("etapas")), room.players.Count * room.stages.Count))
                : me != null && me.ready ? L.T("Você está pronto. Aguardando o host largar.") : L.T("Confirme quando estiver pronto: o host só larga com todos prontos.");
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
                : L.F("<b><color={0}>{1}</color></b>: {2}", (ColorUtility.TryParseHtmlString(c.color, out _) ? c.color : "#E5EBE3"), Safe(c.name), text);
            log.Add(Text(line, "chat-line"));
            while (log.contentContainer.childCount > ChatKeep) log.contentContainer.RemoveAt(0);
            log.schedule.Execute(() => log.scrollOffset = new Vector2(0f, log.contentContainer.layout.height)).ExecuteLater(30);
        }
    }
}
