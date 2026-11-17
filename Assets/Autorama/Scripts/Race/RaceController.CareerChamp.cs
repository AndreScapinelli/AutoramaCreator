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
            turbo = n.tipo != "relogio" && (n.turbo || n.Has("turboLivre")), reslotTime = set.reslotTime, halfGas = set.halfGas,
            byTime = n.tipo == "campeonato" && n.porTempo, secs = n.segundos,
        };

        Championship NewChamp(NodeDef n)
        {
            var C = new Championship { id = Champ.NewId("k"), name = n.nome, faixas = KRegion.faixas, created = Champ.Now, byTime = n.porTempo };
            C.pilots.Add(new Pilot { id = "voce", name = "Você", tag = "VOC", color = "#E0362C" });
            for (int i = 0; i < n.pilotos.Length; i++)
            {
                var d = n.pilotos[i];
                C.pilots.Add(new Pilot { id = "ia" + i, name = d.nome, tag = Champ.TagOf(d.nome), color = d.cor, ai = true, skill = d.ia, carro = d.carro.Clamped });
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
                s.setup[k] = p >= 0 ? C.pilots[p].carro : default;
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
            int L = h.p.Length, me = System.Array.IndexOf(h.p, 0), next = 1;
            for (int k = 0; k < RaceSim.MaxCars; k++) { kLaneOf[k] = k; kPilotOf[k] = -1; }
            kLaneOf[0] = me; kPilotOf[0] = 0;
            for (int l = 0; l < L; l++) if (l != me) { kLaneOf[next] = l; kPilotOf[next] = h.p[l]; next++; }
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                int p = k < L ? kPilotOf[k] : -1;
                sim.present[k] = p >= 0;
                sim.aiSkill[k] = p > 0 ? Mathf.Clamp(C.pilots[p].skill, 1, RaceSim.AiMaxSkill) : 0;
                sim.setup[k] = p > 0 ? C.pilots[p].carro : p == 0 ? CarShop.RaceCar(kSave, n) : default;
            }
            ApplyCameraSettings();
            sim.Reset();
            kTurbo = false; kSaved = false; kInterim = false; kOut = false; kObjSig = null;
            DrawPadHeads();
            StartRace();
            for (int k = 0; k < L; k++) sim.cars[k].lane = kLaneOf[k];
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
            Text($"Tabela · {done} de {C.stages.Count} etapas", "k-sub", box);
            foreach (var r in table)
            {
                var p = C.pilots[r.i];
                string line = $"{table.IndexOf(r) + 1}º  {p.name}  ·  {r.pts} pts" + (r.wins > 0 ? $"  ·  {r.wins} {(r.wins == 1 ? "vitória" : "vitórias")}" : "");
                Text(r.i == 0 ? $"<b><color=#F5C21B>{line}</color></b>" : line, "k-goal", box);
            }
        }

        // Entre baterias: a do jogador, as simuladas e a tabela.
        void ShowChampHeat(VisualElement box)
        {
            var C = KChamp;
            int next = Champ.CurrentHeat(C);
            Text(C.byTime ? $"Você ficou em {kRes.posicao}º" : kRes.terminou ? $"Você chegou em {kRes.posicao}º" : "Você não terminou", "k-title", box);
            Text(kHeatLine, "k-sub", box);
            foreach (var l in kSimLog) Text("Simulada · " + l, "k-sub", box);
            ChampTable(box, C, Champ.Standings(C));
            Text($"Próxima: {C.stages[C.heats[next].s].name}", "k-sub", box);
            var row = El("row k-res-row", box);
            Btn("Mapa (Esc)", () => CareerScreen(KScreen.Map), "btn", row);
            Btn("Próxima bateria (Enter)", ChampGo, "btn btn--primary", row);
        }

        // Linha dos objetivos ao vivo: a etapa e a posição atual na tabela.
        string ChampLiveLine()
        {
            var C = KChamp;
            if (C == null) return "";
            int i = Champ.CurrentHeat(C);
            int pos = Champ.Standings(C).FindIndex(r => r.i == 0) + 1;
            return i < 0 ? "" : $"\n<color=#F5C21B>Etapa {C.heats[i].s + 1} de {C.stages.Count} · você é {pos}º na tabela</color>";
        }

        string ChampCard(NodeDef n)
        {
            var sb = new System.Text.StringBuilder($"{n.etapas.Length} etapas: ");
            sb.Append(string.Join(" · ", System.Array.ConvertAll(n.etapas, id => Career.Track(id)?.nome ?? id)));
            sb.Append("\nPilotos: ").Append(string.Join(", ", System.Array.ConvertAll(n.pilotos, p => $"{p.nome} (IA {p.ia})")));
            int heats = Champ.HeatsPerStage(n.pilotos.Length + 1, KRegion.faixas, n.porTempo);
            if (n.porTempo) sb.Append($"\nPor tempo: {n.segundos} s por etapa, passando por todos os trilhos ({heats} {(heats == 1 ? "bateria" : "baterias")} por etapa)");
            else sb.Append($"\n{heats} baterias por etapa (você corre {KRegion.faixas}; as outras são simuladas)");
            var C = KChamp;
            if (C != null) sb.Append($"\nEm andamento: {Champ.StagesDone(C)} de {C.stages.Count} etapas");
            return sb.ToString();
        }

        // Na tela final o campeonato já saiu do save: os nomes vêm do que acabou (kFinalChamp).
        Championship ChampShown => KChamp ?? kFinalChamp;
        string ChampName(int k) => kPilotOf[k] > 0 && ChampShown != null ? ChampShown.pilots[kPilotOf[k]].name.ToUpperInvariant() : null;
        Color? ChampColor(int k) => kPilotOf[k] > 0 && ChampShown != null ? ChampShown.pilots[kPilotOf[k]].Color : (Color?)null;
    }
}
