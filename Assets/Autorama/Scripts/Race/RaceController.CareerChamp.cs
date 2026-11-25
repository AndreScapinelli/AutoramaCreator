using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Campeonato da região na carreira (seção 73 do guia): o rodízio, as etapas e a tabela do campeonato offline (Champ),
    // com o jogador sempre no carro 0 (as teclas dele) e os trilhos trocados por bateria. As baterias sem o jogador são
    // simuladas na hora, sem cena. O campeonato em andamento fica no save (CareerSave.campeonato) e continua depois.
    public partial class RaceController
    {
        bool IsChamp => KNode != null && KNode.tipo == "campeonato";
        readonly int[] kLaneOf = new int[RaceSim.MaxCars];     // trilho de cada carro na bateria
        readonly int[] kPilotOf = new int[RaceSim.MaxCars];    // piloto do campeonato em cada carro (−1: trilho vazio)
        readonly List<string> kSimLog = new List<string>();    // baterias simuladas desde a última do jogador
        List<StandRow> kFinalTable;
        Championship kFinalChamp;

        Championship KChamp => IsChamp && kSave.campNo == KNode.id && kSave.campeonato != null && kSave.campeonato.heats.Count > 0 ? kSave.campeonato : null;

        RaceSettings NodeSettings(NodeDef n) => new RaceSettings
        {
            laps = n.voltas, vmax = set.vmax, ramp = set.ramp, grip = set.grip * (n.Has("molhada") ? Career.Wet : 1f), collide = set.collide,
            turbo = n.tipo != "relogio" && (n.Has("turboLivre") || n.turbo && Career.Has(kSave, Career.ItemTurbo)), reslotTime = set.reslotTime, halfGas = set.halfGas,
            byTime = n.tipo == "campeonato" && n.porTempo, secs = n.segundos,
        };

        Championship NewChamp(NodeDef n)
        {
            var C = new Championship { id = Champ.NewId("k"), name = n.nome, faixas = KRegion.faixas, created = Champ.Now, byTime = n.porTempo };
            C.pilots.Add(new Pilot { id = "voce", name = L.T("Você"), tag = L.T("VOC"), color = "#E0362C" });
            for (int i = 0; i < n.pilotos.Length; i++)
            {
                var d = n.pilotos[i];
                C.pilots.Add(new Pilot { id = "ia" + i, name = d.nome, tag = Champ.TagOf(d.nome), color = d.cor, ai = true, skill = d.ia, carro = d.carro.Clamped, style = Career.Style(d.estilo), nivel = d.nivel });
            }
            foreach (var id in n.etapas)
            {
                var t = Career.Track(id);
                C.stages.Add(new Stage { name = t.nome, laps = n.voltas, secs = n.segundos, hash = Champ.TrackHash(t), data = t });
            }
            C.heats = Champ.MakeHeats(C.pilots.Count, C.stages.Count, C.faixas, C.byTime, Random.Range(1, int.MaxValue));
            return C;
        }

        // Correr: começa (ou continua) o campeonato, simula as baterias sem o jogador e larga a próxima dele.
        void ChampGo()
        {
            var n = KNode;
            kFinalChamp = null; kFinalTable = null;
            if (KChamp == null) { kSave.campeonato = NewChamp(n); kSave.campNo = n.id; Career.Save(kSlot, kSave); }
            kSimLog.Clear();
            SimulateUntilPlayer();
            if (Champ.CurrentHeat(KChamp) < 0) { ChampOver(); ShowResult(); return; }
            RunChampHeat();
        }

        static bool PlayerIn(Heat h) => System.Array.IndexOf(h.p, 0) >= 0;

        void SimulateUntilPlayer()
        {
            var C = KChamp;
            for (int i = Champ.CurrentHeat(C); i >= 0 && !PlayerIn(C.heats[i]); i = Champ.CurrentHeat(C))
            {
                SimulateHeat(C, C.heats[i]);
                kSimLog.Add(HeatLine(C, C.heats[i]));
            }
            Career.Save(kSlot, kSave);
        }

        // Bateria só de IA, sem cena: o carro do trilho l é o piloto p[l].
        void SimulateHeat(Championship C, Heat h)
        {
            var data = C.stages[h.s].data;
            var segs = TrackLayout.Build(data.Origin, data.Pieces(), data.Tail, data.StartZ);
            var s = new RaceSim(NodeSettings(KNode));
            s.SetTrack(segs, true, data.Faixas);
            s.cutoff = C.byTime ? (float?)null : Champ.Cutoff;
            for (int k = 0; k < s.N; k++)
            {
                int p = k < h.p.Length ? h.p[k] : -1;
                s.present[k] = p >= 0;
                s.aiSkill[k] = p >= 0 ? Mathf.Clamp(C.pilots[p].skill, 1, RaceSim.AiMaxSkill) : 0;
                s.setup[k] = p >= 0 ? Career.AiCar(C.pilots[p].carro, KRegion, C.pilots[p].nivel) : default;
                s.aiStyle[k] = p >= 0 ? C.pilots[p].style : 0;
            }
            s.StartRace();
            for (int i = 0; i < 60 * 900 && s.phase != RaceSim.Phase.Done; i++) s.Step(1f / 60f);
            var res = new HeatResult[h.p.Length];
            for (int l = 0; l < h.p.Length; l++) res[l] = h.p[l] < 0 || l >= s.N ? new HeatResult { dnf = true } : ResOf(s, l);
            h.res = res;
        }

        static HeatResult ResOf(RaceSim s, int k)
        {
            var c = s.cars[k];
            return new HeatResult { laps = c.laps, time = c.finish ?? s.t, dnf = !c.finish.HasValue, best = c.best ?? -1f, fin = c.finish ?? -1f, offs = c.offs, hits = c.hits, dist = Mathf.Max(0f, s.Dist(k)) };
        }

        // A bateria do jogador na mesa: ele no carro 0, no trilho que o rodízio deu; os outros nos trilhos que sobram.
        void RunChampHeat()
        {
            var n = KNode;
            var C = KChamp;
            var h = C.heats[Champ.CurrentHeat(C)];
            CareerScreen(KScreen.Race);
            ClearPins();
            sim.set = NodeSettings(n);
            sim.cutoff = C.byTime ? (float?)null : Champ.Cutoff;
            builder.ShowTrack(TrackSave.FromJson(C.stages[h.s].data.ToJson(false)));
            int nL = h.p.Length, me = System.Array.IndexOf(h.p, 0), next = 1;
            for (int k = 0; k < RaceSim.MaxCars; k++) { kLaneOf[k] = k; kPilotOf[k] = -1; }
            kLaneOf[0] = me; kPilotOf[0] = 0;
            for (int l = 0; l < nL; l++) if (l != me) { kLaneOf[next] = l; kPilotOf[next] = h.p[l]; next++; }
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                int p = k < nL ? kPilotOf[k] : -1;
                sim.present[k] = p >= 0;
                sim.aiSkill[k] = p > 0 ? Mathf.Clamp(C.pilots[p].skill, 1, RaceSim.AiMaxSkill) : 0;
                sim.setup[k] = p > 0 ? Career.AiCar(C.pilots[p].carro, KRegion, C.pilots[p].nivel) : p == 0 ? CarShop.RaceCar(kSave, n) : default;
                sim.aiStyle[k] = p > 0 ? C.pilots[p].style : 0;
            }
            ApplyCameraSettings();
            sim.Reset();
            kTurbo = false; kSaved = false; kInterim = false; kOut = false; kObjSig = null;
            DrawPadHeads();
            StartRace();
            for (int k = 0; k < nL; k++) sim.cars[k].lane = kLaneOf[k];
        }

        // Fim da bateria do jogador: grava, simula as que vêm até a próxima dele e diz se acabou.
        void ChampHeatDone()
        {
            var C = KChamp;
            var h = C.heats[Champ.CurrentHeat(C)];
            var res = new HeatResult[h.p.Length];
            for (int l = 0; l < res.Length; l++) res[l] = new HeatResult { dnf = true };
            for (int k = 0; k < h.p.Length; k++) if (kPilotOf[k] >= 0) res[kLaneOf[k]] = ResOf(sim, k);
            h.res = res;
            kRes = Result();
            kHeatLine = HeatLine(C, h);
            kSimLog.Clear();
            SimulateUntilPlayer();
            kInterim = Champ.CurrentHeat(C) >= 0;
            if (!kInterim) ChampOver();
        }

        string kHeatLine;

        // Fim do campeonato: posição na tabela, etapas vencidas, estrelas; o campeonato sai do save.
        void ChampOver()
        {
            var C = KChamp;
            var table = Champ.Standings(C);
            int pos = table.FindIndex(r => r.i == 0) + 1, won = 0, offs = 0;
            for (int s = 0; s < C.stages.Count; s++) { var rows = Champ.StageResults(C, s, out _, out _, out _); if (rows.Count > 0 && rows[0].i == 0) won++; }
            foreach (var h in C.heats) for (int l = 0; l < h.p.Length; l++) if (h.p[l] == 0 && h.res != null && l < h.res.Length) offs += h.res[l].offs;
            kFinalTable = table; kFinalChamp = C;
            kSave.campeonato = null; kSave.campNo = "";
            Finish(new ResultadoCorrida { terminou = true, posicao = pos, carros = C.pilots.Count, etapas = C.stages.Count, etapasVencidas = won, saidas = offs });
        }

        static string HeatLine(Championship C, Heat h)
        {
            var parts = new List<(int p, HeatResult r)>();
            for (int l = 0; l < h.p.Length; l++) if (h.p[l] >= 0 && h.res != null) parts.Add((h.p[l], h.res[l]));
            if (C.byTime) parts.Sort((a, b) => b.r.dist.CompareTo(a.r.dist));
            else parts.Sort((a, b) => a.r.dnf != b.r.dnf ? a.r.dnf.CompareTo(b.r.dnf) : a.r.laps != b.r.laps ? b.r.laps.CompareTo(a.r.laps) : a.r.time.CompareTo(b.r.time));
            var sb = new System.Text.StringBuilder($"{C.stages[h.s].name}: ");
            for (int i = 0; i < parts.Count; i++) sb.Append(i > 0 ? " · " : "").Append($"{i + 1}º {C.pilots[parts[i].p].name}");
            return sb.ToString();
        }

        void ChampTable(VisualElement box, Championship C, List<StandRow> table)
        {
            int done = Champ.StagesDone(C);
            Text(L.F("Tabela · {0} de {1} etapas", done, C.stages.Count), "k-sub", box);
            foreach (var r in table)
            {
                var p = C.pilots[r.i];
                string line = L.F("{0}º  {1}  ·  {2} pts", table.IndexOf(r) + 1, p.name, r.pts) + (r.wins > 0 ? (r.wins == 1 ? L.F("  ·  {0} vitória", r.wins) : L.F("  ·  {0} vitórias", r.wins)) : "");
                Text(r.i == 0 ? $"<b><color=#F5C21B>{line}</color></b>" : line, "k-goal", box);
            }
        }

        // Entre baterias: a do jogador, as simuladas e a tabela.
        void ShowChampHeat(VisualElement box)
        {
            var C = KChamp;
            int next = Champ.CurrentHeat(C);
            Text(C.byTime ? L.F("Você ficou em {0}º", kRes.posicao) : kRes.terminou ? L.F("Você chegou em {0}º", kRes.posicao) : L.T("Você não terminou"), "k-title", box);
            Text(kHeatLine, "k-sub", box);
            foreach (var l in kSimLog) Text(L.T("Simulada · ") + l, "k-sub", box);
            ChampTable(box, C, Champ.Standings(C));
            Text(L.F("Próxima: {0}", C.stages[C.heats[next].s].name), "k-sub", box);
            var row = El("row k-res-row", box);
            Btn(L.T("Mapa (Esc)"), () => CareerScreen(KScreen.Map), "btn", row);
            Btn(L.T("Próxima bateria (Enter)"), ChampGo, "btn btn--primary", row);
        }

        // Linha dos objetivos ao vivo: a etapa e a posição atual na tabela.
        string ChampLiveLine()
        {
            var C = KChamp;
            if (C == null) return "";
            int i = Champ.CurrentHeat(C);
            int pos = Champ.Standings(C).FindIndex(r => r.i == 0) + 1;
            return i < 0 ? "" : L.F("\n<color=#F5C21B>Etapa {0} de {1} · você é {2}º na tabela</color>", C.heats[i].s + 1, C.stages.Count, pos);
        }

        string ChampCard(NodeDef n)
        {
            var sb = new System.Text.StringBuilder(L.F("{0} etapas: ", n.etapas.Length));
            sb.Append(string.Join(" · ", System.Array.ConvertAll(n.etapas, id => Career.Track(id)?.nome ?? id)));
            sb.Append(L.T("\nPilotos: ")).Append(string.Join(", ", System.Array.ConvertAll(n.pilotos, p => L.F("{0} (IA {1})", p.nome, p.ia))));
            if (KRegion.nivelIA > 0) sb.Append(L.F("\nCarros da IA no nível {0} da garagem", KRegion.nivelIA));
            if (n.premio > 0)
            {
                sb.Append(L.T("\nPrêmios: "));
                for (int i = 1; i <= Mathf.Min(n.pilotos.Length + 1, 4); i++) sb.Append(i > 1 ? " · " : "").Append(L.F("{0}º ◎ {1}", i, Career.Purse(n, i)));
            }
            int heats = Champ.HeatsPerStage(n.pilotos.Length + 1, KRegion.faixas, n.porTempo);
            if (n.porTempo) sb.Append(L.F("\nPor tempo: {0} s por etapa, passando por todos os trilhos ({1} {2} por etapa)", n.segundos, heats, (heats == 1 ? L.T("bateria") : L.T("baterias"))));
            else sb.Append(L.F("\n{0} baterias por etapa (você corre {1}; as outras são simuladas)", heats, KRegion.faixas));
            var C = KChamp;
            if (C != null) sb.Append(L.F("\nEm andamento: {0} de {1} etapas", Champ.StagesDone(C), C.stages.Count));
            return sb.ToString();
        }

        // Na tela final o campeonato já saiu do save: os nomes vêm do que acabou (kFinalChamp).
        Championship ChampShown => KChamp ?? kFinalChamp;
        string ChampName(int k) => kPilotOf[k] > 0 && ChampShown != null ? ChampShown.pilots[kPilotOf[k]].name.ToUpperInvariant() : null;
        Color? ChampColor(int k) => kPilotOf[k] > 0 && ChampShown != null ? ChampShown.pilots[kPilotOf[k]].Color : (Color?)null;
    }
}
