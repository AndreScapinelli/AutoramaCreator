using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Aba Carreira (seção 68 do guia): lista de nós da região, corrida a partir de um nó e as estrelas no fim.
    // O jogador é sempre o carro 0; os adversários são a IA do nó.
    public partial class RaceController
    {
        int kSlot = 1, kRegion;
        string kNode;
        CareerSave kSave;
        bool kTurbo, kSaved;
        string kLast = "";
        VisualElement kList, kCard, kScore;
        Label kHead, kClock, kResult;
        readonly Button[] kSlotBtns = new Button[Career.Slots];

        RegionDef KRegion => Career.Def.regioes[Mathf.Clamp(kRegion, 0, Career.Def.regioes.Length - 1)];
        NodeDef KNode => System.Array.Find(KRegion.nos, n => n.id == kNode);

        void BuildCareerUI()
        {
            var box = panel.CareerBox;
            var slots = Section(box, "SAVE");
            var row = El("segmented", slots);
            for (int i = 0; i < Career.Slots; i++)
            {
                int s = i + 1;
                kSlotBtns[i] = Btn($"Carreira {s}", () => { if (Racing) return; kSlot = s; kSave = Career.Load(s); kNode = null; UseCareerNode(); }, "btn", row);
            }

            var reg = Section(box, "REGIÃO");
            kHead = Text("", "hint", reg);
            kList = El(null, reg);

            var node = Section(box, "PROVA");
            kCard = El(null, node);
            var r = El("row", node);
            Btn("Largar (Enter)", CareerEnter, "btn btn--primary", r);
            Btn("Voltar ao grid", () => { sim.Reset(); scoreDirty = true; }, "btn", r);
            var clockRow = El("row", node);
            Text("Tempo de prova", "stats-key", clockRow).style.marginRight = 8;
            kClock = Text("0,00 s", "clock", clockRow);
            kResult = Text("", "hint", node);
            kScore = El("score", node);
        }

        // Escolhe a pista e os carros do nó; sem nó escolhido, o primeiro aberto ainda sem 3 estrelas.
        void UseCareerNode()
        {
            kSave ??= Career.Load(kSlot);
            var R = KRegion;
            var n = KNode;
            if (n == null || !Career.NodeOpen(kSave, R, n))
            {
                n = null;
                foreach (var x in R.nos) if (Career.NodeOpen(kSave, R, x)) { n = x; if (Career.Stars(kSave, x.id) < 3) break; }
                kNode = n?.id;
            }
            if (n != null)
            {
                bool clock = n.tipo == "relogio";
                sim.set = new RaceSettings { laps = n.voltas, vmax = set.vmax, ramp = set.ramp, grip = set.grip, collide = set.collide, turbo = !clock && n.turbo, reslotTime = set.reslotTime, halfGas = set.halfGas };
                sim.cutoff = null;
                int ais = clock ? 0 : n.adversarios;
                for (int k = 0; k < RaceSim.MaxCars; k++)
                {
                    sim.present[k] = k <= ais;
                    sim.aiSkill[k] = k > 0 && k <= ais ? Mathf.Clamp(n.ia, 1, 100) : 0;
                    sim.setup[k] = default;
                }
                var t = Career.Track(n.pista);
                if (t != null) { builder.ShowTrack(t); panel.FrameTrack(); }
            }
            kLast = "";
            sim.Reset();
            scoreDirty = true;
            DrawCareerSide();
            DrawPadHeads();
        }

        void CareerEnter()
        {
            if (Racing || KNode == null) return;
            StartRace();
        }

        // Quadro a quadro: anota o turbo e, no fim da prova, dá as estrelas (uma vez por prova).
        void CareerTick()
        {
            if (sim.phase == RaceSim.Phase.Countdown && kSaved) { kSaved = false; kTurbo = false; kLast = ""; DrawCareerSide(); }      // prova nova
            if (sim.phase == RaceSim.Phase.Running && sim.cars[0].boost) kTurbo = true;
            if (sim.phase != RaceSim.Phase.Done || kSaved || KNode == null) return;
            kSaved = true;
            var c = sim.cars[0];
            var ord = Order();
            var res = new ResultadoCorrida
            {
                terminou = c.finish.HasValue, posicao = ord.IndexOf(0) + 1, carros = ord.Count, saidas = c.offs,
                turbo = kTurbo, melhorVolta = c.best ?? -1f, tempo = c.finish ?? sim.t,
            };
            int got = Career.Avaliar(KNode, res);
            int novas = Career.Record(kSave, KNode, res);
            if (!Career.Save(kSlot, kSave)) panel.Toast("Não deu para salvar a carreira.");
            kLast = $"{Stars(got)}  {(got == 0 ? "Não foi desta vez." : novas > 0 ? $"+{novas} {(novas == 1 ? "estrela nova" : "estrelas novas")}!" : "Sem estrela nova.")}";
            if (got == 3) Fogos();
            DrawCareerSide();
        }

        static string Stars(int n) => new string('★', n) + new string('☆', 3 - n);

        void DrawCareerSide()
        {
            if (kList == null) return;
            for (int i = 0; i < Career.Slots; i++) kSlotBtns[i].EnableInClassList("btn--on", kSlot == i + 1);
            var R = KRegion;
            kHead.text = $"{R.nome} · {R.faixas} faixas · {Career.RegionStars(kSave, R)} de {R.nos.Length * 3} estrelas";
            kList.Clear();
            foreach (var n in R.nos)
            {
                var node = n;
                bool open = Career.NodeOpen(kSave, R, n);
                var item = new Button(() => { if (!Racing && open) { kNode = node.id; UseCareerNode(); } });
                item.AddToClassList("lib-item");
                item.AddToClassList("pick");
                if (n.id == kNode) item.AddToClassList("lib-item--on");
                item.SetEnabled(open);
                var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                Text((open ? "" : "🔒 ") + n.nome, "lib-name", texts).pickingMode = PickingMode.Ignore;
                Text($"{TipoNome(n.tipo)} · {Stars(Career.Stars(kSave, n.id))}", "small", texts).pickingMode = PickingMode.Ignore;
                kList.Add(item);
            }

            kCard.Clear();
            var cur = KNode;
            if (cur == null) { Text("Nenhuma prova aberta.", "hint", kCard); return; }
            Text($"<b>{cur.nome}</b> · {TipoNome(cur.tipo)} · {cur.voltas} voltas" + (cur.tipo == "relogio" ? " · sem turbo" : $" · {cur.adversarios} {(cur.adversarios == 1 ? "adversário" : "adversários")} (IA {cur.ia})"), "hint", kCard);
            var sb = new StringBuilder();
            for (int i = 0; i < cur.estrelas.Length; i++) sb.Append(i > 0 ? "\n" : "").Append(new string('★', i + 1)).Append("  ").Append(Career.Texto(cur.estrelas[i]));
            Text(sb.ToString(), "small", kCard);
            var ns = Career.Of(kSave, cur.id);
            if (ns != null) Text($"Melhor: {Stars(ns.estrelas)}" + (ns.melhorPos > 0 && cur.tipo != "relogio" ? $" · {ns.melhorPos}º" : "") + (ns.melhorVolta >= 0f ? $" · volta {Fmt(ns.melhorVolta)}" : ""), "small", kCard);
            kResult.text = kLast;
        }

        static string TipoNome(string t) => t == "relogio" ? "Contra o relógio" : t == "corrida" ? "Corrida" : t;

        void DrawCareerScore()
        {
            kScore.Clear();
            void Row(string[] cells, string cls)
            {
                var r = El("score-row " + cls, kScore);
                for (int i = 0; i < cells.Length; i++) Text(cells[i], i == 0 ? "score-cell score-cell--name" : "score-cell", r);
            }
            Row(new[] { "Carro", "Voltas", "Última", "Melhor", "Saídas" }, "score-head");
            for (int k = 0; k < sim.N; k++)
            {
                if (!sim.present[k]) continue;
                var c = sim.cars[k];
                Row(new[] { (k == 0 ? "Você" : $"IA {k}") + (sim.winner == k ? " · 1º" : ""), LapsCell(k), Fmt(c.lastLap), Fmt(c.best), c.offs.ToString() },
                    "score-" + LaneCls[k] + (sim.winner == k ? " score-win" : ""));
            }
        }

        void CareerStatus()
        {
            var n = KNode;
            if (n == null) panel.SetStatus("Carreira: nenhuma prova aberta", "status--warn");
            else if (sim.phase == RaceSim.Phase.Done) panel.SetStatus($"{n.nome} · {kLast}", "status--ok");
            else if (sim.phase == RaceSim.Phase.Countdown) panel.SetStatus("Atenção à largada", "");
            else if (sim.phase == RaceSim.Phase.Running) panel.SetStatus($"{n.nome} · volta {Mathf.Min(sim.cars[0].laps + 1, n.voltas)} de {n.voltas}", "");
            else panel.SetStatus($"{n.nome} · pronto para largar", "");
        }
    }
}
