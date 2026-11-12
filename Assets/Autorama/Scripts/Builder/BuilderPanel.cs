using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Autorama
{
    // Painel do construtor em UI Toolkit: paleta com ícones e prévia fantasma, peça selecionada, resumo,
    // sequência, "Minhas pistas" com miniaturas, réguas da mesa (1, 2, 3… / A, B, C…) e célula sob o mouse.
    [RequireComponent(typeof(UIDocument))]
    public class BuilderPanel : MonoBehaviour
    {
        public TrackBuilder builder;
        public TopDownCamera cam;
        public StyleSheet styles;

        static BuilderPanel instance;

        // O mouse está em cima de algo do painel? (posição do Input System, y para cima)
        public static bool PointerOverUI(Vector2 screen)
        {
            var root = instance ? instance.root : null;
            if (root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            return root.panel.Pick(p) != null;
        }

        // Algum campo de texto está recebendo o teclado? Aí os atalhos ficam desligados.
        public static bool IsTyping
        {
            get
            {
                var fe = instance ? instance.root?.focusController?.focusedElement as VisualElement : null;
                return fe != null && (fe is TextField || fe.GetFirstAncestorOfType<TextField>() != null);
            }
        }

        // ---------- Abas Construtor / Corrida / Campeonato / Online ----------

        public enum Mode { Build, Race, Champ, Online }
        public Mode CurrentMode { get; private set; }
        public bool RaceMode => CurrentMode == Mode.Race || CurrentMode == Mode.Champ || (CurrentMode == Mode.Online && OnlineRacing);        // corrida ou campeonato: os carrinhos andam
        public bool OnlineRacing { get; private set; }            // campeonato online em andamento: a aba Online vira a tela de corrida
        public void SetOnlineRacing(bool on) { OnlineRacing = on; Refresh(); }
        public bool ChampMode => CurrentMode == Mode.Champ;
        public bool OnlineMode => CurrentMode == Mode.Online;
        public bool Locked => CurrentMode != Mode.Build;          // fora do construtor a pista não se edita
        public static bool EditLocked => instance && (instance.Locked || instance.MenuOpen);
        // Tela inicial: aparece ao abrir o jogo; escolher um modo (ou um convite da Steam) fecha.
        public bool MenuOpen { get; private set; }
        VisualElement startMenu;
        public event System.Action<Mode> ModeChanged;
        public VisualElement RaceBox => raceBox;          // conteúdo da aba Corrida na barra lateral
        public VisualElement ChampBox => champBox;        // conteúdo da aba Campeonato
        public VisualElement OnlineBox => onlineBox;      // conteúdo da aba Online
        public VisualElement Root => root;                // camada por cima da mesa (controles dos jogadores, luzes)
        public float SideWidth => sideVisible && side != null ? side.layout.width : 0f;

        public void SetMode(Mode m)
        {
            if (MenuOpen && !(OnlineRacing && m != Mode.Online)) ShowMenu(false);
            if (m == CurrentMode) return;
            if (OnlineRacing && m != Mode.Online) { Say("Termine o campeonato online (ou saia da sala) antes de trocar de aba."); return; }
            CurrentMode = m;
            tabBuild.EnableInClassList("tab--on", m == Mode.Build);
            tabRace.EnableInClassList("tab--on", m == Mode.Race);
            tabChamp.EnableInClassList("tab--on", m == Mode.Champ);
            tabOnline.EnableInClassList("tab--on", m == Mode.Online);
            buildBox.style.display = m == Mode.Build ? DisplayStyle.Flex : DisplayStyle.None;
            raceBox.style.display = m == Mode.Race ? DisplayStyle.Flex : DisplayStyle.None;
            champBox.style.display = m == Mode.Champ ? DisplayStyle.Flex : DisplayStyle.None;
            onlineBox.style.display = m == Mode.Online ? DisplayStyle.Flex : DisplayStyle.None;
            if (m != Mode.Build) closeBtn.style.display = DisplayStyle.None;      // "Fechar circuito" é do Construtor: fora dele, pista aberta nem aparece
            if (Locked) { builder.CancelAutoClose(); builder.HideGhost(); builder.SetHoverCell(null); hover = null; }
            root.focusController?.focusedElement?.Blur();
            ModeChanged?.Invoke(m);
            Refresh();
        }

        public void SetStatus(string text, string cls)
        {
            statusBox.RemoveFromClassList("status--ok"); statusBox.RemoveFromClassList("status--warn");
            if (!string.IsNullOrEmpty(cls)) statusBox.AddToClassList(cls);
            statusText.text = text;
        }

        public void Toast(string text) => Say(text);

        UIDocument doc;
        VisualElement buildBox, raceBox, champBox, onlineBox;
        Button tabBuild, tabRace, tabChamp, tabOnline;
        VisualElement root, side, content, showTab, topRuler, leftRuler, info, scaleBar;
        VisualElement statusBox, statusDot, selectedBox, summaryBox, seqBox, libList;
        Label statusText, hint, cellLabel, scaleText, toast, viewBadge;
        // Sobre a mesa, no construtor: o selo de status (com a ação que resolve) e a barra de ações.
        VisualElement tableWrap, tableStatus, tableDot, actionBar;
        Label tableText;
        Button tableClose, tableShow, barUndo, barRedo, barFlip, barRemove, bar3D;
        TextField nameField;
        Button undoBtn, redoBtn, viewBtn, saveAsBtn, closeBtn, endFrontBtn, endBackBtn;

        VisualElement closeBox, closeFill;
        Label closeLabel;
        bool wasClosing;

        // A busca roda aos poucos, quadro a quadro (PumpClose), com barra de progresso e botão de cancelar.
        void AutoClose()
        {
            string msg = builder.BeginAutoClose();
            if (!string.IsNullOrEmpty(msg)) Say(msg);
            UpdateCloseBox();
        }

        void PumpClose()
        {
            if (builder.Closing)
            {
                if (builder.PumpAutoClose(8) && builder.Info != null && builder.Info.closed) FrameTrack();
            }
            UpdateCloseBox();
        }

        void UpdateCloseBox()
        {
            var job = builder.CloseJob;
            bool on = job != null;
            closeBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on)
            {
                closeBtn.style.display = DisplayStyle.None;
                closeLabel.text = $"{job.Status}  ({job.Elapsed:0} s)";
                closeFill.style.width = Length.Percent(Mathf.Clamp01(job.Progress) * 100f);
            }
            if (wasClosing && !on) Refresh();                 // volta o botão de fechar
            wasClosing = on;
        }
        readonly List<Label> colLabels = new List<Label>(), rowLabels = new List<Label>();
        List<TrackLibrary.Entry> library = new List<TrackLibrary.Entry>();
        string armed;
        float armedUntil, toastUntil;
        bool sideVisible = true, framed;
        Vector2Int? hover;

        static readonly CultureInfo PtBr = new CultureInfo("pt-BR");

        // ---------- Montagem ----------

        void OnEnable()
        {
            instance = this;
            doc = GetComponent<UIDocument>();
            root = doc.rootVisualElement;
            root.Clear();
            if (styles) root.styleSheets.Add(styles);
            root.AddToClassList("root");
            root.pickingMode = PickingMode.Ignore;
            BuildLayout();
            builder.Changed += Refresh;
            builder.Notice += Say;
            builder.Shifted += cam.Nudge;
            RefreshLibrary();
            Refresh();
            root.RegisterCallback<GeometryChangedEvent>(OnFirstLayout);
        }

        void OnDisable()
        {
            if (builder) { builder.Changed -= Refresh; builder.Notice -= Say; builder.Shifted -= cam.Nudge; }
            if (instance == this) instance = null;
        }

        // Na primeira vez que o painel tem tamanho: em tela estreita começa escondido; depois enquadra a pista.
        void OnFirstLayout(GeometryChangedEvent e)
        {
            if (root.layout.width <= 0) return;
            root.UnregisterCallback<GeometryChangedEvent>(OnFirstLayout);
            if (root.layout.width < 760f) SetSide(false);
            root.schedule.Execute(FrameTrack).ExecuteLater(30);
        }

        // Lista de pistas com altura máxima (.lista-pistas): passa disso, rola.
        internal static ScrollView Caixa(VisualElement parent)
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.AddToClassList("lista-pistas");
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            parent?.Add(s);
            return s;
        }

        internal static VisualElement El(string cls, VisualElement parent = null)
        {
            var e = new VisualElement();
            if (cls != null) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        internal static Label Text(string text, string cls, VisualElement parent = null)
        {
            var l = new Label(text);
            if (cls != null) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        internal static Button Btn(string text, System.Action onClick, string cls = "btn", VisualElement parent = null)
        {
            var b = new Button(onClick) { text = text };
            foreach (var c in cls.Split(' ')) b.AddToClassList(c);
            parent?.Add(b);
            return b;
        }

        VisualElement Section(string title)
        {
            var s = El("section", buildBox);
            Text(title, "h2", s);
            return s;
        }

        Image Icon(string name, string cls)
        {
            var img = new Image { image = IconTex(name), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList(cls);
            return img;
        }

        // Na pista de 4 carros, o ícone da peça larga ("icone4-…"); se ainda não existir, o de 2 trilhos.
        Texture2D IconTex(string name)
        {
            if (builder.Faixas >= 4 && name.StartsWith("icone-"))
            {
                // Pista de 6: "icone6-…", e se faltar, o de 4.
                Texture2D t = builder.Faixas == 6 ? builder.theme.Icon("icone6-" + name.Substring(6)) : null;
                t = t != null ? t : builder.theme.Icon("icone4-" + name.Substring(6));
                if (t != null) return t;
            }
            return builder.theme.Icon(name);
        }

        readonly List<(Image img, string name)> paletteIcons = new List<(Image, string)>();
        Button kind2Btn, kind4Btn, kind6Btn;
        Label subtitle;
        int iconsFor;

        // A paleta e o título acompanham o tipo da pista (2 ou 4 carros).
        void RefreshKind()
        {
            int f = builder.Faixas;
            kind2Btn.EnableInClassList("btn--on", f == 2);
            kind4Btn.EnableInClassList("btn--on", f == 4);
            kind6Btn.EnableInClassList("btn--on", f == 6);
            subtitle.text = $"AUTORAMA · {f} TRILHOS";
            if (iconsFor == f) return;
            iconsFor = f;
            foreach (var (img, name) in paletteIcons) img.image = IconTex(name);
        }

        // Trocar o tipo adapta a pista aberta: a mesma forma, com a largura e os trilhos do tipo novo (Ctrl+Z desfaz).
        void ChooseKind(int faixas)
        {
            if (builder.Faixas == faixas) return;
            builder.AdaptTo(faixas);
            FrameTrack();
            Say(builder.pieces.Count > 1 ? $"Pista adaptada para {faixas} carros." : $"Pista de {faixas} carros.");
        }

        void BuildLayout()
        {
            // Réguas primeiro (ficam embaixo do painel), sem capturar o mouse.
            topRuler = El("ruler-top", root); topRuler.pickingMode = PickingMode.Ignore;
            leftRuler = El("ruler-left", root); leftRuler.pickingMode = PickingMode.Ignore;
            info = El("info", root); info.pickingMode = PickingMode.Ignore;
            cellLabel = Text("Célula —", "info-text", info);
            scaleBar = El("scale-bar", info);
            scaleText = Text("1 m", "scale-text", info);
            viewBadge = Text("Vista 3D · V volta para a de cima · botão direito gira", "view-badge", root);
            viewBadge.pickingMode = PickingMode.Ignore;

            side = El("side", root);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("side-scroll");
            side.Add(scroll);
            content = El("side-content", scroll.contentContainer);

            var titleRow = El("title-row", content);
            var titles = El("titles", titleRow);
            Text("CONSTRUTOR DE PISTA", "title", titles);
            subtitle = Text("AUTORAMA · 2 TRILHOS", "subtitle", titles);
            Btn("⌂", () => { if (OnlineRacing) Say("Termine o campeonato online (ou saia da sala) antes de voltar ao menu."); else ShowMenu(true); }, "btn icon-btn", titleRow).tooltip = "Tela inicial";
            Btn("◀", () => SetSide(false), "btn icon-btn", titleRow).tooltip = "Esconder o painel (Tab)";

            var tabs = El("tabs", content);
            tabBuild = Btn("CONSTRUTOR", () => SetMode(Mode.Build), "tab tab--on", tabs);
            tabRace = Btn("CORRIDA", () => SetMode(Mode.Race), "tab", tabs);
            tabChamp = Btn("CAMPEONATO", () => SetMode(Mode.Champ), "tab", tabs);
            tabOnline = Btn("ONLINE", () => SetMode(Mode.Online), "tab", tabs);

            statusBox = El("status", content);
            statusDot = El("status-dot", statusBox);
            statusText = Text("", "status-text", statusBox);
            closeBtn = Btn("Fechar circuito (Q)", AutoClose, "btn btn--accent close-btn", content);
            closeBtn.tooltip = "Completa a pista da ponta até a largada com o mínimo de peças, sem passar por cima da pista. Ctrl+Z desfaz.";

            closeBox = El("close-box", content);
            closeLabel = Text("", "meter-label", closeBox);
            closeFill = El("meter-fill", El("meter", closeBox));
            Btn("Cancelar (Esc)", () => builder.CancelAutoClose(), "btn", closeBox).tooltip = "Para a busca. A pista fica como estava.";
            closeBox.style.display = DisplayStyle.None;

            buildBox = El(null, content);
            raceBox = El(null, content);
            raceBox.style.display = DisplayStyle.None;
            champBox = El(null, content);
            champBox.style.display = DisplayStyle.None;
            onlineBox = El(null, content);
            onlineBox.style.display = DisplayStyle.None;

            // Tipo da pista: 2, 4 ou 6 carros. Trocar adapta a pista aberta, sem perder peças.
            var kind = Section("TAMANHO DA PISTA");
            var kindRow = El("segmented", kind);
            kind2Btn = Btn("2 trilhos", () => ChooseKind(2), "btn", kindRow);
            kind4Btn = Btn("4 trilhos", () => ChooseKind(4), "btn", kindRow);
            kind6Btn = Btn("6 trilhos", () => ChooseKind(6), "btn", kindRow);
            kind6Btn.tooltip = "Pista muito larga, de 6 trilhos (1 célula = o triplo da largura). Campeonatos não misturam os tipos.";
            kind2Btn.tooltip = "Pista estreita, de 2 trilhos. Adapta a pista aberta (Ctrl+Z desfaz).";
            kind4Btn.tooltip = "Pista larga, de 4 trilhos (1 célula = o dobro da largura). Campeonatos não misturam os dois tipos.";

            // Paleta
            var pal = Section("PEÇAS");
            hint = Text("", "hint", pal);
            var endRow = El("row", pal);
            endFrontBtn = Btn("Frente ▸", () => builder.SetBack(false), "btn", endRow);
            endBackBtn = Btn("◂ Trás", () => builder.SetBack(true), "btn", endRow);
            endFrontBtn.tooltip = "Encaixa no fim da pista, na seta amarela (E alterna)";
            endBackBtn.tooltip = "Encaixa atrás da largada (E alterna)";
            // Paleta em grupos: retas, curvas, inclinadas e especiais (cruzamentos e rampas).
            var groups = new Dictionary<string, VisualElement>();
            foreach (var g in new[] { "RETAS", "CURVAS", "CURVAS INCLINADAS", "CRUZAMENTOS E RAMPAS" })
            {
                Text(g, "pal-group", pal);
                groups[g] = El("palette", pal);
            }
            foreach (var e in TrackBuilder.Palette)
            {
                var grid = groups[PaletteGroup(e)];
                var entry = e;
                var tile = new Button(() => { builder.Add(entry); builder.ShowGhost(entry); });
                tile.AddToClassList("tile");
                var tileIcon = Icon(e.icon, "tile-icon");
                paletteIcons.Add((tileIcon, e.icon));
                tile.Add(tileIcon);
                var texts = El("tile-texts", tile); texts.pickingMode = PickingMode.Ignore;
                Text(e.label, "tile-name", texts).pickingMode = PickingMode.Ignore;
                Text(e.sub, "tile-sub", texts).pickingMode = PickingMode.Ignore;
                Text(KeyName(e.key), "key", tile).pickingMode = PickingMode.Ignore;
                tile.tooltip = $"{e.label} · {e.sub} (tecla {KeyName(e.key)})";
                tile.RegisterCallback<PointerEnterEvent>(_ => builder.ShowGhost(entry));
                tile.RegisterCallback<PointerLeaveEvent>(_ => builder.HideGhost());
                grid.Add(tile);
            }

            selectedBox = El(null, Section("PEÇA SELECIONADA"));
            summaryBox = El(null, Section("RESUMO"));

            var seq = Section("SEQUÊNCIA");
            Text("Da largada até a ponta, no sentido em que os carrinhos correm.", "hint", seq);
            seqBox = El("seq", seq);

            // Minhas pistas
            var lib = Section("MINHAS PISTAS");
            var nameRow = El("name-row", lib);
            nameField = new TextField { maxLength = 60 };
            nameField.AddToClassList("name-field");
            nameField.RegisterValueChangedCallback(ev => builder.TrackName = ev.newValue);
            nameField.RegisterCallback<KeyDownEvent>(ev =>
            {
                if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { SaveTrack(); nameField.Blur(); }
                if (ev.keyCode == KeyCode.Escape) nameField.Blur();
            }, TrickleDown.TrickleDown);
            nameRow.Add(nameField);
            Btn("Salvar", SaveTrack, "btn btn--primary", nameRow).style.marginBottom = 0;
            libList = El(null, Caixa(lib));
            var libRow = El("row", lib);
            Btn("Exemplo: oval", () => Open(Kind(TrackSave.Oval())), "btn", libRow);
            Btn("Exemplo: 8 com ponte", () => Open(Kind(TrackSave.Eight())), "btn", libRow);
            var newBtn = Btn("Nova pista", null, "btn btn--danger", libRow);
            newBtn.clicked += () => builder.NewTrack();     // do mesmo tipo da atual
            Text("Pasta: " + TrackLibrary.Folder, "small", lib);

            // Vista e edição
            var view = Section("VISTA E EDIÇÃO");
            var viewRow = El("row", view);
            Btn("Enquadrar (C)", FrameTrack, "btn", viewRow);
            viewBtn = Btn("Vista 3D (V)", () => cam.ToggleTilt(), "btn", viewRow);
            undoBtn = Btn("Desfazer", () => builder.Undo(), "btn", viewRow);
            redoBtn = Btn("Refazer", () => builder.Redo(), "btn", viewRow);

            var keys = Section("ATALHOS");
            Text("<b>1–0, T</b> peças · <b>X/B</b> cruzamento · <b>U/D</b> rampa\n" +
                 "<b>Clique</b> seleciona · <b>F</b> inverte a curva · <b>R</b> gira a largada · <b>E</b> ponta: frente/trás\n" +
                 "<b>Del</b> remove a selecionada · <b>⌫</b> remove a última · <b>Esc</b> solta\n" +
                 "<b>Q</b> fecha o circuito · <b>Ctrl+Z / Ctrl+Y</b> desfazer / refazer\n" +
                 "<b>C</b> enquadra · <b>V</b> vista 3D\n" +
                 "<b>Botão direito</b> move a vista (gira na 3D) · <b>Roda</b> zoom · <b>Tab</b> painel\n" +
                 "<b>Arrastar a largada</b> move a pista inteira", "small", keys);

            BuildTableTools();

            showTab = Btn("☰  Painel (Tab)", () => SetSide(true), "btn show-tab", root);
            showTab.style.display = DisplayStyle.None;

            BuildStartMenu();

            toast = Text("", "toast", root);
            toast.pickingMode = PickingMode.Ignore;
            toast.style.display = DisplayStyle.None;
        }

        // ---------- Atualização ----------

        void BuildStartMenu()
        {
            startMenu = El("start", root);
            MenuOpen = true;
            var card = El("start-card", startMenu);
            Text("AUTORAMA", "start-title", card);
            Text("Monte a pista, chame alguém e acelere.", "start-sub", card);
            var grid = El("start-grid", card);
            void Opt(string title, string sub, string icon, Mode m)
            {
                var b = new Button(() => { ShowMenu(false); SetMode(m); });
                b.AddToClassList("start-opt");
                Text(icon, "start-icon", b).pickingMode = PickingMode.Ignore;
                var t = El("start-texts", b); t.pickingMode = PickingMode.Ignore;
                Text(title, "start-opt-title", t).pickingMode = PickingMode.Ignore;
                Text(sub, "start-opt-sub", t).pickingMode = PickingMode.Ignore;
                grid.Add(b);
            }
            Opt("Construir", "Encaixe as peças e feche o circuito", "▦", Mode.Build);
            Opt("Correr", "Dois jogadores no mesmo teclado ou contra o robô", "▶", Mode.Race);
            Opt("Campeonato", "Pilotos, etapas, tabela e recordes", "★", Mode.Champ);
            Opt("Online", "Campeonato com amigos pela Steam", "◉", Mode.Online);
            var extra = El("start-extra", card);
            Btn("Opções", () => OptionsRequested?.Invoke(), "btn start-btn", extra);
            Btn("Sair", QuitGame, "btn start-btn", extra);
            Text("O botão ⌂ no painel volta para esta tela.", "start-foot", card);
        }

        // Pedido do botão Opções da tela inicial: quem tem as opções (a RaceController, com as teclas) abre a tela.
        public event System.Action OptionsRequested;

        // Sair: fecha o jogo (no Editor, para o Play mode).
        static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void ShowMenu(bool open)
        {
            if (startMenu == null) return;
            MenuOpen = open;
            startMenu.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) { builder.CancelAutoClose(); builder.HideGhost(); builder.SetHoverCell(null); hover = null; }
            root.focusController?.focusedElement?.Blur();
        }

        static string PaletteGroup(PaletteEntry e)
        {
            if (e.rise != 0 || e.def == PieceCatalog.Cross || e.def == PieceCatalog.CrossS) return "CRUZAMENTOS E RAMPAS";
            if (e.def == PieceCatalog.CurveB || e.def == PieceCatalog.HalfB) return "CURVAS INCLINADAS";
            if (e.def == PieceCatalog.Curve || e.def == PieceCatalog.Half) return "CURVAS";
            return "RETAS";
        }

        void BuildTableTools()
        {
            // Selo no topo da mesa: o mesmo status do painel, com o botão que resolve (fechar ou mostrar a sobreposta).
            tableWrap = El("table-status-wrap", root); tableWrap.pickingMode = PickingMode.Ignore;
            tableStatus = El("table-status", tableWrap);
            tableDot = El("status-dot", tableStatus);
            tableText = Text("", "table-status-text", tableStatus);
            tableClose = Btn("Fechar circuito (Q)", AutoClose, "btn btn--accent table-btn", tableStatus);
            tableShow = Btn("Mostrar", ShowOverlap, "btn table-btn", tableStatus);
            tableShow.tooltip = "Seleciona uma das peças sobrepostas";

            // Barra de ações, no canto de baixo à direita da mesa.
            actionBar = El("action-bar", root);
            Button B(string t, string tip, System.Action a) { var b = Btn(t, a, "btn bar-btn", actionBar); b.tooltip = tip; return b; }
            void Gap() => El("bar-gap", actionBar);
            barUndo = B("↶", "Desfazer (Ctrl+Z)", () => builder.Undo());
            barRedo = B("↷", "Refazer (Ctrl+Y)", () => builder.Redo());
            Gap();
            barFlip = B("⇄", "Inverter a curva selecionada (F)", () => builder.FlipSelected());
            B("⟳", "Girar a largada (R)", () => builder.RotateStart());
            barRemove = B("✕", "Remover a selecionada (Del) ou a última (⌫)", () => { if (builder.Selected >= 0) builder.RemoveSelected(); else builder.RemoveLast(); });
            Gap();
            B("⌖", "Enquadrar a pista (C)", FrameTrack);
            bar3D = B("3D", "Vista 3D (V)", () => cam.ToggleTilt());
            B("−", "Afastar", () => cam.ZoomStep(1.25f));
            B("+", "Aproximar", () => cam.ZoomStep(0.8f));
        }

        void ShowOverlap()
        {
            var o = builder.Info.overlaps;
            if (o.Count == 0) return;
            int first = int.MaxValue;
            foreach (int k in o) first = Mathf.Min(first, k);
            if (builder.Selected != first) builder.Select(first);
        }

        void RefreshTableTools(string txt, string cls)
        {
            var i = builder.Info;
            tableText.text = txt;
            tableStatus.RemoveFromClassList("status--ok"); tableStatus.RemoveFromClassList("status--warn");
            if (cls != "") tableStatus.AddToClassList(cls);
            tableClose.style.display = !i.closed && builder.pieces.Count > 1 && i.overlaps.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            tableShow.style.display = i.overlaps.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            barUndo.SetEnabled(builder.CanUndo);
            barRedo.SetEnabled(builder.CanRedo);
            bool sel = builder.Selected >= 0;
            barFlip.SetEnabled(sel);
            barRemove.SetEnabled(builder.pieces.Count > 1);
        }

        void Refresh()
        {
            if (root == null || builder.Info == null) return;
            var i = builder.Info;
            if (Locked) return;              // fora do construtor, o status e a barra lateral são da aba em uso

            // Status
            string txt; string cls;
            if (i.overlaps.Count > 0) { txt = (i.closed ? "Circuito fechado, mas há " : "Há ") + i.overlaps.Count + " peças sobrepostas"; cls = "status--warn"; }
            else if (i.closed) { txt = "Circuito fechado"; cls = "status--ok"; }
            else if (i.closedXY) { txt = $"A ponta chega na largada, mas no nível {i.endZ}. Desça até o chão"; cls = "status--warn"; }
            else if (builder.pieces.Count == 1) { txt = "Só a largada. Adicione peças"; cls = ""; }
            else { txt = $"Pista aberta · ponta a {Num(i.gap)} cél. da largada"; cls = ""; }
            statusBox.RemoveFromClassList("status--ok"); statusBox.RemoveFromClassList("status--warn");
            if (cls != "") statusBox.AddToClassList(cls);
            statusText.text = txt;
            RefreshTableTools(txt, cls);
            // O botão de fechar aparece só quando há o que fechar.
            closeBtn.style.display = !i.closed && builder.pieces.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;

            hint.text = builder.Back
                ? "Encaixando na <b>ponta de trás</b>, atrás da largada (seta amarela atrás dela). A largada não sai do lugar."
                : builder.Selected < 0
                ? "Clique para encaixar no <b>fim da pista</b>, onde está a seta amarela. Passe o mouse para ver a peça."
                : $"Encaixando <b>depois da peça {builder.Selected + 1}</b> ({builder.pieces[builder.Selected].Describe().ToLowerInvariant()}).";
            endFrontBtn.EnableInClassList("btn--on", !builder.Back);
            endBackBtn.EnableInClassList("btn--on", builder.Back);

            RefreshKind();
            DrawSelected();
            DrawSummary();
            DrawSequence();

            if (!IsTyping) nameField.SetValueWithoutNotify(builder.TrackName ?? "");
            undoBtn.SetEnabled(builder.CanUndo);
            redoBtn.SetEnabled(builder.CanRedo);
            DrawLibrary();
        }

        void DrawSelected()
        {
            selectedBox.Clear();
            int sel = builder.Selected;
            if (sel < 0)
            {
                Text("Nenhuma. Clique numa peça da mesa para inserir depois dela, trocar a altura ou remover.", "small", selectedBox);
                return;
            }
            var p = builder.pieces[sel];
            var seg = builder.Segments[sel];
            string height = seg.zIn == seg.zOut ? Level(seg.zIn) : $"{Level(seg.zIn)} → {Level(seg.zOut)}";
            var head = El("sel-head", selectedBox);
            head.Add(Icon(IconOf(p), "sel-icon"));
            var texts = El("sel-texts", head);
            Text($"<b>Peça {sel + 1} · {p.Def.Name}</b>", "body", texts);
            Text($"{Side(p)}{Num(p.Def.CenterLength)} cél. · {height}", "small", texts);

            var row = El("row", selectedBox);
            if (sel == 0)
            {
                Btn("Girar 90° (R)", builder.RotateStart, "btn", row);
                Btn("Soltar (Esc)", () => builder.Select(-1), "btn", row);
                Text("Arraste a largada na mesa para mover a pista inteira. Ela não pode ser removida.", "small", selectedBox);
                return;
            }
            if (p.Def.IsCurve) Btn("Inverter (F)", builder.FlipSelected, "btn", row);
            Btn("Remover (Del)", builder.RemoveSelected, "btn btn--danger", row);
            Btn("Soltar (Esc)", () => builder.Select(-1), "btn", row);

            var seg2 = El("segmented", selectedBox);
            Text("Altura", "seg-label", seg2);
            foreach (var (r, t) in new[] { (1, "Sobe"), (0, "Plana"), (-1, "Desce") })
            {
                int rise = r;
                Btn(t, () => builder.SetRise(rise), p.rise == rise ? "btn btn--on" : "btn", seg2);
            }
        }

        void DrawSummary()
        {
            summaryBox.Clear();
            var i = builder.Info;
            float cm = builder.cellCm * builder.Scale;     // na pista de 4, a célula tem o dobro da largura
            string M(float cells) => Num(cells * cm / 100f, 2) + " m";
            var L = i.laneLengths;
            int lo = 0, hi = 0;
            for (int l = 1; l < L.Length; l++) { if (L[l] < L[lo]) lo = l; if (L[l] > L[hi]) hi = l; }
            float diff = L[hi] - L[lo];
            Stat("Peças", builder.pieces.Count + (i.highCount > 0 ? $" · {i.highCount} no alto" : ""));
            Stat("Linha central", M(i.centerLength));
            for (int l = 0; l < L.Length; l++) Stat($"Trilho {l + 1}", M(L[l]), LaneSwatch(l));
            Stat("Mais longo", diff < 0.01f ? "iguais" : $"trilho {hi + 1} +{Num(diff * cm, 0)} cm" + (L.Length > 2 ? $" (sobre o {lo + 1})" : ""));
            Stat("Área ocupada", $"{Num(i.bounds.width * cm / 100f, 2)} × {Num(i.bounds.height * cm / 100f, 2)} m");
            bool fits = i.bounds.xMin >= -0.01f && i.bounds.yMin >= -0.01f && i.bounds.xMax <= builder.boardSize.x + 0.01f && i.bounds.yMax <= builder.boardSize.y + 0.01f;
            Stat("Na mesa", (fits ? "cabe" : "passa da borda") + $" · {builder.boardSize.x} × {builder.boardSize.y} cél.");
        }

        void Stat(string key, string value, string swatch = null)
        {
            var row = El("stats-row", summaryBox);
            var k = El("row", row);
            k.style.alignItems = Align.Center;
            if (swatch != null) El("swatch " + swatch, k);
            Text(key, "stats-key", k);
            Text(value, "stats-val", row);
        }

        void DrawSequence()
        {
            seqBox.Clear();
            for (int i = 0; i < builder.pieces.Count; i++)
            {
                int idx = i;
                var p = builder.pieces[i];
                var chip = new Button(() => builder.Select(idx));
                chip.AddToClassList("chip");
                if (i == builder.Selected) chip.AddToClassList("chip--on");
                if (builder.Info.overlaps.Contains(i)) chip.AddToClassList("chip--bad");
                chip.tooltip = $"{i + 1}. {p.Describe()}";
                chip.Add(Icon(IconOf(p), "chip-icon"));
                var s = builder.Segments[i];
                Text($"{i + 1}{(s.High ? "↑" : "")}", "chip-num", chip).pickingMode = PickingMode.Ignore;
                seqBox.Add(chip);
            }
        }

        void DrawLibrary()
        {
            libList.Clear();
            if (library.Count == 0) { Text("Nenhuma pista salva ainda. Dê um nome e salve.", "small", libList); return; }
            foreach (var e in library)
            {
                var entry = e;
                var item = El("lib-item", libList);
                if (e.save.nome == builder.TrackName) item.AddToClassList("lib-item--on");
                item.Add(new TrackThumb(e.save));
                var texts = El("lib-texts", item);
                Text(e.save.nome, "lib-name", texts);
                Text(TrackLine(e.save), "small", texts);
                Btn("Abrir", () => Open(entry.save), "btn", item);
                string key = "apagar:" + e.path;
                var del = Btn(IsArmed(key) ? "Certeza?" : "Apagar", null, "btn btn--danger", item);
                del.clicked += () =>
                {
                    if (!Confirm(key)) { del.text = "Certeza?"; return; }
                    TrackLibrary.Delete(entry.path);
                    RefreshLibrary();
                    Say($"“{entry.save.nome}” apagada.");
                };
            }
        }

        // ---------- Ações do painel ----------

        void SaveTrack()
        {
            if (string.IsNullOrWhiteSpace(builder.TrackName)) builder.TrackName = $"Pista {library.Count + 1}";
            TrackLibrary.Save(builder.ToSave());
            RefreshLibrary();
            Say($"“{builder.TrackName}” salva.");
        }

        // Os exemplos abrem no tipo da pista atual (a forma em células é a mesma).
        TrackSave Kind(TrackSave s) { s.faixas = builder.Faixas; return s; }

        void Open(TrackSave s)
        {
            builder.Load(s);
            FrameTrack();
            Say($"“{s.nome}” aberta.");
        }

        void RefreshLibrary()
        {
            library = TrackLibrary.List();
            if (root != null && builder.Info != null) DrawLibrary();
        }

        public void FrameTrack()
        {
            if (builder.Info == null) return;
            var r = builder.Info.bounds;        // em células; na pista de 4, o mundo é 2× maior
            float k = builder.Scale;
            cam.Frame(new Rect(builder.transform.position.x + r.x * k, builder.transform.position.z + r.y * k, r.width * k, r.height * k), SidePixels());
        }

        // Largura do painel em pixels de tela (0 se escondido).
        float SidePixels()
        {
            if (!sideVisible || root.layout.width <= 0) return 0f;
            return side.layout.width * Screen.width / root.layout.width;
        }

        public bool SideVisible => sideVisible;

        // A corrida recolhe o painel na largada e devolve na chegada (só se foi ela quem recolheu).
        public void SetSideFromRace(bool visible) { if (visible != sideVisible) SetSide(visible); }

        void SetSide(bool visible)
        {
            sideVisible = visible;
            side.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            showTab.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            root.focusController?.focusedElement?.Blur();
            root.schedule.Execute(FrameTrack).ExecuteLater(20);
        }

        public bool ConfirmTwice(string key) => Confirm(key);     // ações que apagam pedem um segundo clique

        void Say(string text)
        {
            toast.text = text;
            toast.style.display = DisplayStyle.Flex;
            toastUntil = Time.unscaledTime + 3.5f;
        }

        bool Confirm(string key)
        {
            if (armed == key && Time.unscaledTime < armedUntil) { armed = null; return true; }
            armed = key; armedUntil = Time.unscaledTime + 3f;
            return false;
        }
        bool IsArmed(string key) => armed == key && Time.unscaledTime < armedUntil;

        // ---------- Por quadro: atalhos do painel, célula sob o mouse, réguas ----------

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && builder.Closing) builder.CancelAutoClose();
            PumpClose();
            if (kb != null && !IsTyping)
            {
                if (kb.tabKey.wasPressedThisFrame) SetSide(!sideVisible);
                if (kb.cKey.wasPressedThisFrame && !kb.ctrlKey.isPressed) FrameTrack();
                if (kb.qKey.wasPressedThisFrame && !Locked) AutoClose();
            }
            if (toast.style.display == DisplayStyle.Flex && Time.unscaledTime > toastUntil) toast.style.display = DisplayStyle.None;

            // Célula sob o mouse (só sobre a mesa e fora do painel).
            Vector2Int? cell = null;
            var m = Mouse.current;
            if (m != null && !Locked && !PointerOverUI(m.position.ReadValue()))
            {
                var w = builder.WorldToCell(cam.MouseOnTable());
                var c = new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.z));
                if (c.x >= 0 && c.y >= 0 && c.x < builder.boardSize.x && c.y < builder.boardSize.y) cell = c;
            }
            if (cell != hover) { hover = cell; builder.SetHoverCell(cell); }
        }

        void LateUpdate()
        {
            if (root?.panel == null || builder.Info == null) return;
            bool top = cam.IsTopView;
            float left = sideVisible ? side.layout.width : 0f;
            topRuler.style.display = leftRuler.style.display = top ? DisplayStyle.Flex : DisplayStyle.None;
            viewBadge.style.display = cam.Tilted ? DisplayStyle.Flex : DisplayStyle.None;
            viewBtn.EnableInClassList("btn--on", cam.Tilted);
            info.style.left = left + 34f;
            // Na corrida o canto de baixo é dos controles dos jogadores.
            info.style.display = RaceMode ? DisplayStyle.None : DisplayStyle.Flex;
            // Selo e barra só no construtor; o selo fica centrado na mesa (à direita do painel).
            var buildOnly = Locked ? DisplayStyle.None : DisplayStyle.Flex;
            tableWrap.style.display = actionBar.style.display = buildOnly;
            tableWrap.style.left = left;
            bar3D.EnableInClassList("btn--on", cam.Tilted);
            cellLabel.text = hover.HasValue ? $"Célula {CellName(hover.Value)}" : "Célula —";
            if (!top) { scaleBar.style.display = scaleText.style.display = DisplayStyle.None; return; }
            scaleBar.style.display = scaleText.style.display = DisplayStyle.Flex;

            topRuler.style.left = left;
            leftRuler.style.left = left;
            var b = builder.boardSize;

            // Pixels de painel por célula (vista de cima: escala igual nos dois eixos).
            Vector2 p0 = ToPanel(builder.CellToWorld(new Vector3(0, 0, 0))), p1 = ToPanel(builder.CellToWorld(new Vector3(1, 0, 0)));
            float cellPx = Mathf.Abs(p1.x - p0.x);
            int step = 1;
            while (step * cellPx < 18f && step < 64) step *= 2;

            EnsureLabels(colLabels, topRuler, b.x);
            for (int c = 0; c < colLabels.Count; c++)
            {
                var l = colLabels[c];
                float x = ToPanel(builder.CellToWorld(new Vector3(c + 0.5f, 0, 0))).x - left;
                bool on = hover.HasValue && hover.Value.x == c;
                bool show = c < b.x && (c % step == 0 || on) && x > 30f && x < root.layout.width - left - 12f;
                l.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                l.text = (c + 1).ToString();
                l.style.left = x - 12f;
                l.EnableInClassList("ruler-label--on", on);
            }
            EnsureLabels(rowLabels, leftRuler, b.y);
            for (int r = 0; r < rowLabels.Count; r++)
            {
                // Linhas contadas de cima para baixo (A no topo), como no HTML.
                var l = rowLabels[r];
                float z = b.y - r - 0.5f;
                float y = ToPanel(builder.CellToWorld(new Vector3(0, 0, z))).y - 20f;
                bool on = hover.HasValue && b.y - 1 - hover.Value.y == r;
                bool show = r < b.y && (r % step == 0 || on) && y > 8f && y < root.layout.height - 28f;
                l.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                l.text = RowName(r);
                l.style.top = y - 8f;
                l.EnableInClassList("ruler-label--on", on);
            }

            // Barra de escala: a maior medida redonda que cabe em ~120 px.
            float cm = builder.cellCm * builder.Scale;     // a célula da pista de 4 tem o dobro da largura
            int[] opts = { 5, 10, 20, 25, 50, 100, 200, 500, 1000 };
            int best = opts[0];
            foreach (int v in opts) if (v / cm * cellPx <= 120f) best = v;
            scaleBar.style.width = best / cm * cellPx;
            scaleText.text = best >= 100 ? Num(best / 100f, best % 100 == 0 ? 0 : 1) + " m" : best + " cm";
        }

        void EnsureLabels(List<Label> list, VisualElement parent, int count)
        {
            while (list.Count < count)
            {
                var l = Text("", "ruler-label", parent);
                l.pickingMode = PickingMode.Ignore;
                list.Add(l);
            }
            for (int i = count; i < list.Count; i++) list[i].style.display = DisplayStyle.None;
        }

        Vector2 ToPanel(Vector3 world)
        {
            Vector3 s = cam.Cam.WorldToScreenPoint(world);
            return RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(s.x, Screen.height - s.y));
        }

        // ---------- Textos ----------

        string CellName(Vector2Int c) => RowName(builder.boardSize.y - 1 - c.y) + (c.x + 1);

        static string RowName(int r)
        {
            string s = "";
            r += 1;
            while (r > 0) { int m = (r - 1) % 26; s = (char)('A' + m) + s; r = (r - 1) / 26; }
            return s;
        }

        internal static string TrackLine(TrackSave s)
        {
            try
            {
                var o = s.Origin;
                var segs = TrackLayout.Build(o, s.Pieces(), s.Tail);
                var a = TrackLayout.Analyze(o, segs, s.Tail, s.Faixas);
                return $"{s.Faixas} trilhos · {segs.Count} peças · {Num(a.centerLength * s.escalaCm * TrackMath.Scale(s.Faixas) / 100f)} m · " + (a.closed ? "fechada" : "<color=#F07A3A>aberta</color>");
            }
            catch { return "não deu para ler"; }
        }

        internal static string LaneSwatch(int l) => "swatch--" + (char)('a' + l);

        static string IconOf(TrackPiece p)
        {
            var d = p.Def;
            if (d.IsStart) return "icone-base-largada";
            if (p.rise != 0 && d == PieceCatalog.Long) return p.rise > 0 ? "icone-rampa-sobe" : "icone-rampa-desce";
            if (!d.IsCurve) return "icone-" + d.Id;
            string side = p.dir < 0 ? "esquerda" : "direita";
            return d == PieceCatalog.Half ? "icone-meia-curva-" + side : $"icone-{d.Id}-{side}";
        }

        static string Side(TrackPiece p) => !p.Def.IsCurve ? "Reta · " : $"Vira à {(p.dir < 0 ? "esquerda" : "direita")} · {p.Def.Angle}° · ";
        static string KeyName(Key k) { string s = k.ToString(); return s.StartsWith("Digit") ? s.Substring(5) : s; }
        static string Level(int z) => z == 0 ? "chão" : "nível " + z;
        internal static string Num(float v, int dec = 1) => v.ToString("N" + dec, PtBr);
    }

    // Miniatura de uma pista salva: a linha central desenhada com Painter2D (trecho no alto em amarelo).
    public class TrackThumb : VisualElement
    {
        readonly List<Vector2> pts = new List<Vector2>();
        readonly List<bool> high = new List<bool>();
        Rect box;

        public TrackThumb(TrackSave s)
        {
            AddToClassList("thumb");
            pickingMode = PickingMode.Ignore;
            try
            {
                var segs = TrackLayout.Build(s.Origin, s.Pieces(), s.Tail);
                foreach (var sg in segs)
                {
                    int n = TrackMath.Steps(sg.def, 10f);
                    for (int k = pts.Count == 0 ? 0 : 1; k <= n; k++)
                    {
                        var q = TrackMath.PoseAt(sg.a, sg.def, sg.piece.dir, k / (float)n);
                        pts.Add(q.pos);
                        high.Add(sg.High);
                    }
                }
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
                box = Rect.MinMaxRect(x0 - 1f, y0 - 1f, x1 + 1f, y1 + 1f);
            }
            catch { pts.Clear(); }
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (pts.Count < 2) return;
            var r = contentRect;
            float k = Mathf.Min(r.width / box.width, r.height / box.height);
            float ox = r.x + (r.width - box.width * k) / 2f, oy = r.y + (r.height - box.height * k) / 2f;
            // z da mesa cresce para cima; na tela do painel, y cresce para baixo.
            Vector2 P(Vector2 p) => new Vector2(ox + (p.x - box.xMin) * k, oy + (box.yMax - p.y) * k);

            var g = ctx.painter2D;
            g.lineWidth = 2.5f;
            g.lineJoin = LineJoin.Round;
            foreach (bool layer in new[] { false, true })
            {
                g.strokeColor = layer ? new Color32(0xF5, 0xC2, 0x1B, 0xFF) : new Color32(0xB8, 0xC1, 0xBA, 0xFF);
                bool open = false;
                for (int i = 1; i < pts.Count; i++)
                {
                    if (high[i] != layer) { if (open) { g.Stroke(); open = false; } continue; }
                    if (!open) { g.BeginPath(); g.MoveTo(P(pts[i - 1])); open = true; }
                    g.LineTo(P(pts[i]));
                }
                if (open) g.Stroke();
            }
            g.fillColor = new Color32(0xF0, 0x4A, 0x3F, 0xFF);
            g.BeginPath();
            g.Arc(P(pts[0]), 2.5f, 0f, 360f);
            g.Fill();
        }
    }
}
