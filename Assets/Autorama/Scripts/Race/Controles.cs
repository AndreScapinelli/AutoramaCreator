using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Autorama
{
    // As teclas dos jogadores de teclado (seção 36 do guia). Até 4 jogadores, e cada um tem quatro ações:
    // acelerar (100%), meia aceleração (o ajuste "halfGas", 60% por padrão), recolocar e turbo (seção 37).
    // Ficam salvas no PlayerPrefs e valem na corrida, no campeonato e no online. O 5º e o 6º jogam só de controle.
    public static class Controles
    {
        public const int Jogadores = 4;
        public const int Acoes = 4;
        public enum Acao { Acelerar = 0, Meia = 1, Recolocar = 2, Turbo = 3 }
        public static readonly string[] AcaoNomes = { "Acelerar", "Meia", "Recolocar", "Turbo" };

        // Padrão (acelerar · meia · recolocar · turbo): jogador 1 A · S · Espaço · D; jogador 2 L · K · ; · J;
        // jogador 3 ↑ · ↓ · Shift direito · →; jogador 4 8 · 5 · 0 · 6 do numérico.
        static readonly Key[] Padrao =
        {
            Key.A, Key.S, Key.Space, Key.D,
            Key.L, Key.K, Key.Semicolon, Key.J,
            Key.UpArrow, Key.DownArrow, Key.RightShift, Key.RightArrow,
            Key.Numpad8, Key.Numpad5, Key.Numpad0, Key.Numpad6,
        };
        static readonly Key[] teclas = (Key[])Padrao.Clone();      // índice = jogador * Acoes + ação
        static bool carregado;
        const string Chave = "autorama.teclas";

        [Serializable] class Dados { public int[] teclas; }

        static void Garante()
        {
            if (carregado) return;
            carregado = true;
            try
            {
                var json = PlayerPrefs.GetString(Chave, "");
                if (json == "") return;
                var d = JsonUtility.FromJson<Dados>(json);
                if (d?.teclas == null) return;
                // Antes do turbo eram 3 ações por jogador: as teclas salvas continuam e o turbo entra com o padrão
                // (se a tecla padrão do turbo já estava em uso por outra ação, o turbo fica sem tecla até o jogador escolher).
                int per = d.teclas.Length / Jogadores;
                if (d.teclas.Length != per * Jogadores || per < 3 || per > Acoes) return;
                for (int j = 0; j < Jogadores; j++)
                    for (int a = 0; a < per; a++) teclas[j * Acoes + a] = (Key)d.teclas[j * per + a];
                for (int j = 0; j < Jogadores; j++)
                    for (int a = per; a < Acoes; a++)
                    {
                        int i = j * Acoes + a;
                        for (int o = 0; o < teclas.Length; o++) if (o != i && teclas[o] == teclas[i]) { teclas[i] = Key.None; break; }
                    }
            }
            catch { }
        }

        // Tecla do jogador (0 a 3) para a ação. Fora desse intervalo (5º e 6º jogador): None, só controle.
        public static Key Get(int jogador, Acao a)
        {
            Garante();
            return jogador >= 0 && jogador < Jogadores ? teclas[jogador * Acoes + (int)a] : Key.None;
        }

        // Põe a tecla na ação. Se ela já era de outra ação, as duas trocam: nenhuma fica sem tecla sem querer.
        public static void Set(int jogador, Acao a, Key k)
        {
            Garante();
            int i = jogador * Acoes + (int)a;
            for (int j = 0; j < teclas.Length; j++) if (j != i && teclas[j] == k && k != Key.None) { teclas[j] = teclas[i]; break; }
            teclas[i] = k;
            Save();
        }

        // Teclas que o jogo usa para outras coisas: não podem ser de um jogador.
        public static bool Reservada(Key k) => k == Key.None || k == Key.Enter || k == Key.NumpadEnter || k == Key.Escape;

        public static void Restaurar()
        {
            Array.Copy(Padrao, teclas, teclas.Length);
            Save();
        }

        static void Save()
        {
            var d = new Dados { teclas = new int[teclas.Length] };
            for (int i = 0; i < teclas.Length; i++) d.teclas[i] = (int)teclas[i];
            PlayerPrefs.SetString(Chave, JsonUtility.ToJson(d));
            PlayerPrefs.Save();
        }

        // Nome curto da tecla para botões e dicas.
        public static string Nome(Key k) => k switch
        {
            Key.UpArrow => "↑", Key.DownArrow => "↓", Key.LeftArrow => "←", Key.RightArrow => "→",
            Key.Space => "Espaço", Key.Semicolon => ";", Key.RightShift => "Shift dir.", Key.LeftShift => "Shift esq.",
            Key.Numpad8 => "8 num.", Key.Numpad5 => "5 num.", Key.Numpad7 => "7 num.", Key.Numpad0 => "0 num.", Key.Numpad6 => "6 num.", Key.Numpad4 => "4 num.",
            Key.None => "controle",
            _ => k.ToString(),
        };
    }
}
