using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // O mesmo JSON do botão "Copiar" do construtor em HTML: uma pista montada lá abre aqui, e vice-versa.
    // O HTML usa Y para baixo e ângulo no sentido horário; aqui, z = altura da mesa − y e ângulo = −h.
    [Serializable] public class BoardSave  { public int w = 32, h = 20; }
    [Serializable] public class OriginSave { public float x, y, h; }
    [Serializable] public class PieceSave  { public string tipo; public string sentido; public string altura; }

    [Serializable]
    public class TrackSave
    {
        public string formato = "autorama-pista";
        public int versao = 1;
        public string nome = "";
        public BoardSave mesa = new BoardSave();
        public float escalaCm = 15f;
        public OriginSave largada = new OriginSave();
        public List<PieceSave> pecas = new List<PieceSave>();
        public int atras;                 // quantas das últimas peças de "pecas" ficam atrás da largada
        public int faixas = 2;            // 2 ou 4 carros (sem o campo: 2). Pista de 4 só existe na Unity (seção 25).

        public int Faixas => TrackMath.Faixas(faixas);

        public string ToJson(bool pretty = true) => JsonUtility.ToJson(this, pretty);

        public static TrackSave FromJson(string json)
        {
            var s = JsonUtility.FromJson<TrackSave>(json);
            if (s == null || s.pecas == null) throw new FormatException("O texto não é uma pista: falta a lista \"pecas\".");
            if (s.mesa == null) s.mesa = new BoardSave();
            if (s.largada == null) s.largada = new OriginSave();
            s.faixas = TrackMath.Faixas(s.faixas);
            return s;
        }

        // ----- conversão para o modelo da Unity -----

        public Pose2 Origin => TrackMath.SnapStart(new Pose2(new Vector2(largada.x, mesa.h - largada.y), -largada.h));

        public int Tail => Mathf.Clamp(atras, 0, pecas.Count);

        public List<TrackPiece> Pieces()
        {
            var list = new List<TrackPiece> { new TrackPiece(PieceCatalog.Start.Id) };
            foreach (var p in pecas)
            {
                var def = PieceCatalog.ById(p.tipo);
                if (def == null || def.IsStart) throw new FormatException($"Peça desconhecida: \"{p.tipo}\".");
                int dir = def.IsCurve ? (p.sentido == "esquerda" ? -1 : 1) : 0;
                int rise = p.altura == "sobe" ? 1 : p.altura == "desce" ? -1 : 0;
                list.Add(new TrackPiece(def.Id, dir, rise));
            }
            return list;
        }

        public static TrackSave From(string name, Vector2Int board, float cellCm, Pose2 origin, IList<TrackPiece> pieces, int tail = 0, int faixas = 2)
        {
            var s = new TrackSave
            {
                nome = name ?? "",
                faixas = TrackMath.Faixas(faixas),
                mesa = new BoardSave { w = board.x, h = board.y },
                escalaCm = cellCm,
                atras = tail,
                largada = new OriginSave { x = origin.pos.x, y = board.y - origin.pos.y, h = TrackMath.Normalize(-origin.heading) },
            };
            for (int i = 1; i < pieces.Count; i++)              // a largada não vai na lista
            {
                var p = pieces[i];
                s.pecas.Add(new PieceSave
                {
                    tipo = p.id,
                    sentido = p.dir < 0 ? "esquerda" : p.dir > 0 ? "direita" : "",
                    altura = p.rise > 0 ? "sobe" : p.rise < 0 ? "desce" : "",
                });
            }
            return s;
        }

        // ----- exemplos (os mesmos do HTML, em coordenadas do HTML) -----

        // O mesmo exemplo como pista de 4 carros (a forma em células é a mesma; no mundo fica 2× maior).
        public static TrackSave Four(TrackSave s) { s.faixas = 4; s.nome += " (4 carros)"; return s; }
        public static TrackSave Six(TrackSave s) { s.faixas = 6; s.nome += " (6 carros)"; return s; }

        static PieceSave P(string tipo, string sentido = "", string altura = "") => new PieceSave { tipo = tipo, sentido = sentido, altura = altura };

        public static TrackSave Oval()
        {
            var s = new TrackSave { nome = "Oval", largada = new OriginSave { x = 8, y = 7.5f, h = 0 } };
            s.pecas.AddRange(new[]
            {
                P("reta-comprida"), P("meia-curva", "esquerda"), P("meia-curva", "direita"), P("curva-longa", "direita"), P("curva-longa", "direita"),
                P("reta-comprida"), P("meia-curva", "esquerda"), P("meia-curva", "direita"), P("reta-curta"), P("curva-longa", "direita"), P("curva-longa", "direita"),
            });
            return s;
        }

        // Um 8 deitado de curvas curtas inclinadas; a diagonal de cima passa pela ponte sobre a do chão.
        public static TrackSave Eight()
        {
            var s = new TrackSave { nome = "8 com ponte", largada = new OriginSave { x = 21, y = 5.5f, h = 0 } };
            PieceSave Q(string side) => P("curva-curta-inclinada", side);
            s.pecas.AddRange(new[]
            {
                Q("direita"), P("reta-curta"), Q("direita"), Q("direita"), P("reta-curta"), Q("direita"), P("reta-curta"), Q("direita"),
                P("reta-comprida", "", "sobe"), P("reta-curtinha"), P("reta-comprida", "", "desce"),
                Q("esquerda"), P("reta-curta"), Q("esquerda"), P("reta-curta"), Q("esquerda"), Q("esquerda"), P("reta-curta"), Q("esquerda"), P("reta-curta"), Q("esquerda"),
                P("reta-comprida"), P("reta-curtinha"), P("reta-comprida"),
                Q("direita"),
            });
            return s;
        }
    }

    // Pistas salvas em disco: uma por arquivo, em persistentDataPath/autorama/pistas.
    public static class TrackLibrary
    {
        public static string Root => Path.Combine(Application.persistentDataPath, "autorama");
        public static string Folder => Path.Combine(Root, "pistas");
        public static string DraftPath => Path.Combine(Root, "rascunho.json");

        public struct Entry { public string path; public TrackSave save; }

        public static List<Entry> List()
        {
            var list = new List<Entry>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var f in Directory.GetFiles(Folder, "*.json"))
            {
                try { list.Add(new Entry { path = f, save = TrackSave.FromJson(File.ReadAllText(f)) }); }
                catch (Exception e) { Debug.LogWarning($"Pista ignorada ({Path.GetFileName(f)}): {e.Message}"); }
            }
            list.Sort((a, b) => File.GetLastWriteTimeUtc(b.path).CompareTo(File.GetLastWriteTimeUtc(a.path)));
            return list;
        }

        // Salva com o nome do arquivo derivado do nome da pista (substitui uma de mesmo nome).
        public static string Save(TrackSave s)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, Slug(s.nome) + ".json");
            File.WriteAllText(path, s.ToJson());
            return path;
        }

        public static void Delete(string path) { if (File.Exists(path)) File.Delete(path); }

        public static void SaveDraft(TrackSave s)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(DraftPath, s.ToJson());
        }

        public static TrackSave LoadDraft()
        {
            try { return File.Exists(DraftPath) ? TrackSave.FromJson(File.ReadAllText(DraftPath)) : null; }
            catch { return null; }
        }

        static string Slug(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "pista";
            var chars = name.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).ToCharArray();
            var sb = new System.Text.StringBuilder();
            foreach (var c in chars)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            }
            return sb.ToString().Trim('-');
        }
    }
}
