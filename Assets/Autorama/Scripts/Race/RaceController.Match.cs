using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Partida rápida (seção 62 do guia): fila por tamanho de pista no servidor Node. Cada um põe uma pista na disputa;
    // com a fila cheia o servidor sorteia uma, cria a sala e larga: uma bateria por tempo (4:00), regras padrão.
    public partial class RaceController
    {
        const string PrefMmSize = "autorama.mm.tamanho", PrefMmTrack = "autorama.mm.pista";
        static readonly int[] MmSizes = { 2, 4, 6, 8 };
        const float MmCarWait = 20f;                    // escolha do carro na partida rápida

        VisualElement mmBox, mmSizes, mmQueueBox;
        readonly List<VisualElement> mmHide = new List<VisualElement>();   // as outras seções da entrada (somem na fila)
        DropdownField mmTrack;
        Label mmInfo, mmQueueTitle, mmQueueCount, mmQueueTracks, mmClock;
        Button mmGo;
        int mmSize = 4, mmIn;                           // mmIn: tamanho da fila em que estou (0: fora)
        float mmSince;
        int[] mmq = new int[4];                         // quantos esperam em cada fila (2, 4, 6, 8)
        List<Option> mmOpts = new List<Option>();
        VisualElement mmRoulette;

        bool MmRoom => room != null && room.mm;

        void BuildMatchUI(VisualElement entry)
        {
            mmSize = Mathf.Clamp(PlayerPrefs.GetInt(PrefMmSize, 4), 2, 8);
            mmBox = Section(entry, "PARTIDA RÁPIDA");
            Text($"Uma corrida de {RaceTimeText(240)} contra quem estiver na fila. Cada um põe uma pista na disputa e o servidor sorteia qual vale. Só larga com a fila cheia.", "hint", mmBox);
            Text("CARROS", "h3", mmBox);
            mmSizes = El("segmented", mmBox);
            mmTrack = new DropdownField("Sua pista");
            mmTrack.AddToClassList("setting");
            mmTrack.style.marginTop = 6;
            mmTrack.RegisterValueChangedCallback(e => { int i = mmTrack.index; if (i >= 0 && i < mmOpts.Count) PlayerPrefs.SetString(PrefMmTrack, mmOpts[i].key); });
            mmBox.Add(mmTrack);
            mmInfo = Text("", "small", mmBox);
            mmInfo.style.marginTop = 4;
            mmGo = Btn("Procurar partida", MatchJoin, "btn btn--primary", mmBox);
            mmGo.style.marginTop = 6;

            mmQueueBox = Section(entry, "PROCURANDO PARTIDA");
            mmQueueTitle = Text("", "lib-name", mmQueueBox);
            var row = El("row", mmQueueBox);
            row.style.alignItems = Align.Center;
            mmQueueCount = Text("", "clock", row);
            mmClock = Text("", "small", row);
            mmClock.style.marginLeft = 12;
            Text("PISTAS NA DISPUTA", "h3", mmQueueBox);
            mmQueueTracks = Text("", "body", mmQueueBox);
            var sair = Btn("Sair da fila", MatchLeave, "btn btn--danger", mmQueueBox);
            sair.style.marginTop = 8;
            mmQueueBox.schedule.Execute(MatchClock).Every(1000);
        }

        static string RaceTimeText(int secs) => $"{secs / 60}:{secs % 60:00}";

        // As pistas fechadas, adaptadas ao tamanho escolhido (qualquer pista serve em qualquer tamanho; seção 32).
        void DrawMatch()
        {
            if (mmBox == null) return;
            bool steam = oSteam.value, queued = mmIn > 0;
            mmBox.style.display = !steam && !queued ? DisplayStyle.Flex : DisplayStyle.None;
            mmQueueBox.style.display = queued ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var s in mmHide) s.style.display = queued ? DisplayStyle.None : DisplayStyle.Flex;
            if (queued) return;

            mmSizes.Clear();
            for (int i = 0; i < MmSizes.Length; i++)
            {
                int n = MmSizes[i];
                var b = Btn(mmq[i] > 0 ? $"{n} · {mmq[i]} na fila" : $"{n}", () => { mmSize = n; PlayerPrefs.SetInt(PrefMmSize, n); DrawMatch(); }, "btn", mmSizes);
                b.EnableInClassList("btn--on", n == mmSize);
            }
            mmOpts = Options().FindAll(o => o.closed);
            var names = mmOpts.ConvertAll(o => o.name + (o.sub != null && o.sub != "" ? " " + o.sub : ""));
            mmTrack.choices = names;
            string key = PlayerPrefs.GetString(PrefMmTrack, "ex:oval");
            int sel = Mathf.Max(0, mmOpts.FindIndex(o => o.key == key));
            if (names.Count > 0) mmTrack.SetValueWithoutNotify(names[sel]); else mmTrack.SetValueWithoutNotify("");
            mmGo.SetEnabled(names.Count > 0);
            int k = System.Array.IndexOf(MmSizes, mmSize);
            mmInfo.text = names.Count == 0 ? "Nenhuma pista fechada. Monte uma no construtor."
                : $"A pista entra adaptada para {mmSize} carros. " + (k >= 0 && mmq[k] > 0 ? $"{mmq[k]} de {mmSize} na fila agora." : "Ninguém na fila agora: toque em Atualizar lista para ver de novo.");
        }

        void MatchJoin()
        {
            string name = RequireName(); if (name == null) return;
            int i = mmTrack.index;
            if (i < 0 || i >= mmOpts.Count) return;
            var data = Adapted(mmOpts[i].save, mmSize);
            if (string.IsNullOrWhiteSpace(data.nome)) data.nome = mmOpts[i].name;
            string json = data.ToJson(false);
            if (json.Length > MaxTrackJson) { panel.Toast("Essa pista é grande demais para o servidor."); return; }
            int size = mmSize;
            WhenConnected(() => net.Send(new NetOut { t = "mm", on = true, faixas = size, name = name, uid = ChampFiles.PlayerUid(), track = data.nome, hash = Champ.TrackHash(data), json = json }));
        }

        void MatchLeave()
        {
            if (net.state == OnlineClient.State.Open) net.Send(new NetOut { t = "mm", on = false });
            mmIn = 0; DrawMatch();
        }

        // Do servidor: a fila em que estou (faixas 0: saí) e quantos esperam em cada uma.
        void OnMatchStatus(NetMsg m)
        {
            if (m.mmq != null && m.mmq.Count == 4) mmq = m.mmq.ToArray();
            if (room != null) return;
            if (m.faixas > 0 && mmIn == 0) mmSince = Time.realtimeSinceStartup;
            mmIn = m.faixas;
            if (mmIn > 0)
            {
                mmQueueTitle.text = $"Partida rápida · {m.need} carros · {RaceTimeText(240)}";
                mmQueueCount.text = $"{m.n} de {m.need}";
                var count = new Dictionary<string, int>();
                foreach (var t in m.tracks) count[t] = count.TryGetValue(t, out int c) ? c + 1 : 1;
                var parts = new List<string>();
                foreach (var kv in count) parts.Add(Safe(kv.Key) + (kv.Value > 1 ? $" ×{kv.Value}" : ""));
                mmQueueTracks.text = string.Join(" · ", parts);
                MatchClock();
            }
            DrawMatch();
        }

        void MatchClock()
        {
            if (mmIn == 0 || mmClock == null) return;
            int s = Mathf.FloorToInt(Time.realtimeSinceStartup - mmSince);
            mmClock.text = $"{s / 60:00}:{s % 60:00} esperando";
        }

        // Fim da partida: jogar de novo (volta à fila com o mesmo tamanho e a mesma pista) ou sair.
        void MatchAgain() { LeaveRoom(); MatchJoin(); }

        // A roleta do sorteio por cima da mesa: os nomes passam e param na pista escolhida.
        void ShowSorteio(NetMM mm)
        {
            mmRoulette?.RemoveFromHierarchy();
            if (mm == null || mm.tracks.Count == 0 || mm.pick < 0 || mm.pick >= mm.tracks.Count) return;
            mmRoulette = El("mm-sorteio", panel.Root);
            mmRoulette.pickingMode = PickingMode.Ignore;
            Text("SORTEIO DA PISTA", "h3", mmRoulette);
            var name = Text("", "mm-sorteio-nome", mmRoulette);
            var by = Text("", "small", mmRoulette);
            var box = mmRoulette;
            int n = mm.tracks.Count, steps = 18 + ((mm.pick - 18) % n + n) % n, step = 0;   // termina exatamente na escolhida
            void Tick()
            {
                if (box.parent == null) return;
                var t = mm.tracks[step % n];
                name.text = Safe(t.name);
                if (step >= steps)
                {
                    by.text = $"vai para {Safe(t.name)} (de {Safe(t.by)})";
                    box.schedule.Execute(() => box.RemoveFromHierarchy()).ExecuteLater(1800);
                    return;
                }
                step++;
                box.schedule.Execute(Tick).ExecuteLater(60 + step * step / 3);   // desacelera
            }
            Tick();
        }
    }
}
