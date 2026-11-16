using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // Modo carreira (seção 68 do guia). A definição vem no jogo (Resources/Carreira/carreira.json e as pistas em
    // Resources/Carreira/pistas); o progresso do jogador fica em persistentDataPath/autorama/carreira-N.json (3 slots).

    [Serializable] public class CareerDef { public RegionDef[] regioes; }

    [Serializable]
    public class RegionDef
    {
        public string id, nome;
        public int faixas = 2;
        public int estrelasRival;   // estrelas da região para abrir o nó "rival"
        public NodeDef[] nos;
    }

    [Serializable]
    public class NodeDef
    {
        public string id, nome;
        public string tipo = "corrida";   // corrida · relogio · desafio · rival (depois: construtor, campeonato)
        public string[] depois;           // abre quando QUALQUER um destes tem 1 estrela (vazio: aberto desde o começo)
        public string pista;              // arquivo em Resources/Carreira/pistas, sem .json
        public int voltas = 3;
        public int ia = 50;               // qualidade da IA, 1–100
        public int adversarios = 1;       // carros da IA (no relógio: 0)
        public bool turbo = true;         // no relógio fica sempre desligado
        public Objetivo[] estrelas;       // 3: a 1ª é concluir; sem ela, nenhuma estrela

        // Desafio (seção 71): regras que se somam. molhada (aderência × Career.Wet) · carro (o ajuste "carro" imposto)
        // · sobrevivencia (mais de "limite" saídas e está fora) · atrasada (larga "atraso" s depois da luz verde)
        // · turboLivre (tanque sempre cheio, para todos).
        public string[] regras;
        public CarSetup carro;
        public int limite = 3;
        public float atraso = 2f;

        // Rival (seção 71): melhor de "baterias", trocando de trilho a cada uma. Nome, cor (#RRGGBB), carro e as falas.
        public string rival, rivalCor, rivalFrase, rivalVitoria, rivalDerrota;
        public CarSetup rivalCarro;
        public int baterias = 3;

        public bool Has(string regra) => regras != null && System.Array.IndexOf(regras, regra) >= 0;
    }

    // tipo: posicao (chegar até a posição valor) · semSair (no máximo valor saídas) · semTurbo · volta (melhor volta até valor s)
    //       · tempo (prova inteira até valor s)
    [Serializable] public class Objetivo { public string tipo; public float valor; }

    // O que a corrida devolve ao fim, do ponto de vista do jogador (carro 0).
    public struct ResultadoCorrida
    {
        public bool terminou;
        public int posicao, carros, saidas;
        public bool turbo;
        public float melhorVolta;   // < 0: nenhuma volta completa
        public float tempo;
        public int vitorias, derrotas;   // só no rival: baterias ganhas e perdidas
    }

    [Serializable] public class NodeSave { public string id; public int estrelas; public int melhorPos; public float melhorVolta = -1f; }
    [Serializable] public class CareerSave { public List<NodeSave> nos = new List<NodeSave>(); }

    public static class Career
    {
        public const int Slots = 3;

        static CareerDef def;
        public static CareerDef Def => def ??= JsonUtility.FromJson<CareerDef>(Resources.Load<TextAsset>("Carreira/carreira").text);
        public static void Reload() => def = null;

        public static TrackSave Track(string id)
        {
            var t = Resources.Load<TextAsset>("Carreira/pistas/" + id);
            return t != null ? TrackSave.FromJson(t.text) : null;
        }

        // ----- estrelas -----

        public static bool Cumpre(Objetivo o, ResultadoCorrida r)
        {
            switch (o.tipo)
            {
                case "posicao": return r.terminou && r.posicao <= o.valor;
                case "semSair": return r.terminou && r.saidas <= o.valor;
                case "semTurbo": return r.terminou && !r.turbo;
                case "volta": return r.melhorVolta >= 0f && r.melhorVolta <= o.valor;
                case "tempo": return r.terminou && r.tempo <= o.valor;
                case "serie": return r.terminou && r.vitorias > r.derrotas;
                case "invicto": return r.terminou && r.vitorias > 0 && r.derrotas == 0;
                default: return false;
            }
        }

        // A 1ª estrela é concluir; sem ela não vale nenhuma. As outras duas valem cada uma por si.
        public static int Avaliar(NodeDef n, ResultadoCorrida r)
        {
            if (n.estrelas == null || n.estrelas.Length == 0 || !Cumpre(n.estrelas[0], r)) return 0;
            int s = 1;
            for (int i = 1; i < n.estrelas.Length && i < 3; i++) if (Cumpre(n.estrelas[i], r)) s++;
            return s;
        }

        public static string Texto(Objetivo o)
        {
            switch (o.tipo)
            {
                case "posicao": return o.valor <= 1 ? "Vencer" : $"Chegar até o {o.valor:0}º";
                case "semSair": return o.valor <= 0 ? "Sem sair da pista" : $"No máximo {o.valor:0} saídas";
                case "semTurbo": return "Sem usar o turbo";
                case "volta": return $"Volta abaixo de {RaceController.Fmt(o.valor)}";
                case "tempo": return $"Prova abaixo de {RaceController.Fmt(o.valor)}";
                case "serie": return "Vencer a série";
                case "invicto": return "Vencer sem perder bateria";
                default: return o.tipo;
            }
        }

        public const float Wet = 0.8f;   // pista molhada: aderência × 0,8

        public static string RegraTexto(NodeDef n, string regra)
        {
            switch (regra)
            {
                case "molhada": return "Pista molhada: 20% menos aderência";
                case "carro": return $"Carro imposto: {(n.carro.Padrao ? "padrão" : n.carro.Resumo)}";
                case "sobrevivencia": return $"Mais de {n.limite} {(n.limite == 1 ? "saída" : "saídas")} e você está fora";
                case "atrasada": return $"Você larga {n.atraso:0.#} s depois";
                case "turboLivre": return "Turbo sempre cheio, para todos";
                default: return regra;
            }
        }

        // ----- progresso -----

        public static NodeSave Of(CareerSave s, string id) => s.nos.Find(x => x.id == id);
        public static int Stars(CareerSave s, string id) => Of(s, id)?.estrelas ?? 0;

        public static int RegionStars(CareerSave s, RegionDef r)
        {
            int n = 0;
            foreach (var x in r.nos) n += Stars(s, x.id);
            return n;
        }

        // Região aberta: a primeira, ou a anterior com o último nó (o campeonato) concluído.
        public static bool RegionOpen(CareerSave s, int i)
        {
            if (i <= 0) return true;
            var prev = Def.regioes[i - 1];
            return RegionOpen(s, i - 1) && prev.nos.Length > 0 && Stars(s, prev.nos[prev.nos.Length - 1].id) > 0;
        }

        public static bool NodeOpen(CareerSave s, RegionDef r, NodeDef n)
        {
            if (n.tipo == "rival" && RegionStars(s, r) < r.estrelasRival) return false;
            if (n.depois == null || n.depois.Length == 0) return true;
            foreach (var d in n.depois) if (Stars(s, d) > 0) return true;
            return false;
        }

        // Guarda o melhor de cada coisa; devolve quantas estrelas NOVAS a corrida deu.
        public static int Record(CareerSave s, NodeDef n, ResultadoCorrida r)
        {
            int got = Avaliar(n, r);
            var ns = Of(s, n.id);
            if (ns == null) { ns = new NodeSave { id = n.id }; s.nos.Add(ns); }
            int novas = Mathf.Max(0, got - ns.estrelas);
            ns.estrelas = Mathf.Max(ns.estrelas, got);
            if (r.terminou && (ns.melhorPos == 0 || r.posicao < ns.melhorPos)) ns.melhorPos = r.posicao;
            if (r.melhorVolta >= 0f && (ns.melhorVolta < 0f || r.melhorVolta < ns.melhorVolta)) ns.melhorVolta = r.melhorVolta;
            return novas;
        }

        // ----- arquivos -----

        public static string PathOf(int slot) => Path.Combine(TrackLibrary.Root, $"carreira-{Mathf.Clamp(slot, 1, Slots)}.json");

        public static CareerSave Load(int slot)
        {
            try { var p = PathOf(slot); return File.Exists(p) ? JsonUtility.FromJson<CareerSave>(File.ReadAllText(p)) ?? new CareerSave() : new CareerSave(); }
            catch { return new CareerSave(); }
        }

        public static bool Save(int slot, CareerSave s)
        {
            try { Directory.CreateDirectory(TrackLibrary.Root); File.WriteAllText(PathOf(slot), JsonUtility.ToJson(s, true)); return true; }
            catch (Exception e) { Debug.LogWarning("Carreira não salva: " + e.Message); return false; }
        }
    }
}
