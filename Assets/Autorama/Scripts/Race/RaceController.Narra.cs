using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Narração da corrida (seção 60 do guia): uma faixa no topo, embaixo das fichas dos pilotos. F2 liga e desliga
    // (RaceSettings.narra, salvo). O Narrador é puro; aqui só monta o quadro e mostra o texto.
    public partial class RaceController
    {
        readonly Narrador narrador = new Narrador();
        readonly NarraQuadro narraQ = new NarraQuadro();
        Label narraLabel;
        string narraShown = "";
        float narraProjT = -1f;
        const float NarraProjCada = 0.5f;      // de quanto em quanto tempo refaz a tabela projetada

        void BuildNarra()
        {
            narraLabel = Text("", "narra", topBox);
            narraLabel.pickingMode = PickingMode.Ignore;
            narraLabel.PlaceInFront(raceClock);           // embaixo do relógio (a torre de posições fica à esquerda)
            narraLabel.style.display = DisplayStyle.None;
        }

        void NarraKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || IsTyping || keysOpen || !kb.f2Key.wasPressedThisFrame || panel == null || !panel.RaceMode) return;
            set.narra = !set.narra; set.Save();
            panel.Toast($"Narração: {(set.narra ? "ligada" : "desligada")} (F2)");
        }

        void NarraTick()
        {
            if (narraLabel == null || sim == null || !sim.Ready) return;
            if (!panel.RaceMode) { narrador.Reset(); NarraShow(""); return; }
            var q = narraQ;
            q.fase = sim.phase; q.n = sim.N; q.voltasTotal = sim.set.laps; q.porTempo = sim.ByTime; q.tempoFalta = sim.TimeLeft;
            var ord = Order();
            q.cores.Clear();
            for (int k = 0; k < sim.N; k++)
            {
                var c = sim.cars[k];
                var lp = sim.PathOf(k).At(c.s);
                q.c[k] = new NarraCarro
                {
                    presente = sim.present[k], fora = c.mode != CarMode.OnSlot, turbo = c.boost && c.turbo > 0f, chegou = c.finish.HasValue,
                    pos = ord.IndexOf(k), voltas = c.laps, saidas = c.offs, batidas = c.hits, onde = lp.loop ? 1 : lp.jump || lp.gap ? 2 : 0,
                    dist = sim.Dist(k), melhor = c.best, aviso = pads[k].flashT > 0f ? pads[k].flash : null, nome = ShortName(hud[k].name.text),
                };
            }
            NarraProj(q);
            for (int k = 0; k < sim.N; k++) q.c[k].cor = "#" + ColorUtility.ToHtmlStringRGB(pads[k].root.resolvedStyle.borderTopColor);
            narrador.Tick(q, Paused ? 0f : Time.deltaTime);
            NarraShow(set.narra ? narrador.Atual : "");
        }

        // A tabela geral se a bateria acabasse agora (o mesmo truque de DrawChampStandings): só quando esta bateria
        // fecha a etapa, porque antes disso os pontos da etapa não entram.
        void NarraProj(NarraQuadro q)
        {
            if (!ChampLike || !HeatNow(out var C, out _, out var heat, out _, out var pl)) { q.proj = null; q.campLider = null; return; }
            // No campeonato, o nome é o do piloto: o mesmo da tabela, para o narrador achar o líder entre os carros.
            for (int k = 0; k < sim.N && k < pl.Length; k++) if (pl[k] != null) q.c[k].nome = pl[k].name;
            foreach (var p in C.pilots) q.cores[p.name] = p.color;   // quem não está nesta bateria (líder projetado)
            if (narraProjT >= 0f && Time.unscaledTime - narraProjT < NarraProjCada && sim.phase == RaceSim.Phase.Running) return;
            narraProjT = Time.unscaledTime;
            var antes = Champ.Standings(C);
            q.campLider = antes.Count > 1 && antes[0].pts > 0 && antes[0].pts > antes[1].pts ? C.pilots[antes[0].i].name : null;
            q.ultimaEtapa = heat.s == C.stages.Count - 1;
            var keep = heat.res;
            heat.res = SimResults(C, pl);
            bool fecha = Champ.StageComplete(C, heat.s);
            var st = fecha ? Champ.Standings(C) : null;
            heat.res = keep;
            if (st == null || (st.Count > 1 && st[0].pts == st[1].pts)) { q.proj = null; return; }   // empate: ninguém lidera
            q.proj ??= new List<string>();
            q.proj.Clear();
            foreach (var r in st) q.proj.Add(C.pilots[r.i].name);
        }

        void NarraShow(string txt)
        {
            if (txt == narraShown) return;
            narraShown = txt;
            narraLabel.text = txt;
            narraLabel.style.display = string.IsNullOrEmpty(txt) ? DisplayStyle.None : DisplayStyle.Flex;
            narraLabel.EnableInClassList("narra--forte", narrador.AtualPrio >= 3);
        }
    }
}
