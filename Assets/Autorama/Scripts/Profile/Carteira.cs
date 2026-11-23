using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // Carteira do jogador (seção 94 do guia): moedas, garagem do carro, mesa e o que já pagou por cada pista. É do jogador, não do
    // save da carreira: recomeçar a carreira não mexe nela. Guarda também as melhores estrelas que cada prova já teve,
    // para não pagar duas vezes a mesma estrela e para as peças e modelos abertos continuarem abertos.
    [Serializable] public class Qtd { public string id; public int n; }

    [Serializable]
    public class CarteiraData
    {
        public int moedas;
        public CarGarage garagem = new CarGarage();
        public int mesa;                                   // nível da mesa, 0 a Carteira.Mesas.Length - 1
        public List<Qtd> pagas = new List<Qtd>();          // pista (arquivo da biblioteca) → moedas já pagas por ela
        public List<Qtd> gastas = new List<Qtd>();         // pista → moedas que saíram de fato da carteira (o que pode voltar)
        public List<Qtd> estrelas = new List<Qtd>();       // a melhor de cada prova, em qualquer carreira
        public int regiao;                                 // a região mais adiantada já aberta
        public int versao;
        public string dia;                                 // moedas fora da carreira (seção 96): o dia e quanto já ganhou nele
        public int hojeOn, hojeOff;
    }

    public static class Carteira
    {
        public const int Inicial = 1000;                  // moedas de quem começa do zero
        static CarteiraData d;
        public static CarteiraData D { get { if (d == null) Load(); return d; } }
        static string PathOf => Path.Combine(TrackLibrary.Root, "carteira.json");

        // ----- mesa: 5 tamanhos, o último é o máximo do construtor -----

        public class Mesa { public int celulas, preco, regiao; public string nome; }
        public static readonly Mesa[] Mesas =
        {
            new Mesa { nome = L.N("Mesa pequena"), celulas = 20,  preco = 0,     regiao = 0 },
            new Mesa { nome = L.N("Mesa média"),   celulas = 35,  preco = 1500,  regiao = 1 },
            new Mesa { nome = L.N("Mesa grande"),  celulas = 55,  preco = 4000,  regiao = 2 },
            new Mesa { nome = L.N("Salão"),        celulas = 80,  preco = 9000,  regiao = 3 },
            new Mesa { nome = L.N("Sem limite"),   celulas = 120, preco = 20000, regiao = 4 },
        };
        public static int MesaMax => Mesas[Mathf.Clamp(D.mesa, 0, Mesas.Length - 1)].celulas;

        // ----- peças: preço base de cada tipo (seção 94); a pista paga a soma ao ser salva -----

        public class Peca { public string id, nome; public int preco; }
        public static readonly Peca[] Pecas =
        {
            new Peca { id = "reta-curta",            nome = L.N("Reta curta"),            preco = 15 },
            new Peca { id = "reta-comprida",         nome = L.N("Reta comprida"),         preco = 25 },
            new Peca { id = "reta-curtinha",         nome = L.N("Reta curtinha"),         preco = 10 },
            new Peca { id = "curva-longa",           nome = L.N("Curva longa"),           preco = 20 },
            new Peca { id = "meia-curva",            nome = L.N("Meia curva"),            preco = 15 },
            new Peca { id = "curva-longa-inclinada", nome = L.N("Curva longa inclinada"), preco = 40 },
            new Peca { id = "curva-curta-inclinada", nome = L.N("Curva curta inclinada"), preco = 30 },
            new Peca { id = "curva-c",               nome = L.N("Curva em C"),            preco = 30 },
            new Peca { id = "curva-c-inclinada",     nome = L.N("Curva em C inclinada"),  preco = 50 },
            new Peca { id = "rampa",                 nome = L.N("Rampa (subida ou descida)"), preco = 40 },
            new Peca { id = "cruzamento-curto",      nome = L.N("Cruzamento curto"),      preco = 50 },
            new Peca { id = "cruzamento",            nome = L.N("Cruzamento"),            preco = 60 },
            new Peca { id = "salto",                 nome = L.N("Rampa de salto"),        preco = 250 },
            new Peca { id = "loop",                  nome = L.N("Loop"),                  preco = 400 },
            new Peca { id = "meia-curva-c",          nome = L.N("Meia curva em C"),       preco = 25 },
            new Peca { id = "meia-curva-c-inclinada", nome = L.N("Meia curva em C inclinada"), preco = 45 },
            new Peca { id = "fechada",               nome = L.N("Fechada"),               preco = 150 },
            new Peca { id = "muro",                  nome = L.N("Muro de parede"),        preco = 300 },
            new Peca { id = "parafuso",              nome = L.N("Parafuso"),              preco = 450 },
        };
        public static Peca PecaOf(string id) { foreach (var p in Pecas) if (p.id == id) return p; return null; }

        // A peça da pista → o tipo do estoque ("" = não conta: a largada). Peça com subida ou descida é rampa.
        public static string Key(TrackPiece p)
        {
            if (p == null || p.Def == null || p.Def.IsStart) return "";
            return p.rise != 0 ? "rampa" : p.id;
        }

        static int Get(List<Qtd> l, string id) { foreach (var q in l) if (q.id == id) return q.n; return 0; }
        static void Set(List<Qtd> l, string id, int n) { foreach (var q in l) if (q.id == id) { q.n = n; return; } l.Add(new Qtd { id = id, n = n }); }

        // ----- pagar a pista (seção 94): ao salvar, paga o custo menos o que já pagou por ela -----

        // Pista sem registro (salva antes da carteira, baixada do online): o que ela já tem conta como pago, de graça.
        public static int Pago(string pista)
        {
            foreach (var q in D.pagas) if (q.id == pista) return q.n;
            var s = TrackLibrary.LoadKey(pista);
            return s != null ? PiecePrice.Total(s) : 0;
        }
        public static int APagar(string pista, int custo) => Mathf.Max(0, custo - Pago(pista));
        // Devolve ao salvar com menos peças: só o que saiu da carteira por essa pista (o que veio de graça não volta).
        public static int Devolve(string pista, int custo) => Mathf.Min(Mathf.Max(0, Pago(pista) - custo), Get(D.gastas, pista));

        public static string CanPay(string pista, int custo)
        {
            int falta = APagar(pista, custo) - D.moedas;
            return falta > 0 ? L.F("Faltam {0} moedas para salvar esta pista.", falta) : "";
        }

        // Pista dada de graça (o oval de todo jogador): conta como paga.
        public static void Dar(string pista, int custo) { Set(D.pagas, pista, Mathf.Max(Pago(pista), custo)); Save(); }

        public static bool Pay(string pista, int custo)
        {
            if (CanPay(pista, custo) != "") return false;
            int c = APagar(pista, custo), r = Devolve(pista, custo);
            if (Get(D.pagas, pista) == custo && c == 0 && r == 0) return true;
            D.moedas += r - c;
            Set(D.pagas, pista, custo);
            Set(D.gastas, pista, Get(D.gastas, pista) + c - r);
            Save();
            return true;
        }

        public static string CanBuyMesa(int i)
        {
            if (i <= D.mesa) return L.T("Já é sua.");
            if (i != D.mesa + 1) return L.T("Compre a mesa de antes primeiro.");
            var m = Mesas[i];
            if (!Progresso.RegionOk(m.regiao)) return L.F("Abre na região {0}.", m.regiao < Career.Def.regioes.Length ? Career.Def.regioes[m.regiao].nome : "?");
            return D.moedas < m.preco ? L.F("Faltam {0} moedas.", m.preco - D.moedas) : "";
        }

        public static bool BuyMesa(int i)
        {
            if (CanBuyMesa(i) != "") return false;
            D.moedas -= Mesas[i].preco;
            D.mesa = i;
            Save();
            return true;
        }

        // ----- estrelas de sempre -----

        public static int Estrelas(string no) => Get(D.estrelas, no);

        // A carreira salvou: guarda as melhores estrelas e a região mais adiantada.
        public static void Sync(CareerSave s)
        {
            foreach (var n in s.nos) if (n.estrelas > Estrelas(n.id)) Set(D.estrelas, n.id, n.estrelas);
            for (int i = 0; i < Career.Def.regioes.Length; i++) if (Career.RegionOpen(s, i)) D.regiao = Mathf.Max(D.regiao, i);
        }

        // ----- arquivo -----

        static void Load()
        {
            try
            {
                if (File.Exists(PathOf)) d = JsonUtility.FromJson<CarteiraData>(File.ReadAllText(PathOf));
            }
            catch (Exception e) { Debug.LogWarning("Carteira não lida: " + e.Message); }
            if (d == null) { d = new CarteiraData { moedas = Inicial }; Migrate(); }     // jogador novo: as moedas iniciais (quem vem de save antigo fica com as dele)
            d.garagem ??= new CarGarage();
            CarShop.Fix(d.garagem);
            d.pagas ??= new List<Qtd>();
            d.gastas ??= new List<Qtd>();
            d.estrelas ??= new List<Qtd>();
        }

        // Antes da carteira cada save tinha as suas moedas e garagem, e havia 3 saves. Fica o save com mais estrelas
        // (vira o carreira-1; o 1 de antes fica como .bak), as moedas e a garagem dele, e as estrelas de todos.
        public const int OldSlots = 3;
        static void Migrate()
        {
            d.versao = 1;
            int best = 0, bestStars = -1;
            var saves = new CareerSave[OldSlots + 1];
            for (int slot = 1; slot <= OldSlots; slot++)
            {
                if (!File.Exists(Career.PathOf(slot))) continue;
                var s = saves[slot] = Career.LoadRaw(slot);
                int st = 0;
                foreach (var n in s.nos) st += n.estrelas;
                if (st > bestStars) { bestStars = st; best = slot; }
                Sync(s);
            }
            if (best == 0) return;
            d.moedas = saves[best].moedas;
            d.garagem = saves[best].garagem ?? new CarGarage();
            if (best != 1)
            {
                try
                {
                    string one = Career.PathOf(1);
                    if (File.Exists(one)) File.Copy(one, one + ".bak", true);
                    File.Copy(Career.PathOf(best), one, true);
                }
                catch (Exception e) { Debug.LogWarning("Carreira não migrada: " + e.Message); }
            }
            Save();
        }

        public static void Save()
        {
            try { Directory.CreateDirectory(TrackLibrary.Root); File.WriteAllText(PathOf, JsonUtility.ToJson(D, true)); }
            catch (Exception e) { Debug.LogWarning("Carteira não salva: " + e.Message); }
            Changed?.Invoke();
        }
        public static event Action Changed;

        // Só para teste: esquece a carteira em memória (a próxima leitura vem do arquivo).
        public static void Reload() => d = null;
    }
}
