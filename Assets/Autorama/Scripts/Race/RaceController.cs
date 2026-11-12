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
                else if (KeyPressed(kb, HalfKeyAt(ks))) g = Mathf.Max(g, set.halfGas / 100f);
            }
            return Mathf.Max(g, Trigger(PadOf(ks)));
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
            if (GasKey(k) == Key.None) return PadOf(KeySet(k)) != null ? "ACELERAR  ·  controle" : "PRECISA DE UM CONTROLE";     // 5º e 6º humano
            string pad = PadOf(KeySet(k)) != null ? "  ·  controle" : "";
            return OnlineRun ? $"ACELERAR  {KeyText(GasKeyAt(0))}  ·  ½ {KeyText(HalfKeyAt(0))}{(Gamepad.all.Count > 0 ? "  ·  controle" : "")}" : $"ACELERAR  {KeyText(GasKey(k))}  ·  ½ {KeyText(HalfKey(k))}{pad}";
        }
        Key ReslotKey(int k) => ReslotKeyAt(KeySet(k));

        // Nome curto da tecla para os botões e dicas.
        static string KeyText(Key k) => Controles.Nome(k);
        static readonly string[] LaneNames = { "Trilho 1", "Trilho 2", "Trilho 3", "Trilho 4", "Trilho 5", "Trilho 6", "Trilho 7", "Trilho 8" };
        static readonly string[] LaneCls = { "a", "b", "c", "d", "e", "f", "g", "h" };       // sufixo das classes de cor do USS (trilho 1 a 8)
        // Modos de câmera da corrida. RaceSettings.cameraMode guarda o valor de TopDownCamera.RaceViewMode.
        static readonly TopDownCamera.RaceViewMode[] CamModeValues = { TopDownCamera.RaceViewMode.Tv, TopDownCamera.RaceViewMode.ThirdPerson, TopDownCamera.RaceViewMode.FirstPerson, TopDownCamera.RaceViewMode.TopFollow };
        static readonly string[] CamModes = { "Transmissão de TV", "Perseguição", "Primeira pessoa", "De cima" };
        static readonly string[] CamModeHints = { "Câmeras em volta da mesa seguem o líder", "Atrás e acima do carrinho, olhando a curva", "Dentro do carrinho; quando ele roda, vira a traseira", "Segue os jogadores; botão direito gira, roda aproxima" };
        static readonly string[] CamTargets = { "Jogador 1", "Jogador 2", "Jogador 3", "Jogador 4", "Jogador 5", "Jogador 6", "Jogador 7", "Jogador 8" };

        RaceSettings set;
        RaceSim sim;
        readonly Transform[] cars = new Transform[RaceSim.MaxCars];
        ParticleSystem sparks;
        TrackSave builderStash;              // a pista do construtor, guardada enquanto a corrida usa outra
        readonly bool[] uiHeld = new bool[RaceSim.MaxCars];
        bool scoreDirty = true, champDirty;
        readonly Material[] carMats = new Material[RaceSim.MaxCars];      // cópia por carrinho: no campeonato ganha a cor do piloto
        RaceSim.Phase lastPhase;

        class PadUI { public VisualElement root, gas, fill, speed, slip, turbo, turboFill; public Label turboText; public Label name, state, gasText; public Button reslot; public string flash; public bool flashRec; public float flashT; }
        readonly PadUI[] pads = new PadUI[RaceSim.MaxCars];
        VisualElement pickList, scoreBox, padsBox, topBox, lights, banner;
        Label raceHint, clock, gripHint, bannerTitle, bannerSub;
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
        class HudCard { public VisualElement root, dot; public Label pos, name, lap, best; }
        readonly HudCard[] hud = new HudCard[RaceSim.MaxCars];
        VisualElement hudBox;
        bool sideAutoHidden;                 // o painel foi recolhido pela largada (devolve na chegada)
        // Contagem grande no meio da mesa (3, 2, 1, VAI!) e o resultado da prova dentro do aviso de vitória.
        VisualElement cdBox, resTable, resBtns;
        Label cdText;
        Button resAgain, resNext;

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
            AudioStart();
            BuildUI();
            BuildChampUI();
            BuildOnlineUI();
            builder.Changed += OnTrackChanged;
            panel.ModeChanged += OnMode;
            panel.OptionsRequested += ShowKeys;
            OnTrackChanged();
            SplitStart();
        }

        void OnDestroy()
        {
            SplitDestroy();
            if (builder) builder.Changed -= OnTrackChanged;
            if (panel) { panel.ModeChanged -= OnMode; panel.OptionsRequested -= ShowKeys; }
            OnlineDestroy();
        }

        static readonly string[] CarNames = { "Carrinho vermelho", "Carrinho azul", "Carrinho verde", "Carrinho amarelo", "Carrinho roxo", "Carrinho ciano", "Carrinho laranja", "Carrinho rosa" };

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
                carViews[k] = CarView.Create(CarNames[k], builder.theme.support, LaneColors[k], transform);
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
            int before = sim.N;
            sim.SetTrack(builder.Segments, builder.Info.closed, builder.Faixas);
            if (sim.N != before) { SetBot(); LayoutForCars(); }
            scoreDirty = true;
            PlaceCars();
        }

        void OnMode(BuilderPanel.Mode m)
        {
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
            else
            {
                sim.set = set; sim.cutoff = null; System.Array.Clear(sim.aiSkill, 0, sim.aiSkill.Length);
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
                try { var segs = TrackLayout.Build(o.save.Origin, o.save.Pieces(), o.save.Tail); o.closed = TrackLayout.Analyze(o.save.Origin, segs, o.save.Tail, o.save.Faixas).closed; }
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
                sim.aiSkill[k] = k < cars && sl.ai ? Mathf.Clamp(sl.skill, 1, 100) : 0;
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
            OnlineTick();
            AudioTick();
            KeysUpdate();                                        // a tela de teclas captura a próxima tecla (e some com Esc)
            if (OnlineRun) { OnlineRunUpdate(); return; }
            if (sim == null || !sim.Ready) return;
            if (!panel.RaceMode) { PlaceCars(); return; }       // no construtor, os carrinhos ficam no grid

            var kb = Keyboard.current;
            bool keys = kb != null && !IsTyping && !keysOpen;
            for (int k = 0; k < sim.N; k++)
            {
                if (sim.aiSkill[k] > 0) { sim.cars[k].gas = 1f; continue; }   // o piloto IA segura e solta sozinho, sempre no fundo
                bool tb = sim.set.turbo && TurboHeld(k, keys);
                float g = tb ? 1f : GasLevel(k, keys);
                sim.cars[k].boost = tb;
                sim.cars[k].held = g > 0f;
                if (g > 0f) sim.cars[k].gas = g;
                if ((keys && KeyWasPressed(kb, ReslotKey(k))) || PadPressed(k, p => p.buttonSouth)) sim.Reslot(k);
            }
            if ((keys && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || AnyPadStart()) { if (panel.ChampMode) ChampEnter(); else StartRace(); }

            sim.Step(Mathf.Min(Time.deltaTime, 0.05f));
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
                for (int i = 0; i < CamCount; i++) PaneCam(i).SetCarTrack(k, st0.mode == CarMode.OnSlot, st0.slip);
                var p = sim.CarPose(k, out float y, out float pitch);
                var pos = new Vector3(p.pos.x, y, p.pos.y);
                cars[k].position = pos;
                // Frente para +X: gira para o rumo, inclina na rampa e deita na curva inclinada.
                cars[k].rotation = Quaternion.Euler(0f, -p.heading, 0f) * Quaternion.Euler(0f, 0f, pitch) * Quaternion.Euler(sim.CarBank(k), 0f, 0f);
                var st = sim.cars[k];
                carViews[k].SetCockpit(cam.InFirstPersonOf(k));
                carViews[k].Tick(st.mode == CarMode.OnSlot ? st.v : 0f, st.mode == CarMode.Off, Time.deltaTime);
                carPos[k] = pos; carHeading[k] = p.heading;
                // Ponto do trilho à frente, para a Perseguição olhar a curva antes de chegar.
                var ahead = sim.PathOf(k).At(st.s + CamAhead * sim.Scale);
                for (int i = 0; i < CamCount; i++) PaneCam(i).SetCarAhead(k, new Vector3(ahead.pos.x, ahead.y, ahead.pos.y));
            }
            if (panel != null && panel.RaceMode)
            {
                if (framedPath != sim.PathOf(0)) FrameTrackForCamera();
                // No online o seu trilho muda de uma bateria para outra: a câmera acompanha o seu carrinho.
                int focus = CamFocusCar();
                if (OnlineRun && focus >= 0) cam.SetThirdPersonCar(focus);
                cam.SetRaceTargets(carPos, carHeading, sim.N, focus >= 0 ? focus : LeaderCar());
                // A "De cima" mantém na tela os carrinhos de gente (no online, só o seu).
                for (int k = 0; k < RaceSim.MaxCars; k++)
                    followMask[k] = k < sim.N && sim.present[k] && (OnlineRun ? k == focus : sim.aiSkill[k] == 0);
                if (System.Array.IndexOf(followMask, true) < 0)          // só robôs: todos os presentes
                    for (int k = 0; k < RaceSim.MaxCars; k++) followMask[k] = k < sim.N && sim.present[k];
                cam.SetFollowCars(followMask);
            }
            UpdateSplit(LeaderCar());
            if (camMenu != null)
            {
                bool show = panel != null && panel.RaceMode;
                camMenu.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show && camPop.style.display == DisplayStyle.Flex) ShowCamPop(false);
            }
            UpdateMinimap();
        }

        // Minimapa: só na aba Corrida/Campeonato com pista pronta. Cada carrinho usa a cor do painel dele.
        void UpdateMinimap()
        {
            if (minimap == null) return;
            bool show = panel != null && panel.RaceMode && sim != null && sim.Ready;
            minimap.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                miniColors[k] = pads[k] != null ? pads[k].root.resolvedStyle.borderTopColor : Color.white;
                string nm = pads[k] != null ? pads[k].name.text : "";
                int cut = nm.IndexOf(" · ");
                miniNames[k] = cut > 0 ? nm.Substring(0, cut) : nm;
            }
            minimap.Refresh(sim, set, miniNames, miniColors, Order(), builder != null ? builder.cellCm : 15f);
        }

        // ---------- Interface ----------

        void BuildUI()
        {
            var box = panel.RaceBox;

            var pick = Section(box, "PISTA");
            Text("Escolha onde correr. Qualquer pista fechada serve, em qualquer tamanho. Trocar aqui não mexe na pista do construtor.", "hint", pick);
            pickList = El(null, Caixa(pick));

            pickNote = Text("", "small", pick);

            // Tamanho: qualquer pista corre em 2, 4, 6 ou 8 trilhos (adaptada na hora; a pista salva não muda).
            var size = Section(box, "TAMANHO DA PISTA");
            var sizeRow = El("segmented", size);
            for (int i = 0; i < 4; i++)
            {
                int f = 2 + 2 * i;
                sizeBtns[i] = Btn($"{f} trilhos", () => { if (Racing || sim.N == f) return; set.raceFaixas = f; set.Save(); UseTrack(set.track); sim.Reset(); scoreDirty = true; }, "btn", sizeRow);
            }

            // Carros: de 1 (treino contra o relógio) até o número de trilhos.
            var carsSec = Section(box, "CARROS NA PISTA");
            var carsRow = El("segmented", carsSec);
            for (int n = 1; n <= RaceSim.MaxCars; n++)
            {
                int c = n;
                carBtns[n - 1] = Btn(n.ToString(), () => { if (Racing || c > sim.N) return; set.cars = c; set.Save(); SetBot(); LayoutForCars(); sim.Reset(); scoreDirty = true; }, "btn car-count", carsRow);
            }

            // Pilotos: um cartão por carro (gente ou robô; o robô tem a qualidade dele).
            var pilots = Section(box, "PILOTOS");
            pilotBox = El(null, pilots);

            // Formato: por voltas (vence quem completar primeiro) ou por tempo (manches, uma por trilho).
            var fmt = Section(box, "FORMATO");
            var kindRow = El("segmented", fmt);

            var race = Section(box, "CORRIDA");
            raceHint = Text("", "hint", race);
            var row = El("row", race);
            startBtn = Btn("Largar (Enter)", StartRace, "btn btn--primary", row);
            Btn("Voltar ao grid", () => { sim.Reset(); scoreDirty = true; }, "btn", row);

            var clockRow = El("row", race);
            Text("Tempo de prova", "stats-key", clockRow).style.marginRight = 8;
            clock = Text("0,00 s", "clock", clockRow);
            scoreBox = El("score", race);

            var how = Fold(box, "COMO PILOTAR");
            helpLabel = Text("", "small", how);
            Text("<b>Controle:</b> gatilho direito acelera aos poucos (o 1º controle é do jogador 1, o 2º do jogador 2, e assim por diante) · <b>A/✕</b> recoloca · <b>X/□</b> ou <b>RB</b> turbo · <b>Start</b> larga\n" +
                 "<b>Enter</b> largar · câmera no menu do canto direito", "small", how);

            var adj = Fold(box, "AJUSTES AVANÇADOS");
            var byLaps = Btn("Por voltas", null, "btn", kindRow);
            var byTime = Btn("Por tempo", null, "btn", kindRow);
            var secs = new SliderInt(2, 20) { value = Mathf.Clamp(set.secs / 30, 2, 20) };     // em meios minutos: 1 a 10 min
            var laps = new SliderInt(1, 20) { value = set.laps };
            var vmax = new Slider(8f, 20f) { value = set.vmax };
            var ramp = new Slider(0.2f, 1.5f) { value = set.ramp };
            var grip = new Slider(15f, 45f) { value = set.grip };
            var col = new Toggle("Colisão entre carrinhos") { value = set.collide };
            var turbo = new Toggle("Turbo") { value = set.turbo };
            var vol = new SliderInt(0, 100) { value = Mathf.Clamp(set.volume, 0, 100) };
            var half = new SliderInt(20, 90) { value = Mathf.Clamp(set.halfGas, 20, 90) };
            void Labels()
            {
                laps.label = $"Voltas: {set.laps}";
                secs.label = $"Duração: {Dur(set.secs)} · {Dur(set.secs / Mathf.Max(1, sim.N))} por trilho";
                byLaps.EnableInClassList("btn--on", !set.byTime);
                byTime.EnableInClassList("btn--on", set.byTime);
                laps.style.display = set.byTime ? DisplayStyle.None : DisplayStyle.Flex;
                secs.style.display = set.byTime ? DisplayStyle.Flex : DisplayStyle.None;
                vmax.label = $"Velocidade máx.: {Num(set.vmax)} cél/s";
                ramp.label = $"Tempo até 100%: {Num(set.ramp, 2)} s";
                grip.label = $"Aderência: {Mathf.RoundToInt(set.grip)}";
                half.label = $"Meia aceleração (tecla de meia): {set.halfGas}%";
                float sc = sim.Scale;
                float Lim(float r, float m) => Mathf.Min(100f, Mathf.Round(sim.CornerLimit(r, m) / (set.vmax * sc) * 100f));
                // Raio do trilho de dentro e do de fora, no mundo: 2,25 e 2,75 na pista de 2; 4,25 e 5,75 na de 4.
                var lanes = TrackMath.LanesOf(builder.Faixas);
                float rIn = (2.5f + lanes[0]) * sc, rOut = (2.5f - lanes[0]) * sc;
                gripHint.text = $"Nas curvas planas, o carrinho segura até {Lim(rIn, 1f)}% da velocidade máxima no trilho de dentro e {Lim(rOut, 1f)}% no de fora. " +
                                $"Nas inclinadas, até {Lim(rIn, PieceCatalog.BankGrip)}% e {Lim(rOut, PieceCatalog.BankGrip)}%.";
            }
            foreach (var s in new VisualElement[] { laps, secs }) { s.AddToClassList("setting"); fmt.Add(s); }
            foreach (var s in new VisualElement[] { vmax, ramp, grip, half, col, turbo }) { s.AddToClassList("setting"); adj.Add(s); }
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
            vol.AddToClassList("setting"); adj.Add(vol);
            // Som do motor: alterna entre as opções (dá para trocar no meio da corrida e ouvir na hora).
            Button motorBtn = null;
            motorBtn = Btn("", () => { NextMotor(); motorBtn.text = $"Som do motor: <b>{MotorName}</b>  ▸"; }, "btn", adj);
            motorBtn.text = $"Som do motor: <b>{MotorName}</b>  ▸";
            motorBtn.tooltip = "Clique para ouvir o próximo. Para usar uma gravação sua, ponha motor.wav (PCM 16 bits) em " + System.IO.Path.Combine(TrackLibrary.Root, "sons");
            vol.label = $"Som: {set.volume}%";
            vol.RegisterValueChangedCallback(e => { set.volume = e.newValue; set.Save(); vol.label = $"Som: {set.volume}%"; });
            half.RegisterValueChangedCallback(e => { set.halfGas = e.newValue; set.Save(); Labels(); });
            Labels();

            // Sobre a mesa: luzes e aviso de vitória no topo; os dois jogadores embaixo.
            var root = panel.Root;
            topBox = El("race-top", root); topBox.pickingMode = PickingMode.Ignore;
            hudBox = El("hud", topBox); hudBox.pickingMode = PickingMode.Ignore;
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
                foreach (var e in h.root.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
                hud[k] = h;
            }
            lights = El("lights", topBox);
            for (int i = 0; i < 3; i++) lightDots.Add(El("light", lights));
            banner = El("banner", topBox);
            bannerTitle = Text("", "banner-title", banner);
            bannerSub = Text("", "banner-sub", banner);
            recCard = El("res-table", banner);              // recordes antes da largada (RaceController.Records.cs)
            recCard.style.display = DisplayStyle.None;
            resTable = El("res-table", banner);
            resBtns = El("res-btns", banner);
            resAgain = Btn("Correr de novo  (Enter)", () => { if (ChampLike) ChampEnter(); else StartRace(); }, "btn btn--accent res-btn", resBtns);
            resNext = Btn("Trocar pista", () => { sideAutoHidden = false; panel.SetSideFromRace(true); }, "btn res-btn", resBtns);
            lights.pickingMode = PickingMode.Ignore;
            minimap = new RaceMinimap();
            root.Add(minimap);

            cdBox = El("cd-box", root); cdBox.pickingMode = PickingMode.Ignore;
            cdText = Text("", "cd-text", cdBox); cdText.pickingMode = PickingMode.Ignore;
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
        Label helpLabel, keysHint;
        readonly Button[,] keyBtns = new Button[Controles.Jogadores, Controles.Acoes];
        int capJ = -1, capA;                    // a tecla que está esperando: jogador e ação (-1 = nenhuma)
        bool keysOpen => keysBox != null && keysBox.style.display == DisplayStyle.Flex;

        void BuildKeysUI(VisualElement root)
        {
            keysBox = El("keys-screen", root);
            keysBox.style.display = DisplayStyle.None;
            var card = El("keys-card", keysBox);
            Text("OPÇÕES", "h2", card);
            Text("TECLAS DOS JOGADORES", "hint", card);
            Text("Clique num botão e aperte a tecla nova. Se ela já era de outra ação, as duas trocam. Esc cancela.", "hint", card);
            for (int j = 0; j < Controles.Jogadores; j++)
            {
                int jj = j;
                var row = El("keys-row", card);
                Text($"Jogador {j + 1}", "pcard-name", El("keys-who", row));
                for (int a = 0; a < Controles.Acoes; a++)
                {
                    int aa = a;
                    keyBtns[j, a] = Btn("", () => StartCapture(jj, aa), "btn keys-btn", row);
                }
            }
            Text("Jogadores 5 e 6 (pista de 6) jogam só com controle.", "small", card);
            keysHint = Text("", "small", card);
            var bottom = El("row", card);
            bottom.style.marginTop = 8;
            Btn("Restaurar padrão", () => { CancelCapture(); Controles.Restaurar(); KeysChanged(); }, "btn", bottom);
            Btn("Fechar  (Esc)", CloseKeys, "btn btn--primary", bottom);
            KeysChanged();
        }

        void ShowKeys()
        {
            if (keysBox == null || Racing) return;
            keysBox.style.display = DisplayStyle.Flex;
            keysBox.BringToFront();
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
                    keysHint.text = $"“{Controles.Nome(kc.keyCode)}” é do jogo (Enter larga). Aperte outra tecla.";
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
                    b.text = wait ? $"{Controles.AcaoNomes[a]}: <b>aperte…</b>" : $"{Controles.AcaoNomes[a]}: <b>{KeyText(Controles.Get(j, (Controles.Acao)a))}</b>";
                    b.EnableInClassList("keys-btn--wait", wait);
                }
            if (keysHint != null) keysHint.text = capJ >= 0 ? "Aperte a tecla nova (Esc cancela)." : "";
            if (helpLabel != null)
                helpLabel.text = "<b>Segure</b> Acelerar e o acelerador sobe de 0 a 100%. <b>Soltou, zerou</b>: o carrinho freia.\n" +
                    "Rápido demais na curva, a traseira escorrega e a barra de <b>derrapagem</b> enche. Cheia, o carrinho sai da pista.\n" +
                    "<b>Recolocar</b> devolve o carrinho ao trilho, um pouco antes de onde ele saiu.\n" +
                    "<b>Turbo</b>: começa vazio e enche um pouco a cada saída de pista (mais para quem está atrás). Segure para andar acima da velocidade máxima até o tanque acabar. Volta com turbo não vale como melhor volta.\n" +
                    $"<b>Teclas</b> (acelerar 100% · meia {set?.halfGas ?? 60}% · recolocar · turbo): " +
                    $"jogador 1 {KeyText(GasKeyAt(0))} · {KeyText(HalfKeyAt(0))} · {KeyText(ReslotKeyAt(0))} · {KeyText(TurboKeyAt(0))}; " +
                    $"jogador 2 {KeyText(GasKeyAt(1))} · {KeyText(HalfKeyAt(1))} · {KeyText(ReslotKeyAt(1))} · {KeyText(TurboKeyAt(1))}; " +
                    $"jogador 3 {KeyText(GasKeyAt(2))} · {KeyText(HalfKeyAt(2))} · {KeyText(ReslotKeyAt(2))} · {KeyText(TurboKeyAt(2))}; " +
                    $"jogador 4 {KeyText(GasKeyAt(3))} · {KeyText(HalfKeyAt(3))} · {KeyText(ReslotKeyAt(3))} · {KeyText(TurboKeyAt(3))}. " +
                    "Mude no menu inicial, em <b>Opções</b>.";
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
                Text($"Carro {k + 1}", "pcard-name", head);
                var seg = El("segmented pcard-seg", head);
                Btn("Jogador", () => SetSlotAi(idx, false), sl.ai ? "btn" : "btn btn--on", seg);
                Btn("Robô", () => SetSlotAi(idx, true), sl.ai ? "btn btn--on" : "btn", seg);
                if (sl.ai)
                {
                    var q = new SliderInt(1, 100) { value = Mathf.Clamp(sl.skill, 1, 100), label = $"Qualidade: {sl.skill}" };
                    q.AddToClassList("setting"); q.AddToClassList("pcard-skill");
                    card.Add(q);
                    var word = Text(Champ.SkillWord(sl.skill), "small", card);
                    // Arrastar não refaz os cartões (isso soltaria o controle): só o rótulo e a IA.
                    q.RegisterValueChangedCallback(e => { sl.skill = e.newValue; set.Save(); q.label = $"Qualidade: {sl.skill}"; word.text = Champ.SkillWord(sl.skill); SetBot(); });
                }
                else
                {
                    // As teclas vão para os humanos na ordem dos carros; do 5º em diante, só controle.
                    int ks = humans++;
                    string pad = PadOf(ks) != null ? $" · controle {ks + 1}" : "";
                    Text(ks < Controles.Jogadores ? $"Acelera {KeyText(GasKeyAt(ks))} · meia {KeyText(HalfKeyAt(ks))} · recoloca {KeyText(ReslotKeyAt(ks))} · turbo {KeyText(TurboKeyAt(ks))}{pad}"
                                             : PadOf(ks) != null ? $"Controle {ks + 1}" : "Só com controle: ligue mais um controle", "small", card);
                }
            }
            if (cars < f) Text(f - cars == 1 ? "1 trilho fica vazio." : $"{f - cars} trilhos ficam vazios.", "small", pilotBox);
            if (cars == 1) Text("Treino: um carro só, contra o relógio.", "small", pilotBox);
        }

        void SetSlotAi(int k, bool ai)
        {
            if (set.slots[k].ai == ai) return;
            set.slots[k].ai = ai; set.Save();
            SetBot(); DrawRaceSetup();
        }

        const float CamAhead = 2.2f;      // células à frente do carrinho que a Perseguição olha
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

        void BuildCamMenu(VisualElement root)
        {
            camMenu = El("cam-menu", root);
            camBtn = Btn("", () => ShowCamPop(camPop.style.display == DisplayStyle.None), "btn cam-btn", camMenu);
            camPop = El("cam-pop", camMenu);
            Text("CÂMERA", "cam-title", camPop);
            for (int i = 0; i < CamModes.Length; i++)
            {
                int m = i;
                var b = Btn("", () => { set.cameraMode = (int)CamModeValues[m]; set.Save(); ApplyCameraSettings(); }, "btn cam-opt", camPop);
                b.text = $"<b>{CamModes[i]}</b>\n<size=10>{CamModeHints[i]}</size>";
                camModeBtns[i] = b;
            }
            splitBtn = Btn("", () => { set.splitScreen = !set.splitScreen; set.Save(); ApplyCameraSettings(); }, "btn cam-opt cam-split", camPop);
            camTargetBox = El("cam-sub", camPop);
            Text("SEGUIR", "cam-title", camTargetBox);
            var seg = El("segmented", camTargetBox);
            for (int k = 0; k < RaceSim.MaxCars; k++)
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
            // Corrida livre com um humano só (os outros são robôs, ou ele corre sozinho): a câmera segue esse humano.
            if (panel != null && panel.CurrentMode == BuilderPanel.Mode.Race)
            {
                int human = -1, humans = 0;
                for (int k = 0; k < RaceCars; k++) if (!set.slots[k].ai) { humans++; human = k; }
                if (humans == 1) return human;
            }
            return -1;
        }

        void ApplyCameraSettings()
        {
            // Valores antigos (0 de cima, 1 3D, 3 Dinâmica) viram Transmissão de TV.
            if (set.cameraMode == 0) set.cameraMode = (int)TopDownCamera.RaceViewMode.TopFollow;     // a "De cima" antiga
            int mode = System.Array.IndexOf(CamModeValues, (TopDownCamera.RaceViewMode)set.cameraMode);
            if (mode < 0) mode = 0;
            int target = Mathf.Clamp(set.cameraTarget, 0, (sim != null ? sim.N : 2) - 1);
            set.cameraDistance = Mathf.Clamp(set.cameraDistance, 70, 160);
            set.cameraMode = (int)CamModeValues[mode];
            set.cameraTarget = target;

            var applied = panel != null && !panel.RaceMode ? TopDownCamera.RaceViewMode.Atual : CamModeValues[mode];
            cam.SetRaceViewMode(applied);
            int focus = CamFocusCar();
            cam.SetThirdPersonCar(OnlineRun && focus >= 0 ? focus : target);
            cam.SetDistanceScale(set.cameraDistance / 100f);

            if (camBtn == null) return;
            camBtn.text = $"Câmera: <b>{CamModes[mode]}</b>  ▾";
            // Na tela dividida, cada jogador escolhe a câmera no canto do seu quadro: o menu geral só mostra a divisão e a distância.
            for (int i = 0; i < camModeBtns.Length; i++)
            {
                camModeBtns[i].EnableInClassList("cam-opt--on", i == mode);
                camModeBtns[i].style.display = SplitOn ? DisplayStyle.None : DisplayStyle.Flex;
            }
            camBtn.text = SplitOn ? "Câmera  ▾" : camBtn.text;
            camTargetBox.style.display = !SplitOn && CamModeValues[mode] != TopDownCamera.RaceViewMode.Tv && CamModeValues[mode] != TopDownCamera.RaceViewMode.TopFollow ? DisplayStyle.Flex : DisplayStyle.None;
            splitBtn.text = $"Tela dividida: <b>{(set.splitScreen ? "sim" : "não")}</b>\n<size=10>Offline, 2 a 4 jogadores: um quadro para cada</size>";
            splitBtn.EnableInClassList("cam-opt--on", set.splitScreen);
            splitBtn.style.display = OnlineRun ? DisplayStyle.None : DisplayStyle.Flex;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                camTargetBtns[k].EnableInClassList("btn--on", k == target);
                camTargetBtns[k].style.display = OnlineRun || k >= sim.N ? DisplayStyle.None : DisplayStyle.Flex;
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
            p.root = El("pad pad--" + LaneCls[k], padsBox);
            var head = El("pad-head", p.root);
            p.name = Text($"JOGADOR {k + 1}", "pad-name", head);
            p.state = Text("No grid", "pad-state", head);
            var body = El("pad-body", p.root);

            // Acelerar: segurar com o mouse ou o dedo (ou a tecla) mantém o acelerador subindo.
            var gas = p.gas = El("gas", body);
            p.fill = El("gas-fill", gas); p.fill.pickingMode = PickingMode.Ignore;
            p.gasText = Text($"ACELERAR  {KeyText(GasKeyAt(k))}  ·  ½ {KeyText(HalfKeyAt(k))}", "gas-text", gas);
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
            // Turbo: a barra é o tanque e também o botão (segurar com o mouse ou o dedo gasta o turbo).
            p.turbo = El("turbo", side);
            p.turboFill = El("turbo-fill", p.turbo); p.turboFill.pickingMode = PickingMode.Ignore;
            p.turboText = Text("TURBO", "turbo-text", p.turbo); p.turboText.pickingMode = PickingMode.Ignore;
            var tbo = p.turbo;
            tbo.RegisterCallback<PointerDownEvent>(e => { uiTurbo[idx] = true; tbo.CapturePointer(e.pointerId); e.StopPropagation(); });
            tbo.RegisterCallback<PointerUpEvent>(e => { uiTurbo[idx] = false; tbo.ReleasePointer(e.pointerId); });
            tbo.RegisterCallback<PointerCaptureOutEvent>(_ => uiTurbo[idx] = false);
            p.reslot = Btn($"Recolocar  {KeyText(ReslotKeyAt(k))}", () => ReslotPressed(idx), "btn reslot", side);
            return p;
        }

        void DrawPicker(List<Option> opts)
        {
            pickList.Clear();
            int open = 0;
            foreach (var o in opts)
            {
                if (!o.closed) { open++; continue; }             // pista aberta não corre: nem aparece
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
                pickNote.text = open == 0 ? "" : open == 1 ? "1 pista aberta não aparece: feche o circuito no Construtor." : $"{open} pistas abertas não aparecem: feche os circuitos no Construtor.";
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
            Row(new[] { "Trilho", sim.ByTime ? "Distância" : "Voltas", "Última", "Melhor", "Saídas", "Toques" }, "score-head");
            for (int k = 0; k < sim.N; k++)
            {
                if (!sim.present[k]) continue;                  // trilho vazio não entra no placar
                var c = sim.cars[k];
                Row(new[] { $"{k + 1}{(sim.winner == k ? " · 1º" : "")}", LapsCell(k), Fmt(c.lastLap), Fmt(c.best), c.offs.ToString(), c.hits.ToString() },
                    "score-" + LaneCls[k] + (sim.winner == k ? " score-win" : ""));
            }
            raceHint.text = !sim.closed ? "Esta pista não é um circuito fechado. Escolha outra acima ou feche o traçado no Construtor."
                : sim.phase == RaceSim.Phase.Idle ? (RaceCars == 1 ? "O carrinho está no grid." : RaceCars == 2 ? "Os dois carrinhos estão no grid." : $"Os {RaceCars} carrinhos estão no grid.") + " Aperte Largar e espere as luzes."
                : sim.phase == RaceSim.Phase.Countdown ? "Pode segurar o acelerador: o carrinho sai quando a luz ficar verde."
                : sim.phase == RaceSim.Phase.Switch ? "Fim da manche: cada carrinho passa para o trilho seguinte."
                : sim.phase == RaceSim.Phase.Running ? (sim.ByTime ? $"Corrida por tempo, {MancheText}: vence quem andar mais em {Dur(set.secs)}." : $"Vence quem completar {set.laps} {(set.laps == 1 ? "volta" : "voltas")} primeiro.")
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
            else if (sim.phase == RaceSim.Phase.Switch) panel.SetStatus("Troca de trilho", "");
            else if (sim.phase == RaceSim.Phase.Running) panel.SetStatus(sim.ByTime ? $"Corrida · {MancheText} · faltam {Fmt(sim.TimeLeft)}" : $"Corrida · volta {Mathf.Min(MaxLaps() + 1, set.laps)} de {set.laps}", "");
            else panel.SetStatus("Pronto para largar", "");
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
                p.speed.style.width = Length.Percent(Mathf.Min(100f, c.v / (sim.set.vmax * sim.Scale) * 100f));
                p.slip.style.width = Length.Percent(Mathf.Min(100f, c.slip * 100f));
                p.slip.style.backgroundColor = c.slip < 0.45f ? new Color(0.24f, 0.75f, 0.45f) : c.slip < 0.75f ? new Color(0.96f, 0.76f, 0.1f) : new Color(0.94f, 0.48f, 0.23f);
                bool off = c.mode == CarMode.Off, ai = sim.aiSkill[k] > 0;
                p.turbo.style.display = sim.set.turbo ? DisplayStyle.Flex : DisplayStyle.None;
                p.turboFill.style.width = Length.Percent(Mathf.Clamp01(c.turbo) * 100f);
                p.turbo.EnableInClassList("turbo--on", c.boost && c.turbo > 0f && c.mode == CarMode.OnSlot);
                p.turbo.EnableInClassList("turbo--empty", c.turbo <= 0f);
                Key tk = OnlineRun ? TurboKeyAt(0) : TurboKeyAt(KeySet(k));
                int tpc = Mathf.RoundToInt(c.turbo * 100f);
                p.turboText.text = ai || tk == Key.None ? $"TURBO  {tpc}%" : $"TURBO  {KeyText(tk)}  ·  {tpc}%";
                if (!ai) p.reslot.text = $"Recolocar  {KeyText(OnlineRun ? ReslotKeyAt(0) : ReslotKey(k))}";
                if (!ai) p.gasText.text = GasLabel(k);         // muda com o rodízio e quando um controle é ligado
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
                    : sim.phase == RaceSim.Phase.Switch ? $"Vai para o trilho {(c.lane + 1) % sim.N + 1}"
                    : sim.phase == RaceSim.Phase.Running && sim.ByTime ? $"{LapsCell(k)} voltas · trilho {c.lane + 1}"
                    : sim.phase == RaceSim.Phase.Running ? $"Volta {Mathf.Min(c.laps + 1, L)} de {L}"
                    : "No grid";
                p.state.EnableInClassList("pad-state--alert", off);
            }
        }

        // Largada: recolhe o painel lateral para a mesa ficar inteira. Chegada (ou volta ao grid): devolve.
        // Se o jogador abriu o painel no meio da prova (Tab), ele fica aberto.
        void SideForPhase()
        {
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

        void UpdateHud()
        {
            bool show = sim.closed && sim.phase != RaceSim.Phase.Idle;
            hudBox.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            int L = sim.set.laps;
            var ord = Order();
            int lead = ord[0];
            for (int k = 0; k < sim.N; k++)
            {
                var c = sim.cars[k]; var h = hud[k];
                string nm = pads[k].name.text;
                int cut = nm.IndexOf(" · ");
                h.name.text = cut > 0 ? nm.Substring(0, cut) : nm;
                h.dot.style.backgroundColor = pads[k].root.resolvedStyle.borderTopColor;
                h.pos.text = sim.winner == k ? "🏁" : $"{ord.IndexOf(k) + 1}º";
                h.root.EnableInClassList("hud-card--lead", k == lead);
                h.lap.text = sim.ByTime ? $"{LapsCell(k)} v · T{c.lane + 1}" : c.finish != null ? "Chegou" : $"Volta {Mathf.Clamp(c.laps + 1, 1, L)}/{L}";
                h.best.text = c.best != null ? $"Melhor {Fmt(c.best)}" : "";
            }
        }

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
                cdText.text = $"TROCA DE TRILHO\n<size=40%>{MancheText.Replace("manche " + (sim.Manche + 1), "manche " + (sim.Manche + 2))}</size>";
                cdText.EnableInClassList("cd-text--go", false);
                cdText.EnableInClassList("cd-text--switch", true);
                cdText.style.scale = new Scale(Vector2.one);
                cdText.style.opacity = 1f;
                return;
            }
            cdBox.style.display = panel.RaceMode && (cd || go) ? DisplayStyle.Flex : DisplayStyle.None;
            if (!(cd || go)) return;
            cdBox.style.left = panel.SideWidth;
            float step = RaceSim.Count / 3f;
            int n = cd ? 3 - Mathf.Min(2, Mathf.FloorToInt(sim.t / step)) : 0;
            float since = sim.t - sim.Manche * (sim.ByTime ? sim.MancheLen : 0f);
            float f = cd ? (sim.t % step) / step : since / 0.8f;      // 0→1 dentro de cada número
            cdText.EnableInClassList("cd-text--switch", false);
            cdText.text = cd ? n.ToString() : "VAI!";
            cdText.EnableInClassList("cd-text--go", !cd);
            cdText.style.scale = new Scale(Vector2.one * Mathf.Lerp(1.35f, 1f, Mathf.Min(1f, f * 3f)));
            cdText.style.opacity = cd ? 1f : 1f - f;
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
            if (show == resShown && sig == resSig) return;
            resShown = show; resSig = sig;
            DrawResult(show);
        }

        void DrawResult(bool show)
        {
            resTable.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            resBtns.style.display = show && !OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
            banner.pickingMode = show && !OnlineRun ? PickingMode.Position : PickingMode.Ignore;
            if (!show) return;
            resTable.Clear();
            var ord = new List<int>(Order());
            for (int pos = 0; pos < ord.Count; pos++)
            {
                int k = ord[pos];
                var c = sim.cars[k];
                var r = El("res-row" + (k == sim.winner ? " res-row--win" : ""), resTable);
                Text($"{pos + 1}º", "res-pos", r);
                El("hud-dot", r).style.backgroundColor = pads[k].root.resolvedStyle.borderTopColor;
                Text(hud[k].name.text, "res-name", r);
                Text(sim.ByTime ? $"{LapsCell(k)} voltas" : c.finish != null ? Fmt(c.finish) : "não terminou", "res-cell", r);
                Text("melhor " + Fmt(c.best), "res-cell res-cell--dim", r);
                Text(c.offs == 1 ? "1 saída" : $"{c.offs} saídas", "res-cell res-cell--dim", r);
            }
            if (ChampLike)
            {
                bool over = !HeatNow(out _, out _, out _, out _, out _);
                resAgain.style.display = over ? DisplayStyle.None : DisplayStyle.Flex;
                resAgain.text = "Salvar e seguir  (Enter)";
                resNext.style.display = DisplayStyle.None;
            }
            else
            {
                resAgain.style.display = DisplayStyle.Flex;
                resAgain.text = "Correr de novo  (Enter)";
                resNext.style.display = DisplayStyle.Flex;
            }
        }

        void UpdateTop()
        {
            topBox.style.left = panel.SideWidth;
            UpdateHud();
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
            bool card = StartCard(out string cardTitle) && !showLights;
            DrawRecCard(card);
            bannerSub.style.display = DisplayStyle.Flex;
            if (ChampLike)
            {
                bool champShow = ChampBanner(out string title, out string sub) && !showLights;
                if (card && !champShow) { champShow = true; title = cardTitle; sub = ""; }
                banner.style.display = champShow ? DisplayStyle.Flex : DisplayStyle.None;
                if (champShow) { bannerTitle.text = title; bannerSub.text = sub; BannerColor(-1); bannerSub.style.display = sub == "" ? DisplayStyle.None : DisplayStyle.Flex; }
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
            if (!sim.closed) { bannerTitle.text = "Pista aberta"; bannerSub.text = "Escolha um circuito fechado na lista."; BannerColor(-1); return; }
            var w = sim.cars[sim.winner];
            bannerTitle.text = $"{LaneNames[sim.winner]} venceu";
            BannerColor(sim.winner);
            bannerSub.text = sim.ByTime ? $"{LapsCell(sim.winner)} voltas em {Dur(sim.set.secs)} · melhor volta {Fmt(w.best)}" : $"{Fmt(w.finish)} · melhor volta {Fmt(w.best)}";
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

        // Distância em voltas com uma casa (por tempo) ou "voltas/total" (por voltas).
        string LapsCell(int k) => sim.ByTime ? Num(Mathf.Max(0f, sim.Dist(k)), 2) : $"{sim.cars[k].laps}/{sim.set.laps}";
        string MancheText => $"manche {Mathf.Min(sim.Manche + 1, sim.N)} de {sim.N}";
        static string Dur(int secs) => secs % 60 == 0 ? $"{secs / 60} min" : $"{secs / 60}:{secs % 60:00}";
        bool Racing => sim.phase == RaceSim.Phase.Countdown || sim.phase == RaceSim.Phase.Running || sim.phase == RaceSim.Phase.Switch;

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
