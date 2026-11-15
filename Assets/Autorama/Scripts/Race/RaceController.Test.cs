namespace Autorama
{
    // Testar a pista no construtor (seção 61 do guia): um carro IA no 1º trilho, sem largada, voltas nem recorde.
    // Anda mesmo com a pista aberta; na ponta dela volta ao grid. Qualquer mudança na pista recomeça do grid.
    public partial class RaceController
    {
        const int TestSkill = 80;     // qualidade do piloto IA de teste
        bool testing;

        void ToggleTest() => SetTest(!testing);

        void SetTest(bool on)
        {
            if (on && (panel.CurrentMode != BuilderPanel.Mode.Build || !sim.Ready)) return;
            testing = on;
            sim.test = on;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                sim.present[k] = !on || k == 0;
                sim.aiSkill[k] = on && k == 0 ? TestSkill : 0;
                sim.setup[k] = default;
            }
            if (on) sim.StartTest(); else sim.Reset();
            panel.SetTestOn(on);
        }
    }
}
