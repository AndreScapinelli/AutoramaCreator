using System.Collections.Generic;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Elo da partida rápida (seção 89 do guia). Cada um entra na fila dizendo o seu Elo; ele chega a todos no
    // "started". No fim da bateria cada cliente faz a mesma conta (Elo.Deltas, pela ordem de chegada) e grava só a
    // sua mudança: em jogador.json e, com a Steam, no placar "elo". Sair da partida depois da largada conta como chegar
    // em último. Segundo jogador do mesmo computador e cliente sem Elo ficam de fora da conta.
    public partial class RaceController
    {
        int myElo = Elo.Start, myEloJ;
        bool eloApplied, eloPulled;
        int eloSeen;
        Label mmElo;
        readonly Dictionary<string, (int elo, int jogos)> mmRatings = new Dictionary<string, (int, int)>();
        readonly Dictionary<string, (int before, int delta)> eloDeltas = new Dictionary<string, (int, int)>();

        void EloInit(VisualElement box)
        {
            ChampFiles.PlayerElo(out myElo, out myEloJ);
            mmElo = Text("", "lib-name", box);
            // Na Steam o placar é a fonte (seção 89.1): o Elo dele vale, mesmo menor que o daqui. Sem linha no placar,
            // o daqui sobe para lá (já assinado, então não foi mexido à mão).
            EloBoard.Mine = (found, e, j) =>
            {
                if (!found) { if (myEloJ > 0) EloBoard.Push(myElo, myEloJ); return; }
                if (e == myElo && j == myEloJ) return;
                myElo = e; myEloJ = j;
                ChampFiles.SavePlayerElo(myElo, myEloJ);
            };
        }

        void EloDraw()
        {
            if (mmElo == null) return;
            if (EloBoard.Available && !eloPulled) { eloPulled = true; EloBoard.Pull(); }
            string jogos = myEloJ == 1 ? L.T("1 partida") : L.F("{0} partidas", myEloJ);
            mmElo.text = L.F("Seu Elo: {0} · {1}", myElo, jogos)
                + (EloBoard.Available && EloBoard.Rank > 0 ? L.F(" · {0}º de {1} na Steam", EloBoard.Rank, EloBoard.Total) : "");
        }

        // A cada quadro: chegou algo da Steam, redesenha o cartão.
        void EloTick()
        {
            EloBoard.Tick();
            if (eloSeen == EloBoard.Version) return;
            eloSeen = EloBoard.Version;
            EloDraw();
        }

        // Na largada da partida rápida: o Elo de cada um (o meu é sempre o daqui).
        void EloStart(NetMsg m)
        {
            mmRatings.Clear(); eloDeltas.Clear(); eloApplied = false;
            if (!MmRoom) { eloApplied = true; return; }
            foreach (var p in m.pilots) if (p.elo >= Elo.Min && !NetGuest.Is(p.id)) mmRatings[p.id] = (p.elo, p.eloj);
            if (myId != null) mmRatings[myId] = (myElo, myEloJ);
            // Na Steam (ids são SteamIDs), o Elo dos outros vem do placar, não do que cada um disse; fora dele: 1000.
            var steam = new List<ulong>();
            foreach (var p in m.pilots)
                if (p.id != myId && !NetGuest.Is(p.id) && p.uid != null && p.uid.StartsWith("steam:") && ulong.TryParse(p.id, out ulong sid)) steam.Add(sid);
            if (steam.Count == 0 || !EloBoard.Available) return;
            foreach (ulong sid in steam) mmRatings[sid.ToString()] = (Elo.Start, 0);
            string room0 = room?.code;
            EloBoard.Users(steam.ToArray(), res =>
            {
                if (eloApplied || room?.code != room0) return;
                foreach (var kv in res) mmRatings[kv.Key.ToString()] = kv.Value;
            });
        }

        // Fim da bateria (quit = false) ou saída no meio (quit = true: eu em último). Uma vez por partida.
        void EloApply(bool quit)
        {
            if (eloApplied || myId == null || !HeatNow(out _, out _, out _, out _, out var pl)) return;
            var ids = new List<string>();
            foreach (int k in Order())
            {
                var p = k < pl.Length ? pl[k] : null;
                if (p != null && mmRatings.ContainsKey(p.id) && !ids.Contains(p.id)) ids.Add(p.id);
            }
            if (quit && ids.Remove(myId)) ids.Add(myId);
            eloApplied = true;
            int me = ids.IndexOf(myId);
            if (me < 0 || ids.Count < 2) return;
            int n = ids.Count;
            var r = new int[n]; var g = new int[n]; var pos = new int[n];
            for (int i = 0; i < n; i++) { (r[i], g[i]) = mmRatings[ids[i]]; pos[i] = i; }
            var d = Elo.Deltas(r, g, pos);
            for (int i = 0; i < n; i++) eloDeltas[ids[i]] = (r[i], d[i]);
            myElo += d[me]; myEloJ++;
            ChampFiles.SavePlayerElo(myElo, myEloJ);
            EloBoard.Push(myElo, myEloJ);
        }

        // Na linha do resultado: "Elo 1032 (+12)".
        void EloCell(Pilot p, VisualElement row)
        {
            if (p == null || !eloDeltas.TryGetValue(p.id, out var e)) return;
            string sign = e.delta > 0 ? "+" + e.delta : e.delta.ToString();
            Text(L.F("Elo {0} ({1})", e.before + e.delta, sign), "res-cell" + (e.delta >= 0 ? "" : " res-cell--dim"), row);
        }
    }
}
