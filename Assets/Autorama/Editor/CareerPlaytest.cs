using System.Text;
using UnityEngine;

namespace Autorama
{
    // Teste de uma região inteira no Play (seção 75): cada prova com a IA `pilot` no lugar do jogador, num save de
    // teste (slot 3, apagado no fim). Pula a oficina (precisa montar). As moedas do perfil voltam ao que eram.
    // script eval 'return Autorama.CareerPlaytest.Run(2, 150);'
    public static class CareerPlaytest
    {
        const System.Reflection.BindingFlags B = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        public static string Run(int region, int pilot)
        {
            var sb = new StringBuilder();
            var rc = Object.FindFirstObjectByType<RaceController>();
            var bp = Object.FindFirstObjectByType<BuilderPanel>();
            if (rc == null || !Application.isPlaying) return "Precisa do Play.";
            var T = typeof(RaceController);
            object F(string n) => T.GetField(n, B).GetValue(rc);
            void Set(string n, object v) => T.GetField(n, B).SetValue(rc, v);
            void Call(string n) => T.GetMethod(n, B).Invoke(rc, null);
            var sim = (RaceSim)F("sim");
            int coins = Progresso.P.moedas;
            UnityEditor.AssetDatabase.Refresh();      // o carreira.json mudou no disco: sem isso o Resources devolve o antigo
            Career.Reload();
            var R = Career.Def.regioes[region];
            bp.ShowMenu(false); bp.SetMode(BuilderPanel.Mode.Career);
            var sv = new CareerSave();
            for (int r = 0; r < region; r++) foreach (var x in Career.Def.regioes[r].nos) sv.nos.Add(new NodeSave { id = x.id, estrelas = 3 });
            foreach (var x in R.nos) sv.nos.Add(new NodeSave { id = x.id, estrelas = 1 });       // abre tudo da região
            Set("kSlot", 3); Set("kSave", sv); Set("kRegion", region);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < R.nos.Length; i++)
                {
                    var n = R.nos[i];
                    if (n.tipo == "construtor") { sb.Append($"{n.id}: (oficina, pulada)\n"); continue; }
                    var t0 = watch.Elapsed.TotalSeconds;
                    Set("kSel", i);
                    Call("CareerRun");
                    int offs = 0, races = 0;
                    for (int g = 0; g < 12; g++)
                    {
                        sim.aiSkill[0] = pilot;
                        for (int s = 0; s < 60 * 900 && sim.phase != RaceSim.Phase.Done && !(bool)F("kOut"); s++) sim.Step(1f / 60f);
                        offs += sim.cars[0].offs;
                        Call("CareerTick"); races++;
                        if (!(bool)F("kInterim")) break;
                        if (n.tipo == "campeonato") Call("ChampGo"); else Call("NextHeat");
                    }
                    var res = (ResultadoCorrida)F("kRes");
                    int ia = 0, iaOffs = 0;
                    for (int k = 1; k < sim.N; k++) if (sim.present[k]) { ia++; iaOffs += sim.cars[k].offs; }
                    sb.Append($"{n.id} ({n.tipo}): {F("kGot")}★ · {(n.tipo == "relogio" ? $"volta {res.melhorVolta:0.00}" : n.tipo == "rival" ? $"{res.vitorias}×{res.derrotas}" : $"{res.posicao}º")}"
                              + (n.tipo == "campeonato" ? $" · etapas {res.etapasVencidas}/{res.etapas}" : "")
                              + $" · saídas suas {offs}, da IA {iaOffs} ({ia} carros) · {races} prova(s) · {watch.Elapsed.TotalSeconds - t0:0}s\n");
                }
            }
            catch (System.Exception e) { sb.Append("ERRO " + (e.InnerException ?? e).ToString().Substring(0, 500)); }
            Progresso.P.moedas = coins; Progresso.Save();
            var p = Career.PathOf(3); if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
            return sb.ToString();
        }
    }
}
