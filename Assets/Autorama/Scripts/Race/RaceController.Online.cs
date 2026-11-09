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
        List<NetListed> listed = new List<NetListed>();
        readonly List<Stage> onlineStages = new List<Stage>();    // do host: com os dados completos (o servidor guarda e devolve só o resumo)

        VisualElement oEntry, oLobby, oRooms, oPlayers, oColors, oStages, oPick, oHostPick, oRun, oRunChatSec;
        Label oMsg, oRoomTitle, oRoomCode, oStageHint, oLobbyMsg, oRulesHint, oStartHint;
        TextField oServer, oName, oRoomName, oCode, oChatInput, oRunChatInput;
        Toggle oPublic, oVisibility, oSteam;
        VisualElement oInviteBox, oFriends;
        ScrollView friendsScroll;                                    // a lista de amigos, com rolagem própria
        readonly HashSet<ulong> invited = new HashSet<ulong>();      // amigos já convidados nesta sala
        ulong launchLobby;
        ScrollView oChat, oRunChat;
        Button oStartBtn, oReadyBtn, oRunQuit;
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

            var st = Section(oLobby, "ETAPAS");
            oStageHint = Text("", "hint", st);
            oStages = El(null, st);
            oHostPick = El(null, st);
            Text("PISTAS DISPONÍVEIS", "h3", oHostPick);
            oPick = El(null, oHostPick);

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
            WhenConnected(() => net.Send(new NetOut { t = "create", name = name, roomName = roomName, pub = pub }));
        }

        void JoinByCode()
        {
            string name = RequireName(); if (name == null) return;
            string code = (oCode.value ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) { SetOnlineMsg("Digite o código da sala."); oCode.Focus(); return; }
            JoinRoom(name, code);
        }

        void JoinRoom(string name, string code) => WhenConnected(() => net.Send(new NetOut { t = "join", name = name, code = code }));

        void RefreshRooms() => WhenConnected(() => net.Send(new NetOut { t = "list" }));

        void LeaveRoom()
        {
            net.Close();                                 // o servidor tira o jogador da sala ao ver a conexão cair
            ResetOnlineState();
            DrawOnline(); OnlineStatus();
        }

        void ResetOnlineState()
        {
            OnlineExitRun();
            room = null; myId = null; wasHost = false; pendingNet = null;
            onlineStages.Clear();
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
            net.Send(new NetOut { t = "start", name = room.name, vmax = set.vmax, ramp = set.ramp, grip = set.grip, collide = set.collide });
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
            ResetOnlineState();
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
                    onlineStages.Clear();
                    chatLog.Clear(); oChat.Clear(); oRunChat.Clear();
                    foreach (var c in m.chat) AddChat(c);
                    SetOnlineMsg("");
                    DrawOnline(); OnlineStatus();
                    break;
                case "room":
                    if (room == null) break;
                    room = m.room;
                    if (IsHost && !wasHost) AdoptStages();
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
                    SetOnlineMsg(m.text);
                    if (room != null) panel.Toast(m.text);
                    break;
                case "started": if (room != null) BeginOnlineRun(m); break;
                case "lobby":
                    OnlineExitRun();
                    if (!string.IsNullOrEmpty(m.text)) { panel.Toast(m.text); AddChat(new NetChat { sys = true, text = m.text }); }
                    DrawOnline(); OnlineStatus();
                    break;
                case "in": OnlineInput(m); break;
                case "snap": if (OnlineRun && !IsHost) OnlineApplySnap(m.snap); break;
                case "ev": OnlineEvent(m); break;
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
                var o = opts.Find(x => x.closed && Champ.TrackHash(x.save) == s.hash);
                if (o.save == null) { lost++; continue; }
                var data = TrackSave.FromJson(o.save.ToJson(false));
                onlineStages.Add(new Stage { name = s.name, laps = s.laps, data = data, hash = s.hash });
            }
            if (lost > 0) panel.Toast($"{lost} {(lost == 1 ? "pista não está" : "pistas não estão")} neste computador e saiu da lista.");
            SendStages();                                // o novo host passa a ser quem guarda o JSON das pistas
        }

        // ---------- Etapas (host) ----------

        void AddOnlineStage(Option o)
        {
            if (!IsHost) return;
            if (onlineStages.Count >= MaxOnlineStages) { panel.Toast($"No máximo {MaxOnlineStages} etapas."); return; }
            var data = TrackSave.FromJson(o.save.ToJson(false));
            if (data.ToJson(false).Length > MaxTrackJson) { panel.Toast("Essa pista é grande demais para o servidor."); return; }
            onlineStages.Add(new Stage { name = data.nome, laps = set.laps, data = data, hash = Champ.TrackHash(data) });
            SendStages(); DrawLobby();
        }

        void SendStages()
        {
            var o = new NetOutStages();
            foreach (var s in onlineStages) o.stages.Add(new NetStage { name = s.name, laps = s.laps, hash = s.hash, json = s.data.ToJson(false) });
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
                string tag = (p.id == room.host ? "host" : "") + (p.id == myId ? (p.id == room.host ? " · você" : "você") : "");
                if (tag != "") Text(tag, "small", texts);
                bool ok = p.ready || p.id == room.host;
                Text(ok ? "✓ pronto" : "aguardando", ok ? "small ok" : "small", row);
            }

            // Cores
            oColors.Clear();
            var me = room.Player(myId);
            for (int i = 0; i < Champ.Colors.Length; i++)
            {
                string hex = Champ.Colors[i];
                var owner = room.players.Find(p => string.Equals(p.color, hex, StringComparison.OrdinalIgnoreCase));
                bool mine = owner != null && owner.id == myId, taken = owner != null && !mine;
                var sw = Btn("", () => net.Send(new NetOut { t = "color", color = hex }), "swatch", oColors);
                ColorUtility.TryParseHtmlString(hex, out var col);
                sw.style.backgroundColor = col;
                sw.tooltip = Champ.ColorNames[i] + (taken ? $" (de {Safe(owner.name)})" : "");
                sw.EnableInClassList("swatch--on", mine);
                sw.EnableInClassList("swatch--used", taken);
                sw.SetEnabled(!taken);
            }

            // Etapas
            oStageHint.text = host
                ? "Escolha as pistas e as voltas de cada etapa. Os visitantes veem a lista, mas só você mexe nela."
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
                Text(local != null ? TrackLine(local.data) : $"{s.laps} {(s.laps == 1 ? "volta" : "voltas")}", "small", texts);
                if (!host || local == null) continue;
                var ctl = El("stage-ctl", item);
                var step = El("stepper", ctl);
                Btn("−", () => { local.laps = Mathf.Max(1, local.laps - 1); SendStages(); }, "btn step-btn", step);
                Text($"{local.laps} {(local.laps == 1 ? "volta" : "voltas")}", "step-val", step);
                Btn("+", () => { local.laps = Mathf.Min(30, local.laps + 1); SendStages(); }, "btn step-btn", step);
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

            // Pronto e largada
            string blocker = LobbyBlocker();
            oRulesHint.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            oRulesHint.text = $"Regras: os ajustes da aba Corrida (velocidade {Num(set.vmax)} cél/s, até 100% em {Num(set.ramp, 2)} s, aderência {Mathf.RoundToInt(set.grip)}, colisão {(set.collide ? "ligada" : "desligada")}). Ficam fixos até o fim.";
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
