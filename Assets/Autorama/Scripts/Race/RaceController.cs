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

        // Conjuntos de teclas: A·S (Q meia), L·K (O meia), ↑·↓ (← meia) e 8·5 do teclado numérico (7 meia).
        static readonly Key[] GasKeys = { Key.A, Key.L, Key.UpArrow, Key.Numpad8 }, ReslotKeys = { Key.S, Key.K, Key.DownArrow, Key.Numpad5 },
                              HalfKeys = { Key.Q, Key.O, Key.LeftArrow, Key.Numpad7 };

        // Teclas de cada trilho: os humanos ganham os conjuntos na ordem dos trilhos (o 1º humano A·S, o 2º L·K…).
        // Com 2 humanos na pista de 2 é A·S no 1 e L·K no 2; com um humano só (os outros são IA), ele usa sempre
        // A·S, em qualquer trilho: no campeonato o rodízio troca o trilho a cada bateria. O mesmo vale para os controles.
        int KeySet(int k)
        {
            if (sim == null) return k;
            int n = 0;
            for (int i = 0; i < k && i < sim.N; i++) if (sim.present[i] && sim.aiSkill[i] <= 0) n++;
            return n;
        }
        // Teclado: só há 4 conjuntos de teclas. O 5º e o 6º humano (pista de 6) jogam só de controle: Key.None.
        static Key GasKeyAt(int ks) => ks < GasKeys.Length ? GasKeys[ks] : Key.None;
        static Key HalfKeyAt(int ks) => ks < HalfKeys.Length ? HalfKeys[ks] : Key.None;
        static Key ReslotKeyAt(int ks) => ks < ReslotKeys.Length ? ReslotKeys[ks] : Key.None;
        Key GasKey(int k) => GasKeyAt(KeySet(k));
        Key HalfKey(int k) => HalfKeyAt(KeySet(k));
        bool KeyPressed(Keyboard kb, Key key) => key != Key.None && kb[key].isPressed;
        bool KeyWasPressed(Keyboard kb, Key key) => key != Key.None && kb[key].wasPressedThisFrame;

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
            return OnlineRun ? $"ACELERAR  A  ·  ½ Q{(Gamepad.all.Count > 0 ? "  ·  controle" : "")}" : $"ACELERAR  {KeyText(GasKey(k))}  ·  ½ {KeyText(HalfKey(k))}{pad}";
        }
        Key ReslotKey(int k) => ReslotKeyAt(KeySet(k));

        // Nome curto da tecla para os botões e dicas.
        static string KeyText(Key k) => k switch
        {
            Key.UpArrow => "↑", Key.DownArrow => "↓", Key.LeftArrow => "←", Key.RightArrow => "→",
            Key.Numpad8 => "8 num.", Key.Numpad5 => "5 num.", Key.Numpad7 => "7 num.", Key.None => "controle",
            _ => k.ToString(),
        };
        static readonly string[] LaneNames = { "Trilho 1", "Trilho 2", "Trilho 3", "Trilho 4", "Trilho 5", "Trilho 6" };
        static readonly string[] LaneCls = { "a", "b", "c", "d", "e", "f" };       // sufixo das classes de cor do USS (trilho 1 a 6)
        // Modos de câmera da corrida, na ordem de TopDownCamera.RaceViewMode.
        static readonly string[] CamModes = { "De cima", "3D", "Atrás do carro", "Dinâmica" };
        static readonly string[] CamModeHints = { "A vista livre da mesa", "Inclinada, gira com o botão direito", "Segue um carrinho por trás", "Acompanha os carrinhos sozinha" };
        static readonly string[] CamTargets = { "Jogador 1", "Jogador 2", "Jogador 3", "Jogador 4", "Jogador 5", "Jogador 6" };

        RaceSettings set;
        RaceSim sim;
        readonly Transform[] cars = new Transform[RaceSim.MaxCars];
        ParticleSystem sparks;
        TrackSave builderStash;              // a pista do construtor, guardada enquanto a corrida usa outra
        readonly bool[] uiHeld = new bool[RaceSim.MaxCars];
        bool scoreDirty = true, champDirty;
        readonly Material[] carMats = new Material[RaceSim.MaxCars];      // cópia por carrinho: no campeonato ganha a cor do piloto
        RaceSim.Phase lastPhase;

        class PadUI { public VisualElement root, gas, fill, speed, slip; public Label name, state, gasText; public Button reslot; public string flash; public bool flashRec; public float flashT; }
        readonly PadUI[] pads = new PadUI[RaceSim.MaxCars];
        VisualElement pickList, scoreBox, padsBox, topBox, lights, banner;
        Label raceHint, clock, gripHint, bannerTitle, bannerSub;
        Button startBtn;
        // Menu da câmera: botão no canto superior direito da mesa que abre a lista de modos.
        VisualElement camMenu, camPop, camTargetBox;
        Button camBtn;
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

        static readonly string[] CarNames = { "Carrinho vermelho", "Carrinho azul", "Carrinho verde", "Carrinho amarelo", "Carrinho roxo", "Carrinho ciano" };

        // Material de cada trilho: vermelho e azul na cena; verde e amarelo no tema (Autorama > Atualizar materiais e ícones).
        Material BaseCarMaterial(int k)
        {
            if (k < carMaterials.Length && carMaterials[k] != null) return carMaterials[k];
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

        void CreateCars()
        {
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = CarNames[k];
                Destroy(go.GetComponent<Collider>());
                var baseMat = BaseCarMaterial(k);
                carMats[k] = new Material(baseMat) { name = baseMat.name + " (cópia)" };
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
            for (int k = 0; k < RaceSim.MaxCars; k++) uiHeld[k] = false;
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
                new Option { key = "ex:oval4", name = "Exemplo: oval de 4 carros", save = TrackSave.Four(TrackSave.Oval()) },
                new Option { key = "ex:oito4", name = "Exemplo: 8 com ponte de 4 carros", save = TrackSave.Four(TrackSave.Eight()) },
                new Option { key = "ex:oval6", name = "Exemplo: oval de 6 carros", save = TrackSave.Six(TrackSave.Oval()) },
                new Option { key = "ex:oito6", name = "Exemplo: 8 com ponte de 6 carros", save = TrackSave.Six(TrackSave.Eight()) },
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
            // Pista de 4: os trilhos 3 e 4 são robôs (com a mesma qualidade), a não ser que o ajuste diga que são gente.
            // "Carros na pista" (2, 3 ou 4) deixa os trilhos de cima vazios.
            int cars = sim.N > 2 ? Mathf.Clamp(set.cars4, 2, sim.N) : sim.N;
            for (int k = 0; k < RaceSim.MaxCars; k++) sim.present[k] = k < cars;
            for (int k = 2; k < RaceSim.MaxCars; k++) sim.aiSkill[k] = k < cars && set.bot34 ? set.botSkill : 0;
            for (int k = 1; k < sim.N; k++) if (sim.aiSkill[k] == 0) sim.cars[k].held = false;     // desligou: solta o acelerador que a IA segurava
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
            for (int k = 0; k < sim.N; k++)
            {
                if (sim.aiSkill[k] > 0) { sim.cars[k].gas = 1f; continue; }   // o piloto IA segura e solta sozinho, sempre no fundo
                float g = GasLevel(k, keys);
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

        void PlaceCars()
        {
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                bool on = k < sim.N && sim.present[k];
                if (cars[k].gameObject.activeSelf != on) cars[k].gameObject.SetActive(on);
                if (!on) continue;
                var p = sim.CarPose(k, out float y, out float pitch);
                var pos = new Vector3(p.pos.x, y + 0.035f, p.pos.y);
                cars[k].position = pos;
                // Deitado, frente para +X; inclina na rampa e gira para o rumo.
                cars[k].rotation = Quaternion.Euler(0f, -p.heading, 0f) * Quaternion.Euler(0f, 0f, pitch) * Quaternion.Euler(90f, 0f, 0f);
                carPos[k] = pos; carHeading[k] = p.heading;
            }
            if (panel != null && panel.RaceMode)
            {
                // No online o seu trilho muda de uma bateria para outra: a câmera acompanha o seu carrinho.
                int focus = CamFocusCar();
                if (OnlineRun && focus >= 0) cam.SetThirdPersonCar(focus);
                cam.SetRaceTargets(carPos, carHeading, sim.N, focus);
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
                botBox.style.display = set.bot || (set.bot34 && sim.N > 2) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // Pista de 4 carros: os trilhos 3 e 4 também podem ser robôs (padrão) ou gente (↑·↓, 8·5 e os controles 3 e 4).
            bot34Toggle = new Toggle("Jogadores 3 e 4 são robôs") { value = set.bot34 };
            bot34Toggle.AddToClassList("setting"); bot34Toggle.AddToClassList("ai-toggle");
            race.Insert(race.IndexOf(botBox), bot34Toggle);
            // Pista de 4: quantos carrinhos correm (com 3, o trilho 4 fica vazio).
            carsRow = El("segmented", null);
            race.Insert(race.IndexOf(bot34Toggle), carsRow);
            for (int n = 2; n <= RaceSim.MaxCars; n++)
            {
                int c = n;
                carsBtns[n - 2] = Btn($"{n} carros", () => { if (Racing) return; set.cars4 = c; set.Save(); SetBot(); LayoutForCars(); sim.Reset(); scoreDirty = true; }, "btn", carsRow);
            }
            bot34Toggle.RegisterValueChangedCallback(e => { set.bot34 = e.newValue; set.Save(); BotLabels(); SetBot(); });
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
                  "<b>A · S</b> jogador 1: acelerar · recolocar · <b>Q</b> meia aceleração\n<b>L · K</b> jogador 2: acelerar · recolocar · <b>O</b> meia aceleração\n" +
                  "Pista de 4: <b>↑ · ↓</b> jogador 3 (<b>←</b> meia) · <b>8 · 5</b> do teclado numérico jogador 4 (<b>7</b> meia)\n" +
                  "<b>Controle:</b> gatilho direito acelera aos poucos (o 1º controle é do jogador 1, o 2º do jogador 2, e assim por diante) · <b>A/✕</b> recoloca · <b>Start</b> larga\n" +
                  "<b>Enter</b> largar · câmera no menu do canto direito", "small", how);

            var adj = Section(box, "AJUSTES");
            // Tipo de prova: por voltas (vence quem completar primeiro) ou por tempo (manches, uma por trilho).
            var kindRow = El("segmented", adj);
            var byLaps = Btn("Por voltas", null, "btn", kindRow);
            var byTime = Btn("Por tempo", null, "btn", kindRow);
            var secs = new SliderInt(2, 20) { value = Mathf.Clamp(set.secs / 30, 2, 20) };     // em meios minutos: 1 a 10 min
            var laps = new SliderInt(1, 20) { value = set.laps };
            var vmax = new Slider(8f, 20f) { value = set.vmax };
            var ramp = new Slider(0.2f, 1.5f) { value = set.ramp };
            var grip = new Slider(15f, 45f) { value = set.grip };
            var col = new Toggle("Colisão entre carrinhos") { value = set.collide };
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
                half.label = $"Meia aceleração (Q · O): {set.halfGas}%";
                float Lim(float r, float m) => Mathf.Min(100f, Mathf.Round(sim.CornerLimit(r, m) / set.vmax * 100f));
                // Raio do trilho de dentro e do de fora, no mundo: 2,25 e 2,75 na pista de 2; 4,25 e 5,75 na de 4.
                var lanes = TrackMath.LanesOf(sim.N);
                float sc = TrackMath.Scale(sim.N), rIn = (2.5f + lanes[0]) * sc, rOut = (2.5f - lanes[0]) * sc;
                gripHint.text = $"Nas curvas planas, o carrinho segura até {Lim(rIn, 1f)}% da velocidade máxima no trilho de dentro e {Lim(rOut, 1f)}% no de fora. " +
                                $"Nas inclinadas, até {Lim(rIn, PieceCatalog.BankGrip)}% e {Lim(rOut, PieceCatalog.BankGrip)}%.";
            }
            foreach (var s in new VisualElement[] { laps, secs, vmax, ramp, grip, half, col }) { s.AddToClassList("setting"); adj.Add(s); }
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
            resTable = El("res-table", banner);
            resBtns = El("res-btns", banner);
            resAgain = Btn("Correr de novo  (Enter)", () => { if (ChampLike) ChampEnter(); else StartRace(); }, "btn btn--accent res-btn", resBtns);
            resNext = Btn("Trocar pista", () => { sideAutoHidden = false; panel.SetSideFromRace(true); }, "btn res-btn", resBtns);
            lights.pickingMode = PickingMode.Ignore;

            cdBox = El("cd-box", root); cdBox.pickingMode = PickingMode.Ignore;
            cdText = Text("", "cd-text", cdBox); cdText.pickingMode = PickingMode.Ignore;
            cdBox.style.display = DisplayStyle.None;

            BuildCamMenu(root);

            padsBox = El("pads", root);
            for (int k = 0; k < RaceSim.MaxCars; k++) pads[k] = BuildPad(k);
            padsBox.style.display = DisplayStyle.None;
            topBox.style.display = DisplayStyle.None;
            LayoutForCars();
            DrawPicker(Options());
        }

        Toggle bot34Toggle;
        VisualElement carsRow;
        readonly Button[] carsBtns = new Button[RaceSim.MaxCars - 1];
        System.Action refreshAdjLabels;

        // Mostra só o que a pista usa: 2 ou 4 painéis de jogador, fichas no placar, alvos da câmera e o ajuste dos robôs 3 e 4.
        void LayoutForCars()
        {
            if (padsBox == null) return;
            int n = sim.N;
            padsBox.EnableInClassList("pads--4", n > 2);
            padsBox.EnableInClassList("pads--6", n > 4);
            hudBox.EnableInClassList("hud--6", n > 4);
            int last = 0;
            for (int k = 0; k < n; k++) if (sim.present[k]) last = k;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var on = k < n && sim.present[k] ? DisplayStyle.Flex : DisplayStyle.None;
                pads[k].root.style.display = on;
                hud[k].root.style.display = on;
                pads[k].root.EnableInClassList("pad--last", k == last);
            }
            bot34Toggle.style.display = n > 2 && (panel.CurrentMode != BuilderPanel.Mode.Race || set.cars4 > 2) ? DisplayStyle.Flex : DisplayStyle.None;
            int shown = Mathf.Clamp(set.cars4, 2, n);
            bot34Toggle.label = shown == 3 ? "Jogador 3 é um robô" : shown == 4 ? "Jogadores 3 e 4 são robôs" : $"Jogadores 3 a {shown} são robôs";
            carsRow.style.display = n > 2 ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < carsBtns.Length; i++)
            {
                carsBtns[i].style.display = i + 2 <= n ? DisplayStyle.Flex : DisplayStyle.None;     // só os que cabem na pista
                carsBtns[i].EnableInClassList("btn--on", Mathf.Clamp(set.cars4, 2, n) == i + 2);
            }
            bot34Toggle.SetValueWithoutNotify(set.bot34);
            refreshAdjLabels?.Invoke();
            ApplyCameraSettings();
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
            if (panel != null && panel.CurrentMode == BuilderPanel.Mode.Race && set.bot) return 0;
            return -1;
        }

        void ApplyCameraSettings()
        {
            int mode = Mathf.Clamp(set.cameraMode, 0, CamModes.Length - 1);
            int target = Mathf.Clamp(set.cameraTarget, 0, (sim != null ? sim.N : 2) - 1);
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
            p.reslot = Btn($"Recolocar  {KeyText(ReslotKeyAt(k))}", () => ReslotPressed(idx), "btn reslot", side);
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
            Row(new[] { "Trilho", sim.ByTime ? "Distância" : "Voltas", "Última", "Melhor", "Saídas", "Toques" }, "score-head");
            for (int k = 0; k < sim.N; k++)
            {
                var c = sim.cars[k];
                Row(new[] { $"{k + 1}{(sim.winner == k ? " · 1º" : "")}", LapsCell(k), Fmt(c.lastLap), Fmt(c.best), c.offs.ToString(), c.hits.ToString() },
                    "score-" + LaneCls[k] + (sim.winner == k ? " score-win" : ""));
            }
            raceHint.text = !sim.closed ? "Esta pista não é um circuito fechado. Escolha outra acima ou feche o traçado no Construtor."
                : sim.phase == RaceSim.Phase.Idle ? (sim.N > 2 ? $"Os {Mathf.Clamp(set.cars4, 2, sim.N)} carrinhos estão no grid." : "Os dois carrinhos estão no grid.") + " Aperte Largar e espere as luzes."
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
            padsBox.style.left = panel.SideWidth + 12f;
            for (int k = 0; k < sim.N; k++)
            {
                var c = sim.cars[k];
                var p = pads[k];
                p.fill.style.width = Length.Percent(c.T * 100f);
                p.speed.style.width = Length.Percent(Mathf.Min(100f, c.v / sim.set.vmax * 100f));
                p.slip.style.width = Length.Percent(Mathf.Min(100f, c.slip * 100f));
                p.slip.style.backgroundColor = c.slip < 0.45f ? new Color(0.24f, 0.75f, 0.45f) : c.slip < 0.75f ? new Color(0.96f, 0.76f, 0.1f) : new Color(0.94f, 0.48f, 0.23f);
                bool off = c.mode == CarMode.Off, ai = sim.aiSkill[k] > 0;
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
                if (champShow) { bannerTitle.text = title; bannerSub.text = sub; BannerColor(-1); }
                SetResult(champShow && sim.phase == RaceSim.Phase.Done && sim.winner >= 0);
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
