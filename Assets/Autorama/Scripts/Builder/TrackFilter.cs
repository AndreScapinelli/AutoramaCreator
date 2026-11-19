using UnityEngine.UIElements;

namespace Autorama
{
    // Filtro das listas de pistas (seção 84): Todas · Minhas · Baixadas, em cima da caixa com rolagem.
    // Baixada: chegou pelo online (campeonato, partida rápida ou sugestão aceita: TrackSave.baixada) ou foi feita por
    // outra pessoa (autor diferente de quem joga aqui). Pistas sem arquivo (exemplo, pista do construtor) contam como minhas.
    public class TrackFilter
    {
        public int Mode { get; private set; }            // 0 todas · 1 minhas · 2 baixadas
        readonly Button[] tabs = new Button[3];
        readonly string[] names = { L.N("Todas"), L.N("Minhas"), L.N("Baixadas") };

        public TrackFilter(VisualElement parent, System.Action changed)
        {
            var row = BuilderPanel.El("segmented track-filter", parent);
            for (int i = 0; i < 3; i++)
            {
                int m = i;
                tabs[i] = BuilderPanel.Btn(L.T(names[i]), () => { if (Mode == m) return; Mode = m; Paint(); changed(); }, "btn", row);
            }
            Paint();
        }

        public static bool Downloaded(TrackSave s)
        {
            if (s == null) return false;
            if (s.baixada) return true;
            string me = BuilderPanel.LocalAuthor;
            return !string.IsNullOrWhiteSpace(s.autor) && me.Length > 0 && s.autor.Trim() != me;
        }

        public bool Shows(TrackSave s) => Mode == 0 || (Mode == 2) == Downloaded(s);

        // Conta as duas e põe nos botões: "Minhas (12)", "Baixadas (3)".
        public void Counts(int mine, int down)
        {
            tabs[0].text = L.T(names[0]) + $" ({mine + down})";
            tabs[1].text = L.T(names[1]) + $" ({mine})";
            tabs[2].text = L.T(names[2]) + $" ({down})";
        }

        void Paint() { for (int i = 0; i < 3; i++) tabs[i].EnableInClassList("btn--on", i == Mode); }
    }
}
