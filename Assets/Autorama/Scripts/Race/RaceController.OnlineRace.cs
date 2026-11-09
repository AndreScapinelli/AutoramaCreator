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
        readonly Dictionary<string, float> remoteGas = new Dictionary<string, float>();   // nível do acelerador de cada visitante
        float lastSentGas = 1f;
        readonly Dictionary<string, int> remoteRe = new Dictionary<string, int>();
        float snapAcc, inAcc;
        bool lastSentHeld;
        int myRe;

        bool OnlinePresent(string id) => room != null && room.Player(id) != null;

        static int LaneOf(Pilot[] pl, string id) { for (int k = 0; k < pl.Length; k++) if (pl[k] != null && pl[k].id == id) return k; return -1; }

        // O trilho do meu carrinho na bateria da vez; −1 se estou só assistindo.
        int MyLane() => HeatNow(out _, out _, out _, out _, out var pl) ? LaneOf(pl, myId) : -1;

        string OnlineWho(Pilot p) => p.id == myId ? "é você · acelera A · recoloca S" : OnlinePresent(p.id) ? "online" : "saiu da sala";

        // ---------- Começar e terminar ----------

        void BeginOnlineRun(NetMsg m)
        {
            Championship C;
            try
            {
                C = new Championship
                {
                    id = "online", name = string.IsNullOrWhiteSpace(m.title) ? "Campeonato online" : Safe(m.title), created = Champ.Now,
                    rules = new ChampRules { vmax = m.rules.vmax, ramp = m.rules.ramp, grip = m.rules.grip, collide = m.rules.collide },
                    byTime = m.rules.byTime,
                };
                for (int i = 0; i < m.pilots.Count; i++)
                {
                    var p = m.pilots[i];
                    string tag = Champ.TagOf(p.name);
                    C.pilots.Add(new Pilot { id = p.id, name = Safe(p.name), tag = tag != "" ? tag : "P" + (i + 1), color = p.color });
                }
                foreach (var s in m.stages) C.stages.Add(new Stage { name = Safe(s.name), laps = s.laps, secs = s.secs > 0 ? s.secs : 120, hash = s.hash, data = TrackSave.FromJson(s.json) });
                if (C.pilots.Count < 2 || C.stages.Count == 0) throw new Exception("campeonato vazio");
                // Pista de 2 ou de 4 (a primeira etapa decide; todas iguais). Com menos jogadores que trilhos, sobra trilho vazio.
                C.faixas = C.stages[0].data.Faixas;
                foreach (var s in C.stages) if (s.data.Faixas != C.faixas) throw new Exception("etapas de tipos diferentes");
                C.heats = Champ.MakeHeats(C.pilots.Count, C.stages.Count, C.faixas, C.byTime);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Campeonato online inválido: " + e.Message);
                panel.Toast("Não deu para abrir as pistas do campeonato. Você saiu da sala.");
                LeaveRoom();
                return;
            }

            onlineChamp = C;
            remoteHeld.Clear(); remoteRe.Clear(); remoteGas.Clear(); lastSentGas = 1f;
            myRe = 0; lastSentHeld = false; snapAcc = 1f; inAcc = 0f;
            for (int k = 0; k < RaceSim.MaxCars; k++) uiHeld[k] = false;
            panel.SetOnlineRacing(true);
            ApplyCameraSettings();
            // A tela do campeonato offline muda de aba, e o chat entra logo abaixo do placar da bateria.
            oRun.Insert(0, cRun);
            cRun.Insert(Mathf.Min(1, cRun.childCount), oRunChatSec);
            cRecs.parent.style.display = DisplayStyle.None;
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
            cRecs.parent.style.display = DisplayStyle.Flex;
            remoteHeld.Clear(); remoteRe.Clear();
            for (int k = 0; k < RaceSim.MaxCars; k++) uiHeld[k] = false;
            sim.set = set; sim.cutoff = null; System.Array.Clear(sim.aiSkill, 0, sim.aiSkill.Length);
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
            for (int k = 0; k < RaceSim.MaxCars; k++) uiHeld[k] = false;
            lastSentHeld = false; snapAcc = 1f;
            if (HeatNow(out _, out int idx, out _, out _, out var pl))
            {
                int lane = LaneOf(pl, myId);
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
            bool keys = kb != null && !IsTyping;
            bool live = HeatNow(out _, out int idx, out _, out _, out var pl);
            int lane = live ? LaneOf(pl, myId) : -1;
            if (live)
            {
                // Online cada um pilota o seu carrinho: A/L/Espaço no fundo, Q/O meia, ou o gatilho de qualquer controle.
                float myGas = 0f;
                if (lane >= 0)
                {
                    if (uiHeld[lane] || (keys && (kb.aKey.isPressed || kb.lKey.isPressed || kb.spaceKey.isPressed))) myGas = 1f;
                    else if (keys && (kb.qKey.isPressed || kb.oKey.isPressed)) myGas = set.halfGas / 100f;
                    foreach (var gp in Gamepad.all) myGas = Mathf.Max(myGas, Trigger(gp));
                }
                bool mine = myGas > 0f;
                if (IsHost)
                {
                    for (int k = 0; k < sim.N && k < pl.Length; k++)
                    {
                        if (k == lane) { sim.cars[k].held = mine; if (mine) sim.cars[k].gas = myGas; continue; }
                        if (pl[k] == null) { sim.cars[k].held = false; continue; }     // trilho vazio
                        sim.cars[k].held = OnlinePresent(pl[k].id) && remoteHeld.TryGetValue(pl[k].id, out var h) && h;
                        sim.cars[k].gas = remoteGas.TryGetValue(pl[k].id, out var g) ? g : 1f;
                    }
                }
                else if (lane >= 0) { sim.cars[lane].held = mine; if (mine) sim.cars[lane].gas = myGas; SendHeld(mine, myGas, dt); }
                bool padReslot = false;
                foreach (var gp in Gamepad.all) padReslot |= gp.buttonSouth.wasPressedThisFrame;
                if (lane >= 0 && ((keys && (kb.sKey.wasPressedThisFrame || kb.kKey.wasPressedThisFrame)) || padReslot)) OnlineReslot(lane);
                if (IsHost && ((keys && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || AnyPadStart())) OnlineEnter();
            }

            sim.Step(dt);
            if (sim.phase != lastPhase) { lastPhase = sim.phase; scoreDirty = champDirty = true; snapAcc = 1f; SideForPhase(); }
            if (IsHost && live) SendSnapshot(dt, idx);

            PlaceCars();
            UpdatePads();
            UpdateTop();
            if (scoreDirty) { scoreDirty = false; DrawScore(); DrawStatus(); }
            if (champDirty) { champDirty = false; DrawOnlineRun(); }
            cClock.text = ClockText();
        }

        // Manda o botão e o nível do acelerador quando mudam (o nível só conta a partir de 3 pontos) ou a cada HeldRefresh.
        void SendHeld(bool held, float gas, float dt)
        {
            inAcc += dt;
            bool gasMoved = held && Mathf.Abs(gas - lastSentGas) >= 0.03f;
            if (held == lastSentHeld && !gasMoved && inAcc < HeldRefresh) return;
            lastSentHeld = held; inAcc = 0f;
            if (held) lastSentGas = gas;
            net.Send(new NetOut { t = "in", held = held, gp = NetSync.GasPct(lastSentGas), re = myRe });
        }

        void SendSnapshot(float dt, int heat)
        {
            snapAcc += dt;
            bool racing = Racing;
            if (snapAcc < (racing ? SnapFast : SnapSlow)) return;
            snapAcc = 0f;
            net.Send(new NetOutSnap { snap = NetSync.Pack(sim, heat) });
        }

        // ---------- Entradas e snapshots recebidos ----------

        void ReslotPressed(int k)
        {
            if (OnlineRun) OnlineReslot(k); else sim.Reslot(k);
        }

        void OnlineReslot(int k)
        {
            sim.Reslot(k);                                // o visitante já vê (predição); o host é quem vale
            if (IsHost) return;
            myRe++;
            lastSentHeld = sim.cars[k].held; inAcc = 0f;
            net.Send(new NetOut { t = "in", held = lastSentHeld, gp = NetSync.GasPct(lastSentGas), re = myRe });
        }

        // Host: o botão (e o "recolocar") de um visitante.
        void OnlineInput(NetMsg m)
        {
            if (!OnlineRun || !IsHost || string.IsNullOrEmpty(m.from)) return;
            remoteHeld[m.from] = m.held;
            remoteGas[m.from] = NetSync.GasOf(m.gp);
            int prev = remoteRe.TryGetValue(m.from, out int r) ? r : 0;
            if (m.re <= prev) return;
            remoteRe[m.from] = m.re;
            if (HeatNow(out _, out _, out _, out _, out var pl)) { int lane = LaneOf(pl, m.from); if (lane >= 0) sim.Reslot(lane); }
        }

        // Visitante: o estado do host. Só vale para a bateria da vez.
        void OnlineApplySnap(NetSnap s)
        {
            if (!HeatNow(out _, out int idx, out _, out _, out var pl) || s.hi != idx) return;
            int before = ScoreSig();
            NetSync.Apply(s, sim, LaneOf(pl, myId));
            if (ScoreSig() != before) scoreDirty = true;
        }

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
                if (m.hi != Champ.CurrentHeat(onlineChamp) || m.heatRes.Count < onlineChamp.heats[m.hi].p.Length) return;
                onlineChamp.heats[m.hi].res = m.heatRes.GetRange(0, onlineChamp.heats[m.hi].p.Length).ToArray();
                if (Champ.CurrentHeat(onlineChamp) < 0) onlineChamp.finished = Champ.Now;
                OnlineHeatLoaded();
            }
            else if (m.k == "reset") { sim.Reset(); scoreDirty = champDirty = true; }
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
                res[k] = new HeatResult { laps = c.laps, time = c.finish ?? sim.t, dnf = !c.finish.HasValue && !sim.ByTime, best = c.best ?? -1f, offs = c.offs, hits = c.hits, dist = Mathf.Max(0f, sim.Dist(k)) };
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
                : ph == RaceSim.Phase.Idle ? (host ? "Espere os pilotos da bateria se prepararem e aperte Largar. Quem corre acelera com A (ou L, Espaço) e recoloca com S."
                                                   : "Aguardando o host largar. Quem corre acelera com A (ou L, Espaço) e recoloca com S.")
                : ph == RaceSim.Phase.Done ? (host ? "Salve para o resultado contar na tabela de todos. Se algo deu errado, corra de novo: esta bateria é descartada."
                                                   : "Bateria encerrada. O host salva o resultado e segue.")
                : $"Vence a bateria quem completar {laps} primeiro. Quem não terminar tem {Champ.Cutoff:0} s depois da bandeirada.";
        }
    }
}
