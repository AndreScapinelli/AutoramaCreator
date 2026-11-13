using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Escolha do carro antes de cada etapa do campeonato, offline e online (seção 48 do guia). Antes da primeira bateria
    // de uma etapa, o cartão do meio da mesa mostra o ajuste de cada piloto de gente; os robôs escolhem pela pista.
    // A escolha fecha quando todos confirmam (online, o host fecha sozinho depois de CarPickWait) e vale a etapa inteira.
    // Online: o visitante manda a escolha num "in" (car, cs); o host junta e manda a todos num ev "cars" (e no sync).
    public partial class RaceController
    {
        VisualElement carPick;                                      // dentro do banner do meio
        string carPickSig;
        bool carPickShown;
        readonly HashSet<string> carOk = new HashSet<string>();     // quem já confirmou o ajuste da etapa carOkStage
        int carOkStage = -1;
        float carPickSince, carSendAcc, carBcastAcc;
        const float CarPickWait = 45f;                              // online: o host fecha a escolha sozinho depois disso

        // O ajuste numa mensagem: 1–27 (o ajuste) mais 27 se o piloto já confirmou. 0 = não é escolha.
        static int CarCode(CarSetup c, bool ok) => 1 + c.motor * 9 + c.bolha * 3 + c.pneu + (ok ? 27 : 0);
        static CarSetup CarOf(int code, out bool ok)
        {
            int v = Mathf.Clamp(code, 1, 54) - 1;
            ok = v >= 27; v %= 27;
            return new CarSetup { motor = v / 9, bolha = v / 3 % 3, pneu = v % 3 };
        }
        static bool Same(CarSetup a, CarSetup b) => a.motor == b.motor && a.bolha == b.bolha && a.pneu == b.pneu;

        // Robô: pista de muita curva, carro de curva (curto · asa · macio); de reta, carro de reta (longo · lisa · macio).
        // O pneu macio nos dois: a IA não aproveita a folga do pneu duro (seção 47.4).
        public static CarSetup AiCar(TrackSave d)
        {
            float curve = 0f, all = 0f;
            foreach (var p in d.pecas)
            {
                string t = p.tipo ?? "";
                float len = t == "curva-c" ? PieceCatalog.Hairpin.CenterLength : t.Contains("curva") ? 2.5f * (t.Contains("longa") ? 90f : 45f) * Mathf.Deg2Rad
                          : t.Contains("comprida") ? 4f : t.Contains("curtinha") ? 1f : 2f;
                all += len;
                if (t.Contains("curva")) curve += len;
            }
            return all > 0f && curve / all >= 0.45f ? new CarSetup { motor = 1, bolha = 1, pneu = 1 } : new CarSetup { motor = 2, bolha = 2, pneu = 1 };
        }

        // Aberta: a bateria da vez é a primeira de uma etapa ainda sem escolha, com a corrida parada.
        bool CarPickOpen(out Championship C, out int s)
        {
            s = -1;
            if (!HeatNow(out C, out _, out var heat, out _, out _)) return false;
            s = heat.s;
            return C.carStage < s && sim.phase == RaceSim.Phase.Idle;
        }

        void CarPickBegin(Championship C, int s)
        {
            carOkStage = s; carOk.Clear(); carPickSig = null;
            carPickSince = Time.realtimeSinceStartup; carSendAcc = carBcastAcc = 1f;
            foreach (var p in C.pilots) if (p.ai) p.carro = AiCar(C.stages[s].data);
            ApplyChampCars();
        }

        // O ajuste de cada piloto da bateria vai para o carro dele (física, carroceria, pneu e som).
        void ApplyChampCars()
        {
            if (!HeatNow(out _, out _, out _, out _, out var pl)) return;
            for (int k = 0; k < RaceSim.MaxCars; k++) sim.setup[k] = k < pl.Length && pl[k] != null ? pl[k].carro.Clamped : default;
            DrawPadHeads();
        }

        // Gente que ainda não confirmou (online: só quem está na sala).
        bool AllCarOk(Championship C)
        {
            foreach (var p in C.pilots)
                if (!p.ai && (!OnlineRun || OnlinePresent(p.id)) && !carOk.Contains(p.id)) return false;
            return true;
        }

        void CarPickClose(Championship C, int s)
        {
            C.carStage = s;
            if (!OnlineRun) SaveStore();
            else if (IsHost) CarBroadcast(C);
            phaseSince = Time.realtimeSinceStartup;           // o host larga sozinho só AutoStart depois de fechar
            ApplyChampCars();
            carPickSig = null; scoreDirty = champDirty = true;
        }

        // Clique no cartão (ok: o botão "Pronto"). Mexer no ajuste tira o "pronto".
        void CarChoose(Championship C, Pilot p, CarSetup c, bool ok)
        {
            if (!CarPickOpen(out _, out int s)) return;
            p.carro = c.Clamped;
            if (ok) carOk.Add(p.id); else carOk.Remove(p.id);
            ApplyChampCars(); carPickSig = null;
            if (!OnlineRun) { SaveStore(); if (AllCarOk(C)) CarPickClose(C, s); return; }
            if (IsHost) carBcastAcc = 1f;
            else { int j = LocalOf(p.id); if (j >= 0) SendCar(j); }
        }

        // Enter (offline): todos prontos com o que está escolhido.
        void CarPickAll(Championship C, int s)
        {
            foreach (var p in C.pilots) if (!p.ai) carOk.Add(p.id);
            CarPickClose(C, s);
        }

        void SendCar(int j)
        {
            var p = onlineChamp?.pilots.Find(x => x.id == LocalId(j));
            if (p == null) return;
            net.Send(new NetOut { t = "in", g = j == 1, held = false, gp = 100, re = myRe[j], ts = NetSync.ClockMs, car = CarCode(p.carro, carOk.Contains(p.id)), cs = carOkStage });
        }

        void CarBroadcast(Championship C)
        {
            carBcastAcc = 0f;
            var ev = new NetOutEv { k = "cars", hi = C.carStage };
            foreach (var p in C.pilots) ev.cars.Add(CarCode(p.carro, carOk.Contains(p.id)));
            net.Send(ev);
        }

        // Host: a escolha de um visitante (só vale para a etapa aberta).
        void OnlineCarIn(string from, int code, int cs)
        {
            if (!CarPickOpen(out var C, out int s) || cs != s) return;
            if (s != carOkStage) CarPickBegin(C, s);
            var p = C.pilots.Find(x => x.id == from);
            if (p == null) return;
            var c = CarOf(code, out bool ok);
            if (Same(p.carro, c) && carOk.Contains(from) == ok) return;
            p.carro = c;
            if (ok) carOk.Add(from); else carOk.Remove(from);
            ApplyChampCars(); carPickSig = null; carBcastAcc = 1f;
        }

        // Visitante: o ajuste de todos, como o host vê. Com a escolha aberta, o meu fica como eu escolhi.
        void OnlineApplyCars(int hi, List<int> cars)
        {
            var C = onlineChamp;
            if (C == null || cars.Count != C.pilots.Count) return;
            bool open = CarPickOpen(out _, out int s);
            if (open && s != carOkStage) CarPickBegin(C, s);
            bool keepMine = open && hi < s;
            for (int i = 0; i < C.pilots.Count; i++)
            {
                var p = C.pilots[i];
                if (keepMine && LocalOf(p.id) >= 0) continue;
                p.carro = CarOf(cars[i], out bool ok);
                if (ok) carOk.Add(p.id); else carOk.Remove(p.id);
            }
            C.carStage = hi;
            ApplyChampCars(); carPickSig = null; scoreDirty = champDirty = true;
        }

        // A cada quadro (UpdateTop, offline e online): começa a escolha, manda e repete as mensagens, fecha no prazo.
        void CarPickTick()
        {
            if (!CarPickOpen(out var C, out int s)) return;
            if (s != carOkStage) CarPickBegin(C, s);
            if (!OnlineRun) return;
            float dt = Time.unscaledDeltaTime;
            if (IsHost)
            {
                if (AllCarOk(C) || Time.realtimeSinceStartup - carPickSince >= CarPickWait) { CarPickClose(C, s); return; }
                carBcastAcc += dt;
                if (carBcastAcc >= 1f) CarBroadcast(C);
            }
            else if ((carSendAcc += dt) >= 1f)
            {
                carSendAcc = 0f;            // repete a cada segundo: o host pode ter trocado ou eu posso ter entrado agora
                for (int j = 0; j < 2; j++) if (LocalOf(LocalId(j)) >= 0 && C.pilots.Exists(p => p.id == LocalId(j))) SendCar(j);
            }
        }

        // O cartão do meio. Verdadeiro: a escolha está na tela (o resto do banner fica de fora).
        bool CarPickUI(bool showLights)
        {
            Championship C = null; int s = -1;
            bool open = ChampLike && !showLights && CarPickOpen(out C, out s);
            if (!open)
            {
                if (carPickShown) { carPickShown = false; carPick.style.display = DisplayStyle.None; carPickSig = null; DrawResult(resShown); }
                return false;
            }
            carPickShown = true;
            carPick.style.display = DisplayStyle.Flex;
            banner.style.display = DisplayStyle.Flex;
            banner.pickingMode = PickingMode.Position;
            DrawRecCard(false);
            resTable.style.display = resBtns.style.display = DisplayStyle.None;
            var stage = C.stages[s];
            bannerTitle.text = $"Escolha do carro · etapa {s + 1}: {stage.name}";
            bannerSub.text = "Motor, bolha e pneu para a etapa inteira. Cada peça dá uma coisa e tira outra.";
            bannerSub.style.display = DisplayStyle.Flex;
            BannerColor(-1);
            int left = OnlineRun ? Mathf.Max(0, Mathf.CeilToInt(CarPickWait - (Time.realtimeSinceStartup - carPickSince))) : -1;
            var sb = new System.Text.StringBuilder($"{s}|{OnlineRun}|{IsHost}|{left}|{openCar}");
            foreach (var p in C.pilots) sb.Append('|').Append(CarCode(p.carro, carOk.Contains(p.id))).Append(OnlineRun && OnlinePresent(p.id));
            string sig = sb.ToString();
            if (sig == carPickSig) return true;
            carPickSig = sig;
            DrawCarPick(C, s, left);
            return true;
        }

        void DrawCarPick(Championship C, int s, int left)
        {
            carPick.Clear();
            var mine = OnlineRun ? C.pilots.FindAll(p => LocalOf(p.id) >= 0) : C.pilots.FindAll(p => !p.ai);
            var grid = El("pick-grid", carPick);
            foreach (var p in mine)
            {
                var pp = p;
                bool ok = carOk.Contains(p.id);
                var card = El("pick-card" + (ok ? " pick-card--ok" : ""), grid);
                var head = El("pick-head", card);
                El("hud-dot", head).style.backgroundColor = p.Color;
                Text(p.name, "pick-name", head);
                Text(p.carro.Resumo, "small pick-sum", head);
                DrawCarCard(card, p.carro, v => CarChoose(C, pp, v, false), false);
                Btn(ok ? "Pronto ✓" : "Pronto", () => CarChoose(C, pp, pp.carro, !carOk.Contains(pp.id)), ok ? "btn btn--on pick-ok" : "btn btn--accent pick-ok", card);
            }
            var others = C.pilots.FindAll(p => !mine.Contains(p));
            if (others.Count > 0)
            {
                var box = El("pick-others", carPick);
                foreach (var p in others)
                {
                    var row = El("res-row res-row--small", box);
                    El("hud-dot", row).style.backgroundColor = p.Color;
                    Text(p.name, "res-name", row);
                    bool gone = OnlineRun && !OnlinePresent(p.id);
                    string st = p.ai ? "robô" : gone ? "saiu" : carOk.Contains(p.id) ? "pronto" : "escolhendo";
                    Text(p.ai || carOk.Contains(p.id) || gone ? p.carro.Resumo : "…", "res-cell", row);
                    Text(st, "res-cell res-cell--dim", row);
                }
            }
            var foot = El("pick-foot", carPick);
            if (!OnlineRun) Text("<b>Enter</b>: todos prontos com o que está escolhido.", "small", foot);
            else
            {
                Text(IsHost ? $"Fecha sozinho em {left} s (quem não confirmou corre com o que estiver escolhido)." : $"O host fecha a escolha em até {left} s.", "small", foot);
                if (IsHost) Btn("Fechar a escolha", () => { if (CarPickOpen(out var c2, out int s2)) CarPickClose(c2, s2); }, "btn", foot);
            }
        }
    }
}
