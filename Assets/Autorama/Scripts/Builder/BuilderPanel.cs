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

        public enum Mode { Build, Race, Champ, Online, Career }
        public Mode CurrentMode { get; private set; }
        public bool RaceMode => CurrentMode == Mode.Race || CurrentMode == Mode.Champ || CurrentMode == Mode.Career || (CurrentMode == Mode.Online && OnlineRacing);        // corrida ou campeonato: os carrinhos andam
        public bool OnlineRacing { get; private set; }            // campeonato online em andamento: a aba Online vira a tela de corrida
        public void SetOnlineRacing(bool on) { OnlineRacing = on; Refresh(); }
        public bool ChampMode => CurrentMode == Mode.Champ;
        public bool OnlineMode => CurrentMode == Mode.Online;
        public bool CareerMode => CurrentMode == Mode.Career;
        public bool Locked => CurrentMode != Mode.Build;          // fora do construtor a pista não se edita
        public static bool EditLocked => instance && (instance.Locked || instance.MenuOpen);
        // Tela inicial: aparece ao abrir o jogo; escolher um modo (ou um convite da Steam) fecha.
        public bool MenuOpen { get; private set; }
        VisualElement startMenu;
        public event System.Action<Mode> ModeChanged;
        public VisualElement RaceBox => raceBox;          // conteúdo da aba Corrida na barra lateral
        public VisualElement ChampBox => champBox;        // conteúdo da aba Campeonato
        public VisualElement OnlineBox => onlineBox;      // conteúdo da aba Online
        public VisualElement CareerBox => careerBox;      // conteúdo da aba Carreira (seção 68)
        public VisualElement Root => root;                // camada por cima da mesa (controles dos jogadores, luzes)
        public float SideWidth => sideVisible && side != null ? side.layout.width : 0f;

        public void SetMode(Mode m)
        {
            if (MenuOpen && !(OnlineRacing && m != Mode.Online)) ShowMenu(false);
            if (m == CurrentMode) return;
            if (OnlineRacing && m != Mode.Online) { Say("Termine o campeonato online (ou saia da sala) antes de trocar de aba."); return; }
            CurrentMode = m;
            panelTitle.text = m switch { Mode.Race => "CORRIDA", Mode.Champ => "CAMPEONATO", Mode.Online => "ONLINE", Mode.Career => "CARREIRA", _ => "CONSTRUTOR" };
            buildBox.style.display = m == Mode.Build ? DisplayStyle.Flex : DisplayStyle.None;
            raceBox.style.display = m == Mode.Race ? DisplayStyle.Flex : DisplayStyle.None;
            champBox.style.display = m == Mode.Champ ? DisplayStyle.Flex : DisplayStyle.None;
            onlineBox.style.display = m == Mode.Online ? DisplayStyle.Flex : DisplayStyle.None;
            careerBox.style.display = m == Mode.Career ? DisplayStyle.Flex : DisplayStyle.None;
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
        VisualElement buildBox, raceBox, champBox, onlineBox, careerBox;
        Label panelTitle;
        VisualElement root, side, content, showTab, topRuler, leftRuler, info, scaleBar;
        VisualElement statusBox, statusDot, selectedBox, summaryBox, seqBox, libList;
        Label statusText, hint, cellLabel, scaleText, toast, viewBadge;
        // Sobre a mesa, no construtor: o selo de status (com a ação que resolve) e a barra de ações.
        VisualElement tableWrap, tableStatus, tableDot, actionBar, palBar;
        Label tableText;
        Button tableClose, tableShow, tableRace, barUndo, barRedo, barFlip, barRemove, bar3D;
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
        int libRatings; float libRatingsAt;
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

        // Dica própria (proposta D): o UI Toolkit não desenha o tooltip no jogo. Parou o mouse 0,4 s em cima de algo
        // que tem tooltip (ou dentro dele), aparece um balão perto do ponteiro; mexeu para fora ou clicou, some.
        Label tip;
        VisualElement tipOwner;
        IVisualElementScheduledItem tipJob;
        Vector2 tipAt;
        void BuildTip()
        {
            tip = Text("", "tip", root);
            tip.pickingMode = PickingMode.Ignore;
            tip.style.display = DisplayStyle.None;
            root.RegisterCallback<PointerMoveEvent>(e =>
            {
                tipAt = e.localPosition;
                VisualElement t = e.target as VisualElement;
                while (t != null && string.IsNullOrEmpty(t.tooltip)) t = t.parent;
                if (t == tipOwner) return;
                tipOwner = t;
                tip.style.display = DisplayStyle.None;
                tipJob?.Pause();
                if (t != null) tipJob = root.schedule.Execute(ShowTip).StartingIn(400);
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(_ => { tipJob?.Pause(); tip.style.display = DisplayStyle.None; }, TrickleDown.TrickleDown);
        }

        void ShowTip()
        {
            if (tipOwner == null || tipOwner.panel == null || string.IsNullOrEmpty(tipOwner.tooltip) || tipOwner.resolvedStyle.display == DisplayStyle.None) return;
            tip.text = tipOwner.tooltip;
            tip.style.display = DisplayStyle.Flex;
            tip.BringToFront();
            float w = root.layout.width, h = root.layout.height;
            tip.style.left = Mathf.Min(tipAt.x + 14f, w - 340f);
            tip.style.top = tipAt.y + 22f > h - 60f ? tipAt.y - 44f : tipAt.y + 22f;
        }

        // "?" nas seções (proposta D): os parágrafos fixos de explicação (.hint com texto já na montagem) ficam
        // escondidos e um botão "?" no título mostra e esconde. Os .hint que nascem vazios são avisos que mudam
        // com o jogo (ex.: "feche o circuito") e continuam sempre à mostra.
        internal static void FoldHints(VisualElement root)
        {
            root.Query(className: "section").ForEach(sec =>
            {
                var title = sec.Q<Label>(className: "h2");
                if (title == null || title.parent != sec || sec.Q(className: "sec-head") != null) return;
                var fixos = new List<VisualElement>();
                foreach (var c in sec.Children())
                    if (c is Label l && l.ClassListContains("hint") && !string.IsNullOrEmpty(l.text)) fixos.Add(c);
                if (fixos.Count == 0) return;
                var head = El("sec-head");
                sec.Insert(sec.IndexOf(title), head);
                head.Add(title);
                bool open = false;
                Button q = null;
                q = Btn("?", () =>
                {
                    open = !open;
                    foreach (var f in fixos) f.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                    q.EnableInClassList("sec-q--on", open);
                }, "btn sec-q", head);
                q.tooltip = "Como funciona";
                foreach (var f in fixos) f.style.display = DisplayStyle.None;
            });
        }

        // Botão com ícone desenhado (UiIcon) e texto; sem texto, só o ícone.
        internal static Button IconBtn(string text, string icon, System.Action onClick, string cls = "btn", VisualElement parent = null)
        {
            var b = Btn("", onClick, cls + " btn--icon", parent);
            b.Add(new UiIcon(icon, "btn-ico"));
            if (!string.IsNullOrEmpty(text)) Text(text, "btn-label", b).pickingMode = PickingMode.Ignore;
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
                Texture2D t = builder.Faixas >= 6 ? builder.theme.Icon($"icone{builder.Faixas}-" + name.Substring(6)) : null;
                t = t != null ? t : builder.theme.Icon("icone4-" + name.Substring(6));
                if (t != null) return t;
            }
            return builder.theme.Icon(name);
        }

        readonly List<(Image img, string name)> paletteIcons = new List<(Image, string)>();
        readonly List<(Button tile, PaletteEntry e)> paletteTiles = new List<(Button, PaletteEntry)>();

        // Construtor da carreira (seção 72): apaga as peças que a prova não deixa usar.
        public void RefreshAllowed()
        {
            foreach (var (tile, e) in paletteTiles)
            {
                bool ok = TrackBuilder.AllowedEntry == null || TrackBuilder.AllowedEntry(e);
                tile.SetEnabled(ok);
                tile.tooltip = ok ? $"{e.label} · {e.sub} (tecla {KeyName(e.key)})" : (TrackBuilder.LockTip?.Invoke(e) ?? "Não vale aqui.");
            }
        }
        Button kind2Btn, kind4Btn, kind6Btn, kind8Btn;
        Label subtitle;
        int iconsFor;

        // A paleta e o título acompanham o tipo da pista (2 ou 4 carros).
        void RefreshKind()
        {
            int f = builder.Faixas;
            kind2Btn.EnableInClassList("btn--on", f == 2);
            kind4Btn.EnableInClassList("btn--on", f == 4);
            kind6Btn.EnableInClassList("btn--on", f == 6);
            kind8Btn.EnableInClassList("btn--on", f == 8);
            subtitle.text = $"PISTA DE {f} TRILHOS";
            if (iconsFor == f) return;
            iconsFor = f;
            foreach (var (img, name) in paletteIcons) img.image = IconTex(name);
        }

        // Trocar o tipo adapta a pista aberta: a mesma forma, com a largura e os trilhos do tipo novo (Ctrl+Z desfaz).
        void ChooseKind(int faixas)
        {
            if (builder.Faixas == faixas) return;
            if (!Progresso.FaixasOk(faixas)) { Say($"Pista de {faixas} trilhos: abre na carreira."); return; }
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
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("side-scroll");
            side.Add(scroll);
            content = El("side-content", scroll.contentContainer);

            var titleRow = El("title-row", content);
            var titles = El("titles", titleRow);
            panelTitle = Text("CONSTRUTOR", "title", titles);
            subtitle = Text("PISTA DE 2 TRILHOS", "subtitle", titles);
            IconBtn(null, "inicio", () => { if (OnlineRacing) Say("Termine o campeonato online (ou saia da sala) antes de voltar ao menu."); else ShowMenu(true); }, "btn icon-btn", titleRow).tooltip = "Tela inicial";
            IconBtn(null, "esconder", () => SetSide(false), "btn icon-btn", titleRow).tooltip = "Esconder o painel (Tab)";


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
            careerBox = El(null, content);
            careerBox.style.display = DisplayStyle.None;

            // Tipo da pista: 2, 4, 6 ou 8 carros. Trocar adapta a pista aberta, sem perder peças.
            var kind = Section("TAMANHO DA PISTA");
            var kindRow = El("segmented", kind);
            kind2Btn = Btn("2 trilhos", () => ChooseKind(2), "btn", kindRow);
            kind4Btn = Btn("4 trilhos", () => ChooseKind(4), "btn", kindRow);
            kind6Btn = Btn("6 trilhos", () => ChooseKind(6), "btn", kindRow);
            kind6Btn.tooltip = "Pista muito larga, de 6 trilhos (1 célula = o triplo da largura). Campeonatos não misturam os tipos.";
            kind8Btn = Btn("8 trilhos", () => ChooseKind(8), "btn", kindRow);
            kind8Btn.tooltip = "Pista enorme, de 8 trilhos (1 célula = 4× a largura). Campeonatos não misturam os tipos.";
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
            // Paleta embaixo da mesa (proposta E): uma barra com os grupos em abas e as peças do grupo aberto.
            // As teclas 1–0 continuam valendo para todas as peças, com qualquer aba aberta.
            palBar = El("pal-bar", root);
            var palTabs = El("pal-tabs", palBar);
            var palTiles = El("pal-tiles", palBar);
            var groups = new Dictionary<string, VisualElement>();
            var groupTabs = new List<(Button tab, VisualElement grid)>();
            foreach (var g in new[] { "RETAS", "CURVAS", "CURVAS INCLINADAS", "CRUZAMENTOS E RAMPAS", "LOOP E SALTO" })
            {
                var grid = groups[g] = El("palette", palTiles);
                Button tab = null;
                tab = Btn(g, () =>
                {
                    foreach (var (t, gr) in groupTabs) { t.EnableInClassList("pal-tab--on", t == tab); gr.style.display = t == tab ? DisplayStyle.Flex : DisplayStyle.None; }
                }, "pal-tab", palTabs);
                groupTabs.Add((tab, grid));
                bool first = groupTabs.Count == 1;
                tab.EnableInClassList("pal-tab--on", first);
                grid.style.display = first ? DisplayStyle.Flex : DisplayStyle.None;
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
                paletteTiles.Add((tile, entry));
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

            var keys = new Foldout { text = "ATALHOS", value = false };     // recolhido: a lista completa fica em Opções
            keys.AddToClassList("section"); keys.AddToClassList("fold");
            buildBox.Add(keys);
            Text("<b>1–0, T</b> peças · <b>X/B</b> cruzamento · <b>U/D</b> rampa\n" +
                 "<b>Clique</b> seleciona · <b>F</b> inverte a curva · <b>R</b> gira a largada · <b>E</b> ponta: frente/trás\n" +
                 "<b>Del</b> remove a selecionada · <b>⌫</b> remove a última · <b>Esc</b> solta\n" +
                 "<b>Q</b> fecha o circuito · <b>Ctrl+Z / Ctrl+Y</b> desfazer / refazer\n" +
                 "<b>C</b> enquadra · <b>V</b> vista 3D\n" +
                 "<b>Botão direito</b> move a vista (gira na 3D) · <b>Roda</b> zoom · <b>Tab</b> painel\n" +
                 "<b>Arrastar a largada</b> move a pista inteira", "small", keys);

            BuildTableTools();

            showTab = IconBtn("Painel (Tab)", "painel", () => SetSide(true), "btn show-tab", root);
            showTab.style.display = DisplayStyle.None;

            BuildStartMenu();
            ApplyChrome();
            BuildTip();
            FoldHints(buildBox);

            toast = Text("", "toast", root);
            toast.pickingMode = PickingMode.Ignore;
            toast.style.display = DisplayStyle.None;
        }

        // ---------- Atualização ----------

        void BuildStartMenu()
        {
            // Tela cheia (proposta B): o nome à esquerda, os modos à direita e, embaixo, Peças · Opções · Sair.
            startMenu = El("start", root);
            MenuOpen = true;
            var card = El("start-card", startMenu);
            var hero = El("start-hero", card);
            Text("AUTORAMA", "start-title", hero);
            Text("MANIA", "start-title start-title--2", hero);
            Text("DEFINITIVE EDITION 1", "start-edition", hero);
            El("start-flag", hero);
            Text("Monte a pista, chame alguém e acelere.", "start-sub", hero);
            var grid = El("start-grid", card);
            void Opt(string title, string sub, string icon, Mode m, string extra = null)
            {
                var b = new Button(() => { ShowMenu(false); SetMode(m); });
                b.AddToClassList("start-opt");
                if (extra != null) b.AddToClassList(extra);
                b.Add(new UiIcon(icon, "start-icon"));
                var t = El("start-texts", b); t.pickingMode = PickingMode.Ignore;
                Text(title, "start-opt-title", t).pickingMode = PickingMode.Ignore;
                Text(sub, "start-opt-sub", t).pickingMode = PickingMode.Ignore;
                grid.Add(b);
            }
            Opt("Carreira", "Da garagem ao campeonato nacional", "carreira", Mode.Career, "start-opt--big");
            Opt("Corrida", "Contra amigos no mesmo PC ou contra o robô", "correr", Mode.Race);
            Opt("Campeonato", "Pilotos, etapas, tabela e recordes", "campeonato", Mode.Champ);
            Opt("Online", "Partida rápida ou sala com amigos", "online", Mode.Online);
            Opt("Construtor", "Encaixe as peças e feche o circuito", "construir", Mode.Build);
            var extra = El("start-extra", startMenu);
            IconBtn("Peças", "pecas", () => ShopRequested?.Invoke(), "btn start-btn", extra);
            IconBtn("Opções", "opcoes", () => OptionsRequested?.Invoke(), "btn start-btn", extra);
            IconBtn("Sair", "sair", QuitGame, "btn start-btn", extra);
        }

        // Pedido do botão Opções da tela inicial: quem tem as opções (a RaceController, com as teclas) abre a tela.
        public event System.Action OptionsRequested;
        // Peças (seção 78): o RaceController abre a tela com o que a carreira já abriu.
        public event System.Action ShopRequested;
        // Testar a pista no construtor (seção 61): o RaceController liga e desliga o carro IA de teste.
        public event System.Action TestRequested;
        public event System.Action RaceHereRequested;
        Button barTest;
        public void SetTestOn(bool on)
        {
            barTest.Q<UiIcon>().Set(on ? "parar" : "correr");
            barTest.tooltip = on ? "Parar o teste" : "Testar a pista com um carro IA (anda mesmo com a pista aberta)";
            barTest.EnableInClassList("btn--accent", on);
        }

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
            if (side != null) { ApplyChrome(); root.schedule.Execute(FrameTrack).ExecuteLater(20); }
            if (open) { builder.CancelAutoClose(); builder.HideGhost(); builder.SetHoverCell(null); hover = null; }
            root.focusController?.focusedElement?.Blur();
        }

        static string PaletteGroup(PaletteEntry e)
        {
            if (e.def.GroundOnly) return "LOOP E SALTO";
            if (e.rise != 0 || e.def == PieceCatalog.Cross || e.def == PieceCatalog.CrossS) return "CRUZAMENTOS E RAMPAS";
            if (e.def == PieceCatalog.CurveB || e.def == PieceCatalog.HalfB) return "CURVAS INCLINADAS";
            if (e.def == PieceCatalog.Curve || e.def == PieceCatalog.Half || e.def == PieceCatalog.Hairpin) return "CURVAS";
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
            tableRace = IconBtn("Correr nesta pista", "correr", () => RaceHereRequested?.Invoke(), "btn btn--accent table-btn", tableStatus);

            // Barra de ações, no canto de baixo à direita da mesa.
            actionBar = El("action-bar", root);
            Button B(string icon, string tip, System.Action a) { var b = IconBtn(null, icon, a, "btn bar-btn", actionBar); b.tooltip = tip; return b; }
            void Gap() => El("bar-gap", actionBar);
            barTest = B("correr", "", () => TestRequested?.Invoke());
            SetTestOn(false);
            Gap();
            barUndo = B("desfazer", "Desfazer (Ctrl+Z)", () => builder.Undo());
            barRedo = B("refazer", "Refazer (Ctrl+Y)", () => builder.Redo());
            Gap();
            barFlip = B("inverter", "Inverter a curva selecionada (F)", () => builder.FlipSelected());
            B("girar", "Girar a largada (R)", () => builder.RotateStart());
            barRemove = B("apagar", "Remover a selecionada (Del) ou a última (⌫)", () => { if (builder.Selected >= 0) builder.RemoveSelected(); else builder.RemoveLast(); });
            Gap();
            B("enquadrar", "Enquadrar a pista (C)", FrameTrack);
            bar3D = Btn("3D", () => cam.ToggleTilt(), "btn bar-btn", actionBar); bar3D.tooltip = "Vista 3D (V)";
            B("menos", "Afastar", () => cam.ZoomStep(1.25f));
            B("mais", "Aproximar", () => cam.ZoomStep(0.8f));
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
            tableRace.style.display = i.closed && i.overlaps.Count == 0 && !chromeHidden && TrackBuilder.AllowedEntry == null ? DisplayStyle.Flex : DisplayStyle.None;
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
                var lv = El("segmented", selectedBox);
                Text("Altura", "seg-label", lv);
                if (builder.StartZ > 0) Btn("Descer", () => builder.MoveStartLevel(-1), "btn", lv);
                if (builder.StartZ < TrackMath.MaxLevel) Btn("Subir", () => builder.MoveStartLevel(1), "btn", lv);
                Text("Arraste a largada na mesa para mover a pista inteira. Ela não pode ser removida.", "small", selectedBox);
                return;
            }
            if (p.Def.Sided) Btn("Inverter (F)", builder.FlipSelected, "btn", row);
            Btn("Remover (Del)", builder.RemoveSelected, "btn btn--danger", row);
            Btn("Soltar (Esc)", () => builder.Select(-1), "btn", row);
            if (p.Def.IsJump)
            {
                var gv = El("segmented", selectedBox);
                Text("Vão", "seg-label", gv);
                for (int v = 1; v <= PieceCatalog.MaxGap; v++)
                {
                    int vao = v;
                    Btn($"{v} célula{(v > 1 ? "s" : "")}", () => builder.SetGap(vao), p.vao == vao ? "btn btn--on" : "btn", gv);
                }
            }
            if (p.Def.IsHairpin)
            {
                var rv = El("segmented", selectedBox);
                Text("Tamanho", "seg-label", rv);
                for (int v = 1; v <= PieceCatalog.MaxHairpin; v++)
                {
                    int raio = v;
                    Btn($"{v} fileira{(v > 1 ? "s" : "")}", () => builder.SetHairpin(raio), p.raio == raio ? "btn btn--on" : "btn", rv);
                }
            }
            if (p.Def.GroundOnly) return;      // loop e salto não sobem nem descem: sem botões de altura

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
            if (string.IsNullOrWhiteSpace(builder.Author)) builder.Author = LocalAuthor;      // a pista é de quem a salvou primeiro
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
            if (!sideVisible || side.style.display == DisplayStyle.None || root.layout.width <= 0) return 0f;
            return side.layout.width * Screen.width / root.layout.width;
        }

        public bool SideVisible => sideVisible;

        // A corrida recolhe o painel na largada e devolve na chegada (só se foi ela quem recolheu).
        public void SetSideFromRace(bool visible) { if (visible != sideVisible) SetSide(visible); }

        // Carreira (seção 69): telas próprias; o painel e o botão de mostrá-lo somem. Ao sair, voltam como estavam.
        bool chromeHidden;
        public void HideChrome(bool hide)
        {
            chromeHidden = hide;
            ApplyChrome();
            root.schedule.Execute(FrameTrack).ExecuteLater(20);
        }

        // Com a carreira (HideChrome) ou o menu inicial na tela, o painel e o botão de mostrá-lo somem; o estado volta depois.
        void ApplyChrome()
        {
            bool off = chromeHidden || MenuOpen;
            side.style.display = sideVisible && !off ? DisplayStyle.Flex : DisplayStyle.None;
            showTab.style.display = !sideVisible && !off ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void SetSide(bool visible)
        {
            sideVisible = visible;
            ApplyChrome();
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
                if (kb.tabKey.wasPressedThisFrame && !chromeHidden) SetSide(!sideVisible);
                if (kb.cKey.wasPressedThisFrame && !kb.ctrlKey.isPressed) FrameTrack();
                if (kb.qKey.wasPressedThisFrame && !Locked) AutoClose();
            }
            if (toast.style.display == DisplayStyle.Flex && Time.unscaledTime > toastUntil) toast.style.display = DisplayStyle.None;
            // Notas das pistas chegando da Steam (seção 83): redesenha a biblioteca, no máximo uma vez por segundo.
            if (libRatings != TrackRatings.Version && Time.unscaledTime - libRatingsAt > 1f && libList != null && builder.Info != null)
            { libRatings = TrackRatings.Version; libRatingsAt = Time.unscaledTime; DrawLibrary(); }

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
            bool top = cam.IsTopView && !Locked && !MenuOpen;     // réguas e escala só no construtor
            float left = sideVisible ? side.layout.width : 0f;
            topRuler.style.display = leftRuler.style.display = top && !chromeHidden ? DisplayStyle.Flex : DisplayStyle.None;
            viewBadge.style.display = cam.Tilted && !chromeHidden ? DisplayStyle.Flex : DisplayStyle.None;
            viewBtn.EnableInClassList("btn--on", cam.Tilted);
            info.style.left = left + 34f;
            // Na corrida o canto de baixo é dos controles dos jogadores.
            info.style.display = Locked || MenuOpen ? DisplayStyle.None : DisplayStyle.Flex;
            // Selo e barra só no construtor; o selo fica centrado na mesa (à direita do painel).
            var buildOnly = Locked || MenuOpen ? DisplayStyle.None : DisplayStyle.Flex;
            tableWrap.style.display = actionBar.style.display = palBar.style.display = buildOnly;
            palBar.style.left = left + 34f;
            info.style.bottom = palBar.layout.height + 18f;
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
                var segs = TrackLayout.Build(o, s.Pieces(), s.Tail, s.StartZ);
                var a = TrackLayout.Analyze(o, segs, s.Tail, s.Faixas);
                string by = string.IsNullOrWhiteSpace(s.autor) ? "" : $" · de {s.autor.Replace("<", "<​")}";
                return $"{s.Faixas} trilhos · {segs.Count} peças · {Num(a.centerLength * s.escalaCm * TrackMath.Scale(s.Faixas) / 100f)} m · " + (a.closed ? "fechada" : "<color=#F07A3A>aberta</color>") + by + TrackRatings.Line(s);
            }
            catch { return "não deu para ler"; }
        }

        // O nome de quem está jogando aqui: o da Steam, ou o que foi digitado no online (seção 83).
        internal static string LocalAuthor
        {
            get
            {
                string n = SteamBoot.Ready ? SteamBoot.PersonaName : PlayerPrefs.GetString("autorama.online.nome", "");
                n = (n ?? "").Trim();
                return n.Length > 20 ? n.Substring(0, 20) : n;
            }
        }

        internal static string LaneSwatch(int l) => "swatch--" + (char)('a' + l);

        static string IconOf(TrackPiece p)
        {
            var d = p.Def;
            if (d.IsStart) return "icone-base-largada";
            if (p.rise != 0 && d == PieceCatalog.Long) return p.rise > 0 ? "icone-rampa-sobe" : "icone-rampa-desce";
            if (!d.Sided) return "icone-" + d.Id;
            string side = p.dir < 0 ? "esquerda" : "direita";
            return d == PieceCatalog.Half ? "icone-meia-curva-" + side : $"icone-{d.Id}-{side}";
        }

        static string Side(TrackPiece p) => p.Def.IsLoop ? $"Sai à {(p.dir < 0 ? "esquerda" : "direita")} · " : p.Def.IsJump ? "Salto · " : !p.Def.IsCurve ? "Reta · " : $"Vira à {(p.dir < 0 ? "esquerda" : "direita")} · {p.Def.Angle}° · ";
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
                var segs = TrackLayout.Build(s.Origin, s.Pieces(), s.Tail, s.StartZ);
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
