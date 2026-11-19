using System;
using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // O carro da carreira (seção 78 do guia): o ajuste escolhido, as opções compradas e o nível de cada opção.
    [Serializable]
    public class CarGarage
    {
        public CarSetup carro;                                  // o ajuste que vai para as provas
        public List<string> opcoes = new List<string>();        // opções compradas: "motor-1", "nitro-2"…
        public int[] niveis = new int[CarShop.Trilhas];         // nível 0–10 de cada opção: peça × 3 + opção
        public int motor, pneu, bolha, nitro;                   // preparação antiga (0–3; versao 1): devolvida em moedas
    }

    // Garagem do carro: o que se compra com as moedas da carreira. Fora da carreira o ajuste continua todo livre.
    // Cada opção de cada peça tem a sua trilha de 10 níveis; cada nível reforça o ponto forte dela.
    public static class CarShop
    {
        public static readonly string[] Pecas = { "motor", "bolha", "pneu", "chassi", "nitro" };
        public const int Trilhas = 15, MaxNivel = 10;

        // Opção fora do padrão: preço para abrir e a região que a abre.
        static int Idx(string peca) => Array.IndexOf(Pecas, peca);
        static readonly int[] OptPrices = { 200, 250, 150, 250, 400 };
        static readonly int[] OptRegions = { 0, 1, 0, 1, 2 };
        // Nível máximo à venda com a região aberta: Garagem 3, Clube 6, Regional 8, Nacional 10.
        public static readonly int[] LevelCap = { 3, 6, 8, 10 };
        public const int LevelStep = 50;                        // o nível n custa 50 × n (a trilha inteira, 2.750)
        public static int LevelPrice(int nivel) => LevelStep * nivel;

        public static string Title(string peca) => peca switch { "motor" => L.T("Motor"), "bolha" => L.T("Bolha"), "pneu" => L.T("Pneu"), "chassi" => L.T("Chassi"), _ => L.T("Turbo") };
        public static string[] Names(string peca) => peca switch
        {
            "motor" => CarSetup.MotorNames, "bolha" => CarSetup.BolhaNames, "pneu" => CarSetup.PneuNames,
            "chassi" => CarSetup.ChassiNames, _ => CarSetup.NitroNames,
        };

        static string P(float v) => (v * 100f).ToString("0.##");
        // O que cada nível da opção dá (o padrão: metade de cada lado).
        public static string LevelText(string peca, int opt)
        {
            string a, b;
            switch (peca)
            {
                case "motor": a = L.F("+{0}% de aceleração", P(CarSetup.LvAccel)); b = L.F("+{0}% de final", P(CarSetup.LvTop)); break;
                case "pneu": a = L.F("+{0}% de aderência", P(CarSetup.LvGrip)); b = L.F("+{0}% de final", P(CarSetup.LvTop)); break;
                case "bolha": a = L.F("+{0}% de aerodinâmica", P(CarSetup.LvAero)); b = L.F("+{0}% de final", P(CarSetup.LvTop)); break;
                case "chassi": a = L.F("+{0}% de resposta no gatilho e no freio", P(CarSetup.LvResp)); b = L.F("−{0}% na derrapagem", P(CarSetup.LvSlip)); break;
                default: a = L.F("+{0} ponto de velocidade do turbo", P(CarSetup.LvBoost)); b = L.F("+{0}% de tanque", P(CarSetup.LvTank)); break;
            }
            return opt == 1 ? a + L.T(" por nível") : opt == 2 ? b + L.T(" por nível") : L.F("metade de cada: {0} e {1}, a cada 2 níveis", a, b);
        }

        public static int Get(CarSetup c, string peca) => peca switch { "motor" => c.motor, "bolha" => c.bolha, "pneu" => c.pneu, "chassi" => c.chassi, _ => c.nitro };
        public static CarSetup With(CarSetup c, string peca, int v)
        {
            switch (peca) { case "motor": c.motor = v; break; case "bolha": c.bolha = v; break; case "pneu": c.pneu = v; break; case "chassi": c.chassi = v; break; default: c.nitro = v; break; }
            return c;
        }

        public static int OptPrice(string peca) => OptPrices[Idx(peca)];
        public static int OptRegion(string peca) => OptRegions[Idx(peca)];
        public static bool HasOpt(CarGarage g, string peca, int v) => v == 0 || Progresso.All || g.opcoes.Contains($"{peca}-{v}");

        static bool RegionOk(CareerSave s, int r) => Progresso.All || r >= Career.Def.regioes.Length || Career.RegionOpen(s, r);
        static string RegionName(int r) => r < Career.Def.regioes.Length ? Career.Def.regioes[r].nome : "?";

        // "" se pode comprar; senão o motivo.
        public static string CanBuyOpt(CareerSave s, string peca, int v)
        {
            if (HasOpt(s.garagem, peca, v)) return L.T("Já é sua.");
            int r = OptRegion(peca);
            if (!RegionOk(s, r)) return L.F("Abre na região {0}.", RegionName(r));
            if (s.moedas < OptPrice(peca)) return L.F("Faltam {0} moedas.", OptPrice(peca) - s.moedas);
            return "";
        }

        public static bool BuyOpt(CareerSave s, string peca, int v)
        {
            if (CanBuyOpt(s, peca, v) != "") return false;
            s.moedas -= OptPrice(peca);
            s.garagem.opcoes.Add($"{peca}-{v}");
            return true;
        }

        public static void Fix(CarGarage g)
        {
            g.opcoes ??= new List<string>();
            if (g.niveis == null || g.niveis.Length != Trilhas) { var n = new int[Trilhas]; if (g.niveis != null) Array.Copy(g.niveis, n, Mathf.Min(g.niveis.Length, Trilhas)); g.niveis = n; }
        }

        public static int Level(CarGarage g, string peca, int opt) => Mathf.Clamp(g.niveis[Idx(peca) * 3 + opt], 0, MaxNivel);

        // O maior nível à venda neste save (pela região mais adiantada aberta).
        public static int Cap(CareerSave s)
        {
            int cap = 0;
            for (int r = 0; r < LevelCap.Length; r++) if (RegionOk(s, r)) cap = LevelCap[r];
            return cap;
        }

        public static string CanBuyLevel(CareerSave s, string peca, int opt)
        {
            int lv = Level(s.garagem, peca, opt);
            if (lv >= MaxNivel) return L.T("Nível máximo.");
            if (!HasOpt(s.garagem, peca, opt)) return L.T("Abra a opção primeiro.");
            if (lv >= Cap(s))
            {
                for (int r = 0; r < LevelCap.Length; r++) if (LevelCap[r] > lv) return L.F("Nível {0} abre na região {1}.", lv + 1, RegionName(r));
            }
            if (s.moedas < LevelPrice(lv + 1)) return L.F("Faltam {0} moedas.", LevelPrice(lv + 1) - s.moedas);
            return "";
        }

        public static bool BuyLevel(CareerSave s, string peca, int opt)
        {
            if (CanBuyLevel(s, peca, opt) != "") return false;
            int lv = Level(s.garagem, peca, opt);
            s.moedas -= LevelPrice(lv + 1);
            s.garagem.niveis[Idx(peca) * 3 + opt] = lv + 1;
            return true;
        }

        // O carro do jogador numa prova: o ajuste da garagem (só opções compradas) ou o carro imposto pela regra
        // "carro", sempre com os níveis que a garagem tem para as opções que ele usa.
        public static CarSetup RaceCar(CareerSave s, NodeDef n)
        {
            var g = s.garagem;
            CarSetup c;
            if (n != null && n.Has("carro"))
            {
                c = n.carro.Clamped;
                if (Progresso.ModelOk(c.bolha, g.carro.modelo)) c.modelo = g.carro.modelo;   // o modelo é seu, mesmo no carro imposto
            }
            else
            {
                c = g.carro.Clamped;
                foreach (var p in Pecas) if (!HasOpt(g, p, Get(c, p))) c = With(c, p, 0);
                if (!Progresso.ModelOk(c.bolha, c.modelo)) c.modelo = 0;
            }
            return Levels(c, g);
        }

        public static CarSetup Levels(CarSetup c, CarGarage g)
        {
            c.lvMotor = Level(g, "motor", c.motor); c.lvBolha = Level(g, "bolha", c.bolha); c.lvPneu = Level(g, "pneu", c.pneu);
            c.lvChassi = Level(g, "chassi", c.chassi); c.lvNitro = Level(g, "nitro", c.nitro);
            return c;
        }

        // "curto 4 · macio 2" (vazio sem nível): os níveis das opções que o carro usa.
        public static string LevelResumo(CarSetup c)
        {
            var l = new List<string>();
            void Add(string peca, int opt, int lv) { if (lv > 0) l.Add($"{Names(peca)[opt].ToLowerInvariant()} {lv}"); }
            Add("motor", c.motor, c.lvMotor); Add("bolha", c.bolha, c.lvBolha); Add("pneu", c.pneu, c.lvPneu);
            Add("chassi", c.chassi, c.lvChassi); Add("nitro", c.nitro, c.lvNitro);
            return l.Count == 0 ? "" : L.T("níveis: ") + string.Join(" · ", l);
        }
    }
}
