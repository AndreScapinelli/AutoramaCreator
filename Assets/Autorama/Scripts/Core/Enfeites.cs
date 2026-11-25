using System;
using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Cosméticos (enfeites) soltos na mesa (seção 97 do guia): árvores, pedras e o que vier. Não mexem na corrida nem na
    // identidade da pista; só somam no custo. O catálogo é um asset (Resources/Cosmeticos/Catalogo): asset novo é uma
    // linha a mais nele, sem código. O tamanho vem da altura em células; a pegada, das bordas do modelo.
    [Serializable]
    public class EnfeiteDef
    {
        public string id;                  // fica no JSON da pista: não muda depois de lançado
        public string nome;                // em português (a chave do L.T)
        public string grupo;               // "Árvores", "Pedras"…
        public GameObject prefab;
        public int preco = 50;
        public float altura = 2f;          // altura na mesa, em células (1 célula = largura da pista)
    }

    // Um enfeite na mesa, em células no plano da Unity (x, z) e giro em graus (em volta do eixo vertical).
    [Serializable]
    public class Enfeite
    {
        public string id;
        public Vector2 pos;
        public float rot;
        public float esc = 1f;             // variação de tamanho (0,9 a 1,1 ao colocar)

        // Arquibancada (seção 101): trecho ao longo da pista. linha = frente, em células; u0 e comp = início e comprimento
        // no caminho da pista (células) para esticar de novo; lado = +1 à esquerda do sentido da pista, −1 à direita.
        public List<Vector2> linha;
        public float u0, comp;
        public int lado, colunas;
        public bool Trecho => linha != null && linha.Count > 1;

        public Enfeite Clone() { var c = (Enfeite)MemberwiseClone(); if (linha != null) c.linha = new List<Vector2>(linha); return c; }
    }

    // No JSON (formato herdado do HTML): y para baixo (y = altura da mesa − z) e ângulo no sentido horário.
    [Serializable] public class EnfeiteSave { public string id; public float x, y, h, esc = 1f; public float[] linha; public float u0, comp; public int lado, colunas; }
}
