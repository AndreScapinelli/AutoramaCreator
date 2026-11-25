using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Preço das peças no construtor (seções 94 e 95 do guia): o preço base (Carteira.Pecas), fixo. Peça com subida ou
    // descida custa como rampa; a largada não custa. Peça que a carreira ainda não abriu pode ser usada, mas custa
    // mais: ×3 se abre na região em que o jogador está, mais ×2 por região à frente (Mult).
    public static class PiecePrice
    {
        public const int LockMult = 3, RegionMult = 2;

        public static int Of(string id, int rise = 0, int vao = 1)
        {
            var p = Carteira.PecaOf(rise != 0 ? "rampa" : id);
            return p != null ? p.preco * Mult(id, rise, vao) : 0;
        }

        public static int Total(IList<TrackPiece> pieces)
        {
            int t = 0;
            foreach (var p in pieces) if (Carteira.Key(p) != "") t += Of(p.id, p.rise, p.vao);
            return t;
        }

        // Os cosméticos (seção 97): o preço do catálogo, sem multiplicador.
        public static int Total(IList<TrackPiece> pieces, IList<Enfeite> enfeites)
        {
            int t = Total(pieces);
            foreach (var e in enfeites) t += EnfeiteCatalog.Preco(e);
            return t;
        }

        public static int Total(TrackSave s) => Total(s.Pieces(), s.Enfeites());

        // 1 se a peça já é sua; senão o multiplicador pela distância na carreira. Rampa é do grupo "rampas"; salto de
        // vão 2 e 3, também de "salto-grande" (vale o mais caro dos dois).
        public static int Mult(string id, int rise = 0, int vao = 1)
        {
            if (Progresso.Zerou) return 1;
            int m = rise != 0 ? ItemMult("rampas") : ItemMult(Progresso.ItemOf(id));
            if (id == "salto" && vao > 1) m = Mathf.Max(m, ItemMult("salto-grande"));
            return m;

            static int ItemMult(string item)
            {
                if (Progresso.Owns(item)) return 1;
                var u = Progresso.UnlockOf(item);
                int r = u != null ? Career.RegionOf(u.no) : -1;
                return r < 0 ? LockMult : LockMult + RegionMult * Mathf.Max(0, r - Progresso.RegionReached());
            }
        }

        public static string Money(int v) => "◎ " + v.ToString("N0", L.Cultura);     // como na loja
    }
}
