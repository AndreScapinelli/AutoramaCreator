using System;

namespace Autorama
{
    // Ranking da partida rápida (seção 89 do guia): o Elo do xadrez, o mesmo número do Age of Empires. Todo mundo
    // começa com 1000. Numa corrida de n carros, cada par de jogadores conta como um duelo: quem chegou na frente
    // ganhou (1), atrás perdeu (0), mesma posição empatou (0,5). A expectativa contra cada um é
    // 1 / (1 + 10^((Rb − Ra) / 400)), e a mudança é K · Σ(resultado − expectativa) / (n − 1), arredondada.
    // K como na FIDE: 40 nas primeiras 30 partidas, 20 depois e 10 a partir de 2400.
    public static class Elo
    {
        public const int Start = 1000, Min = 100;
        public const int NewGames = 30, HighRating = 2400;

        public static double Expected(int a, int b) => 1.0 / (1.0 + Math.Pow(10.0, (b - a) / 400.0));

        public static int K(int rating, int games) => games < NewGames ? 40 : rating >= HighRating ? 10 : 20;

        // places: a posição de cada um na chegada (0 = primeiro; repetida = empate). Devolve a mudança de cada um.
        public static int[] Deltas(int[] ratings, int[] games, int[] places)
        {
            int n = ratings.Length;
            var d = new int[n];
            if (n < 2) return d;
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double res = places[i] < places[j] ? 1 : places[i] == places[j] ? 0.5 : 0;
                    s += res - Expected(ratings[i], ratings[j]);
                }
                d[i] = (int)Math.Round(K(ratings[i], games[i]) * s / (n - 1), MidpointRounding.AwayFromZero);
                if (ratings[i] + d[i] < Min) d[i] = Min - ratings[i];
            }
            return d;
        }
    }
}
