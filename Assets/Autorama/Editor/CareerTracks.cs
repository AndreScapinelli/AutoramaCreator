using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Autorama
{
    // Gera as pistas da carreira (seção 56 do guia) em Assets/Autorama/Carreira/pistas.
    // Cada pista é a parte desenhada à mão (o "jeito" dela) e o fechar sozinho (Q) completa o circuito.
    // Rodar: script eval 'return Autorama.CareerTracks.Gerar();'
    public static class CareerTracks
    {
        public const string Folder = "Assets/Autorama/Resources/Carreira/pistas";

        // Peças em texto, separadas por espaço: código + lado (e/d) + altura (^ sobe, v desce) + número (vão ou raio).
        // R reta curta · L comprida · t curtinha · X cruzamento · x cruzamento curto · C curva longa · M meia curva
        // B curva longa inclinada · b curta inclinada · U curva em C · O loop · J salto
        struct Def { public string id, nome, pecas; public int faixas; public bool inclinada; }

        static Def P(string id, string nome, int faixas, string pecas, bool inclinada = false) =>
            new Def { id = id, nome = nome, faixas = faixas, pecas = pecas, inclinada = inclinada };

        static readonly Def[] Pistas =
        {
            // ----- 1 · Garagem (2 faixas): reta, curva longa e meia curva, no chão -----
            P("garagem-1", "Primeira Volta", 2, "L Cd Cd R L Cd Cd"),
            P("garagem-2", "Reta da Bancada", 2, "L L Cd Cd R L L Cd Cd"),
            P("garagem-3", "Feijão", 2, "L Cd Cd R Me Md Cd"),
            P("garagem-5", "Cotovelo", 2, "L Cd R Ce Cd Cd L"),
            P("garagem-6", "Serpentina", 2, "R Md Me Md Me Cd Cd"),
            P("garagem-7", "Pista do Tio Beto", 2, "L R Cd Md Md R Ce Cd Cd"),
            P("garagem-c1", "Copa Garagem · Oval", 2, "L L R Cd Cd L L L Cd Cd"),
            P("garagem-c2", "Copa Garagem · Gancho", 2, "L Cd Cd R Ce Ce Cd Cd"),
            P("garagem-c3", "Copa Garagem · Caracol", 2, "L R Cd Cd Md Me L Cd"),

            // ----- 2 · Clube (4 faixas): + ponte, cruzamento e curva inclinada -----
            P("clube-1", "Salão do Clube", 4, "L L Bd Bd R L L Bd Bd", true),
            P("clube-2", "Troca de Faixa", 4, "L X Cd Cd L R Cd Cd|L X Cd Cd L Cd Cd|L X Cd Cd"),
            P("clube-3", "Viaduto", 4, "L^ t Lv Bd Bd R Bd", true),
            P("clube-5", "Grampo", 4, "L R Cd Cd R Me Me Cd"),
            P("clube-6", "Inclinado Duplo", 4, "L Bd Bd R bd be Bd", true),
            P("clube-7", "Pista da Marina", 4, "L L Bd R Cd Ce Cd Bd", true),
            P("clube-8", "Travessa", 4, "R Cd Cd R X R Ce Ce"),
            P("clube-c1", "Taça Clube · Rápida", 4, "L L L Bd Bd R L L L Bd Bd", true),
            P("clube-c2", "Taça Clube · Troca", 4, "L R X Cd Cd L Me Md Cd|L R X Cd Cd L L Cd Cd|L R X Cd Cd"),
            P("clube-c3", "Taça Clube · Ponte", 4, "L^ t Lv Cd Cd L Md Me Cd", true),

            // ----- 3 · Regional (6 faixas): + curva em C e salto (vão 1) -----
            P("regional-1", "Ginásio", 6, "L L J L Bd Bd R L Bd Bd|L L J L Bd Bd|L J L Bd Bd", true),
            P("regional-2", "Rampa Curta", 6, "L J L Cd Cd L Cd Cd"),
            P("regional-3", "Cotovelo em C", 6, "L R Ud R Ce Ce L|L R Ud L Ce|L L Ud L L"),
            P("regional-4", "Ziguezague", 6, "L Md Me Md Me R Bd Bd L Bd", true),
            P("regional-6", "Salto e Ponte", 6, "J L^ t Lv Bd Bd L Bd", true),
            P("regional-7", "Mirante", 6, "L L Bd R be bd Bd L R", true),
            P("regional-8", "Pista do Dr. Curva", 6, "L Bd bd bd R be be Bd L Bd|L Bd bd bd R Bd|L Bd Bd R bd be Bd", true),
            P("regional-c1", "Regional · Abertura", 6, "L L J L Bd Bd L L Bd Bd", true),
            P("regional-c2", "Regional · Troca", 6, "L R X J L Bd Bd L L Bd Bd|L X J L Bd Bd|L X L Bd Bd", true),
            P("regional-c3", "Regional · Gancho", 6, "L R Ud R Ce Ce L Md Me|L R Ud L Ce L|L L Ud L", true),
            P("regional-c4", "Regional · Final", 6, "L J L Bd R bd bd Bd L", true),

            // ----- 4 · Nacional (8 faixas): + loop e saltos de vão 2 e 3 -----
            P("nacional-1", "Estádio", 8, "L L O L Bd Bd L L Bd Bd", true),
            P("nacional-2", "Grande Salto", 8, "L J2 L Bd Bd L L Bd Bd", true),
            P("nacional-3", "Looping", 8, "L Od L Cd Cd R Md Me Cd"),
            P("nacional-4", "Abismo", 8, "L L J3 L Bd Bd L Bd", true),
            P("nacional-5", "Nacional · Troca", 8, "L X R Bd Bd J2 L Bd Bd|L X Bd Bd J2 L|L X Bd Bd L", true),
            P("nacional-6", "Torre", 8, "L^ t Lv Bd Bd O R Bd", true),
            P("nacional-7", "Serra", 8, "L Md Me Md Me J2 Bd Bd L Bd", true),
            P("nacional-8", "Arena do Campeão", 8, "L J2 L Bd bd bd O Bd|L J2 L Bd Bd O L Bd|L J2 L Bd Bd O", true),
            P("nacional-c1", "Nacional · Velocidade", 8, "L L L J L Bd Bd L L L Bd Bd", true),
            P("nacional-c2", "Nacional · Loop", 8, "L O L Bd Bd R Ud R Ce|L O L Bd Bd L|L O L Bd Bd", true),
            P("nacional-c3", "Nacional · Salto", 8, "L J3 L Bd Bd R Md Me Bd", true),
            P("nacional-c4", "Nacional · Troca", 8, "L J X Bd bd bd L J2 Bd Bd|L J X Bd Bd L J2|L J X Bd Bd L", true),
            P("nacional-c5", "Nacional · Grande Final", 8, "L L O L Bd bd bd J2 L Bd Bd", true),
        };

        static readonly Regex Tok = new Regex(@"^(t|x|b|[RLXCMBUOJ])([ed])?([\^v])?(\d)?$");

        static List<TrackPiece> Parse(string s)
        {
            var list = new List<TrackPiece> { new TrackPiece(PieceCatalog.Start.Id) };
            foreach (var w in s.Split(' '))
            {
                var m = Tok.Match(w);
                if (!m.Success) throw new System.FormatException("Peça inválida: " + w);
                string id = m.Groups[1].Value switch
                {
                    "R" => "reta-curta", "L" => "reta-comprida", "t" => "reta-curtinha", "X" => "cruzamento", "x" => "cruzamento-curto",
                    "C" => "curva-longa", "M" => "meia-curva", "B" => "curva-longa-inclinada", "b" => "curva-curta-inclinada",
                    "U" => "curva-c", "O" => "loop", _ => "salto",
                };
                var def = PieceCatalog.ById(id);
                int dir = def.Sided ? (m.Groups[2].Value == "e" ? -1 : 1) : 0;
                int rise = m.Groups[3].Value == "^" ? 1 : m.Groups[3].Value == "v" ? -1 : 0;
                int n = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 1;
                list.Add(new TrackPiece(id, dir, rise, def.IsJump ? n : 1, def.IsHairpin ? n : 1));
            }
            return list;
        }

        // Fecha com o Q; se a ponta ficou presa (em cima do começo), tira até 3 peças do fim e tenta de novo.
        static bool Fechar(Pose2 origin, string pecas, bool inclinada, out List<TrackPiece> pieces, out TrackAnalysis a)
        {
            pieces = Parse(pecas);
            a = TrackLayout.Analyze(origin, TrackLayout.Build(origin, pieces));
            for (int cortadas = 0; !a.closed; cortadas++)
            {
                var r = TrackCloser.Close(origin, pieces, new List<TrackPiece>(), inclinada);
                if (r.ok) { pieces.AddRange(r.pieces); a = TrackLayout.Analyze(origin, TrackLayout.Build(origin, pieces)); break; }
                if (cortadas == 3 || pieces.Count <= 3) break;
                pieces.RemoveAt(pieces.Count - 1);
            }
            return a.closed && a.overlaps.Count == 0;
        }

        // prefixo: só as pistas cujo id começa com ele ("garagem", "clube-3"…); vazio gera todas.
        public static string Gerar(string prefixo = "")
        {
            Directory.CreateDirectory(Folder);
            var sb = new StringBuilder();
            int ok = 0, total = 0;
            foreach (var d in Pistas)
            {
                if (!d.id.StartsWith(prefixo)) continue;
                total++;
                var origin = new Pose2(new Vector2(0f, 0.5f), 0f);
                // Alternativas separadas por "|": vale a primeira que fecha sem sobrepor.
                List<TrackPiece> pieces = null;
                TrackAnalysis a = null;
                foreach (var alt in d.pecas.Split('|'))
                    if (Fechar(origin, alt.Trim(), d.inclinada, out pieces, out a)) break;
                if (!a.closed || a.overlaps.Count > 0) { sb.AppendLine($"{d.id}: inválida (fecha {a.closed}, sobrepõe {a.overlaps.Count})"); continue; }

                // Mesa justa com margem de 2 células; a largada anda um número inteiro de células (continua na grade).
                const int margin = 2;
                var dx = Mathf.Ceil(margin - a.bounds.xMin);
                var dy = Mathf.Ceil(margin - a.bounds.yMin);
                var o = new Pose2(origin.pos + new Vector2(dx, dy), origin.heading);
                var board = new Vector2Int(Mathf.CeilToInt(a.bounds.xMax + dx) + margin, Mathf.CeilToInt(a.bounds.yMax + dy) + margin);
                var save = TrackSave.From(d.nome, board, 15f, o, pieces, 0, d.faixas);
                File.WriteAllText(Path.Combine(Folder, d.id + ".json"), save.ToJson());
                ok++;
                sb.AppendLine($"{d.id}: {pieces.Count - 1} peças, {a.centerLength:0.0} células, mesa {board.x}×{board.y}, alto {a.highCount}");
            }
            UnityEditor.AssetDatabase.Refresh();
            return $"{ok}/{total} pistas\n" + sb;
        }
    }
}
