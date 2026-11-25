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
        public Material[] carMaterials = new Material[2];   // carrinho vermelho e azul (URP/Lit com recorte); o verde e o amarelo vêm do tema
        public Material sparkMaterial;

        // Teclas de cada trilho: os humanos ganham os conjuntos na ordem dos trilhos (o 1º humano são as teclas do
        // jogador 1, o 2º as do jogador 2…). Elas são configuráveis (Controles, botão "Teclas dos jogadores").
        // Com um humano só (os outros são IA), ele usa sempre o conjunto 1, em qualquer trilho: no campeonato o rodízio
        // troca o trilho a cada bateria. O mesmo vale para os controles.
        int KeySet(int k)
        {
            if (sim == null) return k;
            int n = 0;
            for (int i = 0; i < k && i < sim.N; i++) if (sim.present[i] && sim.aiSkill[i] <= 0) n++;
            return n;
        }
        // Teclado: só há 4 conjuntos de teclas. O 5º e o 6º humano (pista de 6) jogam só de controle: Key.None.
        static Key GasKeyAt(int ks) => Controles.Get(ks, Controles.Acao.Acelerar);
        static Key HalfKeyAt(int ks) => Controles.Get(ks, Controles.Acao.Meia);
        static Key ReslotKeyAt(int ks) => Controles.Get(ks, Controles.Acao.Recolocar);
        static Key TurboKeyAt(int ks) => Controles.Get(ks, Controles.Acao.Turbo);
        Key GasKey(int k) => GasKeyAt(KeySet(k));
        Key HalfKey(int k) => HalfKeyAt(KeySet(k));
        bool KeyPressed(Keyboard kb, Key key) => key != Key.None && kb[key].isPressed;
        bool KeyWasPressed(Keyboard kb, Key key) => key != Key.None && kb[key].wasPressedThisFrame;
        // Online: o seu carrinho aceita as teclas de qualquer conjunto (quem está no teclado é o dono delas).
        bool AnySetPressed(Keyboard kb, Controles.Acao a)
        {
            for (int j = 0; j < Controles.Jogadores; j++) if (KeyPressed(kb, Controles.Get(j, a))) return true;
            return false;
        }
        bool AnySetWasPressed(Keyboard kb, Controles.Acao a)
        {
            for (int j = 0; j < Controles.Jogadores; j++) if (KeyWasPressed(kb, Controles.Get(j, a))) return true;
            return false;
        }

        // Controle (gamepad): o 1º conectado é do jogador 1 (A·S) e o 2º do jogador 2 (L·K); segue o KeySet,
        // então com um humano só o 1º controle pilota em qualquer trilho. Gatilho direito = acelerador analógico,
        // A/✕ = recolocar, Start = largar.
        const float TriggerDead = 0.06f;
        static Gamepad PadOf(int set) => set < Gamepad.all.Count ? Gamepad.all[set] : null;
        static float Trigger(Gamepad g)
        {
            if (g == null) return 0f;
            float v = g.rightTrigger.ReadValue();
            return v <= TriggerDead ? 0f : Mathf.Clamp01((v - TriggerDead) / (1f - TriggerDead));
        }

        // Nível pedido pelo jogador do trilho k neste quadro (0 = solto): o maior entre teclado, meia, controle e o botão da tela.
        float GasLevel(int k, bool keys)
        {
            var kb = Keyboard.current;
            int ks = KeySet(k);
            float g = uiHeld[k] ? 1f : 0f;
            if (keys && kb != null)
            {
                if (KeyPressed(kb, GasKeyAt(ks))) g = 1f;
                else if (KeyPressed(kb, HalfKeyAt(ks))) g = Mathf.Max(g, HalfOf(k) / 100f);
                HalfKeys(k, kb, Controles.Get(ks, Controles.Acao.MeiaMenos), Controles.Get(ks, Controles.Acao.MeiaMais));
            }
            HalfPad(k, PadOf(ks));
            return Mathf.Max(g, Trigger(PadOf(ks)));
        }

        // Até onde a meia aceleração vai, por trilho (seção 66): começa no ajuste "Meia aceleração" (set.halfGas) e o
        // jogador muda na hora, arrastando o marcador na barra ACELERAR ou com meia − / meia + (← · → no jogador 1).
        public const int HalfMin = 10, HalfMax = 95, HalfStep = 5;
        readonly int[] halfPct = new int[RaceSim.MaxCars];
        int HalfOf(int k) => halfPct[k] > 0 ? halfPct[k] : set.halfGas;
        void SetHalf(int k, int v)
        {
            halfPct[k] = Mathf.Clamp(v, HalfMin, HalfMax);
            if (pads != null && k < pads.Length && pads[k] != null) { pads[k].half.style.left = Length.Percent(halfPct[k]); pads[k].gasText.text = GasLabel(k); pads[k].halfTri.text = HalfTri(k); }
        }
        // O ▲ do marcador com as teclas de meia − e meia + do jogador dos lados (← ▲ →).
        string HalfTri(int k)
        {
            int ks = OnlineRun ? 0 : KeySet(k);
            Key a = Controles.Get(ks, Controles.Acao.MeiaMenos), b = Controles.Get(ks, Controles.Acao.MeiaMais);
            return a == Key.None && b == Key.None ? "▲" : $"{KeyText(a)}  ▲  {KeyText(b)}";
        }
        void HalfKeys(int k, Keyboard kb, Key less, Key more)
        {
            if (KeyWasPressed(kb, less)) SetHalf(k, HalfOf(k) - HalfStep);
            if (KeyWasPressed(kb, more)) SetHalf(k, HalfOf(k) + HalfStep);
        }
        // No controle, ◀ ▶ do direcional fazem meia − e meia + (o teclado pode não ler S e a seta juntos).
        void HalfPad(int k, Gamepad g)
        {
            if (g == null) return;
            if (g.dpad.left.wasPressedThisFrame) SetHalf(k, HalfOf(k) - HalfStep);
            if (g.dpad.right.wasPressedThisFrame) SetHalf(k, HalfOf(k) + HalfStep);
        }
        // Na prova, as setas são de meia −/+: nenhum controle da tela fica com o foco, senão a navegação do UI Toolkit
        // também usa as setas (o slider "Meia aceleração" mudava junto e desfazia a seta). Campo de texto (chat) fica.
        void HalfFocusGuard()
        {
            if (sim == null || !sim.Ready || !(OnlineRun || panel.RaceMode) || IsTyping) return;
            if (sim.phase != RaceSim.Phase.Countdown && sim.phase != RaceSim.Phase.Running) return;
            (panel.Root?.focusController?.focusedElement as VisualElement)?.Blur();
        }

        // Turbo (seção 37): a tecla de turbo do jogador, X/□ ou RB do controle dele, ou a barra TURBO da tela.
        // Apertar o turbo também acelera a 100% (S = 60%, A = 100%, D = 100% + turbo enquanto houver carga).
        static bool PadTurbo(Gamepad g) => g != null && (g.buttonWest.isPressed || g.rightShoulder.isPressed);
        readonly bool[] uiTurbo = new bool[RaceSim.MaxCars];
        bool TurboHeld(int k, bool keys)
        {
            var kb = Keyboard.current;
            int ks = KeySet(k);
            return uiTurbo[k] || (keys && kb != null && KeyPressed(kb, TurboKeyAt(ks))) || PadTurbo(PadOf(ks));
        }

        bool PadPressed(int k, System.Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> b)
        {
            var g = PadOf(KeySet(k));
            return g != null && b(g).wasPressedThisFrame;
        }

        static bool AnyPadStart()
        {
            foreach (var g in Gamepad.all) if (g.startButton.wasPressedThisFrame) return true;
            return false;
        }

        // O que aparece no botão ACELERAR de cada jogador.
        string GasLabel(int k)
        {
            if (GasKey(k) == Key.None) return PadOf(KeySet(k)) != null ? L.T("ACELERAR  ·  controle") : L.T("PRECISA DE UM CONTROLE");     // 5º e 6º humano
            string pad = PadOf(KeySet(k)) != null ? L.T("  ·  controle") : "";
            return OnlineRun ? L.F("ACELERAR  {0}  ·  {1}% {2}{3}", KeyText(GasKeyAt(0)), HalfOf(k), KeyText(HalfKeyAt(0)), (Gamepad.all.Count > 0 ? L.T("  ·  controle") : "")) : L.F("ACELERAR  {0}  ·  {1}% {2}{3}", KeyText(GasKey(k)), HalfOf(k), KeyText(HalfKey(k)), pad);
        }
        Key ReslotKey(int k) => ReslotKeyAt(KeySet(k));

        // Nome curto da tecla para os botões e dicas.
        static string KeyText(Key k) => Controles.Nome(k);
        static string[] LaneNames => new[] { L.T("Trilho 1"), L.T("Trilho 2"), L.T("Trilho 3"), L.T("Trilho 4"), L.T("Trilho 5"), L.T("Trilho 6"), L.T("Trilho 7"), L.T("Trilho 8") };
        static readonly string[] LaneCls = { "a", "b", "c", "d", "e", "f", "g", "h" };       // sufixo das classes de cor do USS (trilho 1 a 8)
        // Modos de câmera da corrida. RaceSettings.cameraMode guarda o valor de TopDownCamera.RaceViewMode.
        static readonly TopDownCamera.RaceViewMode[] CamModeValues = { TopDownCamera.RaceViewMode.Tv, TopDownCamera.RaceViewMode.ThirdPerson, TopDownCamera.RaceViewMode.FirstPerson, TopDownCamera.RaceViewMode.TopFollow };
        static string[] CamModes => new[] { L.T("Transmissão de TV"), L.T("Perseguição"), L.T("Primeira pessoa"), L.T("De cima") };
        static string[] CamModeHints => new[] { L.T("Câmeras em volta da mesa seguem o líder"), L.T("Atrás e acima do carrinho, olhando a curva"), L.T("Dentro do carrinho; quando ele roda, vira a traseira"), L.T("Segue os jogadores; botão direito gira, roda aproxima") };
        static string[] CamTargets => new[] { L.T("Jogador 1"), L.T("Jogador 2"), L.T("Jogador 3"), L.T("Jogador 4"), L.T("Jogador 5"), L.T("Jogador 6"), L.T("Jogador 7"), L.T("Jogador 8") };

        RaceSettings set;
        RaceSim sim;
        readonly Transform[] cars = new Transform[RaceSim.MaxCars];
        ParticleSystem sparks;
        TrackSave builderStash;              // a pista do construtor, guardada enquanto a corrida usa outra
        readonly bool[] uiHeld = new bool[RaceSim.MaxCars];
        bool scoreDirty = true, champDirty;
        readonly Material[] carMats = new Material[RaceSim.MaxCars];      // cópia por carrinho: no campeonato ganha a cor do piloto
        RaceSim.Phase lastPhase;

        class PadUI { public VisualElement root, gas, fill, speed, slip, turbo, turboFill; public Label turboText; public Label name, state, gasText; public VisualElement half; public Label halfTri; public Button reslot; public string flash; public bool flashRec; public float flashT; }
        readonly PadUI[] pads = new PadUI[RaceSim.MaxCars];
        VisualElement pickList, scoreBox, padsBox, topBox, lights, banner, raceClock;
        Label raceClockBig, raceClockSub;
        Label raceHint, clock, gripHint, bannerTitle, bannerSub, bannerRec;
        Button startBtn;
        // Menu da câmera: botão no canto superior direito da mesa que abre a lista de modos.
        VisualElement camMenu, camPop, camTargetBox;
        Button camBtn;
        Button splitBtn;
        readonly Button[] camModeBtns = new Button[4], camTargetBtns = new Button[RaceSim.MaxCars];
        Label camTargetNote;
        SliderInt cameraDistanceSlider;
        readonly List<VisualElement> lightDots = new List<VisualElement>();
        // Placar sobre a mesa: uma ficha por carrinho (posição, nome, volta, melhor volta).
        class HudCard { public VisualElement root, dot; public Label pos, name, lap, best, cur; }
        readonly HudCard[] hud = new HudCard[RaceSim.MaxCars];
        VisualElement hudBox;
        bool lastRaceUI;
        VisualElement optRules, optPersonal;      // regras da corrida e ajustes pessoais, montados na aba Corrida e mostrados em Opções
        bool sideAutoHidden;                 // o painel foi recolhido pela largada (devolve na chegada)
        // Contagem grande no meio da mesa (3, 2, 1, VAI!) e o resultado da prova dentro do aviso de vitória.
        VisualElement cdBox, resTable, resBtns;
        Label cdText;
        VisualElement cdList;                // troca de trilho: a distância entre os carrinhos
        string cdListSig;
        Button resAgain, resNext;

        // ---------- Início ----------

        void Start()
        {
            set = RaceSettings.Load();
            set.carTagMode = 1; set.narra = true;            // o jogo sempre abre com a etiqueta (F1) e a narração (F2) ligadas
            sim = new RaceSim(set);
            sim.ScoreChanged += () => scoreDirty = true;
            sim.LapDone += OnLapDone;
            CreateCars();
            GhostStart();
            CreateSparks();
            CreateVfx();
            AudioStart();
            BuildUI();
            BuildTags();
            BuildNarra();
            BuildChampUI();
            BuildCareerUI();
            BuildShopUI();
            BuildOnlineUI();
            OnlineBoot();
            BuildTutorialUI();
            builder.Changed += OnTrackChanged;
            panel.ModeChanged += OnMode;
            panel.OptionsRequested += ShowKeys;
            panel.TestRequested += ToggleTest;
            // "Correr nesta pista" no construtor: abre a corrida com a pista do construtor.
            panel.RaceHereRequested += () => { set.track = "builder"; set.raceFaixas = 0; set.Save(); panel.SetMode(BuilderPanel.Mode.Race); };
            OnTrackChanged();
            SplitStart();
            BuilderPanel.FoldHints(panel.Root);         // "?" no lugar dos parágrafos de explicação
        }

        void OnDestroy()
        {
            SplitDestroy();
            TagsDestroy();
            CareerDestroy();
            Progresso.Changed -= OnProfileChanged;
            if (builder) builder.Changed -= OnTrackChanged;
            if (panel) { panel.ModeChanged -= OnMode; panel.OptionsRequested -= ShowKeys; panel.TestRequested -= ToggleTest; }
            OnlineDestroy();
        }

        static string[] CarNames => new[] { L.T("Carrinho vermelho"), L.T("Carrinho azul"), L.T("Carrinho verde"), L.T("Carrinho amarelo"), L.T("Carrinho roxo"), L.T("Carrinho ciano"), L.T("Carrinho laranja"), L.T("Carrinho rosa") };

        // Material de cada trilho: vermelho e azul na cena; verde e amarelo no tema (Autorama > Atualizar materiais e ícones).
        Material BaseCarMaterial(int k)
        {
            if (k < carMaterials.Length && carMaterials[k] != null) return carMaterials[k];
            if (k >= 6)
            {
                var more8 = builder.theme != null ? builder.theme.cars78 : null;
                if (more8 != null && k - 6 < more8.Length && more8[k - 6] != null) return more8[k - 6];
                return carMaterials[k % 2];
            }
            if (k >= 4)
            {
                var more = builder.theme != null ? builder.theme.cars56 : null;
                if (more != null && k - 4 < more.Length && more[k - 4] != null) return more[k - 4];
                return carMaterials[k % 2];
            }
            var extra = builder.theme != null ? builder.theme.cars34 : null;
            if (extra != null && k - 2 < extra.Length && k >= 2 && extra[k - 2] != null) return extra[k - 2];
            return carMaterials[k % 2];      // sem o tema novo: repete a cor (a pintura do campeonato cobre)
        }

        readonly CarView[] carViews = new CarView[RaceSim.MaxCars];

        void CreateCars()
        {
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                // Carrinho 3D (seção 28): duas cores, a principal é a do trilho ou a do piloto.
                carViews[k] = CarView.Create(CarNames[k], builder.theme.support, LaneColors[k], transform, builder.theme.carModels);
                cars[k] = carViews[k].transform;
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
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            main.maxParticles = 1500;
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
            int before = sim.N;
            sim.SetTrack(builder.Segments, builder.Info.closed, builder.Faixas);
            if (testing) sim.StartTest();
            if (sim.N != before) { SetBot(); LayoutForCars(); }
            scoreDirty = true;
            PlaceCars();
        }

        void OnMode(BuilderPanel.Mode m)
        {
            if (testing) SetTest(false);
            if (kBuilding && m != BuilderPanel.Mode.Build) EndBuild();      // saiu do construtor da carreira por uma aba
            if (m != BuilderPanel.Mode.Career) CareerLeave();
            bool race = m != BuilderPanel.Mode.Build;
            // Saindo do construtor, guarda a pista dele; voltando, reencontra.
            if (race && builderStash == null) builderStash = builder.ToSave();
            builder.RaceMode = race;
            for (int k = 0; k < RaceSim.MaxCars; k++) uiHeld[k] = uiTurbo[k] = false;
            if (m == BuilderPanel.Mode.Race)
            {
                sim.set = set; sim.cutoff = null;
                for (int k = 0; k < RaceSim.MaxCars; k++) sim.present[k] = true;
                SetBot();
                ApplyCameraSettings();
                UseTrack(set.track);
            }
            else if (m == BuilderPanel.Mode.Champ)
            {
                ApplyCameraSettings();
                UseChampTrack();
            }
            else if (m == BuilderPanel.Mode.Career) CareerEnterMode();
            else
            {
                sim.set = set; sim.cutoff = null; System.Array.Clear(sim.aiSkill, 0, sim.aiSkill.Length); System.Array.Clear(sim.setup, 0, sim.setup.Length);
                cam.SetRaceViewMode(TopDownCamera.RaceViewMode.Atual);
                if (builderStash != null) builder.ShowTrack(builderStash);
                if (m == BuilderPanel.Mode.Build) builderStash = null;     // no lobby online a pista do construtor fica só de fundo
                panel.FrameTrack();
            }
            if (m == BuilderPanel.Mode.Online) OnlineShown();
            sim.Reset();
            topBox.style.display = panel.RaceMode ? DisplayStyle.Flex : DisplayStyle.None;
            ShowCamPop(false);
            if (keysOpen) CloseKeys();                    // um convite da Steam troca de modo com Opções ou Peças abertas
            if (ShopOpen) ShowShop(false);
            if (!panel.RaceMode) RaceUiOff();
            DrawPadHeads();
            scoreDirty = champDirty = true;
        }

        // Fora da corrida (construtor, lobby online, fim da corrida online): desliga já o que só o quadro da corrida
        // apagaria. Com a pista do construtor aberta o Update nem chega ao PlaceCars, e a tela dividida, o menu de
        // câmera, o minimapa, a contagem e o semáforo 3D ficavam como estavam.
        void RaceUiOff()
        {
            PauseClear();
            if (SplitOn) LayoutSplit(0, 0);
            cdBox.style.display = hudBox.style.display = minimap.style.display = camMenu.style.display = DisplayStyle.None;
            TrackPieceView.SetStartLights(0, false);
        }

        struct Option { public string key, name, sub; public TrackSave save; public bool closed; }

        List<Option> Options()
        {
            var list = new List<Option>
            {
                new Option { key = "builder", name = L.T("Pista do construtor"), sub = builderStash != null && builderStash.nome != "" ? $"“{builderStash.nome}”" : L.T("a que está aberta agora"), save = builderStash ?? builder.ToSave() },
                new Option { key = "ex:oito", name = L.T("Exemplo: 8 com ponte"), save = TrackSave.Eight() },
            };
            foreach (var e in TrackLibrary.List()) list.Add(new Option { key = e.path, name = e.save.nome, save = e.save });
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                try { var segs = TrackLayout.Build(o.save.Origin, o.save.Pieces(), o.save.Tail, o.save.StartZ); o.closed = TrackLayout.Analyze(o.save.Origin, segs, o.save.Tail, o.save.Faixas).closed; }
                catch { o.closed = false; }
                list[i] = o;
            }
            return list;
        }

        // A mesma pista adaptada para outro tamanho (2, 4 ou 6 trilhos): uma cópia, a original não muda (seção 32).
        static TrackSave Adapted(TrackSave s, int faixas)
        {
            var c = TrackSave.FromJson(s.ToJson(false));
            c.faixas = TrackMath.Faixas(faixas);
            return c;
        }

        // Troca a pista da corrida sem mexer na que está no construtor. Só circuitos fechados entram na lista.
        // A pista corre no tamanho escolhido (set.raceFaixas); escolher outra pista volta ao tamanho em que ela foi feita.
        void UseTrack(string key)
        {
            var opts = Options();
            var o = opts.Find(x => x.key == key && x.closed);
            if (o.save == null) o = opts.Find(x => x.closed);          // os exemplos estão sempre fechados
            if (o.key != set.track) set.raceFaixas = 0;
            set.track = o.key;
            set.Save();
            builder.ShowTrack(Adapted(o.save, set.raceFaixas != 0 ? set.raceFaixas : o.save.Faixas));
            panel.FrameTrack();
            DrawPicker(opts);
            SetBot(); LayoutForCars();
        }

        // Vale na hora, até no meio da prova. Só na aba Corrida: no campeonato quem manda são os pilotos da bateria.
        // Cada carro tem o próprio lugar (set.slots): gente ou robô com a qualidade dele. Os trilhos além de "cars" ficam vazios.
        void SetBot()
        {
            if (panel.CurrentMode != BuilderPanel.Mode.Race) return;
            int cars = RaceCars;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var sl = set.slots[k];
                sim.present[k] = k < cars;
                sim.aiSkill[k] = k < cars && sl.ai ? Mathf.Clamp(sl.skill, 1, RaceSim.AiMaxSkill) : 0;
                sim.setup[k] = k < cars ? sl.carro : default;
                if (k < sim.N && sim.aiSkill[k] == 0) sim.cars[k].held = false;     // virou gente: solta o acelerador que a IA segurava
            }
            DrawPadHeads();
        }

        // Quantos carros correm na corrida livre: de 1 até o número de trilhos da pista.
        int RaceCars => Mathf.Clamp(set.cars, 1, sim.N);

        void StartRace()
        {
            if (!sim.closed) return;
            sim.StartRace();
            scoreDirty = true;
        }

        // ---------- Quadro a quadro ----------

        void Update()
        {
            TrackRatings.Tick();
            RatingsRedraw();
            EloTick();
            OnlineTick();
            AudioTick();
            KeysUpdate();
            ShopKeys();
            CoinTick();
            WetTick();                                        // a tela de teclas captura a próxima tecla (e some com Esc)
            CamKeys();
            TagsKeys();
            NarraKeys();
            NarraTick();
            if (kBuilding) BuildTick();
            TutTick();
            HalfFocusGuard();
            if (OnlineRun) { OnlineRunUpdate(); return; }
            if (sim == null || !sim.Ready) return;
            sim.mirrored = false;
            if (!panel.RaceMode) { if (testing) sim.Step(Mathf.Min(Time.deltaTime, 0.05f)); PlaceCars(); return; }       // no construtor, os carrinhos ficam no grid

            var kb = Keyboard.current;
            bool keys = kb != null && !IsTyping && !keysOpen;
            for (int k = 0; k < sim.N; k++)
            {
                if (sim.aiSkill[k] > 0) { sim.cars[k].gas = 1f; continue; }   // o piloto IA segura e solta sozinho, sempre no fundo
                bool tb = sim.set.turbo && TurboHeld(k, keys);
                float g = GasLevel(k, keys);                  // sempre: as teclas de meia −/+ valem com o turbo apertado
                if (tb) g = 1f;
                sim.cars[k].boost = tb;
                sim.cars[k].held = g > 0f;
                if (g > 0f) sim.cars[k].gas = g;
                if ((keys && KeyWasPressed(kb, ReslotKey(k))) || PadPressed(k, p => p.buttonSouth)) sim.Reslot(k);
            }
            if (!Paused && ((keys && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || AnyPadStart())) { if (panel.ChampMode) ChampEnter(); else if (!panel.CareerMode && !TutOn) StartRace(); }

            if (keys && kb.pKey.wasPressedThisFrame) PausePressed(null);
            if (!PauseTick() && CareerSteps && !TutHold) sim.Step(Mathf.Min(Time.deltaTime, 0.05f));
            if (panel.CareerMode) CareerTick();
            PlaceCars();
            UpdatePads();
            UpdateTop();
            if (sim.phase != lastPhase) { lastPhase = sim.phase; scoreDirty = true; champDirty = true; SideForPhase(); }
            if (scoreDirty) { scoreDirty = false; DrawScore(); DrawStatus(); }
            if (champDirty && panel.ChampMode) { champDirty = false; DrawChampSide(); }
            (panel.ChampMode ? cClock : clock).text = ClockText();
        }

        readonly Vector3[] carPos = new Vector3[RaceSim.MaxCars];
        readonly float[] carHeading = new float[RaceSim.MaxCars];
        // Minimapa (RaceMinimap): traçado 2D no canto superior direito, com um ponto por carrinho na cor do painel dele.
        RaceMinimap minimap;
        readonly Color[] miniColors = new Color[RaceSim.MaxCars];
        readonly string[] miniNames = new string[RaceSim.MaxCars];

        void PlaceCars()
        {
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                bool on = k < sim.N && sim.present[k];
                if (cars[k].gameObject.activeSelf != on) cars[k].gameObject.SetActive(on);
                if (!on) continue;
                var st0 = sim.cars[k];
                float speed01 = st0.mode == CarMode.OnSlot ? Mathf.Clamp01(st0.v / Mathf.Max(1f, sim.set.vmax * sim.Speed * sim.setup[k].Top)) : 0f;
                for (int i = 0; i < CamCount; i++) PaneCam(i).SetCarTrack(k, st0.mode == CarMode.OnSlot, st0.slip, speed01);
                var p = sim.CarPose(k, out float y, out float pitch);
                var pos = new Vector3(p.pos.x, y, p.pos.y);
                cars[k].position = pos;
                // Frente para +X: gira para o rumo, inclina na rampa e deita na curva inclinada.
                cars[k].rotation = Quaternion.Euler(0f, -p.heading, 0f) * Quaternion.Euler(0f, 0f, pitch) * Quaternion.Euler(sim.CarBank(k), 0f, 0f);
                var st = sim.cars[k];
                carViews[k].SetBody((CarBody)sim.setup[k].bolha, sim.setup[k].modelo);
                carViews[k].SetTire(sim.setup[k].pneu);
                carViews[k].SetChassi(sim.setup[k].chassi);
                carViews[k].SetCockpit(cam.InFirstPersonOf(k));
                carViews[k].Tick(st.mode == CarMode.OnSlot ? st.v : 0f, st.mode == CarMode.Off, Time.deltaTime);
                UpdateVfx(k, st);
                carPos[k] = pos; carHeading[k] = p.heading;
                // Ponto do trilho à frente, para a Perseguição olhar a curva antes de chegar.
                var ahead = sim.PathOf(k).At(st.s + CamAhead);
                // Tangente do trilho no carrinho (pontos CamBehind antes e depois): a Perseguição gira junto, sem atraso na entrada da curva.
                var tb = sim.PathOf(k).At(st.s - CamBehind); var tf = sim.PathOf(k).At(st.s + CamBehind);
                var behind = sim.PathOf(k).At(st.s); behind.pos -= tf.pos - tb.pos;
                for (int i = 0; i < CamCount; i++)
                {
                    PaneCam(i).SetCarAhead(k, new Vector3(ahead.pos.x, ahead.y, ahead.pos.y));
                    PaneCam(i).SetCarBehind(k, new Vector3(behind.pos.x, behind.y, behind.pos.y));
                }
                // Loop logo à frente (ou o carrinho dentro dele): a Perseguição vai para o lado (seção 57).
                bool nearLoop = sim.PathOf(k).LoopNear(st.s, LoopCamAfter * sim.Scale,
                    Mathf.Max(LoopCamMin * sim.Scale, st.v * LoopCamLead), out var loopC, out float loopH) && st.mode == CarMode.OnSlot;
                for (int i = 0; i < CamCount; i++) PaneCam(i).SetCarLoop(k, nearLoop, loopC, loopH);
            }
            FlushVfx();
            if (panel != null && panel.RaceMode)
            {
                if (framedPath != sim.PathOf(0)) FrameTrackForCamera();
                // No online o seu trilho muda de uma bateria para outra: a câmera acompanha o seu carrinho.
                int focus = CamFocusCar();
                if (OnlineRun) { if (focus >= 0) cam.SetThirdPersonCar(focus); }
                else cam.SetThirdPersonCar(ChaseCar());
                cam.SetRaceTargets(carPos, carHeading, sim.N, focus >= 0 ? focus : LeaderCar());
                // A "De cima" mantém na tela os carrinhos de gente (no online, só o seu).
                for (int k = 0; k < RaceSim.MaxCars; k++)
                    followMask[k] = k < sim.N && sim.present[k] && (OnlineRun ? k == focus : sim.aiSkill[k] == 0);
                if (System.Array.IndexOf(followMask, true) < 0)          // só robôs: todos os presentes
                    for (int k = 0; k < RaceSim.MaxCars; k++) followMask[k] = k < sim.N && sim.present[k];
                cam.SetFollowCars(followMask);
            }
            GhostTick();
            UpdateSplit(LeaderCar());
            if (camMenu != null)
            {
                bool show = panel != null && panel.RaceMode && RaceUI;
                camMenu.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show && camPop.style.display == DisplayStyle.Flex) ShowCamPop(false);
            }
            UpdateMinimap();
        }

        // Minimapa: só na aba Corrida/Campeonato com pista pronta. Cada carrinho usa a cor do painel dele.
        void UpdateMinimap()
        {
            if (minimap == null) return;
            // Saiu do menu: os painéis dos jogadores (desenhados só em alguns eventos) voltam.
            bool ui = RaceUI;
            if (ui != lastRaceUI) { lastRaceUI = ui; DrawPadHeads(); }
            if (!panel.RaceMode) hudBox.style.display = DisplayStyle.None;
            // Menu inicial aberto no meio da corrida: placar, aviso e contagem ficam escondidos (sem mexer no display deles).
            if (topBox != null) topBox.visible = cdBox.visible = hudBox.visible = !panel.MenuOpen;
            bool show = panel != null && panel.RaceMode && RaceUI && sim != null && sim.Ready;
            minimap.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                miniColors[k] = pads[k] != null ? pads[k].root.resolvedStyle.borderTopColor : Color.white;
                string nm = pads[k] != null ? pads[k].name.text : "";
                miniNames[k] = ShortName(nm);
            }
            minimap.Refresh(sim, set, miniNames, miniColors, Order(), builder != null ? builder.cellCm : 15f);
        }

        // ---------- Interface ----------

        void BuildUI()
        {
            var box = panel.RaceBox;

            var pick = Section(box, L.T("PISTA"));
            Text(L.T("Escolha onde correr. Qualquer pista fechada serve, em qualquer tamanho. Trocar aqui não mexe na pista do construtor."), "hint", pick);
            pickFilter = new TrackFilter(pick, () => DrawPicker(Options()));
            pickList = El(null, Caixa(pick));

            pickNote = Text("", "small", pick);

            // Tamanho: qualquer pista corre em 2, 4, 6 ou 8 trilhos (adaptada na hora; a pista salva não muda).
            var size = Section(box, L.T("TAMANHO DA PISTA"));
            var sizeRow = El("segmented", size);
            for (int i = 0; i < 4; i++)
            {
                int f = 2 + 2 * i;
                sizeBtns[i] = Btn(L.F("{0} trilhos", f), () => { if (Racing || sim.N == f) return; if (!Progresso.FaixasOk(f)) { panel.Toast(L.F("Pista de {0} trilhos: abre na carreira.", f)); return; } set.raceFaixas = f; set.Save(); UseTrack(set.track); sim.Reset(); scoreDirty = true; }, "btn", sizeRow);
            }

            // Carros: de 1 (treino contra o relógio) até o número de trilhos.
            var carsSec = Section(box, L.T("CARROS NA PISTA"));
            var carsRow = El("segmented", carsSec);
            for (int n = 1; n <= RaceSim.MaxCars; n++)
            {
                int c = n;
                carBtns[n - 1] = Btn(n.ToString(), () => { if (Racing || c > sim.N) return; set.cars = c; set.Save(); SetBot(); LayoutForCars(); sim.Reset(); scoreDirty = true; }, "btn car-count", carsRow);
            }

            // Pilotos: um cartão por carro (gente ou robô; o robô tem a qualidade dele).
            var pilots = Section(box, L.T("PILOTOS"));
            pilotBox = El(null, pilots);

            BuildGhostUI(box);                             // fantasma (RaceController.Ghost.cs)

            // Formato: por voltas (vence quem completar primeiro) ou por tempo (manches, uma por trilho).
            var fmt = Section(box, L.T("FORMATO"));
            var kindRow = El("segmented", fmt);

            var race = Section(box, L.T("CORRIDA"));
            raceHint = Text("", "hint", race);
            var row = El("row", race);
            startBtn = Btn(L.T("Largar (Enter)"), StartRace, "btn btn--primary", row);
            Btn(L.T("Voltar ao grid"), () => { sim.Reset(); scoreDirty = true; }, "btn", row);

            var clockRow = El("row", race);
            Text(L.T("Tempo de prova"), "stats-key", clockRow).style.marginRight = 8;
            clock = Text(L.T("0,00 s"), "clock", clockRow);
            scoreBox = El("score", race);

            var how = Fold(box, L.T("COMO PILOTAR"));
            helpLabel = Text("", "small", how);
            Text(L.T("<b>Controle:</b> gatilho direito acelera aos poucos (o 1º controle é do jogador 1, o 2º do jogador 2, e assim por diante) · <b>A/✕</b> recoloca · <b>X/□</b> ou <b>RB</b> turbo · <b>Start</b> larga\n<b>Enter</b> largar · câmera no menu do canto direito"), "small", how);

            // Regras da corrida e ajustes pessoais: ficam na tela de Opções (BuildKeysUI), com o aviso de onde valem.
            var adj = optRules = El(null);
            optPersonal = El(null);
            var byLaps = Btn(L.T("Por voltas"), null, "btn", kindRow);
            var byTime = Btn(L.T("Por tempo"), null, "btn", kindRow);
            var secs = new SliderInt(2, 20) { value = Mathf.Clamp(set.secs / 30, 2, 20) };     // em meios minutos: 1 a 10 min
            var laps = new SliderInt(1, 20) { value = set.laps };
            var vmax = new Slider(8f, 20f) { value = set.vmax };
            var ramp = new Slider(0.2f, 1.5f) { value = set.ramp };
            var grip = new Slider(15f, 45f) { value = set.grip };
            var col = new Toggle(L.T("Colisão entre carrinhos")) { value = set.collide };
            var turbo = new Toggle(L.T("Turbo")) { value = set.turbo };
            var vol = new SliderInt(0, 100) { value = Mathf.Clamp(set.volume, 0, 100) };
            var half = new SliderInt(20, 90) { value = Mathf.Clamp(set.halfGas, 20, 90) };
            var reslot = new Slider(0.1f, 1.5f) { value = Mathf.Clamp(set.reslotTime, 0.1f, 1.5f) };
            void Labels()
            {
                laps.label = L.F("Voltas: {0}", set.laps);
                secs.label = L.F("Duração: {0} · {1} por trilho", Dur(set.secs), Dur(set.secs / Mathf.Max(1, sim.N)));
                byLaps.EnableInClassList("btn--on", !set.byTime);
                byTime.EnableInClassList("btn--on", set.byTime);
                laps.style.display = set.byTime ? DisplayStyle.None : DisplayStyle.Flex;
                secs.style.display = set.byTime ? DisplayStyle.Flex : DisplayStyle.None;
                vmax.label = L.F("Velocidade máx.: {0} cél/s", Num(set.vmax));
                ramp.label = L.F("Tempo até 100%: {0} s", Num(set.ramp, 2));
                grip.label = L.F("Aderência: {0}", Mathf.RoundToInt(set.grip));
                half.label = L.F("Meia aceleração (tecla de meia): {0}%", set.halfGas);
                reslot.label = L.F("Tempo para recolocar: {0} s", Num(set.reslotTime, 2));
                float sc = sim.Scale;
                float Lim(float r, float m) => Mathf.Min(100f, Mathf.Round(sim.CornerLimit(r, m) / (set.vmax * sim.Speed) * 100f));
                // Raio do trilho de dentro e do de fora, no mundo: 2,25 e 2,75 na pista de 2; 4,25 e 5,75 na de 4.
                var lanes = TrackMath.LanesOf(builder.Faixas);
                float rIn = (2.5f + lanes[0]) * sc, rOut = (2.5f - lanes[0]) * sc;
                gripHint.text = L.F("Nas curvas planas, o carrinho segura até {0}% da velocidade máxima no trilho de dentro e {1}% no de fora. Nas inclinadas, até {2}% e {3}%.", Lim(rIn, 1f), Lim(rOut, 1f), Lim(rIn, PieceCatalog.BankGrip), Lim(rOut, PieceCatalog.BankGrip));
            }
            foreach (var s in new VisualElement[] { laps, secs }) { s.AddToClassList("setting"); fmt.Add(s); }
            foreach (var s in new VisualElement[] { vmax, ramp, grip, reslot, col, turbo }) { s.AddToClassList("setting"); adj.Add(s); }
            half.AddToClassList("setting"); optPersonal.Add(half);
            void Kind(bool t) { if (Racing) return; set.byTime = t; set.Save(); Labels(); sim.Reset(); scoreDirty = true; }
            byLaps.clicked += () => Kind(false);
            byTime.clicked += () => Kind(true);
            secs.RegisterValueChangedCallback(e => { set.secs = e.newValue * 30; set.Save(); Labels(); scoreDirty = true; });
            refreshAdjLabels = Labels;
            gripHint = Text("", "small", adj);
            laps.RegisterValueChangedCallback(e => { set.laps = e.newValue; set.Save(); Labels(); scoreDirty = true; });
            vmax.RegisterValueChangedCallback(e => { set.vmax = e.newValue; set.Save(); Labels(); });
            ramp.RegisterValueChangedCallback(e => { set.ramp = e.newValue; set.Save(); Labels(); });
            grip.RegisterValueChangedCallback(e => { set.grip = e.newValue; set.Save(); Labels(); });
            col.RegisterValueChangedCallback(e => { set.collide = e.newValue; set.Save(); });
            turbo.RegisterValueChangedCallback(e => { set.turbo = e.newValue; set.Save(); });
            vol.AddToClassList("setting"); optPersonal.Add(vol);
            // Som do motor: alterna entre as opções (dá para trocar no meio da corrida e ouvir na hora).
            Button motorBtn = null;
            motorBtn = Btn("", () => { NextMotor(); motorBtn.text = L.F("Som do motor: <b>{0}</b>  ▸", MotorName); }, "btn", optPersonal);
            motorBtn.style.alignSelf = Align.FlexStart;
            motorBtn.text = L.F("Som do motor: <b>{0}</b>  ▸", MotorName);
            motorBtn.tooltip = L.T("Clique para ouvir o próximo. Para usar uma gravação sua, ponha motor.wav (PCM 16 bits) em ") + System.IO.Path.Combine(TrackLibrary.Root, "sons");
            vol.label = L.F("Som: {0}%", set.volume);
            vol.RegisterValueChangedCallback(e => { set.volume = e.newValue; set.Save(); vol.label = L.F("Som: {0}%", set.volume); });
            half.RegisterValueChangedCallback(e => { set.halfGas = e.newValue; set.Save(); for (int i = 0; i < halfPct.Length; i++) if (halfPct[i] > 0) SetHalf(i, e.newValue); Labels(); });
            reslot.RegisterValueChangedCallback(e => { set.reslotTime = e.newValue; set.Save(); Labels(); });
            Labels();

            // Sobre a mesa: luzes e aviso de vitória no topo; os dois jogadores embaixo.
            var root = panel.Root;
            topBox = El("race-top", root); topBox.pickingMode = PickingMode.Ignore;
            // Volta e relógio da prova, grandes, em cima das fichas (sem precisar abrir o painel com Tab).
            raceClock = El("race-clock", topBox); raceClock.pickingMode = PickingMode.Ignore;
            raceClockBig = Text("", "race-clock-big", raceClock); raceClockBig.pickingMode = PickingMode.Ignore;
            raceClockSub = Text("", "race-clock-sub", raceClock); raceClockSub.pickingMode = PickingMode.Ignore;
            raceClock.style.display = DisplayStyle.None;
            // Torre de posições (proposta F): as fichas numa coluna no canto de cima, à esquerda da mesa.
            hudBox = El("hud", root); hudBox.pickingMode = PickingMode.Ignore; hudBox.style.display = DisplayStyle.None;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var h = new HudCard { root = El("hud-card hud-card--" + LaneCls[k], hudBox) };
                h.pos = Text("1º", "hud-pos", h.root);
                h.dot = El("hud-dot", h.root);
                var info = El("hud-info", h.root);
                h.name = Text("", "hud-name", info);
                var hudRow = El("hud-row", info);
                h.lap = Text("", "hud-lap", hudRow);
                h.best = Text("", "hud-best", hudRow);
                h.cur = Text("", "hud-cur", h.root);          // diferença para o da frente (o líder: LÍDER)
                foreach (var e in h.root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
                hud[k] = h;
            }
            lights = El("lights", topBox);
            for (int i = 0; i < 3; i++) lightDots.Add(El("light", lights));
            banner = El("banner", topBox);
            bannerTitle = Text("", "banner-title", banner);
            bannerSub = Text("", "banner-sub", banner);
            bannerRec = Text("", "banner-rec", banner);    // selo de recorde da pista batido (campeonato)
            bannerRec.style.display = DisplayStyle.None;
            startBar = El("start-bar", banner);             // online: quanto falta para o host largar sozinho
            startBarText = Text("", "start-bar-text", startBar);
            startBarFill = El("start-bar-fill", El("start-bar-track", startBar));
            startBar.style.display = DisplayStyle.None;
            recCard = El("res-table", banner);              // recordes antes da largada (RaceController.Records.cs)
            recCard.style.display = DisplayStyle.None;
            carPick = El("car-pick", banner);              // escolha do carro antes da etapa (RaceController.CarPick.cs)
            carPick.style.display = DisplayStyle.None;
            BuildPauseUI();                                // pausa no P (RaceController.Pause.cs)
            resTable = El("res-table", banner);
            rateBox = El("rate-box", banner);                // nota da pista no fim de cada corrida online (seção 83)
            rateBox.style.display = DisplayStyle.None;
            resBtns = El("res-btns", banner);
            resAgain = Btn(L.T("Correr de novo  (Enter)"), () => { if (ChampLike) ChampEnter(); else StartRace(); }, "btn btn--accent res-btn", resBtns);
            resNext = Btn(L.T("Trocar pista"), () => { sideAutoHidden = false; panel.SetSideFromRace(true); }, "btn res-btn", resBtns);
            lights.pickingMode = PickingMode.Ignore;
            minimap = new RaceMinimap();
            root.Add(minimap);

            cdBox = El("cd-box", root); cdBox.pickingMode = PickingMode.Ignore;
            cdText = Text("", "cd-text", cdBox); cdText.pickingMode = PickingMode.Ignore;
            cdList = El("cd-list", cdBox); cdList.pickingMode = PickingMode.Ignore;
            cdBox.style.display = DisplayStyle.None;

            BuildCamMenu(root);

            padsBox = El("pads", root);
            for (int k = 0; k < RaceSim.MaxCars; k++) pads[k] = BuildPad(k);
            padsBox.style.display = DisplayStyle.None;
            topBox.style.display = DisplayStyle.None;
            BuildKeysUI(root);                  // por cima de tudo: a tela de teclas
            LayoutForCars();
            DrawPicker(Options());
        }

        System.Action refreshAdjLabels;
        Label pickNote;
        VisualElement pilotBox;
        readonly Button[] sizeBtns = new Button[4], carBtns = new Button[RaceSim.MaxCars];

        // ---------- Teclas dos jogadores (tela de opções, seção 34 do guia) ----------

        VisualElement keysBox;                  // a tela: cobre a aba inteira e some com Esc ou Fechar
        VisualElement netOpts;                  // Steam ou servidor (o toggle e o endereço vêm de BuildOnlineUI)
        Label helpLabel, keysHint, langHint;
        readonly Button[] langBtns = new Button[L.Codigos.Length];
        readonly Button[,] keyBtns = new Button[Controles.Jogadores, Controles.Acoes];
        int capJ = -1, capA;                    // a tecla que está esperando: jogador e ação (-1 = nenhuma)
        bool keysOpen => keysBox != null && keysBox.style.display == DisplayStyle.Flex;

        void BuildKeysUI(VisualElement root)
        {
            keysBox = El("keys-screen", root);
            keysBox.style.display = DisplayStyle.None;
            var outer = El("keys-card", keysBox);
            Text(L.T("OPÇÕES"), "h2", outer);
            var card = new ScrollView(ScrollViewMode.Vertical);
            card.AddToClassList("keys-scroll");
            outer.Add(card);
            // Idioma (seção 88): grava a escolha e recarrega a cena, que monta a interface de novo no idioma novo.
            // Na sala online não troca: a recarga derrubaria a conexão.
            Text(L.T("IDIOMA"), "hint", card);
            var langRow = El("row", card);
            langHint = Text("", "small", card);
            for (int i = 0; i < L.Codigos.Length; i++)
            {
                string cod = L.Codigos[i];
                langBtns[i] = Btn(L.Nomes[i], () =>
                {
                    if (cod == L.Atual || room != null) return;
                    L.Escolhe(cod);
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                }, "btn", langRow);
                langBtns[i].EnableInClassList("btn--on", cod == L.Atual);
            }
            Text(L.T("TECLAS DOS JOGADORES"), "hint", card).style.marginTop = 14;
            Text(L.T("Clique num botão e aperte a tecla nova. Se ela já era de outra ação, as duas trocam. Esc cancela."), "hint", card);
            for (int j = 0; j < Controles.Jogadores; j++)
            {
                int jj = j;
                var row = El("keys-row", card);
                Text(L.F("Jogador {0}", j + 1), "pcard-name", El("keys-who", row));
                for (int a = 0; a < Controles.Acoes; a++)
                {
                    int aa = a;
                    keyBtns[j, a] = Btn("", () => StartCapture(jj, aa), "btn keys-btn", row);
                }
            }
            Text(L.T("Jogadores 5 e 6 (pista de 6) jogam só com controle."), "small", card);
            keysHint = Text("", "small", card);
            var reset = Btn(L.T("Restaurar padrão"), () => { CancelCapture(); Controles.Restaurar(); KeysChanged(); }, "btn", card);
            reset.style.alignSelf = Align.FlexStart;

            Text(L.T("REGRAS DA CORRIDA"), "hint", card).style.marginTop = 14;
            Text(L.T("<b>Onde valem:</b>\n• <b>Corrida livre</b> e <b>carreira</b>: na hora (na carreira, a pista molhada tira aderência quando a prova pede).\n• <b>Campeonato offline</b>: são copiadas quando o campeonato é criado. Mudar aqui depois não mexe num campeonato em andamento.\n• <b>Campeonato online</b>: valem as do <b>host</b>, enviadas na largada e fixas até o fim. As dos outros jogadores não contam.\n• <b>Partida rápida</b>: não valem. Lá as regras são sempre as padrão: velocidade 17, 0,5 s até 100%, aderência 43, colisão e turbo ligados, recolocar em 0,3 s."),
                 "small", card).style.marginBottom = 8;
            card.Add(optRules);
            Text(L.T("SEU CONTROLE E SOM"), "hint", card).style.marginTop = 14;
            Text(L.T("Só seus, em qualquer modo, também no online: não vão para os outros jogadores."), "small", card).style.marginBottom = 8;
            card.Add(optPersonal);

            Text(L.T("JOGO ONLINE"), "hint", card).style.marginTop = 14;
            Text(L.T("O jogo abre conectado à Steam. Desligue para jogar pelo servidor (Node) no endereço abaixo."), "hint", card);
            netOpts = El(null, card);

            Text(L.T("TODAS AS TECLAS"), "hint", card).style.marginTop = 14;
            foreach (var (grupo, linhas) in AllKeys)
            {
                Text(grupo, "h3", card);
                foreach (var (k, o) in linhas)
                {
                    var row = El("row", card);
                    var kl = Text($"<b>{k}</b>", "small", row); kl.style.width = 170; kl.style.flexShrink = 0;
                    Text(o, "small", row).style.flexShrink = 1;
                }
            }
            var bottom = El("row", outer);
            bottom.style.marginTop = 8;
            Btn(L.T("Refazer tutorial"), TutRedo, "btn", bottom);
            Btn(L.T("Fechar  (Esc)"), CloseKeys, "btn btn--primary", bottom);
            KeysChanged();
        }

        // A lista da tela de Opções. As do carrinho vêm de Controles (trocáveis, acima).
        static (string, (string, string)[])[] AllKeys => new[]
        {
            (L.T("CONSTRUTOR"), new[] {
                ("1 · 2 · T", L.T("reta curta · comprida · curtinha")),
                ("3 · 4", L.T("curva longa esquerda · direita")),
                ("5 · 6", L.T("meia curva esquerda · direita")),
                ("7 · 8", L.T("curva longa inclinada esquerda · direita")),
                ("9 · 0", L.T("curva curta inclinada esquerda · direita")),
                ("G · H", L.T("curva em C esquerda · direita")),
                ("N · M", L.T("curva em C inclinada esquerda · direita")),
                ("U · D", L.T("rampa sobe · desce")),
                ("K · L", L.T("loop saindo à esquerda · à direita")),
                ("J", L.T("rampa de salto")),
                ("X · B", L.T("cruzamento · cruzamento curto")),
                ("Y · I", L.T("meia curva em C esquerda · direita")),
                ("O · A", L.T("meia curva em C inclinada esquerda · direita")),
                ("W · S", L.T("muro de parede à esquerda · à direita")),
                (L.T("Vírgula · Ponto"), L.T("parafuso à esquerda · à direita")),
                ("Z", L.T("fechada (os trilhos se juntam)")),
                ("F", L.T("troca o lado (esquerda · direita) da peça selecionada")),
                ("E", L.T("troca a ponta de encaixe: fim da pista · atrás da largada")),
                ("R", L.T("gira a pista 90° em volta da largada")),
                ("Q", L.T("fecha o circuito sozinho (Esc cancela)")),
                (L.T("Delete · Backspace"), L.T("apaga a selecionada · a última")),
                (L.T("Ctrl+Z · Ctrl+Y (Ctrl+Shift+Z)"), L.T("desfaz · refaz")),
                (L.T("Ctrl+S"), L.T("salva a pista")),
                (L.T("Esc"), L.T("tira a seleção")),
                (L.T("Clique"), L.T("seleciona a peça; arrastar a largada move a pista")),
                (L.T("Cosméticos"), L.T("clique no cartão e na mesa para pôr; arrastar move · R gira · Delete tira · Esc solta")),
            }),
            (L.T("CÂMERA E TELA"), new[] {
                (L.T("Roda do mouse"), L.T("aproxima e afasta")),
                (L.T("Botão direito · do meio"), L.T("arrasta a mesa; com a câmera inclinada, o direito gira")),
                ("V", L.T("câmera de cima · inclinada")),
                ("C", L.T("enquadra a pista")),
                (L.T("Tab"), L.T("mostra e esconde o painel")),
                (L.T("1 · 2 · 3 · 4 (corrida)"), L.T("troca a câmera, na ordem do menu")),
            }),
            (L.T("CORRIDA"), new[] {
                (L.T("Enter (ou Start no controle)"), L.T("larga · próxima bateria")),
                ("P", L.T("pausa (online: 3 por piloto, 60 s cada)")),
                ("F1", L.T("o que aparece em cima dos carros")),
                ("F2", L.T("liga e desliga a narração")),
                (L.T("Esc"), L.T("fecha esta tela")),
            }),
        };

        void ShowKeys()
        {
            if (keysBox == null || Racing) return;
            if (oSteam != null) oSteam.SetEnabled(room == null);         // na sala não dá para trocar de rede
            foreach (var b in langBtns) b?.SetEnabled(room == null);      // nem de idioma
            langHint.text = room == null ? "" : L.T("Saia da sala para trocar o idioma.");
            keysBox.style.display = DisplayStyle.Flex;
            keysBox.BringToFront();
            refreshAdjLabels?.Invoke();                                   // limites das curvas da pista atual
            KeysChanged();
        }

        void CloseKeys()
        {
            CancelCapture();
            if (keysBox != null) keysBox.style.display = DisplayStyle.None;
        }

        void StartCapture(int j, int a)
        {
            capJ = j; capA = a;
            KeysChanged();
        }

        void CancelCapture()
        {
            capJ = -1;
            KeysChanged();
        }

        // Enquanto uma tecla espera, Esc cancela; qualquer outra tecla apertada vira a tecla da ação.
        void KeysUpdate()
        {
            if (!keysOpen) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (capJ >= 0) CancelCapture(); else CloseKeys();
                return;
            }
            if (capJ < 0) return;
            for (int i = 0; i < kb.allKeys.Count; i++)
            {
                var kc = kb.allKeys[i];
                if (!kc.wasPressedThisFrame || kc.keyCode == Key.None) continue;
                if (Controles.Reservada(kc.keyCode))
                {
                    keysHint.text = L.F("“{0}” é do jogo (Enter larga). Aperte outra tecla.", Controles.Nome(kc.keyCode));
                    return;
                }
                Controles.Set(capJ, (Controles.Acao)capA, kc.keyCode);
                capJ = -1;
                KeysChanged();
                return;
            }
        }

        // Redesenha a tela de teclas, a ajuda da aba e os cartões (com as teclas novas).
        void KeysChanged()
        {
            for (int j = 0; j < Controles.Jogadores; j++)
                for (int a = 0; a < Controles.Acoes; a++)
                {
                    var b = keyBtns[j, a];
                    if (b == null) continue;
                    bool wait = capJ == j && capA == a;
                    b.text = wait ? L.F("{0}: <b>aperte…</b>", Controles.AcaoNomes[a]) : L.F("{0}: <b>{1}</b>", Controles.AcaoNomes[a], KeyText(Controles.Get(j, (Controles.Acao)a)));
                    b.EnableInClassList("keys-btn--wait", wait);
                }
            if (keysHint != null) keysHint.text = capJ >= 0 ? L.T("Aperte a tecla nova (Esc cancela).") : "";
            if (helpLabel != null)
                helpLabel.text = L.F("<b>Segure</b> Acelerar e o acelerador sobe de 0 a 100%. <b>Soltou, zerou</b>: o carrinho freia.\nRápido demais na curva, a traseira escorrega e a barra de <b>derrapagem</b> enche. Cheia, o carrinho sai da pista.\n<b>Recolocar</b> devolve o carrinho ao trilho, um pouco antes de onde ele saiu.\n<b>Turbo</b>: começa vazio e enche um pouco a cada volta completa e um pouquinho a cada saída de pista (mais para quem está atrás). O tipo de turbo fica no ajuste do carro: explosivo ou eficiente. Segure para andar acima da velocidade máxima até o tanque acabar. Volta com turbo não vale como melhor volta.\n<b>Teclas</b> (acelerar 100% · meia {0}% · recolocar · turbo): jogador 1 {1} · {2} · {3} · {4}; jogador 2 {5} · {6} · {7} · {8}; jogador 3 {9} · {10} · {11} · {12}; jogador 4 {13} · {14} · {15} · {16}. Até onde a meia acelera: arraste o marcador na barra ACELERAR ou use meia − · meia + ({17} · {18} no jogador 1; ◀ ▶ do direcional no controle). Mude no menu inicial, em <b>Opções</b>.", set?.halfGas ?? 60, KeyText(GasKeyAt(0)), KeyText(HalfKeyAt(0)), KeyText(ReslotKeyAt(0)), KeyText(TurboKeyAt(0)), KeyText(GasKeyAt(1)), KeyText(HalfKeyAt(1)), KeyText(ReslotKeyAt(1)), KeyText(TurboKeyAt(1)), KeyText(GasKeyAt(2)), KeyText(HalfKeyAt(2)), KeyText(ReslotKeyAt(2)), KeyText(TurboKeyAt(2)), KeyText(GasKeyAt(3)), KeyText(HalfKeyAt(3)), KeyText(ReslotKeyAt(3)), KeyText(TurboKeyAt(3)), KeyText(Controles.Get(0, Controles.Acao.MeiaMenos)), KeyText(Controles.Get(0, Controles.Acao.MeiaMais)));
            DrawRaceSetup();                                // os cartões mostram as teclas novas
        }

        // Seção recolhida (ajustes e ajuda): fica fora do caminho de quem só quer correr.
        static VisualElement Fold(VisualElement parent, string title)
        {
            var f = new Foldout { text = title, value = false };
            f.AddToClassList("section"); f.AddToClassList("fold");
            parent.Add(f);
            return f;
        }

        // Mostra só os carros que correm: painéis de jogador, fichas no placar e alvos da câmera. Refaz os cartões de piloto.
        void LayoutForCars()
        {
            if (padsBox == null) return;
            int n = sim.N, shown = 0, last = 0;
            for (int k = 0; k < n; k++) if (sim.present[k]) { shown++; last = k; }
            padsBox.EnableInClassList("pads--4", shown > 2);
            padsBox.EnableInClassList("pads--6", shown > 4);
            hudBox.EnableInClassList("hud--6", shown > 4);
            padsBox.EnableInClassList("pads--8", shown > 6);
            hudBox.EnableInClassList("hud--8", shown > 6);
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var on = k < n && sim.present[k] ? DisplayStyle.Flex : DisplayStyle.None;
                pads[k].root.style.display = on;
                hud[k].root.style.display = on;
                pads[k].root.EnableInClassList("pad--last", k == last);
            }
            refreshAdjLabels?.Invoke();
            SplitDirty();
            ApplyCameraSettings();
            DrawRaceSetup();
        }

        // Tamanho, quantidade de carros e os cartões dos pilotos da corrida livre.
        void DrawRaceSetup()
        {
            if (pilotBox == null) return;
            int f = sim.N, cars = RaceCars;
            for (int i = 0; i < sizeBtns.Length; i++) sizeBtns[i].EnableInClassList("btn--on", f == 2 + 2 * i);
            for (int i = 0; i < carBtns.Length; i++)
            {
                carBtns[i].SetEnabled(i < f);                    // não cabe mais carro que trilho
                carBtns[i].EnableInClassList("btn--on", i + 1 == cars);
            }
            pilotBox.Clear();
            int humans = 0;
            for (int k = 0; k < cars; k++)
            {
                int idx = k;
                var sl = set.slots[k];
                var card = El("pcard", pilotBox);
                card.style.borderLeftColor = LaneColors[k];
                var head = El("pcard-head", card);
                Text(L.F("Carro {0}", k + 1), "pcard-name", head);
                var seg = El("segmented pcard-seg", head);
                Btn(L.T("Jogador"), () => SetSlotAi(idx, false), sl.ai ? "btn" : "btn btn--on", seg);
                Btn(L.T("Robô"), () => SetSlotAi(idx, true), sl.ai ? "btn btn--on" : "btn", seg);
                if (sl.ai)
                {
                    var q = new SliderInt(1, RaceSim.AiMaxSkill) { value = Mathf.Clamp(sl.skill, 1, RaceSim.AiMaxSkill), label = L.F("Qualidade: {0}", sl.skill) };
                    q.AddToClassList("setting"); q.AddToClassList("pcard-skill");
                    card.Add(q);
                    var word = Text(Champ.SkillWord(sl.skill), "small", card);
                    // Arrastar não refaz os cartões (isso soltaria o controle): só o rótulo e a IA.
                    q.RegisterValueChangedCallback(e => { sl.skill = e.newValue; set.Save(); q.label = L.F("Qualidade: {0}", sl.skill); word.text = Champ.SkillWord(sl.skill); SetBot(); });
                }
                else
                {
                    // As teclas vão para os humanos na ordem dos carros; do 5º em diante, só controle.
                    int ks = humans++;
                    string pad = PadOf(ks) != null ? L.F(" · controle {0}", ks + 1) : "";
                    Text(ks < Controles.Jogadores ? L.F("Acelera {0} · meia {1} · recoloca {2} · turbo {3}{4}", KeyText(GasKeyAt(ks)), KeyText(HalfKeyAt(ks)), KeyText(ReslotKeyAt(ks)), KeyText(TurboKeyAt(ks)), pad)
                                             : PadOf(ks) != null ? L.F("Controle {0}", ks + 1) : L.T("Só com controle: ligue mais um controle"), "small", card);
                }
                // Ajuste do carro (seção 47): o botão abre o cartão dentro do próprio cartão do piloto.
                Btn(L.F("Carro: <b>{0}</b>  {1}", sl.carro.Resumo, (openCar == k ? "▾" : "▸")), () => { openCar = openCar == idx ? -1 : idx; DrawRaceSetup(); }, "btn pcard-car", card);
                if (openCar == k) DrawCarCard(card, sl.carro, v => { if (Racing) return; sl.carro = v; set.Save(); SetBot(); DrawRaceSetup(); });
            }
            if (cars < f) Text(f - cars == 1 ? L.T("1 trilho fica vazio.") : L.F("{0} trilhos ficam vazios.", f - cars), "small", pilotBox);
            if (cars == 1) Text(L.T("Treino: um carro só, contra o relógio."), "small", pilotBox);
        }

        int openCar = -1;      // qual cartão de piloto está com o ajuste do carro aberto

        // Modelo da bolha (seção 80): os 3 da bolha escolhida; os que a carreira ainda não abriu ficam travados.
        void ModelRow(VisualElement box, CarSetup su, System.Action<CarSetup> change)
        {
            var row = El("segmented car-row", box);
            Text(L.T("Modelo"), "seg-label", row);
            for (int m = 0; m < CarView.ModelsOf(su.bolha); m++)
            {
                int mm = m;
                bool ok = Progresso.ModelOk(su.bolha, m);
                var b = Btn(ok ? CarView.ModelName(su.bolha, m) : "🔒 " + CarView.ModelName(su.bolha, m),
                            () => { var n = su; n.modelo = mm; change(n); }, su.modelo == m ? "btn btn--on" : "btn", row);
                b.SetEnabled(ok);
                b.tooltip = ok ? "" : Progresso.ModelWhere(su.bolha, m);
            }
        }

        // O cartão "Carro": as quatro peças (cada uma com as três opções), o que o ajuste dá e tira contra o padrão e o
        // limite da curva plana para este carro nesta pista. `change` recebe o ajuste novo a cada clique.
        void DrawCarCard(VisualElement parent, CarSetup su, System.Action<CarSetup> change, bool full = true, bool rows = true)
        {
            var box = El("car-card", parent);
            VisualElement Row(string title, string[] names, int cur, System.Func<int, CarSetup> with)
            {
                var row = El("segmented car-row", box);
                Text(title, "seg-label", row);
                foreach (int o in new[] { 1, 0, 2 })
                {
                    int oo = o;
                    Btn(names[o], () => change(with(oo)), cur == o ? "btn btn--on" : "btn", row);
                }
                return row;
            }
            if (rows)
            {
                Row(L.T("Motor"), CarSetup.MotorNames, su.motor, o => { var n = su; n.motor = o; return n; });
                var fr = Row(L.T("Bolha"), CarSetup.BolhaNames, su.bolha, o => { var n = su; n.bolha = o; if (!Progresso.ModelOk(o, n.modelo)) n.modelo = 0; return n; });
                // A Fórmula (seção 93), na mesma linha: só para quem foi campeão do Mundial na carreira.
                {
                    bool ok = Progresso.FormulaOk;
                    var fb = Btn(ok ? CarSetup.BolhaNames[3] : "🔒 " + CarSetup.BolhaNames[3], () => { var n = su; n.bolha = 3; n.modelo = 0; change(n); }, su.bolha == 3 ? "btn btn--on" : "btn", fr);
                    fb.SetEnabled(ok);
                    fb.tooltip = ok ? L.T("Um pouco da asa e um pouco da lisa.") : L.T("Seja campeão do Mundial na carreira para abrir.");
                }
                ModelRow(box, su, change);
                Row(L.T("Pneu"), CarSetup.PneuNames, su.pneu, o => { var n = su; n.pneu = o; return n; });
                Row(L.T("Chassi"), CarSetup.ChassiNames, su.chassi, o => { var n = su; n.chassi = o; return n; });
                Row(L.T("Turbo"), CarSetup.NitroNames, su.nitro, o => { var n = su; n.nitro = o; return n; });
            }
            // Barra a partir do meio: verde para mais, laranja para menos. "range" é a variação que enche meia barra.
            void Stat(string name, float m, float range, string tip)
            {
                var row = El("car-stat", box);
                row.tooltip = tip;
                Text(name, "seg-label car-stat-name", row);
                var bar = El("car-bar", row);
                var fill = El("car-fill", bar);
                float d = Mathf.Clamp((m - 1f) / range, -1f, 1f) * 50f;
                fill.style.left = Length.Percent(d < 0f ? 50f + d : 50f);
                fill.style.width = Length.Percent(Mathf.Abs(d));
                fill.EnableInClassList("car-fill--menos", d < 0f);
                El("car-mid", bar);
                Text(Mathf.Abs(m - 1f) < 0.0005f ? L.T("padrão") : ((m - 1f) * 100f).ToString("+0.#;−0.#") + "%", "small car-stat-val", row);
            }
            Stat(L.T("Final"), su.Top, 0.08f, L.T("Velocidade máxima na reta. Motor longo, bolha lisa e pneu duro."));
            Stat(L.T("Aceleração"), su.Accel, 0.1f, L.T("Quanto o carro embala ao sair da curva. Motor curto."));
            Stat(L.T("Aderência"), su.Grip, 0.06f, L.T("Pneu: segura o carro em qualquer curva, devagar ou rápido. Macio segura mais."));
            Stat(L.T("Aerodinâmica"), 1f + su.Aero, 0.05f, L.T("Bolha com asa: segura o carro na curva, e mais quanto mais rápido ele vai. Não ajuda na curva feita devagar."));
            Stat(L.T("Agilidade"), su.Resp, 0.2f, L.T("Chassi leve: o gatilho e o freio respondem mais rápido."));
            Stat(L.T("Estabilidade"), 1f / su.Slip, 0.15f, L.T("Chassi pesado: demora mais para escapar quando você passa do limite na curva."));
            Text(L.F("Do zero a 95% da final em {0:0.00} s (padrão {1:0.00} s).", RaceSim.Time95(su, set.ramp), RaceSim.Time95(default, set.ramp)), "small", box);
            if (su.chassi != 0)
                Text(su.chassi == 1 ? L.T("Chassi leve: responde rápido, mas escapa fácil no limite da curva.")
                                    : L.T("Chassi pesado: mais lento no gatilho, perdoa o exagero na curva."), "small", box);
            Text(L.F("Turbo {0}: +{1:0}% de velocidade máxima por {2:0.#} s de tanque cheio", CarSetup.NitroNames[su.nitro].ToLowerInvariant(), su.Boost * 100f, su.BurnTime)
                 + (su.nitro == 1 ? L.T(" (mais explosão, menos volume).") : su.nitro == 2 ? L.T(" (menos explosão, mais volume).") : "."), "small", box);
            if (!full) return;
            float sc = sim.Scale;
            var lanes = TrackMath.LanesOf(builder.Faixas);
            float Lim(float r) => Mathf.Round(Mathf.Min(CarSetup.CurveV(sim.set.grip * sim.G * su.Grip, 1f / r, su.Aero, sim.set.vmax * sim.Speed) / (sim.set.vmax * sim.Speed * su.Top), 1f) * 100f);
            Text(L.F("Curva plana: segura até {0}% da final deste carro no trilho de dentro e {1}% no de fora. Meia aceleração: {2}%.", Lim((2.5f + lanes[0]) * sc), Lim((2.5f - lanes[0]) * sc), set.halfGas), "small", box);
        }

        void SetSlotAi(int k, bool ai)
        {
            if (set.slots[k].ai == ai) return;
            set.slots[k].ai = ai; set.Save();
            SetBot(); DrawRaceSetup();
        }

        const float CamBehind = 0.05f;    // meia corda em volta do carrinho: a tangente do trilho ali (Perseguição)
        const float CamAhead = 2.2f;      // distância no mundo à frente do carrinho que a Perseguição olha (sem a escala da pista)
        const float LoopCamLead = 0.6f;   // segundos de antecedência para a câmera ir para o lado do loop
        const float LoopCamMin = 1.5f;    // células: antecedência mínima
        const float LoopCamAfter = 0.5f;  // células depois da saída do loop até voltar para trás do carrinho
        LanePath framedPath;
        Rect trackFrameRect; float trackFrameTop;
        readonly bool[] followMask = new bool[RaceSim.MaxCars];

        // Contorno da pista (amostras do trilho 1) e altura máxima, para as câmeras da Transmissão de TV.
        void FrameTrackForCamera()
        {
            var P = sim.PathOf(0);
            framedPath = P;
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = -lo;
            float top = 0f;
            for (float d = 0f; d < P.Total; d += 0.25f)
            {
                var q = P.At(d);
                lo = Vector2.Min(lo, q.pos); hi = Vector2.Max(hi, q.pos); top = Mathf.Max(top, q.y);
            }
            trackFrameRect = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y); trackFrameTop = top;
            for (int i = 0; i < CamCount; i++) PaneCam(i).SetTrackFrame(trackFrameRect, top, sim.Scale);
        }

        // Quem está na frente (mais distância andada), para a TV seguir.
        int LeaderCar()
        {
            int best = -1;
            for (int k = 0; k < sim.N; k++)
                if (sim.present[k] && (best < 0 || sim.cars[k].s > sim.cars[best].s)) best = k;
            return best;
        }

        // Teclas 1 a 4 escolhem a câmera, na ordem do menu (sem tela dividida: lá cada jogador tem o seu botão).
        static readonly Key[] CamKeysList = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
        void CamKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || IsTyping || keysOpen || SplitOn || panel == null || !panel.RaceMode) return;
            for (int i = 0; i < CamKeysList.Length; i++)
                if (kb[CamKeysList[i]].wasPressedThisFrame) { set.cameraMode = (int)CamModeValues[i]; set.Save(); ApplyCameraSettings(); }
        }

        void BuildCamMenu(VisualElement root)
        {
            camMenu = El("cam-menu", root);
            camBtn = Btn("", () => ShowCamPop(camPop.style.display == DisplayStyle.None), "btn cam-btn", camMenu);
            camPop = El("cam-pop", camMenu);
            Text(L.T("CÂMERA"), "cam-title", camPop);
            for (int i = 0; i < CamModes.Length; i++)
            {
                int m = i;
                var b = Btn("", () => { set.cameraMode = (int)CamModeValues[m]; set.Save(); ApplyCameraSettings(); }, "btn cam-opt", camPop);
                b.text = L.F("<b>{0}  {1}</b>\n<size=10>{2}</size>", i + 1, CamModes[i], CamModeHints[i]);
                camModeBtns[i] = b;
            }
            splitBtn = Btn("", () => { set.splitScreen = !set.splitScreen; set.Save(); ApplyCameraSettings(); }, "btn cam-opt cam-split", camPop);
            camTargetBox = El("cam-sub", camPop);
            Text(L.T("SEGUIR"), "cam-title", camTargetBox);
            var seg = El("segmented", camTargetBox);
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                int t = k;
                camTargetBtns[k] = Btn(CamTargets[k], () => { set.cameraTarget = t; set.Save(); ApplyCameraSettings(); }, "btn", seg);
            }
            camTargetNote = Text(L.T("No online, segue o seu carrinho."), "small", camTargetBox);
            cameraDistanceSlider = new SliderInt(40, 160) { value = Mathf.Clamp(set.cameraDistance, 40, 160) };
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
            if (OnlineRun) return TwoLocal && HeatNow(out _, out _, out _, out _, out var opl) && LaneOf(opl, myId) >= 0 && LaneOf(opl, LocalId(1)) >= 0 ? -1 : MyLane();   // dois daqui na bateria: enquadra os dois
            // Corrida livre com um humano só (os outros são robôs, ou ele corre sozinho): a câmera segue esse humano.
            if (panel != null && panel.CurrentMode == BuilderPanel.Mode.Race)
            {
                int human = -1, humans = 0;
                for (int k = 0; k < RaceCars; k++) if (!set.slots[k].ai) { humans++; human = k; }
                if (humans == 1) return human;
            }
            return -1;
        }

        // Carrinho da Perseguição e da primeira pessoa fora do online: o escolhido em "Seguir", se for de gente;
        // se for robô (o rodízio do campeonato e da carreira muda os trilhos), o primeiro humano. Só robôs: o escolhido.
        int ChaseCar()
        {
            int n = sim != null ? sim.N : 2;
            int t = Mathf.Clamp(set.cameraTarget, 0, n - 1);
            if (sim == null) return t;
            if (sim.present[t] && sim.aiSkill[t] == 0) return t;
            for (int k = 0; k < n; k++) if (sim.present[k] && sim.aiSkill[k] == 0) return k;
            return t;
        }

        void ApplyCameraSettings()
        {
            // Valores antigos (0 de cima, 1 3D, 3 Dinâmica) viram Transmissão de TV.
            if (set.cameraMode == 0) set.cameraMode = (int)TopDownCamera.RaceViewMode.TopFollow;     // a "De cima" antiga
            int mode = System.Array.IndexOf(CamModeValues, (TopDownCamera.RaceViewMode)set.cameraMode);
            if (mode < 0) mode = 0;
            int target = Mathf.Clamp(set.cameraTarget, 0, (sim != null ? sim.N : 2) - 1);
            set.cameraDistance = Mathf.Clamp(set.cameraDistance, 40, 160);
            set.cameraMode = (int)CamModeValues[mode];
            set.cameraTarget = target;

            var applied = panel != null && !panel.RaceMode ? TopDownCamera.RaceViewMode.Atual : CamModeValues[mode];
            cam.SetRaceViewMode(applied);
            int focus = CamFocusCar();
            cam.SetThirdPersonCar(OnlineRun ? (focus >= 0 ? focus : target) : ChaseCar());
            cam.SetDistanceScale(set.cameraDistance / 100f);

            if (camBtn == null) return;
            camBtn.text = L.F("Câmera: <b>{0}</b>  ▾", CamModes[mode]);
            // Na tela dividida, cada jogador escolhe a câmera no canto do seu quadro: o menu geral só mostra a divisão e a distância.
            for (int i = 0; i < camModeBtns.Length; i++)
            {
                camModeBtns[i].EnableInClassList("cam-opt--on", i == mode);
                camModeBtns[i].style.display = SplitOn ? DisplayStyle.None : DisplayStyle.Flex;
            }
            camBtn.text = SplitOn ? L.T("Câmera  ▾") : camBtn.text;
            camTargetBox.style.display = !SplitOn && CamModeValues[mode] != TopDownCamera.RaceViewMode.Tv && CamModeValues[mode] != TopDownCamera.RaceViewMode.TopFollow ? DisplayStyle.Flex : DisplayStyle.None;
            splitBtn.text = L.F("Tela dividida: <b>{0}</b>\n<size=10>2 a 4 jogadores no mesmo computador: um quadro para cada</size>", (set.splitScreen ? L.T("sim") : L.T("não")));
            splitBtn.EnableInClassList("cam-opt--on", set.splitScreen);
            splitBtn.style.display = OnlineRun && !TwoLocal ? DisplayStyle.None : DisplayStyle.Flex;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                camTargetBtns[k].EnableInClassList("btn--on", k == target);
                camTargetBtns[k].style.display = OnlineRun || k >= sim.N ? DisplayStyle.None : DisplayStyle.Flex;
            }
            camTargetNote.style.display = OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
            cameraDistanceSlider.SetValueWithoutNotify(set.cameraDistance);
            cameraDistanceSlider.label = L.F("Distância: {0}%", set.cameraDistance);
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
            p.root = El("pad pad--" + LaneCls[k], padsBox);
            var head = El("pad-head", p.root);
            p.name = Text(L.F("JOGADOR {0}", k + 1), "pad-name", head);
            p.state = Text(L.T("No grid"), "pad-state", head);
            var body = El("pad-body", p.root);

            // Acelerar: segurar com o mouse ou o dedo (ou a tecla) mantém o acelerador subindo.
            var gas = p.gas = El("gas", body);
            p.fill = El("gas-fill", gas); p.fill.pickingMode = PickingMode.Ignore;
            p.gasText = Text(L.F("ACELERAR  {0}  ·  {1}% {2}", KeyText(GasKeyAt(k)), HalfOf(k), KeyText(HalfKeyAt(k))), "gas-text", gas);
            p.gasText.pickingMode = PickingMode.Ignore;
            int idx = k;
            gas.RegisterCallback<PointerDownEvent>(e => { uiHeld[idx] = true; gas.CapturePointer(e.pointerId); e.StopPropagation(); });
            gas.RegisterCallback<PointerUpEvent>(e => { uiHeld[idx] = false; gas.ReleasePointer(e.pointerId); });
            gas.RegisterCallback<PointerCaptureOutEvent>(_ => uiHeld[idx] = false);

            // Marcador da meia aceleração: arrastar muda até onde a meia acelera (não acelera o carro).
            var half = p.half = El("half-mark", gas);
            gas.Insert(1, half);                                   // atrás do texto, na frente do preenchimento
            El("half-line", half).pickingMode = PickingMode.Ignore;
            p.halfTri = Text(HalfTri(k), "half-tri", half); p.halfTri.pickingMode = PickingMode.Ignore;
            half.style.left = Length.Percent(HalfOf(k));
            void Drag(IPointerEvent e) => SetHalf(idx, Mathf.RoundToInt(gas.WorldToLocal(e.position).x / Mathf.Max(1f, gas.layout.width) * 100f));
            half.RegisterCallback<PointerDownEvent>(e => { half.CapturePointer(e.pointerId); Drag(e); e.StopPropagation(); });
            half.RegisterCallback<PointerMoveEvent>(e => { if (half.HasPointerCapture(e.pointerId)) Drag(e); });
            half.RegisterCallback<PointerUpEvent>(e => { half.ReleasePointer(e.pointerId); e.StopPropagation(); });

            var side = El("pad-side", body);
            Text(L.T("Velocidade"), "meter-label", side);
            p.speed = El("meter-fill", El("meter", side));
            Text(L.T("Derrapagem"), "meter-label", side);
            p.slip = El("meter-fill", El("meter", side));
            // Turbo: a barra é o tanque e também o botão (segurar com o mouse ou o dedo gasta o turbo).
            p.turbo = El("turbo", side);
            p.turboFill = El("turbo-fill", p.turbo); p.turboFill.pickingMode = PickingMode.Ignore;
            p.turboText = Text(L.T("TURBO"), "turbo-text", p.turbo); p.turboText.pickingMode = PickingMode.Ignore;
            var tbo = p.turbo;
            tbo.RegisterCallback<PointerDownEvent>(e => { uiTurbo[idx] = true; tbo.CapturePointer(e.pointerId); e.StopPropagation(); });
            tbo.RegisterCallback<PointerUpEvent>(e => { uiTurbo[idx] = false; tbo.ReleasePointer(e.pointerId); });
            tbo.RegisterCallback<PointerCaptureOutEvent>(_ => uiTurbo[idx] = false);
            p.reslot = Btn(L.F("Recolocar  {0}", KeyText(ReslotKeyAt(k))), () => ReslotPressed(idx), "btn reslot", side);
            return p;
        }

        TrackFilter pickFilter;

        // Conta as fechadas de cada tipo e põe no filtro.
        static void CountFilter(TrackFilter f, List<Option> opts)
        {
            int mine = 0, down = 0;
            foreach (var o in opts) if (o.closed) { if (TrackFilter.Downloaded(o.save)) down++; else mine++; }
            f.Counts(mine, down);
        }

        void DrawPicker(List<Option> opts)
        {
            pickList.Clear();
            CountFilter(pickFilter, opts);
            int open = 0;
            foreach (var o in opts)
            {
                if (!o.closed) { open++; continue; }             // pista aberta não corre: nem aparece
                if (!pickFilter.Shows(o.save)) continue;
                var opt = o;
                var item = new Button(() => { if (!Racing) { UseTrack(opt.key); sim.Reset(); scoreDirty = true; } });
                item.AddToClassList("lib-item");
                item.AddToClassList("pick");
                if (o.key == set.track) item.AddToClassList("lib-item--on");
                item.Add(new TrackThumb(o.save));
                var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                Text(o.name, "lib-name", texts).pickingMode = PickingMode.Ignore;
                Text((o.sub != null ? o.sub + " · " : "") + TrackLine(o.save), "small", texts).pickingMode = PickingMode.Ignore;
                pickList.Add(item);
            }
            if (pickNote != null)
            {
                pickNote.text = open == 0 ? "" : open == 1 ? L.T("1 pista aberta não aparece: feche o circuito no Construtor.") : L.F("{0} pistas abertas não aparecem: feche os circuitos no Construtor.", open);
                pickNote.style.display = open > 0 ? DisplayStyle.Flex : DisplayStyle.None;
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
            Row(new[] { L.T("Trilho"), sim.ByTime ? L.T("Distância") : L.T("Voltas"), L.T("Última"), L.T("Melhor"), L.T("Saídas"), L.T("Toques") }, "score-head");
            for (int k = 0; k < sim.N; k++)
            {
                if (!sim.present[k]) continue;                  // trilho vazio não entra no placar
                var c = sim.cars[k];
                Row(new[] { $"{k + 1}{(sim.winner == k ? " · 1º" : "")}", LapsCell(k), Fmt(c.lastLap), Fmt(c.best), c.offs.ToString(), c.hits.ToString() },
                    "score-" + LaneCls[k] + (sim.winner == k ? " score-win" : ""));
            }
            raceHint.text = !sim.closed ? L.T("Esta pista não é um circuito fechado. Escolha outra acima ou feche o traçado no Construtor.")
                : sim.phase == RaceSim.Phase.Idle ? (RaceCars == 1 ? L.T("O carrinho está no grid.") : RaceCars == 2 ? L.T("Os dois carrinhos estão no grid.") : L.F("Os {0} carrinhos estão no grid.", RaceCars)) + L.T(" Aperte Largar e espere as luzes.")
                : sim.phase == RaceSim.Phase.Countdown ? L.T("Pode segurar o acelerador: o carrinho sai quando a luz ficar verde.")
                : sim.phase == RaceSim.Phase.Switch ? L.T("Fim da manche: cada carrinho passa para o trilho seguinte.")
                : sim.phase == RaceSim.Phase.Running ? (sim.ByTime ? L.F("Corrida por tempo, {0}: vence quem andar mais em {1}.", MancheText, Dur(set.secs)) : L.F("Vence quem completar {0} {1} primeiro.", set.laps, (set.laps == 1 ? L.T("volta") : L.T("voltas"))))
                : L.T("Prova encerrada. Aperte Largar para correr de novo.");
            startBtn.text = sim.phase == RaceSim.Phase.Idle ? L.T("Largar (Enter)") : L.T("Nova largada (Enter)");
            startBtn.SetEnabled(sim.closed);
        }

        void DrawStatus()
        {
            if (!panel.RaceMode) return;
            if (ChampLike) { ChampStatus(); return; }
            if (panel.CareerMode) return;
            if (!sim.closed) panel.SetStatus(L.T("Feche o circuito no Construtor para correr"), "status--warn");
            else if (sim.winner >= 0) panel.SetStatus(L.F("{0} venceu", LaneNames[sim.winner]), "status--ok");
            else if (sim.phase == RaceSim.Phase.Countdown) panel.SetStatus(L.T("Atenção à largada"), "");
            else if (sim.phase == RaceSim.Phase.Switch) panel.SetStatus(L.T("Troca de trilho"), "");
            else if (sim.phase == RaceSim.Phase.Running) panel.SetStatus(sim.ByTime ? L.F("Corrida · {0} · faltam {1}", MancheText, Fmt(sim.TimeLeft)) : L.F("Corrida · volta {0} de {1}", Mathf.Min(MaxLaps() + 1, set.laps), set.laps), "");
            else panel.SetStatus(L.T("Pronto para largar"), "");
        }

        int MaxLaps()
        {
            int m = 0;
            foreach (var c in sim.cars) m = Mathf.Max(m, c.laps);
            return m;
        }

        void UpdatePads()
        {
            padsBox.style.left = SplitOn ? 0f : panel.SideWidth + 12f;
            for (int k = 0; k < sim.N; k++)
            {
                var c = sim.cars[k];
                var p = pads[k];
                p.fill.style.width = Length.Percent(c.T * 100f);
                p.speed.style.width = Length.Percent(Mathf.Min(100f, c.v / (sim.set.vmax * sim.Speed * sim.setup[k].Top) * 100f));
                p.slip.style.width = Length.Percent(Mathf.Min(100f, c.slip * 100f));
                p.slip.style.backgroundColor = c.slip < 0.45f ? new Color(0.24f, 0.75f, 0.45f) : c.slip < 0.75f ? new Color(0.96f, 0.76f, 0.1f) : new Color(0.94f, 0.48f, 0.23f);
                bool off = c.mode == CarMode.Off, ai = sim.aiSkill[k] > 0;
                p.turbo.style.display = sim.set.turbo ? DisplayStyle.Flex : DisplayStyle.None;
                p.turboFill.style.width = Length.Percent(Mathf.Clamp01(c.turbo) * 100f);
                p.turbo.EnableInClassList("turbo--on", c.boost && c.turbo > 0f && c.mode == CarMode.OnSlot);
                p.turbo.EnableInClassList("turbo--empty", c.turbo <= 0f);
                Key tk = OnlineRun ? TurboKeyAt(0) : TurboKeyAt(KeySet(k));
                int tpc = Mathf.RoundToInt(c.turbo * 100f);
                p.turboText.text = ai || tk == Key.None ? L.F("TURBO  {0}%", tpc) : L.F("TURBO  {0}  ·  {1}%", KeyText(tk), tpc);
                if (!ai) p.reslot.text = L.F("Recolocar  {0}", KeyText(OnlineRun ? ReslotKeyAt(0) : ReslotKey(k)));
                if (!ai) { p.gasText.text = GasLabel(k); p.halfTri.text = HalfTri(k); }         // muda com o rodízio e quando um controle é ligado
                p.reslot.SetEnabled(off && !ai);                   // a IA se recoloca sozinha
                p.reslot.EnableInClassList("reslot--ready", off && !ai);
                p.root.EnableInClassList("pad--held", c.held);
                if (p.flashT > 0f) p.flashT -= Time.deltaTime;
                bool flash = p.flashT > 0f && !off;
                p.state.EnableInClassList("pad-state--rec", flash && p.flashRec);
                p.state.EnableInClassList("pad-state--pb", flash && !p.flashRec);
                int nV = sim.set.laps;
                p.state.text = flash ? p.flash
                    : c.finish != null ? (sim.winner == k ? L.T("Venceu!") : L.T("Chegou"))
                    : sim.phase == RaceSim.Phase.Done ? L.T("Não terminou")
                    : off ? L.T("Fora da pista!")
                    : c.mode == CarMode.Returning ? L.T("Voltando ao trilho")
                    : c.hitFx > 0f ? L.T("Batida!")
                    : sim.phase == RaceSim.Phase.Countdown ? L.T("Aguarde a luz verde")
                    : sim.phase == RaceSim.Phase.Switch ? L.F("Vai para o trilho {0}", (c.lane + 1) % sim.N + 1)
                    : sim.phase == RaceSim.Phase.Running && sim.ByTime ? L.F("{0} voltas · trilho {1}", LapsCell(k), c.lane + 1)
                    : sim.phase == RaceSim.Phase.Running ? L.F("Volta {0} de {1}", Mathf.Min(c.laps + 1, nV), nV)
                    : L.T("No grid");
                p.state.EnableInClassList("pad-state--alert", off);
            }
        }

        // Largada: recolhe o painel lateral para a mesa ficar inteira. Chegada (ou volta ao grid): devolve.
        // Se o jogador abriu o painel no meio da prova (Tab), ele fica aberto.
        void SideForPhase()
        {
            if (panel.CareerMode) return;                // a carreira já esconde o painel
            bool racing = Racing;
            if (racing && panel.SideVisible) { sideAutoHidden = true; panel.SetSideFromRace(false); }
            else if (!racing && sideAutoHidden) { sideAutoHidden = false; panel.SetSideFromRace(true); }
        }

        // Ordem da prova: quem chegou antes; senão mais voltas; senão mais adiante na volta.
        // Na pista de 4 os trilhos têm comprimentos diferentes: "mais adiante" é a fração da volta, não a distância.
        readonly List<int> order = new List<int>();
        List<int> Order()
        {
            order.Clear();
            for (int k = 0; k < sim.N; k++) if (sim.present[k]) order.Add(k);
            if (order.Count == 0) order.Add(0);
            order.Sort((i, j) =>
            {
                var a = sim.cars[i]; var b = sim.cars[j];
                if (a.finish != null || b.finish != null)
                    return a.finish == null ? 1 : b.finish == null ? -1 : a.finish.Value.CompareTo(b.finish.Value) != 0 ? a.finish.Value.CompareTo(b.finish.Value) : i.CompareTo(j);
                // Distância: bandeiradas + fração da volta (vale nos dois modos e em trilhos de comprimentos diferentes).
                int f = sim.Dist(j).CompareTo(sim.Dist(i));
                return f != 0 ? f : i.CompareTo(j);
            });
            return order;
        }

        int Leader() => Order()[0];

        // O nome do painel sem o trilho (" · ESQUERDA") e sem o resumo do ajuste ("  <size=10>…"), que "JOGADOR 1" tem sem trilho.
        static string ShortName(string nm)
        {
            int cut = nm.IndexOf(" · ");
            int aj = nm.IndexOf("  <size");
            if (aj >= 0 && (cut < 0 || aj < cut)) cut = aj;
            return cut > 0 ? nm.Substring(0, cut) : nm;
        }

        void UpdateHud()
        {
            bool show = panel.RaceMode && sim.closed && sim.phase != RaceSim.Phase.Idle && RaceUI;
            hudBox.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            int nV = sim.set.laps;
            var ord = Order();
            int lead = ord[0], fast = FastestCar();
            for (int k = 0; k < sim.N; k++)
            {
                var c = sim.cars[k]; var h = hud[k];
                h.name.text = ShortName(pads[k].name.text);
                h.dot.style.backgroundColor = pads[k].root.resolvedStyle.borderTopColor;
                h.pos.text = sim.winner == k ? "🏁" : L.F("{0}º", ord.IndexOf(k) + 1);
                h.root.EnableInClassList("hud-card--lead", k == lead);
                h.lap.text = sim.ByTime ? L.F("{0} v · T{1}", LapsCell(k), c.lane + 1) : c.finish != null ? L.T("Chegou") : L.F("Volta {0}/{1}", Mathf.Clamp(c.laps + 1, 1, nV), nV);
                h.best.text = c.best != null ? (k == fast ? $"★ {Fmt(c.best)}" : L.F("Melhor {0}", Fmt(c.best))) : "";
                h.best.EnableInClassList("hud-best--top", k == fast);
                h.root.EnableInClassList("hud-card--fast", k == fast);
                // Diferença para o da frente, em segundos (quanto tempo faz que ele passou por onde este está agora,
                // pelo histórico das etiquetas: TagGap). Uma volta ou mais atrás: "+1 v". O líder: "LÍDER".
                int pos = ord.IndexOf(k);
                bool going = sim.phase == RaceSim.Phase.Running && c.finish == null;
                string gap = "";
                if (going && pos == 0) gap = L.T("LÍDER");
                else if (going && pos > 0)
                {
                    int front = ord[pos - 1];
                    float dl = sim.Dist(front) - sim.Dist(k);
                    float g = dl >= 1f ? -1f : TagGap(front, k);
                    // Sem histórico (começo da manche, por tempo): a distância vezes a volta do da frente.
                    float? refLap = sim.cars[front].best ?? sim.cars[front].lastLap;
                    if (g < 0f && dl < 1f && refLap != null) g = Mathf.Max(0f, dl) * refLap.Value;
                    gap = dl >= 1f ? L.F("+{0} v", Mathf.FloorToInt(dl)) : g >= 0f ? $"+{Num(g, 1)}" : "—";
                }
                h.cur.text = gap;
                h.cur.EnableInClassList("hud-cur--lider", pos == 0);
                // Torre: cada ficha na altura da posição; a transição de "top" (USS) anima as ultrapassagens.
                h.root.style.top = ord.IndexOf(k) * HudRow(h);
            }
            hudBox.style.height = sim.N * HudRow(hud[0]);
        }

        static float HudRow(HudCard h) => Mathf.Max(26f, h.root.resolvedStyle.height) + 4f;

        // 3 · 2 · 1 acompanhando as luzes vermelhas; no primeiro segundo da prova, VAI! em verde.
        void UpdateCountdown()
        {
            // Por tempo, entre as manches: "TROCA DE TRILHO" no meio da mesa; o "VAI!" vale para cada manche.
            bool sw = sim.phase == RaceSim.Phase.Switch;
            bool cd = sim.phase == RaceSim.Phase.Countdown, go = sim.phase == RaceSim.Phase.Running && sim.t - sim.Manche * (sim.ByTime ? sim.MancheLen : 0f) < 0.8f;
            if (sw)
            {
                cdBox.style.display = panel.RaceMode ? DisplayStyle.Flex : DisplayStyle.None;
                cdBox.style.left = panel.SideWidth;
                cdText.text = L.F("TROCA DE TRILHO\n<size=40%>{0}</size>", L.F("manche {0} de {1}", Mathf.Min(sim.Manche + 2, sim.N), sim.N));
                cdText.EnableInClassList("cd-text--go", false);
                cdText.EnableInClassList("cd-text--switch", true);
                cdText.style.scale = new Scale(Vector2.one);
                cdText.style.opacity = 1f;
                DrawGaps(true);
                return;
            }
            cdBox.style.display = panel.RaceMode && (cd || go) ? DisplayStyle.Flex : DisplayStyle.None;
            DrawGaps(cd && sim.ByTime && sim.Manche > 0);       // continua na contagem da manche seguinte
            if (!(cd || go)) return;
            cdBox.style.left = panel.SideWidth;
            float step = RaceSim.Count / 3f;
            int n = cd ? 3 - Mathf.Min(2, Mathf.FloorToInt(sim.t / step)) : 0;
            float since = sim.t - sim.Manche * (sim.ByTime ? sim.MancheLen : 0f);
            float f = cd ? (sim.t % step) / step : since / 0.8f;      // 0→1 dentro de cada número
            cdText.EnableInClassList("cd-text--switch", false);
            cdText.text = cd ? n.ToString() : L.T("VAI!");
            cdText.EnableInClassList("cd-text--go", !cd);
            cdText.style.scale = new Scale(Vector2.one * Mathf.Lerp(1.35f, 1f, Mathf.Min(1f, f * 3f)));
            cdText.style.opacity = cd ? 1f : 1f - f;
        }

        // Na troca de trilho (corrida por tempo), embaixo do aviso e do 3, 2, 1: a ordem, as voltas de cada um e quanto
        // falta para o da frente e para o líder, em voltas (a mesma conta do Dist: bandeiradas + fração da volta).
        void DrawGaps(bool show)
        {
            string sig = null;
            var ord = Order();
            if (show)
            {
                var sb = new System.Text.StringBuilder();
                foreach (int k in ord) sb.Append(k).Append(':').Append(Num(sim.Dist(k), 2)).Append('|');
                sig = sb.ToString();
            }
            if (sig == cdListSig) return;
            cdListSig = sig;
            cdList.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            cdList.Clear();
            if (!show) return;
            float lead = sim.Dist(ord[0]);
            for (int pos = 0; pos < ord.Count; pos++)
            {
                int k = ord[pos];
                float d = Mathf.Max(0f, sim.Dist(k));
                var r = El("res-row res-row--small" + (pos == 0 ? " res-row--win" : ""), cdList);
                Text(L.F("{0}º", pos + 1), "res-pos", r);
                El("hud-dot", r).style.backgroundColor = pads[k].root.resolvedStyle.borderTopColor;
                Text(hud[k].name.text, "res-name", r);
                Text(L.F("{0} v", Num(d, 2)), "res-cell", r);
                Text(pos == 0 ? L.T("líder") : L.F("−{0} do líder · −{1} do {2}º", Num(lead - sim.Dist(k), 2), Num(sim.Dist(ord[pos - 1]) - sim.Dist(k), 2), pos), "res-cell res-cell--dim", r);
            }
        }

        // Resultado da prova dentro do aviso: as duas posições com tempo total, melhor volta e saídas,
        // e os botões. No online não há botões (quem comanda a sala é o host, pela tela do campeonato).
        bool resShown;
        string resSig;
        void SetResult(bool show)
        {
            string sig = null;
            if (show)
            {
                var sb = new System.Text.StringBuilder($"{sim.winner}|{panel.CurrentMode}");
                foreach (var c in sim.cars) sb.Append('|').Append(c.finish).Append('|').Append(c.offs);
                sig = sb.ToString();
            }
            if (show) PayRace(sig);
            if (show == resShown && sig == resSig) return;
            resShown = show; resSig = sig;
            DrawResult(show);
        }

        void DrawResult(bool show)
        {
            resTable.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            resBtns.style.display = show && !OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
            DrawRate(show);
            banner.pickingMode = show && !OnlineRun ? PickingMode.Position : PickingMode.Ignore;
            if (!show) return;
            resTable.Clear();
            var ord = new List<int>(Order());
            int fast = FastestCar(), recCar = ChampLike ? RecordCar() : -1;
            Pilot[] elop = null;
            if (MmRoom && OnlineRun && sim.phase == RaceSim.Phase.Done) { EloApply(false); HeatNow(out _, out _, out _, out _, out elop); }
            for (int pos = 0; pos < ord.Count; pos++)
            {
                int k = ord[pos];
                var c = sim.cars[k];
                var r = El("res-row" + (k == sim.winner ? " res-row--win" : ""), resTable);
                Text(L.F("{0}º", pos + 1), "res-pos", r);
                El("hud-dot", r).style.backgroundColor = pads[k].root.resolvedStyle.borderTopColor;
                Text(hud[k].name.text, "res-name", r);
                if (elop != null && k < elop.Length) EloCell(elop[k], r);
                Text(sim.ByTime ? L.F("{0} voltas", LapsCell(k)) : c.finish != null ? Fmt(c.finish) : L.T("não terminou"), "res-cell", r);
                if (k == fast) Text(L.T("★ melhor volta ") + Fmt(c.best), "res-cell res-cell--fast", r);
                else Text(L.T("melhor ") + Fmt(c.best), "res-cell res-cell--dim", r);
                Text(c.offs == 1 ? L.T("1 saída") : L.F("{0} saídas", c.offs), "res-cell res-cell--dim", r);
                if (k == recCar) Text(L.T("RECORDE"), "res-badge", r);
            }
            if (earnNote != null && coinSig == resSig) Text(earnNote, lastEarn > 0 ? "res-coins" : "res-coins res-coins--none", resTable);
            if (ChampLike)
            {
                bool over = !HeatNow(out _, out _, out _, out _, out _);
                resAgain.style.display = over ? DisplayStyle.None : DisplayStyle.Flex;
                resAgain.text = L.T("Salvar e seguir  (Enter)");
                resNext.style.display = DisplayStyle.None;
                DrawChampStandings(resTable);
            }
            else
            {
                resAgain.style.display = DisplayStyle.Flex;
                resAgain.text = L.T("Correr de novo  (Enter)");
                resNext.style.display = DisplayStyle.Flex;
            }
        }

        // ---------- Nota da pista (seção 83) ----------

        VisualElement rateBox;
        string rateHash;
        int ratingsSeen; float ratingsAt;

        // No resultado de cada corrida online (campeonato e partida rápida), embaixo da classificação: 1 a 5 estrelas
        // para a pista, com a média de todos. Só com a Steam (a nota fica no placar dela).
        void DrawRate(bool show)
        {
            string hash = show && !carPickShown && OnlineRun && TrackRatings.Available && HeatNow(out _, out _, out _, out var st, out _) ? st.hash : null;
            rateHash = hash;
            rateBox.style.display = hash != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (hash == null) return;
            rateBox.Clear();
            var i = TrackRatings.Get(hash);
            Text(i.mine > 0 ? L.T("Sua nota para esta pista (clique para trocar)") : L.T("Que nota você dá para esta pista?"), "rate-title", rateBox);
            var row = El("rate-stars", rateBox);
            for (int n = 1; n <= TrackRatings.Max; n++)
            {
                int v = n;
                var b = Btn(n <= i.mine ? "★" : "☆", () => { TrackRatings.Rate(hash, v); DrawRate(true); panel.Toast(L.F("Nota {0} enviada.", v)); }, "rate-star", row);
                b.EnableInClassList("rate-star--on", n <= i.mine);
                b.tooltip = (v == 1 ? L.F("{0} estrela", v) : L.F("{0} estrelas", v));
            }
            Text(!i.done ? L.T("lendo as notas…") : i.count > 0 ? L.F("média ★ {0} · {1} {2}", Num(i.avg), i.count, (i.count == 1 ? L.T("nota") : L.T("notas"))) : L.T("ainda sem notas: a sua é a primeira"), "rate-sub", rateBox);
        }

        // As notas chegam aos poucos: refaz o cartão do resultado e o lobby online, no máximo uma vez por segundo.
        void RatingsRedraw()
        {
            if (ratingsSeen == TrackRatings.Version || Time.unscaledTime - ratingsAt < 1f || panel == null) return;
            ratingsSeen = TrackRatings.Version; ratingsAt = Time.unscaledTime;
            if (rateHash != null) DrawRate(true);
            if (room != null && !OnlineRun) DrawLobby();
        }

        void UpdateTop()
        {
            topBox.style.left = panel.SideWidth;
            hudBox.style.left = panel.SideWidth + 12f;
            UpdateHud();
            UpdateRaceClock();
            UpdateCountdown();
            bool showLights = sim.phase == RaceSim.Phase.Countdown || (sim.phase == RaceSim.Phase.Running && sim.t - sim.Manche * (sim.ByTime ? sim.MancheLen : 0f) < 1f);
            lights.style.display = showLights ? DisplayStyle.Flex : DisplayStyle.None;
            // O semáforo 3D do pórtico acompanha as luzes da tela.
            TrackPieceView.SetStartLights(sim.phase == RaceSim.Phase.Countdown ? Mathf.Min(3, Mathf.FloorToInt(sim.t / (RaceSim.Count / 3f)) + 1) : 0, sim.phase == RaceSim.Phase.Running && showLights);
            if (showLights)
            {
                int n = sim.phase == RaceSim.Phase.Countdown ? Mathf.Min(3, Mathf.FloorToInt(sim.t / (RaceSim.Count / 3f)) + 1) : 0;
                for (int i = 0; i < 3; i++)
                {
                    lightDots[i].EnableInClassList("light--red", sim.phase == RaceSim.Phase.Countdown && i < n);
                    lightDots[i].EnableInClassList("light--green", sim.phase == RaceSim.Phase.Running);
                }
            }
            // Antes do 3, 2, 1: os recordes da pista (e de cada piloto da bateria).
            if (ChampLike) CarPickTick();
            StartBarUI(showLights);
            if (PauseUI()) return;
            if (panel.CareerMode) { banner.style.display = DisplayStyle.None; SetResult(false); return; }   // a carreira tem o resultado dela
            if (CarPickUI(showLights)) return;
            bool card = StartCard(out string cardTitle) && !showLights;
            DrawRecCard(card);
            bannerSub.style.display = DisplayStyle.Flex;
            bannerRec.style.display = DisplayStyle.None;
            if (ChampLike)
            {
                bool champShow = ChampBanner(out string title, out string sub) && !showLights;
                if (card && !champShow) { champShow = true; title = cardTitle; sub = ""; }
                banner.style.display = champShow ? DisplayStyle.Flex : DisplayStyle.None;
                if (champShow) { bannerTitle.text = title; bannerSub.text = sub; BannerColor(-1); bannerSub.style.display = sub == "" ? DisplayStyle.None : DisplayStyle.Flex; }
                if (champShow && sim.phase == RaceSim.Phase.Done && champRecText != null) { bannerRec.text = champRecText; bannerRec.style.display = DisplayStyle.Flex; }
                SetResult(champShow && sim.phase == RaceSim.Phase.Done && sim.winner >= 0);
                if (card) DrawResult(false);                 // os botões do resultado começam visíveis
                return;
            }
            if (card && sim.winner < 0)
            {
                banner.style.display = DisplayStyle.Flex;
                SetResult(false); DrawResult(false);
                bannerTitle.text = cardTitle; bannerSub.style.display = DisplayStyle.None; BannerColor(-1);
                return;
            }
            bool showBanner = !showLights && (sim.winner >= 0 || !sim.closed);
            banner.style.display = showBanner ? DisplayStyle.Flex : DisplayStyle.None;
            SetResult(showBanner && sim.closed);
            if (!showBanner) return;
            if (!sim.closed) { bannerTitle.text = L.T("Pista aberta"); bannerSub.text = L.T("Escolha um circuito fechado na lista."); BannerColor(-1); return; }
            var w = sim.cars[sim.winner];
            bannerTitle.text = L.F("{0} venceu", pads[sim.winner].name.text);
            BannerColor(sim.winner);
            bannerSub.text = $"{LaneNames[sim.winner]} · " + (sim.ByTime ? L.F("{0} voltas em {1} · melhor volta {2}", LapsCell(sim.winner), Dur(sim.set.secs), Fmt(w.best)) : L.F("{0} · melhor volta {1}", Fmt(w.finish), Fmt(w.best)));
        }

        // O carro com a melhor volta da prova (gente ou IA): fica em roxo no placar e no resultado. −1: ninguém ainda.
        int FastestCar()
        {
            int f = -1;
            for (int k = 0; k < sim.N; k++)
                if (sim.present[k] && sim.cars[k].best != null && (f < 0 || sim.cars[k].best < sim.cars[f].best)) f = k;
            return f;
        }

        // Online, com a corrida parada: a barra que esvazia até o host largar sozinho (Enter do host larga na hora).
        VisualElement startBar, startBarFill;
        Label startBarText;
        void StartBarUI(bool showLights)
        {
            float wait = 0f, left = OnlineRun && !showLights && !panel.CareerMode && sim.phase == RaceSim.Phase.Idle ? AutoLeftF(out wait) : -1f;
            bool on = left >= 0f && HeatNow(out _, out _, out _, out _, out _);
            startBar.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            int n = Mathf.CeilToInt(left);
            startBarText.text = IsHost ? L.F("Largada em {0} s · Enter larga agora", n) : L.F("Largada em {0} s", n);
            startBarFill.style.width = Length.Percent(100f * left / wait);
        }

        void BannerColor(int lane)
        {
            for (int k = 0; k < LaneCls.Length; k++) bannerTitle.EnableInClassList("banner-title--" + LaneCls[k], k == lane);
        }

        // ---------- Corrida por tempo (seção 26) ----------

        // Relógio: por voltas, o tempo de prova; por tempo, quanto falta (o total, antes da largada).
        string ClockText()
        {
            if (sim.ByTime) return sim.phase == RaceSim.Phase.Idle ? Fmt(sim.set.secs) : Fmt(sim.TimeLeft);
            return Fmt(sim.phase == RaceSim.Phase.Idle || sim.phase == RaceSim.Phase.Countdown ? 0f : sim.t);
        }

        // Placar do topo: por voltas, a volta (a do jogador, se só um corre neste computador; senão a do líder) e o tempo
        // de prova; por tempo, quanto falta (vermelho nos últimos 10 s) e a manche.
        void UpdateRaceClock()
        {
            bool show = sim.closed && RaceUI && (sim.phase == RaceSim.Phase.Countdown || sim.phase == RaceSim.Phase.Running || sim.phase == RaceSim.Phase.Switch);
            raceClock.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            bool cd = sim.phase == RaceSim.Phase.Countdown;
            if (sim.ByTime)
            {
                float left = cd ? sim.set.secs : sim.TimeLeft;
                raceClockBig.text = Clock(left);
                raceClockSub.text = sim.N > 1 ? L.F("faltam · {0}", MancheText) : "faltam";
                raceClock.EnableInClassList("race-clock--fim", !cd && left <= 10f);
                return;
            }
            int nV = sim.set.laps, me = -1, mine = 0;
            for (int k = 0; k < sim.N; k++) if (sim.present[k] && TagMine(k)) { me = k; mine++; }
            int who = mine == 1 ? me : Order()[0];
            int lap = Mathf.Clamp(sim.cars[who].laps + 1, 1, nV);
            bool last = !cd && lap == nV && sim.cars[who].finish == null;
            raceClockBig.text = last ? L.T("ÚLTIMA VOLTA") : L.F("VOLTA {0}/{1}", lap, nV);
            raceClockSub.text = Clock(cd ? 0f : sim.t) + (mine == 1 ? "" : L.T(" · líder"));
            raceClock.EnableInClassList("race-clock--fim", last);
        }

        // 1:05 · 0:09 (relógio do placar do topo).
        static string Clock(float s)
        {
            int t = Mathf.CeilToInt(Mathf.Max(0f, s) - 0.001f);
            return $"{t / 60}:{t % 60:00}";
        }

        // Distância em voltas com uma casa (por tempo) ou "voltas/total" (por voltas).
        string LapsCell(int k) => sim.ByTime ? Num(Mathf.Max(0f, sim.Dist(k)), 2) : $"{sim.cars[k].laps}/{sim.set.laps}";
        string MancheText => L.F("manche {0} de {1}", Mathf.Min(sim.Manche + 1, sim.N), sim.N);
        static string Dur(int secs) => secs % 60 == 0 ? L.F("{0} min", secs / 60) : $"{secs / 60}:{secs % 60:00}";
        bool Racing => sim.phase == RaceSim.Phase.Countdown || sim.phase == RaceSim.Phase.Running || sim.phase == RaceSim.Phase.Switch;

        internal static string Fmt(float? s)
        {
            if (s == null) return "—";
            float v = s.Value;
            if (v < 60f) return Num(v, 2) + L.T(" s");
            int m = Mathf.FloorToInt(v / 60f);
            return m + ":" + Num(v - m * 60f, 2).PadLeft(5, '0');
        }
    }
}
