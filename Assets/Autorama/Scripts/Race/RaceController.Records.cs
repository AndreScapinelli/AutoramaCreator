using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // O cartão de recordes antes da largada (seção 39 do guia): na faixa do meio da mesa, com a corrida parada e a pista
    // fechada. Some no 3, 2, 1. Campeonato (offline e online): recorde de volta, de corrida, e de cada piloto da bateria
    // o recorde do trilho dele e o melhor pessoal. Corrida livre: só os recordes da pista, e só se ela tiver algum.
    public partial class RaceController
    {
        VisualElement recCard;
        string recCardSig;
        TrackAnalysis freeInfo;              // a pista da corrida livre cuja identidade está guardada em freeHash
        string freeHash;
        bool freeOdd;

        struct CardRow { public Color? dot; public string name, a, b; }
        readonly List<CardRow> cardRows = new List<CardRow>();

        static string RecText(Rec r) => r != null && r.Has ? $"{Fmt(r.t)} {r.pilot?.tag}{(r.online ? " · online" : "")}" : "ninguém ainda";

        // Monta as linhas do cartão. Falso: nada a mostrar.
        bool StartCard(out string title)
        {
            title = "Recordes da pista";
            cardRows.Clear();
            if (sim.phase != RaceSim.Phase.Idle || !sim.closed) return false;
            if (ChampLike)
            {
                if (!HeatNow(out var C, out _, out _, out var stage, out var pl)) return false;
                var R = FindRecords(stage.hash);
                bool odd = Champ.CrossCount(stage.data) % 2 == 1, lanes = !odd && !C.byTime;
                title = $"Recordes · {stage.name}";
                cardRows.Add(new CardRow { name = "Volta", a = RecText(R?.lap) });
                if (!C.byTime) cardRows.Add(new CardRow { name = $"{stage.laps} {(stage.laps == 1 ? "volta" : "voltas")}", a = RecText(R?.Race(stage.laps)) });
                for (int k = 0; k < pl.Length; k++)
                {
                    var p = pl[k];
                    if (p == null) continue;
                    string key = BestKey(p, OnlineRun);
                    var b = key != null ? R?.Best(key, false) : null;
                    cardRows.Add(new CardRow
                    {
                        dot = p.Color, name = $"{p.name} · {LaneNames[k]}",
                        a = !lanes ? "" : R != null && R.Lane(k).Has ? "trilho " + RecText(R.Lane(k)) : "trilho sem recorde",
                        b = b != null && b.lap >= 0f ? $"seu melhor {Fmt(b.lap)}" : p.ai || key == null ? "" : "primeira vez aqui",
                    });
                }
                return true;
            }
            // Corrida livre: a identidade da pista em uso (recalcula só quando a pista muda).
            if (builder.Info == null) return false;
            if (!ReferenceEquals(builder.Info, freeInfo))
            {
                var save = builder.ToSave();
                freeInfo = builder.Info; freeHash = Champ.TrackHash(save); freeOdd = Champ.CrossCount(save) % 2 == 1;
            }
            var F = FindRecords(freeHash);
            if (F == null || !F.lap.Has) return false;
            cardRows.Add(new CardRow { name = "Volta", a = RecText(F.lap) });
            if (!sim.ByTime) { var rr = F.Race(sim.set.laps); if (rr != null && rr.Has) cardRows.Add(new CardRow { name = $"{sim.set.laps} voltas", a = RecText(rr) }); }
            if (!freeOdd && !sim.ByTime)
                for (int k = 0; k < sim.N; k++)
                    if (sim.present[k] && F.Lane(k).Has) cardRows.Add(new CardRow { dot = pads[k].root.resolvedStyle.borderTopColor, name = LaneNames[k], a = RecText(F.Lane(k)) });
            return true;
        }

        void DrawRecCard(bool show)
        {
            string sig = null;
            if (show)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var r in cardRows) sb.Append(r.dot).Append('|').Append(r.name).Append('|').Append(r.a).Append('|').Append(r.b).Append('\n');
                sig = sb.ToString();
            }
            if (sig == recCardSig) return;
            recCardSig = sig;
            recCard.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            recCard.Clear();
            if (!show) return;
            foreach (var r in cardRows)
            {
                var row = El("res-row", recCard);
                var dot = El("hud-dot", row);
                if (r.dot.HasValue) dot.style.backgroundColor = r.dot.Value; else dot.style.visibility = Visibility.Hidden;
                Text(r.name, "res-name", row);
                if (!string.IsNullOrEmpty(r.a)) Text(r.a, "res-cell", row);
                if (!string.IsNullOrEmpty(r.b)) Text(r.b, "res-cell res-cell--dim", row);
            }
        }
    }
}
