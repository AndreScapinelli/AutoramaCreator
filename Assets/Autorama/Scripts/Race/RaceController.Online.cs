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
        const string PrefServer = "autorama.online.servidor", PrefName = "autorama.online.nome";
        const string DefaultServer = "ws://localhost:8080";
        const int MaxOnlineStages = 12, MaxTrackJson = 100 * 1024, ChatKeep = 100;

        OnlineClient net;
        NetRoom room;                                   // null fora da sala
        string myId;
        bool wasHost;
        Action pendingNet;                              // o que fazer assim que a conexão abrir
        List<NetListed> listed = new List<NetListed>();
        readonly List<Stage> onlineStages = new List<Stage>();    // do host: com os dados completos (o servidor guarda e devolve só o resumo)

        VisualElement oEntry, oLobby, oRooms, oPlayers, oColors, oStages, oPick, oHostPick;
        Label oMsg, oRoomTitle, oRoomCode, oStageHint, oLobbyMsg;
        TextField oServer, oName, oRoomName, oCode, oChatInput;
        Toggle oPublic, oVisibility;
        ScrollView oChat;
        Button oStartBtn;

        // Tira "<" de nomes e mensagens dos outros: o rótulo aceita rich text e não pode deixar ninguém mudar o visual.
        static string Safe(string s) => (s ?? "").Replace("<", "<\u200B");
        bool IsHost => room != null && room.host == myId;

        // ---------- Montagem ----------

        void BuildOnlineUI()
        {
            net = new OnlineClient();
            net.Opened += OnNetOpen;
            net.Closed += OnNetClosed;
            net.Message += OnNetMessage;
            var box = panel.OnlineBox;

            // Antes de entrar numa sala
            oEntry = El(null, box);
            var head = Section(oEntry, "CAMPEONATO ONLINE");
            Text("Um jogador cria a sala e escolhe as pistas. Os outros entram pelo código ou pela lista de salas públicas.", "hint", head);
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
            oChat = new ScrollView(ScrollViewMode.Vertical);
            oChat.AddToClassList("chat-log");
            chat.Add(oChat);
            var chatRow = El("row", chat);
            chatRow.style.flexWrap = Wrap.NoWrap;
            chatRow.style.marginTop = 6;
            oChatInput = Field("Escreva uma mensagem", 200, chatRow);
            oChatInput.RegisterCallback<KeyDownEvent>(ev => { if (IsEnter(ev)) { SendChat(); ev.StopPropagation(); } }, TrickleDown.TrickleDown);
            Btn("Enviar", SendChat, "btn", chatRow);

            var acts = Section(oLobby, "CAMPEONATO");
            oLobbyMsg = Text("", "small bad", acts);
            oStartBtn = Btn("Começar campeonato (em breve)", null, "btn btn--primary", acts);
            oStartBtn.SetEnabled(false);
            oStartBtn.tooltip = "A largada online ainda não está pronta: por enquanto só o lobby funciona.";
            var leave = Btn("Sair da sala", LeaveRoom, "btn btn--danger", acts);
            leave.style.marginTop = 8;

            DrawOnline();
        }

        static bool IsEnter(KeyDownEvent ev) => ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter;

        // ---------- Quadro a quadro e ciclo de vida ----------

        void OnlineTick() => net?.Pump();

        void OnlineDestroy() { if (net != null) { net.Close(); net = null; } }

        void OnlineShown() { DrawOnline(); OnlineStatus(); }

        void OnlineStatus()
        {
            if (!panel.OnlineMode) return;
            if (room != null) panel.SetStatus($"Sala {room.code} · {room.players.Count}/{room.max} pilotos", "status--ok");
            else if (net.state == OnlineClient.State.Connecting) panel.SetStatus("Conectando ao servidor…", "");
            else panel.SetStatus("Online: crie uma sala ou entre numa", "");
        }

        // ---------- Ações do jogador ----------

        void SetOnlineMsg(string text)
        {
            if (oMsg != null) oMsg.text = text ?? "";
            if (oLobbyMsg != null) oLobbyMsg.text = room != null ? text ?? "" : "";
        }

        // O nome é obrigatório para criar e para entrar.
        string RequireName()
        {
            string n = (oName.value ?? "").Trim();
            if (n.Length == 0) { SetOnlineMsg("Digite seu nome primeiro."); oName.Focus(); return null; }
            return n;
        }

        // Conecta (se preciso) e só então roda a ação.
        void WhenConnected(Action act)
        {
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
            room = null; myId = null; wasHost = false; pendingNet = null;
            onlineStages.Clear();
            oChat.Clear();
        }

        void SendChat()
        {
            string text = (oChatInput.value ?? "").Trim();
            if (text.Length == 0 || room == null) return;
            net.Send(new NetOut { t = "chat", text = text });
            oChatInput.SetValueWithoutNotify("");
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
            bool wasInRoom = room != null;
            ResetOnlineState();
            SetOnlineMsg(reason);
            if (wasInRoom) panel.Toast("Você saiu da sala: " + reason);
            DrawOnline(); OnlineStatus();
        }

        void OnNetMessage(NetMsg m)
        {
            switch (m.t)
            {
                case "joined":
                    myId = m.you; room = m.room; wasHost = IsHost;
                    onlineStages.Clear();
                    oChat.Clear();
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
            if (lost > 0) { panel.Toast($"{lost} {(lost == 1 ? "pista não está" : "pistas não estão")} neste computador e saiu da lista."); SendStages(); }
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
            oLobby.style.display = room != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (room == null) DrawRooms(); else DrawLobby();
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
        }

        // ---------- Chat ----------

        void AddChat(NetChat c)
        {
            string text = Safe(c.text);
            string line = c.sys ? $"<i><color=#94A197>{text}</color></i>"
                : $"<b><color={(ColorUtility.TryParseHtmlString(c.color, out _) ? c.color : "#E5EBE3")}>{Safe(c.name)}</color></b>: {text}";
            var l = Text(line, "chat-line");
            oChat.Add(l);
            while (oChat.contentContainer.childCount > ChatKeep) oChat.contentContainer.RemoveAt(0);
            oChat.schedule.Execute(() => oChat.scrollOffset = new Vector2(0f, oChat.contentContainer.layout.height)).ExecuteLater(30);
        }
    }
}
