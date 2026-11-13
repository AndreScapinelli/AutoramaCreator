using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // A corrida do campeonato online (seção 23 do guia). Host autoritativo:
    // - o host roda o RaceSim de verdade, junta o botão de cada piloto e manda um snapshot ~30 vezes por segundo;
    // - quem está na bateria manda só o estado do botão (e "recolocar") ao host e roda o mesmo RaceSim como predição,
    //   corrigida pelo snapshot (NetSync); quem só assiste aplica o snapshot;
    // - o host salva o resultado e avisa todos (ev "res"); a tela é a do campeonato offline (DrawRun), com o chat no meio.
    public partial class RaceController
    {
        Championship onlineChamp;                       // o campeonato em andamento (null: lobby)
        bool OnlineRun => onlineChamp != null;
        Championship ActiveChamp => onlineChamp ?? store.Active;
        bool ChampLike => panel.ChampMode || OnlineRun;       // a tela de campeonato (offline ou online)

        const float SnapFast = 1f / 30f, SnapSlow = 0.25f;     // na corrida, e parado/encerrado
        const float HeldRefresh = 0.25f;                       // o visitante repete o estado do botão, por segurança
        readonly Dictionary<string, bool> remoteHeld = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> remoteBoost = new Dictionary<string, bool>();   // turbo apertado de cada visitante
        readonly Dictionary<string, float> remoteGas = new Dictionary<string, float>();   // nível do acelerador de cada visitante
        // O que mandei de cada jogador deste computador (0: eu, 1: o segundo jogador; seção 44 do guia).
        readonly bool[] lastSentBoost = new bool[2], lastSentHeld = new bool[2];
        readonly float[] lastSentGas = { 1f, 1f }, inAcc = new float[2];
        readonly int[] myRe = new int[2];
        readonly Dictionary<string, int> remoteRe = new Dictionary<string, int>();
        readonly Dictionary<string, float> remoteInAt = new Dictionary<string, float>();  // quando chegou o último "in" de cada visitante
        readonly Dictionary<string, int> remoteTs = new Dictionary<string, int>();        // o relógio que veio nele (volta no snapshot)
        const float InTimeout = 1f;                            // sem "in" por esse tempo, o botão do visitante conta como solto
        float snapAcc;
        readonly float[] seenBest = new float[RaceSim.MaxCars];   // a melhor volta de cada carro já vista (aviso de recorde)
        int snapSeq, lastSq;                                   // host: número do último snapshot mandado · visitante: do último aplicado
        float rtt = -1f;                                       // visitante: latência de ida e volta (s), média; −1 desconhecida
        readonly OwnHistory ownHist = new OwnHistory(), ownHist2 = new OwnHistory();   // visitante: a predição do carro próprio (e do segundo jogador)
        float hiMismatchSince = -1f, syncAskedAt = -10f;       // visitante: snapshot de outra bateria desde quando · último pedido de sync
        NetMsg startedMsg;                                     // o campeonato como chegou (para responder ao sync se eu virar host)
        bool takeOverStages;                                   // virei host no meio: no lobby, mando as etapas
        float phaseSince;                                      // quando a fase atual começou (largar e salvar sozinho)
        int autoShown = -1;
        const float AutoStart = 20f, AutoSave = 10f;           // o host larga e salva sozinho depois desse tempo parado

        bool OnlinePresent(string id) => room != null && room.Player(id) != null;
        string OnlineHostName() => room?.Player(room.host)?.name is string n && n != "" ? Safe(n) : "host";

        static int LaneOf(Pilot[] pl, string id) { for (int k = 0; k < pl.Length; k++) if (pl[k] != null && pl[k].id == id) return k; return -1; }

        // Os jogadores deste computador: 0 sou eu, 1 é o segundo jogador (id "<meu id>#2"), se ele está no campeonato.
        string LocalId(int j) => j == 0 ? myId : NetGuest.Id(myId);
        bool TwoLocal => onlineChamp != null && myId != null && onlineChamp.pilots.Exists(p => p.id == NetGuest.Id(myId));
        int LocalOf(string id) => id == null || myId == null ? -1 : id == myId ? 0 : id == NetGuest.Id(myId) ? 1 : -1;
        // Com dois jogadores aqui, cada um tem o seu conjunto de teclas e o seu controle (o 1º e o 2º); sozinho, vale qualquer um.
        int LocalKeySet(int j) => TwoLocal ? j : 0;

        // O trilho de um carrinho meu na bateria da vez (o meu, ou o do segundo jogador se eu não corro nela); −1 se só assisto.
        int MyLane()
        {
            if (!HeatNow(out _, out _, out _, out _, out var pl)) return -1;
            int a = LaneOf(pl, myId);
            return a >= 0 ? a : LaneOf(pl, LocalId(1));
        }
        bool IsLocalLane(int k) => HeatNow(out _, out _, out _, out _, out var pl) && k >= 0 && k < pl.Length && pl[k] != null && LocalOf(pl[k].id) >= 0;

        string OnlineWho(Pilot p)
        {
            int j = LocalOf(p.id);
            if (j >= 0) return $"{(j == 0 ? "é você" : "jogador 2 daqui")} · acelera {KeyText(GasKeyAt(LocalKeySet(j)))} · recoloca {KeyText(ReslotKeyAt(LocalKeySet(j)))}";
            return OnlinePresent(p.id) ? "online" : "saiu da sala";
        }

        // Acelerador e turbo de um jogador deste computador no trilho lane.
        void LocalInput(int j, int lane, Keyboard kb, bool keys, out float gas, out bool boost)
        {
            gas = 0f; boost = false;
            if (lane < 0) return;
            bool two = TwoLocal;
            bool Pressed(Controles.Acao a) => keys && (two ? KeyPressed(kb, Controles.Get(j, a)) : AnySetPressed(kb, a));
            if (uiHeld[lane] || Pressed(Controles.Acao.Acelerar)) gas = 1f;
            else if (Pressed(Controles.Acao.Meia)) gas = set.halfGas / 100f;
            if (two) gas = Mathf.Max(gas, Trigger(PadOf(j))); else foreach (var gp in Gamepad.all) gas = Mathf.Max(gas, Trigger(gp));
            if (!sim.set.turbo) return;
            boost = uiTurbo[lane] || Pressed(Controles.Acao.Turbo);
            if (two) boost |= PadTurbo(PadOf(j)); else foreach (var gp in Gamepad.all) boost |= PadTurbo(gp);
            if (boost) gas = 1f;
        }

        bool LocalReslotPressed(int j, Keyboard kb, bool keys)
        {
            bool two = TwoLocal;
            bool hit = keys && (two ? KeyWasPressed(kb, ReslotKeyAt(j)) : AnySetWasPressed(kb, Controles.Acao.Recolocar));
            if (two) { var g = PadOf(j); hit |= g != null && g.buttonSouth.wasPressedThisFrame; }
            else foreach (var gp in Gamepad.all) hit |= gp.buttonSouth.wasPressedThisFrame;
            return hit;
        }

        // ---------- Começar e terminar ----------

        void BeginOnlineRun(NetMsg m)
        {
            Championship C;
            try
            {
                C = new Championship
                {
                    id = "online", name = string.IsNullOrWhiteSpace(m.title) ? "Campeonato online" : Safe(m.title), created = Champ.Now,
                    rules = new ChampRules { vmax = m.rules.vmax, ramp = m.rules.ramp, grip = m.rules.grip, collide = m.rules.collide, turbo = m.rules.turbo },
                    byTime = m.rules.byTime,
                };
                for (int i = 0; i < m.pilots.Count; i++)
                {
                    var p = m.pilots[i];
                    string tag = Champ.TagOf(p.name);
                    // uid: o id fixo do jogador (melhor pessoal online). Na Steam ele sai do próprio SteamID, não do que o host diz.
                    string uid = p.uid != null && p.uid.StartsWith("steam:") ? "steam:" + p.id : p.uid;
                    C.pilots.Add(new Pilot { id = p.id, name = Safe(p.name), tag = tag != "" ? tag : "P" + (i + 1), color = p.color, uid = uid });
                }
                // A identidade da pista (recordes) é calculada aqui, a partir do JSON, e não a que o host mandou.
                foreach (var s in m.stages) { var d = TrackSave.FromJson(s.json); C.stages.Add(new Stage { name = Safe(s.name), laps = s.laps, secs = s.secs > 0 ? s.secs : 120, hash = Champ.TrackHash(d), data = d }); }
                if (C.pilots.Count < 2 || C.stages.Count == 0) throw new Exception("campeonato vazio");
                // Pista de 2 ou de 4 (a primeira etapa decide; todas iguais). Com menos jogadores que trilhos, sobra trilho vazio.
                C.faixas = C.stages[0].data.Faixas;
                foreach (var s in C.stages) if (s.data.Faixas != C.faixas) throw new Exception("etapas de tipos diferentes");
                C.heats = Champ.MakeHeats(C.pilots.Count, C.stages.Count, C.faixas, C.byTime, m.rules.sorteio);      // o host sorteia; servidor antigo manda 0
            }
            catch (Exception e)
            {
                Debug.LogWarning("Campeonato online inválido: " + e.Message);
                panel.Toast("Não deu para abrir as pistas do campeonato. Você saiu da sala.");
                LeaveRoom();
                return;
            }

            onlineChamp = C;
            startedMsg = m;
            ClearRemote();
            for (int j = 0; j < 2; j++) { lastSentBoost[j] = lastSentHeld[j] = false; lastSentGas[j] = 1f; myRe[j] = 0; inAcc[j] = 0f; }
            snapAcc = 1f;
            snapSeq = 0; lastSq = 0; rtt = -1f; ClearOwnHist(true); hiMismatchSince = -1f; takeOverStages = false;
            for (int k = 0; k < RaceSim.MaxCars; k++) uiHeld[k] = false;
            panel.SetOnlineRacing(true);
            ApplyCameraSettings();
            // A tela do campeonato offline muda de aba, e o chat entra logo abaixo do placar da bateria.
            oRun.Insert(0, cRun);
            cRun.Insert(Mathf.Min(1, cRun.childCount), oRunChatSec);
            cRun.style.display = DisplayStyle.Flex;
            topBox.style.display = DisplayStyle.Flex;
            DrawOnline();
            OnlineHeatLoaded();
        }

        // Volta ao lobby (campeonato acabou, host saiu, conexão caiu…).
        void OnlineExitRun()
        {
            if (!OnlineRun) return;
            onlineChamp = null;
            panel.SetOnlineRacing(false);
            oRunChatSec.RemoveFromHierarchy();
            panel.ChampBox.Add(cRun);                    // devolve a tela à aba Campeonato (ela se redesenha ao abrir)
            ClearRemote(); startedMsg = null;
            for (int k = 0; k < RaceSim.MaxCars; k++) uiHeld[k] = false;
            sim.set = set; sim.cutoff = null; System.Array.Clear(sim.aiSkill, 0, sim.aiSkill.Length); System.Array.Clear(sim.setup, 0, sim.setup.Length);
            for (int k = 0; k < RaceSim.MaxCars; k++) sim.present[k] = true;
            sim.Reset();
            cam.SetRaceViewMode(TopDownCamera.RaceViewMode.Atual);
            if (builderStash != null) builder.ShowTrack(builderStash);
            panel.FrameTrack();
            topBox.style.display = DisplayStyle.None;
            DrawPadHeads();
            scoreDirty = true;
        }

        // Uma bateria nova está valendo: carrega a pista, zera o grid e redesenha.
        void OnlineHeatLoaded()
        {
            UseChampTrack();
            sim.Reset();
            ClearSeenBest();
            for (int k = 0; k < RaceSim.MaxCars; k++) uiHeld[k] = false;
            lastSentHeld[0] = lastSentHeld[1] = false; snapAcc = 1f;
            ClearOwnHist(false); hiMismatchSince = -1f; phaseSince = Time.realtimeSinceStartup;
            if (HeatNow(out _, out int idx, out _, out _, out var pl))
            {
                int lane = MyLane();
                if (lane >= 0) cam.SetThirdPersonCar(lane);
                // Ninguém da bateria está na sala: vale como não terminada, sem esperar.
                if (IsHost && !Array.Exists(pl, p => p != null && OnlinePresent(p.id))) { OnlineCommit(idx, OnlineDnf()); return; }
            }
            scoreDirty = champDirty = true;
            DrawOnlineRun();
        }

        void DrawOnlineRun()
        {
            if (!OnlineRun) return;
            DrawRun(onlineChamp);
            oRunQuit.text = QuitLabel();
        }

        string QuitLabel() => IsHost ? "Encerrar campeonato" : "Sair da sala";

        // ---------- Quadro a quadro ----------

        void OnlineRunUpdate()
        {
            if (sim == null || !sim.Ready || !OnlineRun) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var kb = Keyboard.current;
            bool keys = kb != null && !IsTyping && !keysOpen;
            bool live = HeatNow(out _, out int idx, out _, out _, out var pl);
            int lane = live ? LaneOf(pl, myId) : -1, lane2 = live ? LaneOf(pl, LocalId(1)) : -1;
            if (live)
            {
                // Online cada um pilota o seu carrinho: sozinho, as teclas de qualquer conjunto e o gatilho de qualquer controle;
                // com o segundo jogador aqui, cada um com o seu conjunto (1º e 2º) e o seu controle. Turbo também acelera a 100%.
                LocalInput(0, lane, kb, keys, out float myGas, out bool myBoost);
                LocalInput(1, lane2, kb, keys, out float gas2, out bool boost2);
                if (IsHost)
                {
                    for (int k = 0; k < sim.N && k < pl.Length; k++)
                    {
                        if (k == lane || k == lane2)
                        {
                            float g0 = k == lane ? myGas : gas2;
                            sim.cars[k].held = g0 > 0f; sim.cars[k].boost = k == lane ? myBoost : boost2; if (g0 > 0f) sim.cars[k].gas = g0;
                            continue;
                        }
                        if (pl[k] == null) { sim.cars[k].held = false; continue; }     // trilho vazio
                        // Sem "in" há mais de 1 s (o visitante repete a cada 0,25 s): a conexão dele travou, o botão conta como solto.
                        bool fresh = remoteInAt.TryGetValue(pl[k].id, out var at) && Time.realtimeSinceStartup - at < InTimeout;
                        sim.cars[k].held = fresh && OnlinePresent(pl[k].id) && remoteHeld.TryGetValue(pl[k].id, out var h) && h;
                        sim.cars[k].gas = remoteGas.TryGetValue(pl[k].id, out var g) ? g : 1f;
                        sim.cars[k].boost = sim.cars[k].held && remoteBoost.TryGetValue(pl[k].id, out var bo) && bo;
                    }
                }
                else
                {
                    if (lane >= 0) { sim.cars[lane].held = myGas > 0f; sim.cars[lane].boost = myBoost; if (myGas > 0f) sim.cars[lane].gas = myGas; SendHeld(0, myGas > 0f, myGas, myBoost, dt); }
                    if (lane2 >= 0) { sim.cars[lane2].held = gas2 > 0f; sim.cars[lane2].boost = boost2; if (gas2 > 0f) sim.cars[lane2].gas = gas2; SendHeld(1, gas2 > 0f, gas2, boost2, dt); }
                }
                if (lane >= 0 && LocalReslotPressed(0, kb, keys)) OnlineReslot(lane);
                if (lane2 >= 0 && LocalReslotPressed(1, kb, keys)) OnlineReslot(lane2);
                if (IsHost && ((keys && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || AnyPadStart())) OnlineEnter();
                // O host parado não trava a sala: larga e salva sozinho.
                float idle = Time.realtimeSinceStartup - phaseSince;
                if (IsHost && sim.phase == RaceSim.Phase.Idle && sim.closed && idle >= AutoStart) OnlineStart();
                else if (IsHost && sim.phase == RaceSim.Phase.Done && idle >= AutoSave) OnlineSave();
                if (AutoLeft() != autoShown) champDirty = true;
            }

            sim.Step(dt);
            if (IsHost) OnlineLapFlash();
            if (sim.phase != lastPhase) { lastPhase = sim.phase; scoreDirty = champDirty = true; snapAcc = 1f; SideForPhase(); phaseSince = Time.realtimeSinceStartup; }
            if (!IsHost && lane >= 0) ownHist.Add(Time.realtimeSinceStartup, sim.cars[lane]);
            if (!IsHost && lane2 >= 0) ownHist2.Add(Time.realtimeSinceStartup, sim.cars[lane2]);
            if (IsHost && live) SendSnapshot(dt, idx, pl);

            PlaceCars();
            UpdatePads();
            UpdateTop();
            if (scoreDirty) { scoreDirty = false; DrawScore(); DrawStatus(); }
            if (champDirty) { champDirty = false; DrawOnlineRun(); }
            cClock.text = ClockText();
        }

        // Manda o botão e o nível do acelerador quando mudam (o nível só conta a partir de 3 pontos) ou a cada HeldRefresh.
        // j: o jogador deste computador (1 = o segundo: a mensagem vai com g).
        void SendHeld(int j, bool held, float gas, bool boost, float dt)
        {
            inAcc[j] += dt;
            bool gasMoved = held && Mathf.Abs(gas - lastSentGas[j]) >= 0.03f;
            if (held == lastSentHeld[j] && boost == lastSentBoost[j] && !gasMoved && inAcc[j] < HeldRefresh) return;
            lastSentHeld[j] = held; lastSentBoost[j] = boost; inAcc[j] = 0f;
            if (held) lastSentGas[j] = gas;
            SendIn(j);
        }

        void SendIn(int j) => net.Send(new NetOut { t = "in", g = j == 1, held = lastSentHeld[j], gp = NetSync.GasPct(lastSentGas[j]), tb = lastSentBoost[j], re = myRe[j], ts = NetSync.ClockMs });

        void ClearOwnHist(bool lag)
        {
            ownHist.Clear(); ownHist2.Clear();
            if (lag) ownHist.lag = ownHist2.lag = -1f;
        }

        // Cada carro de visitante leva o eco do relógio dele (ts do último "in") e há quanto tempo chegou: ele mede a latência.
        void SendSnapshot(float dt, int heat, Pilot[] pl)
        {
            snapAcc += dt;
            bool racing = Racing;
            if (snapAcc < (racing ? SnapFast : SnapSlow)) return;
            snapAcc = 0f;
            var s = NetSync.Pack(sim, heat);
            s.sq = ++snapSeq;
            float now = Time.realtimeSinceStartup;
            for (int k = 0; k < s.cars.Count && k < pl.Length; k++)
                if (pl[k] != null && remoteTs.TryGetValue(pl[k].id, out int ts) && remoteInAt.TryGetValue(pl[k].id, out float at))
                { s.cars[k].ec = ts; s.cars[k].ea = Mathf.RoundToInt((now - at) * 1000f); }
            net.Send(new NetOutSnap { b = NetSync.Encode(s) });
        }

        // Segundos até o host largar ou salvar sozinho; −1 fora dessas fases.
        int AutoLeft()
        {
            float wait = sim.phase == RaceSim.Phase.Idle && sim.closed ? AutoStart : sim.phase == RaceSim.Phase.Done ? AutoSave : -1f;
            return wait < 0f ? -1 : Mathf.Max(0, Mathf.CeilToInt(wait - (Time.realtimeSinceStartup - phaseSince)));
        }

        void ClearRemote() { remoteHeld.Clear(); remoteRe.Clear(); remoteGas.Clear(); remoteBoost.Clear(); remoteInAt.Clear(); remoteTs.Clear(); }

        // ---------- Entradas e snapshots recebidos ----------

        void ReslotPressed(int k)
        {
            if (OnlineRun) OnlineReslot(k); else sim.Reslot(k);
        }

        void OnlineReslot(int k)
        {
            sim.Reslot(k);                                // o visitante já vê (predição); o host é quem vale
            if (IsHost || !HeatNow(out _, out _, out _, out _, out var pl) || k < 0 || k >= pl.Length || pl[k] == null) return;
            int j = LocalOf(pl[k].id);
            if (j < 0) return;
            myRe[j]++;
            lastSentHeld[j] = sim.cars[k].held; inAcc[j] = 0f;
            SendIn(j);
        }

        // Host: o botão (e o "recolocar") de um visitante.
        void OnlineInput(NetMsg m)
        {
            if (!OnlineRun || !IsHost || string.IsNullOrEmpty(m.from)) return;
            remoteHeld[m.from] = m.held;
            remoteGas[m.from] = NetSync.GasOf(m.gp);
            remoteBoost[m.from] = m.tb;
            remoteInAt[m.from] = Time.realtimeSinceStartup;
            remoteTs[m.from] = m.ts;
            int prev = remoteRe.TryGetValue(m.from, out int r) ? r : 0;
            if (m.re <= prev) return;
            remoteRe[m.from] = m.re;
            if (HeatNow(out _, out _, out _, out _, out var pl)) { int lane = LaneOf(pl, m.from); if (lane >= 0) sim.Reslot(lane); }
        }

        // Visitante: o estado do host. Só vale para a bateria da vez, e só se for mais novo que o último (na Steam ele pode chegar fora de ordem).
        void OnlineApplySnap(string b)
        {
            var s = NetSync.Decode(b);
            if (s == null || !HeatNow(out _, out int idx, out _, out _, out var pl)) return;
            if (s.hi != idx)
            {
                // Host noutra bateria por mais de 1,5 s: perdi um resultado. Peço o campeonato de novo.
                if (hiMismatchSince < 0f) hiMismatchSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - hiMismatchSince > 1.5f) AskSync();
                return;
            }
            hiMismatchSince = -1f;
            if (s.sq <= lastSq) return;
            lastSq = s.sq;
            int lane = LaneOf(pl, myId), lane2 = LaneOf(pl, LocalId(1)), eco = lane >= 0 ? lane : lane2;
            if (eco >= 0 && eco < s.cars.Count)
            {
                float r = NetSync.Rtt(s.cars[eco], NetSync.ClockMs);
                if (r >= 0f) { rtt = rtt < 0f ? r : Mathf.Lerp(rtt, r, 0.1f); ownHist.lag = ownHist2.lag = rtt * 0.5f; }
            }
            int before = ScoreSig();
            NetSync.Apply(s, sim, lane, ownHist, lane2, ownHist2);
            OnlineLapFlash();
            if (ScoreSig() != before) scoreDirty = true;
        }

        // Online o aviso "Recorde da pista!" sai da melhor volta que muda: no host, depois do Step; no visitante, depois do
        // snapshot (a predição do visitante não conta). Zera a cada bateria e a cada largada.
        void OnlineLapFlash()
        {
            float human = float.PositiveInfinity;
            for (int k = 0; k < sim.N; k++) if (sim.present[k] && !float.IsPositiveInfinity(seenBest[k])) human = Mathf.Min(human, seenBest[k]);
            for (int k = 0; k < sim.N; k++)
            {
                float b = sim.cars[k].best ?? float.PositiveInfinity;
                if (b < seenBest[k] && sim.phase != RaceSim.Phase.Idle) LapFlash(k, b, seenBest[k], human);
                seenBest[k] = b;
            }
        }

        void ClearSeenBest() { for (int k = 0; k < seenBest.Length; k++) seenBest[k] = float.PositiveInfinity; }

        int ScoreSig()
        {
            unchecked
            {
                int h = (int)sim.phase * 31 + sim.winner;
                foreach (var c in sim.cars)
                    h = (((((h * 31 + c.laps) * 31 + c.offs) * 31 + c.hits) * 31 + (c.finish.HasValue ? 1 : 0)) * 31 + (c.lastLap ?? -1f).GetHashCode()) * 31 + (c.best ?? -1f).GetHashCode();
                return h;
            }
        }

        // Visitante: o host salvou um resultado (res) ou mandou correr a bateria de novo (reset).
        void OnlineEvent(NetMsg m)
        {
            if (!OnlineRun || IsHost) return;
            if (m.k == "res")
            {
                if (m.hi < 0 || m.hi >= onlineChamp.heats.Count || m.heatRes.Count < onlineChamp.heats[m.hi].p.Length) return;
                if (m.hi != Champ.CurrentHeat(onlineChamp)) { AskSync(); return; }      // perdi um resultado: o host manda tudo de novo
                var res = m.heatRes.GetRange(0, onlineChamp.heats[m.hi].p.Length).ToArray();
                HeatRecords(onlineChamp, m.hi, res, true);         // cada um grava os recordes no seu recordes.json
                onlineChamp.heats[m.hi].res = res;
                if (Champ.CurrentHeat(onlineChamp) < 0) onlineChamp.finished = Champ.Now;
                OnlineHeatLoaded();
            }
            else if (m.k == "reset") { sim.Reset(); ClearSeenBest(); ClearOwnHist(false); phaseSince = Time.realtimeSinceStartup; scoreDirty = champDirty = true; }
        }

        // ---------- Sync: entrar no meio, voltar de uma queda, host novo ----------

        // Visitante: pede ao host o campeonato inteiro (no máximo a cada 3 s).
        void AskSync()
        {
            if (IsHost || Time.realtimeSinceStartup - syncAskedAt < 3f) return;
            syncAskedAt = Time.realtimeSinceStartup;
            net.Send(new NetOut { t = "sync" });
        }

        NetOutEv SyncEv(string to)
        {
            var ev = new NetOutEv { k = "sync", to = to };
            if (!string.IsNullOrEmpty(to) && startedMsg != null)
            {
                ev.title = startedMsg.title; ev.rules = startedMsg.rules;
                ev.pilots.AddRange(startedMsg.pilots); ev.stages.AddRange(startedMsg.stages);
            }
            foreach (var h in onlineChamp.heats) { var x = new NetHeatRes(); if (h.Saved) x.r.AddRange(h.res); ev.heats.Add(x); }
            return ev;
        }

        // Host: alguém pediu o campeonato (entrou para assistir, voltou de uma queda ou perdeu um resultado).
        void OnlineSyncRequest(string from)
        {
            if (!OnlineRun || !IsHost || string.IsNullOrEmpty(from) || startedMsg == null) return;
            net.Send(SyncEv(from));
        }

        // Visitante: o campeonato do host. Sem campeonato aberto, ele começa daqui (precisa das pistas: só vem no sync pedido).
        // Os resultados do host substituem os meus e a bateria da vez recomeça. Recordes das baterias perdidas não são gravados.
        void OnlineApplySync(NetMsg m)
        {
            if (IsHost) return;
            if (!OnlineRun)
            {
                if (m.stages.Count == 0) { syncAskedAt = -10f; AskSync(); return; }
                BeginOnlineRun(new NetMsg { t = "started", title = m.title, rules = m.rules, pilots = m.pilots, stages = m.stages });
                if (!OnlineRun) return;
            }
            var C = onlineChamp;
            if (m.heats.Count != C.heats.Count) return;
            for (int i = 0; i < C.heats.Count; i++)
                C.heats[i].res = m.heats[i].r.Count == C.heats[i].p.Length ? m.heats[i].r.ToArray() : null;
            C.finished = Champ.CurrentHeat(C) < 0 ? Champ.Now : 0;
            lastSq = 0;
            OnlineHeatLoaded();
        }

        // O host saiu no meio e eu virei host: todos recebem a minha tabela e a bateria da vez recomeça.
        // As etapas do lobby vêm do campeonato (o JSON das pistas eu tenho); vão para a sala quando voltarmos ao lobby.
        void OnlineTakeOver()
        {
            ClearRemote();
            snapSeq = 0;
            onlineStages.Clear();
            foreach (var s in onlineChamp.stages) onlineStages.Add(new Stage { name = s.name, laps = s.laps, secs = s.secs, data = s.data, hash = s.hash });
            takeOverStages = true;
            net.Send(SyncEv(null));
            panel.Toast("Você agora é o host: a bateria da vez recomeça.");
            OnlineHeatLoaded();
        }

        // Visitante: o host mudou. O número dos snapshots recomeça com ele, e a latência é outra.
        void OnlineHostChanged()
        {
            lastSq = 0; rtt = -1f; ClearOwnHist(true); hiMismatchSince = -1f;
        }

        // ---------- Ações do host ----------

        void OnlineStart()
        {
            if (!IsHost || !HeatNow(out _, out _, out _, out _, out _) || sim.phase != RaceSim.Phase.Idle || !sim.closed) return;
            sim.StartRace();
            scoreDirty = champDirty = true; snapAcc = 1f;
        }

        void OnlineEnter()
        {
            if (sim.phase == RaceSim.Phase.Idle) OnlineStart();
            else if (sim.phase == RaceSim.Phase.Done) OnlineSave();
        }

        void OnlineRestart()
        {
            if (!IsHost) return;
            sim.Reset();
            ClearSeenBest();
            var ev = new NetOutEv { k = "reset" };
            net.Send(ev);
            scoreDirty = champDirty = true; snapAcc = 1f;
        }

        HeatResult[] OnlineResults()
        {
            var res = new HeatResult[sim.N];
            for (int k = 0; k < sim.N; k++)
            {
                var c = sim.cars[k];
                if (!sim.present[k]) { res[k] = new HeatResult { dnf = true }; continue; }
                res[k] = new HeatResult { laps = c.laps, time = c.finish ?? sim.t, dnf = !c.finish.HasValue && !sim.ByTime, best = c.best ?? -1f, fin = c.finish ?? -1f, offs = c.offs, hits = c.hits, dist = Mathf.Max(0f, sim.Dist(k)) };
            }
            return res;
        }

        HeatResult[] OnlineDnf() { var r = new HeatResult[sim.N]; for (int k = 0; k < r.Length; k++) r[k] = new HeatResult { dnf = true }; return r; }

        void OnlineSave()
        {
            if (!IsHost || sim.phase != RaceSim.Phase.Done || !HeatNow(out _, out int idx, out _, out _, out _)) return;
            OnlineCommit(idx, OnlineResults());
        }

        // Grava o resultado aqui, avisa todos e carrega a próxima bateria.
        void OnlineCommit(int idx, HeatResult[] res)
        {
            var C = onlineChamp;
            HeatRecords(C, idx, res, true);
            C.heats[idx].res = res;
            if (Champ.CurrentHeat(C) < 0) C.finished = Champ.Now;
            var ev = new NetOutEv { k = "res", hi = idx };
            ev.heatRes.AddRange(res);
            net.Send(ev);
            OnlineHeatLoaded();
        }

        void OnlineEnd() { if (IsHost) net.Send(new NetOut { t = "end", text = "O campeonato terminou." }); }

        void OnlineQuit()
        {
            if (!panel.ConfirmTwice("online-quit"))
            {
                oRunQuit.text = "Certeza? Clique de novo";
                oRunQuit.schedule.Execute(() => oRunQuit.text = QuitLabel()).ExecuteLater(3000);
                return;
            }
            oRunQuit.text = QuitLabel();
            if (IsHost) net.Send(new NetOut { t = "end", text = "O host encerrou o campeonato." });
            else LeaveRoom();
        }

        string Keys2Hint() => TwoLocal ? $" (o jogador 2 daqui: {KeyText(GasKeyAt(1))} e {KeyText(ReslotKeyAt(1))}, ou o 2º controle)" : "";

        // Os botões e a dica do cartão da bateria (chamado por DrawRun no lugar dos botões do offline).
        void DrawOnlineActs(bool live, int idx, Stage stage)
        {
            var ph = sim.phase;
            bool host = IsHost;
            if (host)
            {
                if (!live) Btn("Voltar ao lobby", OnlineEnd, "btn btn--primary", cActs);
                else if (ph == RaceSim.Phase.Idle) Btn("Largar (Enter)", OnlineStart, "btn btn--primary", cActs).SetEnabled(sim.closed);
                else if (ph == RaceSim.Phase.Done) { Btn("Salvar e seguir (Enter)", OnlineSave, "btn btn--primary", cActs); Btn("Correr de novo", OnlineRestart, "btn", cActs); }
                else Btn("Recomeçar bateria", OnlineRestart, "btn", cActs);
            }
            string laps = stage != null ? $"{stage.laps} {(stage.laps == 1 ? "volta" : "voltas")}" : "";
            cHint.text = !live ? (host ? "O campeonato acabou. Voltar ao lobby leva todos de volta para montar outro."
                                       : "O campeonato acabou. O host leva todos de volta ao lobby.")
                : ph == RaceSim.Phase.Idle ? (host ? $"Espere os pilotos da bateria se prepararem e aperte Largar. Quem corre acelera com {KeyText(GasKeyAt(0))} e recoloca com {KeyText(ReslotKeyAt(0))}{Keys2Hint()}."
                                                   : $"Aguardando o host largar. Quem corre acelera com {KeyText(GasKeyAt(0))} e recoloca com {KeyText(ReslotKeyAt(0))}{Keys2Hint()}.")
                : ph == RaceSim.Phase.Done ? (host ? "Salve para o resultado contar na tabela de todos. Se algo deu errado, corra de novo: esta bateria é descartada."
                                                   : "Bateria encerrada. O host salva o resultado e segue.")
                : $"Vence a bateria quem completar {laps} primeiro. Quem não terminar tem {Champ.Cutoff:0} s depois da bandeirada.";
            autoShown = live ? AutoLeft() : -1;
            if (autoShown >= 0) cHint.text += ph == RaceSim.Phase.Idle ? $" Larga sozinho em {autoShown} s." : $" Salva e segue sozinho em {autoShown} s.";
        }
    }
}
