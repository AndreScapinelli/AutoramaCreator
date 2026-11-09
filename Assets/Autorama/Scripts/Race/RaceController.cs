using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // A aba Corrida: escolhe a pista, roda a física (RaceSim), desenha os carrinhos e monta os controles.
    // Cada jogador tem dois botões: Acelerar (segure: o acelerador sobe de 0 a 100%; soltou, zera) e Recolocar.
    public partial class RaceController : MonoBehaviour
    {
        public TrackBuilder builder;
        public BuilderPanel panel;
        public TopDownCamera cam;
        public Material[] carMaterials = new Material[2];   // carrinho vermelho e azul (URP/Lit com recorte)
        public Material sparkMaterial;

        static readonly Key[] GasKeys = { Key.A, Key.L }, ReslotKeys = { Key.S, Key.K };
        static readonly string[] LaneNames = { "Trilho 1", "Trilho 2" };
        // Modos de câmera da corrida, na ordem de TopDownCamera.RaceViewMode.
        static readonly string[] CamModes = { "De cima", "3D", "Atrás do carro", "Dinâmica" };
        static readonly string[] CamModeHints = { "A vista livre da mesa", "Inclinada, gira com o botão direito", "Segue um carrinho por trás", "Acompanha os carrinhos sozinha" };
        static readonly string[] CamTargets = { "Jogador 1", "Jogador 2" };

        RaceSettings set;
        RaceSim sim;
        readonly Transform[] cars = new Transform[2];
        ParticleSystem sparks;
        TrackSave builderStash;              // a pista do construtor, guardada enquanto a corrida usa outra
        readonly bool[] uiHeld = new bool[2];
        bool scoreDirty = true, champDirty;
        readonly Material[] carMats = new Material[2];      // cópia por carrinho: no campeonato ganha a cor do piloto
        RaceSim.Phase lastPhase;

        class PadUI { public VisualElement root, gas, fill, speed, slip; public Label name, state, gasText; public Button reslot; public string flash; public bool flashRec; public float flashT; }
        readonly PadUI[] pads = new PadUI[2];
        VisualElement pickList, scoreBox, padsBox, topBox, lights, banner;
        Label raceHint, clock, gripHint, bannerTitle, bannerSub;
        Button startBtn;
        // Menu da câmera: botão no canto superior direito da mesa que abre a lista de modos.
        VisualElement camMenu, camPop, camTargetBox;
        Button camBtn;
        readonly Button[] camModeBtns = new Button[4], camTargetBtns = new Button[2];
        Label camTargetNote;
        SliderInt cameraDistanceSlider;
        readonly List<VisualElement> lightDots = new List<VisualElement>();
        // Placar sobre a mesa: uma ficha por carrinho (posição, nome, volta, melhor volta).
        class HudCard { public VisualElement root, dot; public Label pos, name, lap, best; }
        readonly HudCard[] hud = new HudCard[2];
        VisualElement hudBox;
        bool sideAutoHidden;                 // o painel foi recolhido pela largada (devolve na chegada)

        // ---------- Início ----------

        void Start()
        {
            set = RaceSettings.Load();
            sim = new RaceSim(set);
            sim.Spark += p => { if (sparks) sparks.Emit(new ParticleSystem.EmitParams { position = p, applyShapeToPosition = true }, 18); };
            sim.ScoreChanged += () => scoreDirty = true;
            sim.LapDone += OnLapDone;
            CreateCars();
            CreateSparks();
            BuildUI();
            BuildChampUI();
            BuildOnlineUI();
            builder.Changed += OnTrackChanged;
            panel.ModeChanged += OnMode;
            OnTrackChanged();
        }

        void OnDestroy()
        {
            if (builder) builder.Changed -= OnTrackChanged;
            if (panel) panel.ModeChanged -= OnMode;
            OnlineDestroy();
        }

        void CreateCars()
        {
            for (int k = 0; k < 2; k++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = k == 0 ? "Carrinho vermelho" : "Carrinho azul";
                Destroy(go.GetComponent<Collider>());
                carMats[k] = new Material(carMaterials[k]) { name = carMaterials[k].name + " (cópia)" };
                go.GetComponent<MeshRenderer>().sharedMaterial = carMats[k];
                // A imagem cobre 0,64 × 0,384 célula; a carroceria (0,60 × 0,32) é a mesma caixa da colisão.
                go.transform.localScale = new Vector3(0.64f, 0.384f, 1f);
                go.transform.SetParent(transform, false);
                cars[k] = go.transform;
            }
        }

        void CreateSparks()
        {
            var go = new GameObject("Faíscas");
            go.transform.SetParent(transform, false);
            sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = 0.3f;
            main.startSpeed = 2.2f;
            main.startSize = 0.07f;
            main.startColor = new Color(1f, 0.8f, 0.2f);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = sparks.emission; emission.enabled = false;
            var shape = sparks.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.05f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMaterial;
            sparks.Play();
        }

        // ---------- Pista e modo ----------

        void OnTrackChanged()
        {
            if (builder.Segments.Count == 0 || builder.Info == null) return;
            sim.SetTrack(builder.Segments, builder.Info.closed);
            scoreDirty = true;
            PlaceCars();
        }

        void OnMode(BuilderPanel.Mode m)
        {
            bool race = m != BuilderPanel.Mode.Build;
            // Saindo do construtor, guarda a pista dele; voltando, reencontra.
            if (race && builderStash == null) builderStash = builder.ToSave();
            builder.RaceMode = race;
            for (int k = 0; k < 2; k++) uiHeld[k] = false;
            if (m == BuilderPanel.Mode.Race)
            {
                sim.set = set; sim.cutoff = null;
                SetBot();
                ApplyCameraSettings();
                UseTrack(set.track);
            }
            else if (m == BuilderPanel.Mode.Champ)
            {
                ApplyCameraSettings();
                UseChampTrack();
            }
            else
            {
                sim.set = set; sim.cutoff = null; sim.aiSkill[0] = sim.aiSkill[1] = 0;
                cam.SetRaceViewMode(TopDownCamera.RaceViewMode.Atual);
                if (builderStash != null) builder.ShowTrack(builderStash);
                if (m == BuilderPanel.Mode.Build) builderStash = null;     // no lobby online a pista do construtor fica só de fundo
                panel.FrameTrack();
            }
            if (m == BuilderPanel.Mode.Online) OnlineShown();
            sim.Reset();
            topBox.style.display = panel.RaceMode ? DisplayStyle.Flex : DisplayStyle.None;
            DrawPadHeads();
            scoreDirty = champDirty = true;
        }

        struct Option { public string key, name, sub; public TrackSave save; public bool closed; }

        List<Option> Options()
        {
            var list = new List<Option>
            {
                new Option { key = "builder", name = "Pista do construtor", sub = builderStash != null && builderStash.nome != "" ? $"“{builderStash.nome}”" : "a que está aberta agora", save = builderStash ?? builder.ToSave() },
                new Option { key = "ex:oval", name = "Exemplo: oval", save = TrackSave.Oval() },
                new Option { key = "ex:oito", name = "Exemplo: 8 com ponte", save = TrackSave.Eight() },
            };
            foreach (var e in TrackLibrary.List()) list.Add(new Option { key = e.path, name = e.save.nome, save = e.save });
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                try { var segs = TrackLayout.Build(o.save.Origin, o.save.Pieces(), o.save.Tail); o.closed = TrackLayout.Analyze(o.save.Origin, segs, o.save.Tail).closed; }
                catch { o.closed = false; }
                list[i] = o;
            }
            return list;
        }

        // Troca a pista da corrida sem mexer na que está no construtor.
        void UseTrack(string key)
        {
            var opts = Options();
            var o = opts.Find(x => x.key == key);
            if (o.save == null) o = opts[0];
            set.track = o.key;
            set.Save();
            builder.ShowTrack(o.save);
            panel.FrameTrack();
            DrawPicker(opts);
        }

        // Vale na hora, até no meio da prova. Só na aba Corrida: no campeonato quem manda são os pilotos da bateria.
        void SetBot()
        {
            if (panel.CurrentMode != BuilderPanel.Mode.Race) return;
            set.botSkill = Mathf.Clamp(set.botSkill, 1, 100);
            sim.aiSkill[0] = 0;
            sim.aiSkill[1] = set.bot ? set.botSkill : 0;
            if (!set.bot) sim.cars[1].held = false;     // desligou: solta o acelerador que a IA segurava
            DrawPadHeads();
        }

        void StartRace()
        {
            if (!sim.closed) return;
            sim.StartRace();
            scoreDirty = true;
        }

        // ---------- Quadro a quadro ----------

        void Update()
        {
            OnlineTick();
            if (OnlineRun) { OnlineRunUpdate(); return; }
            if (sim == null || !sim.Ready) return;
            if (!panel.RaceMode) { PlaceCars(); return; }       // no construtor, os carrinhos ficam no grid

            var kb = Keyboard.current;
            bool keys = kb != null && !IsTyping;
            for (int k = 0; k < 2; k++)
            {
                if (sim.aiSkill[k] > 0) continue;              // o piloto IA segura e solta sozinho
                sim.cars[k].held = uiHeld[k] || (keys && kb[GasKeys[k]].isPressed);
                if (keys && kb[ReslotKeys[k]].wasPressedThisFrame) sim.Reslot(k);
            }
            if (keys && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { if (panel.ChampMode) ChampEnter(); else StartRace(); }

            sim.Step(Mathf.Min(Time.deltaTime, 0.05f));
            PlaceCars();
            UpdatePads();
            UpdateTop();
            if (sim.phase != lastPhase) { lastPhase = sim.phase; scoreDirty = true; champDirty = true; SideForPhase(); }
            if (scoreDirty) { scoreDirty = false; DrawScore(); DrawStatus(); }
            if (champDirty && panel.ChampMode) { champDirty = false; DrawChampSide(); }
            (panel.ChampMode ? cClock : clock).text = Fmt(sim.phase == RaceSim.Phase.Idle || sim.phase == RaceSim.Phase.Countdown ? 0f : sim.t);
        }

        void PlaceCars()
        {
            Vector3 p0 = Vector3.zero, p1 = Vector3.zero;
            float h0 = 0f, h1 = 0f;
            for (int k = 0; k < 2; k++)
            {
                var p = sim.CarPose(k, out float y, out float pitch);
                var pos = new Vector3(p.pos.x, y + 0.035f, p.pos.y);
                cars[k].position = pos;
                // Deitado, frente para +X; inclina na rampa e gira para o rumo.
                cars[k].rotation = Quaternion.Euler(0f, -p.heading, 0f) * Quaternion.Euler(0f, 0f, pitch) * Quaternion.Euler(90f, 0f, 0f);
                if (k == 0) { p0 = pos; h0 = p.heading; }
                else { p1 = pos; h1 = p.heading; }
            }
            if (panel != null && panel.RaceMode)
            {
                // No online o seu trilho muda de uma bateria para outra: a câmera acompanha o seu carrinho.
                int focus = CamFocusCar();
                if (OnlineRun && focus >= 0) cam.SetThirdPersonCar(focus);
                cam.SetRaceTargets(p0, h0, p1, h1, focus);
            }
            if (camMenu != null)
            {
                bool show = panel != null && panel.RaceMode;
                camMenu.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show && camPop.style.display == DisplayStyle.Flex) ShowCamPop(false);
            }
        }

        // ---------- Interface ----------

        void BuildUI()
        {
            var box = panel.RaceBox;

            var pick = Section(box, "PISTA");
            Text("Escolha onde correr. Trocar aqui não mexe na pista que está no construtor.", "hint", pick);
            pickList = El(null, pick);

            var race = Section(box, "CORRIDA");
            raceHint = Text("", "hint", race);
            var row = El("row", race);
            startBtn = Btn("Largar (Enter)", StartRace, "btn btn--primary", row);
            Btn("Voltar ao grid", () => { sim.Reset(); scoreDirty = true; }, "btn", row);
            // Corrida contra o computador: o jogador 2 vira um piloto IA (a mesma IA do campeonato), no trilho 2.
            var bot = new Toggle("Jogador 2 é um robô") { value = set.bot };
            bot.AddToClassList("setting"); bot.AddToClassList("ai-toggle");
            race.Add(bot);
            var botBox = El(null, race);
            var botSkill = new SliderInt(1, 100) { value = Mathf.Clamp(set.botSkill, 1, 100) };
            botSkill.AddToClassList("setting");
            botBox.Add(botSkill);
            var botHint = Text("", "small", botBox);
            botHint.style.marginBottom = 8;
            void BotLabels()
            {
                botSkill.label = $"Qualidade do robô: {set.botSkill}";
                botHint.text = Champ.SkillWord(set.botSkill);
                botBox.style.display = set.bot ? DisplayStyle.Flex : DisplayStyle.None;
            }
            bot.RegisterValueChangedCallback(e => { set.bot = e.newValue; set.Save(); BotLabels(); SetBot(); });
            botSkill.RegisterValueChangedCallback(e => { set.botSkill = e.newValue; set.Save(); BotLabels(); SetBot(); });
            BotLabels();

            var clockRow = El("row", race);
            Text("Tempo de prova", "stats-key", clockRow).style.marginRight = 8;
            clock = Text("0,00 s", "clock", clockRow);
            scoreBox = El("score", race);

            var how = Section(box, "COMO PILOTAR");
            Text("<b>Segure</b> Acelerar e o acelerador sobe de 0 a 100%. <b>Soltou, zerou</b>: o carrinho freia.\n" +
                 "Rápido demais na curva, a traseira escorrega e a barra de <b>derrapagem</b> enche. Cheia, o carrinho sai da pista.\n" +
                 "<b>Recolocar</b> devolve o carrinho ao trilho, um pouco antes de onde ele saiu.\n\n" +
                  "<b>A · S</b> jogador 1: acelerar · recolocar\n<b>L · K</b> jogador 2: acelerar · recolocar\n<b>Enter</b> largar · câmera no menu do canto direito", "small", how);

            var adj = Section(box, "AJUSTES");
            var laps = new SliderInt(1, 20) { value = set.laps };
            var vmax = new Slider(8f, 20f) { value = set.vmax };
            var ramp = new Slider(0.2f, 1.5f) { value = set.ramp };
            var grip = new Slider(15f, 45f) { value = set.grip };
            var col = new Toggle("Colisão entre carrinhos") { value = set.collide };
            void Labels()
            {
                laps.label = $"Voltas: {set.laps}";
                vmax.label = $"Velocidade máx.: {Num(set.vmax)} cél/s";
                ramp.label = $"Tempo até 100%: {Num(set.ramp, 2)} s";
                grip.label = $"Aderência: {Mathf.RoundToInt(set.grip)}";
                float Lim(float r, float m) => Mathf.Min(100f, Mathf.Round(sim.CornerLimit(r, m) / set.vmax * 100f));
                gripHint.text = $"Nas curvas planas, o carrinho segura até {Lim(2.25f, 1f)}% da velocidade máxima no trilho de dentro e {Lim(2.75f, 1f)}% no de fora. " +
                                $"Nas inclinadas, até {Lim(2.25f, PieceCatalog.BankGrip)}% e {Lim(2.75f, PieceCatalog.BankGrip)}%.";
            }
            foreach (var s in new VisualElement[] { laps, vmax, ramp, grip, col }) { s.AddToClassList("setting"); adj.Add(s); }
            gripHint = Text("", "small", adj);
            laps.RegisterValueChangedCallback(e => { set.laps = e.newValue; set.Save(); Labels(); scoreDirty = true; });
            vmax.RegisterValueChangedCallback(e => { set.vmax = e.newValue; set.Save(); Labels(); });
            ramp.RegisterValueChangedCallback(e => { set.ramp = e.newValue; set.Save(); Labels(); });
            grip.RegisterValueChangedCallback(e => { set.grip = e.newValue; set.Save(); Labels(); });
            col.RegisterValueChangedCallback(e => { set.collide = e.newValue; set.Save(); });
            Labels();

            // Sobre a mesa: luzes e aviso de vitória no topo; os dois jogadores embaixo.
            var root = panel.Root;
            topBox = El("race-top", root); topBox.pickingMode = PickingMode.Ignore;
            hudBox = El("hud", topBox); hudBox.pickingMode = PickingMode.Ignore;
            for (int k = 0; k < 2; k++)
            {
                var h = new HudCard { root = El("hud-card " + (k == 0 ? "hud-card--a" : "hud-card--b"), hudBox) };
                h.pos = Text("1º", "hud-pos", h.root);
                h.dot = El("hud-dot", h.root);
                var info = El("hud-info", h.root);
                h.name = Text("", "hud-name", info);
                var hudRow = El("hud-row", info);
                h.lap = Text("", "hud-lap", hudRow);
                h.best = Text("", "hud-best", hudRow);
                foreach (var e in h.root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
                hud[k] = h;
            }
            lights = El("lights", topBox);
            for (int i = 0; i < 3; i++) lightDots.Add(El("light", lights));
            banner = El("banner", topBox);
            bannerTitle = Text("", "banner-title", banner);
            bannerSub = Text("", "banner-sub", banner);
            foreach (var e in new[] { lights, banner }) e.pickingMode = PickingMode.Ignore;

            BuildCamMenu(root);

            padsBox = El("pads", root);
            for (int k = 0; k < 2; k++) pads[k] = BuildPad(k);
            padsBox.style.display = DisplayStyle.None;
            topBox.style.display = DisplayStyle.None;
            DrawPicker(Options());
        }

        void BuildCamMenu(VisualElement root)
        {
            camMenu = El("cam-menu", root);
            camBtn = Btn("", () => ShowCamPop(camPop.style.display == DisplayStyle.None), "btn cam-btn", camMenu);
            camPop = El("cam-pop", camMenu);
            Text("CÂMERA", "cam-title", camPop);
            for (int i = 0; i < CamModes.Length; i++)
            {
                int m = i;
                var b = Btn("", () => { set.cameraMode = m; set.Save(); ApplyCameraSettings(); }, "btn cam-opt", camPop);
                b.text = $"<b>{CamModes[i]}</b>\n<size=10>{CamModeHints[i]}</size>";
                camModeBtns[i] = b;
            }
            camTargetBox = El("cam-sub", camPop);
            Text("SEGUIR", "cam-title", camTargetBox);
            var seg = El("segmented", camTargetBox);
            for (int k = 0; k < 2; k++)
            {
                int t = k;
                camTargetBtns[k] = Btn(CamTargets[k], () => { set.cameraTarget = t; set.Save(); ApplyCameraSettings(); }, "btn", seg);
            }
            camTargetNote = Text("No online, segue o seu carrinho.", "small", camTargetBox);
            cameraDistanceSlider = new SliderInt(70, 160) { value = Mathf.Clamp(set.cameraDistance, 70, 160) };
            cameraDistanceSlider.AddToClassList("setting");
            camPop.Add(cameraDistanceSlider);
            cameraDistanceSlider.RegisterValueChangedCallback(e => { set.cameraDistance = e.newValue; set.Save(); ApplyCameraSettings(); });
            ShowCamPop(false);
            camMenu.style.display = DisplayStyle.None;
            ApplyCameraSettings();
        }

        void ShowCamPop(bool open)
        {
            camPop.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            camBtn.EnableInClassList("cam-btn--open", open);
        }

        // Carrinho que a câmera acompanha: no online é sempre o seu (seja trilho 1 ou 2);
        // contra o robô, o jogador 1; nos outros casos -1 (a dinâmica enquadra os dois).
        int CamFocusCar()
        {
            if (OnlineRun) return MyLane();
            if (panel != null && panel.CurrentMode == BuilderPanel.Mode.Race && set.bot) return 0;
            return -1;
        }

        void ApplyCameraSettings()
        {
            int mode = Mathf.Clamp(set.cameraMode, 0, CamModes.Length - 1);
            int target = Mathf.Clamp(set.cameraTarget, 0, CamTargets.Length - 1);
            set.cameraDistance = Mathf.Clamp(set.cameraDistance, 70, 160);
            set.cameraMode = mode;
            set.cameraTarget = target;

            int appliedMode = mode;
            if (panel != null && !panel.RaceMode) appliedMode = (int)TopDownCamera.RaceViewMode.Atual;

            cam.SetRaceViewMode((TopDownCamera.RaceViewMode)appliedMode);
            int focus = CamFocusCar();
            cam.SetThirdPersonCar(OnlineRun && focus >= 0 ? focus : target);
            cam.SetDistanceScale(set.cameraDistance / 100f);

            if (camBtn == null) return;
            camBtn.text = $"Câmera: <b>{CamModes[mode]}</b>  ▾";
            for (int i = 0; i < camModeBtns.Length; i++) camModeBtns[i].EnableInClassList("cam-opt--on", i == mode);
            camTargetBox.style.display = mode == (int)TopDownCamera.RaceViewMode.ThirdPerson ? DisplayStyle.Flex : DisplayStyle.None;
            for (int k = 0; k < 2; k++)
            {
                camTargetBtns[k].EnableInClassList("btn--on", k == target);
                camTargetBtns[k].style.display = OnlineRun ? DisplayStyle.None : DisplayStyle.Flex;
            }
            camTargetNote.style.display = OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
            cameraDistanceSlider.SetValueWithoutNotify(set.cameraDistance);
            cameraDistanceSlider.label = $"Distância: {set.cameraDistance}%";
        }

        static VisualElement Section(VisualElement parent, string title)
        {
            var s = El("section", parent);
            Text(title, "h2", s);
            return s;
        }

        PadUI BuildPad(int k)
        {
            var p = new PadUI();
            p.root = El("pad " + (k == 0 ? "pad--a" : "pad--b"), padsBox);
            var head = El("pad-head", p.root);
            p.name = Text($"JOGADOR {k + 1}", "pad-name", head);
            p.state = Text("No grid", "pad-state", head);
            var body = El("pad-body", p.root);

            // Acelerar: segurar com o mouse ou o dedo (ou a tecla) mantém o acelerador subindo.
            var gas = p.gas = El("gas", body);
            p.fill = El("gas-fill", gas); p.fill.pickingMode = PickingMode.Ignore;
            p.gasText = Text($"ACELERAR  {GasKeys[k]}", "gas-text", gas);
            p.gasText.pickingMode = PickingMode.Ignore;
            int idx = k;
            gas.RegisterCallback<PointerDownEvent>(e => { uiHeld[idx] = true; gas.CapturePointer(e.pointerId); e.StopPropagation(); });
            gas.RegisterCallback<PointerUpEvent>(e => { uiHeld[idx] = false; gas.ReleasePointer(e.pointerId); });
            gas.RegisterCallback<PointerCaptureOutEvent>(_ => uiHeld[idx] = false);

            var side = El("pad-side", body);
            Text("Velocidade", "meter-label", side);
            p.speed = El("meter-fill", El("meter", side));
            Text("Derrapagem", "meter-label", side);
            p.slip = El("meter-fill", El("meter", side));
            p.reslot = Btn($"Recolocar  {ReslotKeys[k]}", () => ReslotPressed(idx), "btn reslot", side);
            return p;
        }

        void DrawPicker(List<Option> opts)
        {
            pickList.Clear();
            foreach (var o in opts)
            {
                var opt = o;
                var item = new Button(() => UseTrack(opt.key));
                item.AddToClassList("lib-item");
                item.AddToClassList("pick");
                if (o.key == set.track) item.AddToClassList("lib-item--on");
                item.SetEnabled(o.closed);
                item.Add(new TrackThumb(o.save));
                var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                Text(o.name, "lib-name", texts).pickingMode = PickingMode.Ignore;
                Text((o.sub != null ? o.sub + " · " : "") + TrackLine(o.save), "small", texts).pickingMode = PickingMode.Ignore;
                pickList.Add(item);
            }
        }

        void DrawScore()
        {
            if (ChampLike) { DrawChampScore(); return; }
            scoreBox.Clear();
            void Row(string[] cells, string cls)
            {
                var r = El("score-row " + cls, scoreBox);
                for (int i = 0; i < cells.Length; i++) Text(cells[i], i == 0 ? "score-cell score-cell--name" : "score-cell", r);
            }
            Row(new[] { "Trilho", "Voltas", "Última", "Melhor", "Saídas", "Toques" }, "score-head");
            for (int k = 0; k < 2; k++)
            {
                var c = sim.cars[k];
                Row(new[] { $"{k + 1}{(sim.winner == k ? " · 1º" : "")}", $"{c.laps}/{set.laps}", Fmt(c.lastLap), Fmt(c.best), c.offs.ToString(), c.hits.ToString() },
                    (k == 0 ? "score-a" : "score-b") + (sim.winner == k ? " score-win" : ""));
            }
            raceHint.text = !sim.closed ? "Esta pista não é um circuito fechado. Escolha outra acima ou feche o traçado no Construtor."
                : sim.phase == RaceSim.Phase.Idle ? "Os dois carrinhos estão no grid. Aperte Largar e espere as luzes."
                : sim.phase == RaceSim.Phase.Countdown ? "Pode segurar o acelerador: o carrinho sai quando a luz ficar verde."
                : sim.phase == RaceSim.Phase.Running ? $"Vence quem completar {set.laps} {(set.laps == 1 ? "volta" : "voltas")} primeiro."
                : "Prova encerrada. Aperte Largar para correr de novo.";
            startBtn.text = sim.phase == RaceSim.Phase.Idle ? "Largar (Enter)" : "Nova largada (Enter)";
            startBtn.SetEnabled(sim.closed);
        }

        void DrawStatus()
        {
            if (!panel.RaceMode) return;
            if (ChampLike) { ChampStatus(); return; }
            if (!sim.closed) panel.SetStatus("Feche o circuito no Construtor para correr", "status--warn");
            else if (sim.winner >= 0) panel.SetStatus($"{LaneNames[sim.winner]} venceu", "status--ok");
            else if (sim.phase == RaceSim.Phase.Countdown) panel.SetStatus("Atenção à largada", "");
            else if (sim.phase == RaceSim.Phase.Running) panel.SetStatus($"Corrida · volta {Mathf.Min(Mathf.Max(sim.cars[0].laps, sim.cars[1].laps) + 1, set.laps)} de {set.laps}", "");
            else panel.SetStatus("Pronto para largar", "");
        }

        void UpdatePads()
        {
            padsBox.style.left = panel.SideWidth + 12f;
            for (int k = 0; k < 2; k++)
            {
                var c = sim.cars[k];
                var p = pads[k];
                p.fill.style.width = Length.Percent(c.T * 100f);
                p.speed.style.width = Length.Percent(Mathf.Min(100f, c.v / sim.set.vmax * 100f));
                p.slip.style.width = Length.Percent(Mathf.Min(100f, c.slip * 100f));
                p.slip.style.backgroundColor = c.slip < 0.45f ? new Color(0.24f, 0.75f, 0.45f) : c.slip < 0.75f ? new Color(0.96f, 0.76f, 0.1f) : new Color(0.94f, 0.48f, 0.23f);
                bool off = c.mode == CarMode.Off, ai = sim.aiSkill[k] > 0;
                p.reslot.SetEnabled(off && !ai);                   // a IA se recoloca sozinha
                p.reslot.EnableInClassList("reslot--ready", off && !ai);
                p.root.EnableInClassList("pad--held", c.held);
                if (p.flashT > 0f) p.flashT -= Time.deltaTime;
                bool flash = p.flashT > 0f && !off;
                p.state.EnableInClassList("pad-state--rec", flash && p.flashRec);
                p.state.EnableInClassList("pad-state--pb", flash && !p.flashRec);
                int L = sim.set.laps;
                p.state.text = flash ? p.flash
                    : c.finish != null ? (sim.winner == k ? "Venceu!" : "Chegou")
                    : sim.phase == RaceSim.Phase.Done ? "Não terminou"
                    : off ? "Fora da pista!"
                    : c.mode == CarMode.Returning ? "Voltando ao trilho"
                    : c.hitFx > 0f ? "Batida!"
                    : sim.phase == RaceSim.Phase.Countdown ? "Aguarde a luz verde"
                    : sim.phase == RaceSim.Phase.Running ? $"Volta {Mathf.Min(c.laps + 1, L)} de {L}"
                    : "No grid";
                p.state.EnableInClassList("pad-state--alert", off);
            }
        }

        // Largada: recolhe o painel lateral para a mesa ficar inteira. Chegada (ou volta ao grid): devolve.
        // Se o jogador abriu o painel no meio da prova (Tab), ele fica aberto.
        void SideForPhase()
        {
            bool racing = sim.phase == RaceSim.Phase.Countdown || sim.phase == RaceSim.Phase.Running;
            if (racing && panel.SideVisible) { sideAutoHidden = true; panel.SetSideFromRace(false); }
            else if (!racing && sideAutoHidden) { sideAutoHidden = false; panel.SetSideFromRace(true); }
        }

        // Quem está na frente: quem chegou antes; senão mais voltas; senão mais adiante na volta.
        int Leader()
        {
            var a = sim.cars[0]; var b = sim.cars[1];
            if (a.finish != null || b.finish != null)
                return b.finish == null ? 0 : a.finish == null ? 1 : a.finish <= b.finish ? 0 : 1;
            if (a.laps != b.laps) return a.laps > b.laps ? 0 : 1;
            return a.s >= b.s ? 0 : 1;
        }

        void UpdateHud()
        {
            bool show = sim.closed && sim.phase != RaceSim.Phase.Idle;
            hudBox.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            int L = sim.set.laps, lead = Leader();
            for (int k = 0; k < 2; k++)
            {
                var c = sim.cars[k]; var h = hud[k];
                string nm = pads[k].name.text;
                int cut = nm.IndexOf(" · ");
                h.name.text = cut > 0 ? nm.Substring(0, cut) : nm;
                h.dot.style.backgroundColor = pads[k].root.resolvedStyle.borderTopColor;
                h.pos.text = sim.winner == k ? "🏁" : (k == lead ? "1º" : "2º");
                h.root.EnableInClassList("hud-card--lead", k == lead);
                h.lap.text = c.finish != null ? "Chegou" : $"Volta {Mathf.Clamp(c.laps + 1, 1, L)}/{L}";
                h.best.text = c.best != null ? $"Melhor {Fmt(c.best)}" : "";
            }
        }

        void UpdateTop()
        {
            topBox.style.left = panel.SideWidth;
            UpdateHud();
            bool showLights = sim.phase == RaceSim.Phase.Countdown || (sim.phase == RaceSim.Phase.Running && sim.t < 1f);
            lights.style.display = showLights ? DisplayStyle.Flex : DisplayStyle.None;
            if (showLights)
            {
                int n = sim.phase == RaceSim.Phase.Countdown ? Mathf.Min(3, Mathf.FloorToInt(sim.t / (RaceSim.Count / 3f)) + 1) : 0;
                for (int i = 0; i < 3; i++)
                {
                    lightDots[i].EnableInClassList("light--red", sim.phase == RaceSim.Phase.Countdown && i < n);
                    lightDots[i].EnableInClassList("light--green", sim.phase == RaceSim.Phase.Running);
                }
            }
            if (ChampLike)
            {
                bool champShow = ChampBanner(out string title, out string sub) && !showLights;
                banner.style.display = champShow ? DisplayStyle.Flex : DisplayStyle.None;
                if (champShow) { bannerTitle.text = title; bannerSub.text = sub; bannerTitle.EnableInClassList("banner-title--a", false); bannerTitle.EnableInClassList("banner-title--b", false); }
                return;
            }
            bool showBanner = !showLights && (sim.winner >= 0 || !sim.closed);
            banner.style.display = showBanner ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showBanner) return;
            if (!sim.closed) { bannerTitle.text = "Pista aberta"; bannerSub.text = "Escolha um circuito fechado na lista."; bannerTitle.EnableInClassList("banner-title--a", false); bannerTitle.EnableInClassList("banner-title--b", false); return; }
            var w = sim.cars[sim.winner];
            bannerTitle.text = $"{LaneNames[sim.winner]} venceu";
            bannerTitle.EnableInClassList("banner-title--a", sim.winner == 0);
            bannerTitle.EnableInClassList("banner-title--b", sim.winner == 1);
            bannerSub.text = $"{Fmt(w.finish)} · melhor volta {Fmt(w.best)}";
        }

        internal static string Fmt(float? s)
        {
            if (s == null) return "—";
            float v = s.Value;
            if (v < 60f) return Num(v, 2) + " s";
            int m = Mathf.FloorToInt(v / 60f);
            return m + ":" + Num(v - m * 60f, 2).PadLeft(5, '0');
        }
    }
}
