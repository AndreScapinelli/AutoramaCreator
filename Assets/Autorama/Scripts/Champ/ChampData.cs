using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Autorama
{
    // Campeonato: as mesmas regras do construtor em HTML (seção 19 do guia).
    // Pilotos ficam num cadastro e são reaproveitados. O campeonato guarda uma cópia dos pilotos e das pistas.
    // Rodízio: com N pilotos, cada etapa tem N baterias; na bateria k correm o piloto k (trilho 1) e o k+1 (trilho 2).
    // Assim cada piloto corre exatamente uma vez em cada trilho. Com 2 pilotos são as 2 baterias com troca de lado.
    // Os arquivos usam listas (o JsonUtility não lê dicionário); o HTML guarda os mesmos dados em mapas.

    [Serializable]
    public class Pilot
    {
        public string id, name, tag, color;      // color: "#RRGGBB"
        public bool ai;                          // o computador dirige
        public int skill = 50;                   // qualidade do piloto IA, 1–100
        public Color Color => ColorUtility.TryParseHtmlString(color, out var c) ? c : Color.red;
        public Pilot Copy() => (Pilot)MemberwiseClone();
    }

    [Serializable] public class Stage { public string name; public int laps = 5; public string hash; public TrackSave data; }

    [Serializable]
    public class HeatResult
    {
        public int laps, offs, hits;
        public float time, best = -1f;           // best −1: nenhuma volta completa
        public bool dnf, rec, raceRec, pb;       // não terminou; bateu recorde de volta, de corrida ou o melhor pessoal
    }

    [Serializable]
    public class Heat
    {
        public int s;                            // etapa
        public int[] p = new int[2];             // pilotos: [trilho 1, trilho 2]
        public HeatResult[] res;                 // vazio até salvar (o JsonUtility grava null como [])
        public bool Saved => res != null && res.Length == 2;
    }

    [Serializable] public class ChampRules { public float vmax, ramp, grip; public bool collide; }

    [Serializable]
    public class Championship
    {
        public string id, name;
        public long created, finished;
        public ChampRules rules = new ChampRules();
        public List<Pilot> pilots = new List<Pilot>();
        public List<Stage> stages = new List<Stage>();
        public List<Heat> heats = new List<Heat>();
    }

    [Serializable] public class ChampDraft { public string name = ""; public List<string> pilots = new List<string>(); public List<Stage> stages = new List<Stage>(); }
    [Serializable] public class HistoryRow { public Pilot pilot; public int pts, wins, podiums, fast; }
    [Serializable] public class HistoryEntry { public string id, name; public long finished; public List<string> stages = new List<string>(); public List<HistoryRow> table = new List<HistoryRow>(); }

    [Serializable]
    public class ChampStore
    {
        public ChampDraft draft = new ChampDraft();
        public bool hasActive;                   // o JsonUtility não grava null: este campo diz se "active" vale
        public Championship active;
        public List<HistoryEntry> history = new List<HistoryEntry>();
        public Championship Active => hasActive ? active : null;
    }

    [Serializable] public class PilotList { public List<Pilot> pilots = new List<Pilot>(); }

    // ---------- Recordes ----------

    [Serializable] public class Rec { public float t = -1f; public Pilot pilot; public long date; public string champ; public int lane; public bool Has => t >= 0f; }
    [Serializable] public class RaceRec { public int laps; public Rec rec = new Rec(); }
    [Serializable] public class PilotBest { public string pilot; public float lap = -1f; public List<RaceRec> race = new List<RaceRec>(); }

    [Serializable]
    public class TrackRecords
    {
        public string hash, name;
        public Rec lap = new Rec();
        public Rec lane1 = new Rec(), lane2 = new Rec();
        public List<RaceRec> race = new List<RaceRec>();
        public List<PilotBest> pb = new List<PilotBest>();

        public Rec Lane(int k) => k == 0 ? lane1 : lane2;
        public void SetLane(int k, Rec r) { if (k == 0) lane1 = r; else lane2 = r; }
        public Rec Race(int laps) { var r = race.Find(x => x.laps == laps); return r != null ? r.rec : null; }
        public void SetRace(int laps, Rec r) { var e = race.Find(x => x.laps == laps); if (e == null) race.Add(new RaceRec { laps = laps, rec = r }); else e.rec = r; }
        public PilotBest Best(string id, bool create)
        {
            var b = pb.Find(x => x.pilot == id);
            if (b == null && create) { b = new PilotBest { pilot = id }; pb.Add(b); }
            return b;
        }
    }

    [Serializable] public class RecordsFile { public List<TrackRecords> tracks = new List<TrackRecords>(); }

    // ---------- Regras e contas ----------

    public class StageRow { public int i, laps, runs, offs, hits, pts = -1; public float time, best = -1f; public bool fast; }
    public class StandRow { public int i, pts, wins, podiums, fast; public float time; }

    public static class Champ
    {
        public static readonly int[] Points = { 10, 8, 6, 5, 4, 3, 2, 1 };   // por posição na etapa; +1 para a melhor volta
        public const float Cutoff = 30f;          // segundos depois da bandeirada do primeiro
        public const int MaxPilots = 8;
        public static readonly string[] Colors = { "#E0362C", "#2563EB", "#F2B705", "#16A34A", "#9333EA", "#F97316", "#0EA5B7", "#E04B95" };
        public static readonly string[] ColorNames = { "Vermelho", "Azul", "Amarelo", "Verde", "Roxo", "Laranja", "Ciano", "Rosa" };

        public static List<Heat> MakeHeats(int nPilots, int nStages)
        {
            var list = new List<Heat>();
            for (int s = 0; s < nStages; s++)
                for (int k = 0; k < nPilots; k++)
                    list.Add(new Heat { s = s, p = new[] { k, (k + 1) % nPilots } });
            return list;
        }

        // A bateria da vez: a primeira sem resultado. −1: campeonato encerrado.
        public static int CurrentHeat(Championship c) => c.heats.FindIndex(h => !h.Saved);

        // Resultado de uma etapa: soma as duas baterias de cada piloto. Mais voltas primeiro, depois menos tempo.
        public static List<StageRow> StageResults(Championship c, int s, out bool complete, out int saved, out int total)
        {
            var rows = new List<StageRow>();
            for (int i = 0; i < c.pilots.Count; i++) rows.Add(new StageRow { i = i });
            complete = true; saved = 0; total = 0;
            foreach (var h in c.heats)
            {
                if (h.s != s) continue;
                total++;
                if (!h.Saved) { complete = false; continue; }
                saved++;
                for (int k = 0; k < 2; k++)
                {
                    var r = h.res[k]; var row = rows[h.p[k]];
                    row.laps += r.laps; row.time += r.time; row.runs++; row.offs += r.offs; row.hits += r.hits;
                    if (r.best >= 0f && (row.best < 0f || r.best < row.best)) row.best = r.best;
                }
            }
            rows.RemoveAll(r => r.runs == 0);
            rows.Sort((a, b) => a.laps != b.laps ? b.laps.CompareTo(a.laps) : a.time.CompareTo(b.time));
            if (complete)
            {
                for (int pos = 0; pos < rows.Count; pos++) rows[pos].pts = pos < Points.Length ? Points[pos] : 0;
                StageRow fastest = null;
                foreach (var r in rows) if (r.best >= 0f && (fastest == null || r.best < fastest.best)) fastest = r;
                if (fastest != null) { fastest.fast = true; fastest.pts++; }
            }
            return rows;
        }

        public static bool StageComplete(Championship c, int s) { StageResults(c, s, out bool done, out _, out _); return done; }

        // Tabela geral com as primeiras `upto` etapas (só as completas contam). Desempate: vitórias, pódios, tempo.
        public static List<StandRow> Standings(Championship c, int upto = -1)
        {
            if (upto < 0) upto = c.stages.Count;
            var t = new List<StandRow>();
            for (int i = 0; i < c.pilots.Count; i++) t.Add(new StandRow { i = i });
            for (int s = 0; s < upto; s++)
            {
                var rows = StageResults(c, s, out bool complete, out _, out _);
                if (!complete) continue;
                for (int pos = 0; pos < rows.Count; pos++)
                {
                    var r = rows[pos]; var row = t[r.i];
                    row.pts += r.pts; if (pos == 0) row.wins++; if (pos < 3) row.podiums++; if (r.fast) row.fast++; row.time += r.time;
                }
            }
            t.Sort((a, b) => a.pts != b.pts ? b.pts.CompareTo(a.pts) : a.wins != b.wins ? b.wins.CompareTo(a.wins)
                           : a.podiums != b.podiums ? b.podiums.CompareTo(a.podiums) : a.time.CompareTo(b.time));
            return t;
        }

        public static int StagesDone(Championship c)
        {
            int n = 0;
            for (int s = 0; s < c.stages.Count; s++) if (StageComplete(c, s)) n++;
            return n;
        }

        // A identidade da pista é a forma (sequência de peças), não o nome nem a posição na mesa.
        // "id:sentido:altura" de cada peça, FNV-1a de 32 bits: o mesmo número que o HTML calcula.
        public static string TrackHash(TrackSave t)
        {
            var sb = new StringBuilder("largada:0:0");
            foreach (var p in t.pecas)
            {
                var def = PieceCatalog.ById(p.tipo);
                int dir = def != null && def.IsCurve ? (p.sentido == "esquerda" ? -1 : 1) : 0;
                int rise = p.altura == "sobe" ? 1 : p.altura == "desce" ? -1 : 0;
                sb.Append('|').Append(p.tipo).Append(':').Append(dir).Append(':').Append(rise);
            }
            uint h = 0x811c9dc5;
            foreach (char ch in sb.ToString()) { h ^= ch; h *= 0x01000193; }
            return h.ToString("x8");
        }

        public static int CrossCount(TrackSave t)
        {
            int n = 0;
            foreach (var p in t.pecas) { var d = PieceCatalog.ById(p.tipo); if (d != null && d.IsCross) n++; }
            return n;
        }

        // Sigla de 3 letras para o placar: sem acento e só letras ou números (André → AND).
        public static string TagOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (ch < 128 && char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
                if (sb.Length == 3) break;
            }
            return sb.ToString();
        }

        public static string SkillWord(int s) =>
            s <= 20 ? "Iniciante: freia cedo e erra bastante" : s <= 45 ? "Amador: anda seguro, às vezes passa do ponto"
            : s <= 70 ? "Bom: entra forte nas curvas" : s <= 90 ? "Experiente: quase no limite, raramente sai" : "Profissional: no limite da aderência";

        // Duração estimada: cada bateria ~ voltas × comprimento ÷ (60% da velocidade máxima), mais a largada.
        public static int EstimateMin(List<Stage> stages, int nPilots, float vmax)
        {
            float sec = 0f;
            foreach (var s in stages)
            {
                float len = 0f;
                try { len = TrackLayout.Analyze(s.data.Origin, TrackLayout.Build(s.data.Origin, s.data.Pieces())).centerLength; } catch { }
                sec += nPilots * (s.laps * len / (0.6f * vmax) + 10f);
            }
            return Mathf.Max(1, Mathf.RoundToInt(sec / 60f));
        }

        public static string NewId(string prefix) => prefix + DateTime.Now.Ticks.ToString("x") + UnityEngine.Random.Range(0, 1 << 20).ToString("x");
        public static long Now => DateTimeOffset.Now.ToUnixTimeMilliseconds();
    }

    // ---------- Arquivos: persistentDataPath/autorama/ ----------

    public static class ChampFiles
    {
        static string PilotsPath => Path.Combine(TrackLibrary.Root, "pilotos.json");
        static string StorePath => Path.Combine(TrackLibrary.Root, "campeonatos.json");
        static string RecordsPath => Path.Combine(TrackLibrary.Root, "recordes.json");

        static T Read<T>(string path) where T : new()
        {
            try { if (File.Exists(path)) return JsonUtility.FromJson<T>(File.ReadAllText(path)) ?? new T(); }
            catch (Exception e) { Debug.LogWarning($"Autorama: não deu para ler {path}: {e.Message}"); }
            return new T();
        }

        static bool Write(string path, object o)
        {
            try { Directory.CreateDirectory(TrackLibrary.Root); File.WriteAllText(path, JsonUtility.ToJson(o, true)); return true; }
            catch (Exception e) { Debug.LogWarning($"Autorama: não deu para salvar {path}: {e.Message}"); return false; }
        }

        public static List<Pilot> LoadPilots()
        {
            var list = Read<PilotList>(PilotsPath).pilots ?? new List<Pilot>();
            list.RemoveAll(p => p == null || string.IsNullOrEmpty(p.id));
            foreach (var p in list) Clean(p);
            return list;
        }
        public static bool SavePilots(List<Pilot> list) => Write(PilotsPath, new PilotList { pilots = list });

        public static ChampStore LoadStore()
        {
            var s = Read<ChampStore>(StorePath);
            s.draft ??= new ChampDraft();
            s.history ??= new List<HistoryEntry>();
            var a = s.active;
            if (s.hasActive && (a == null || a.pilots.Count < 2 || a.stages.Count == 0 || a.heats.Count != a.pilots.Count * a.stages.Count)) s.hasActive = false;
            if (s.hasActive) foreach (var p in a.pilots) Clean(p);
            return s;
        }
        public static bool SaveStore(ChampStore s) => Write(StorePath, s);

        public static RecordsFile LoadRecords() => Read<RecordsFile>(RecordsPath);
        public static bool SaveRecords(RecordsFile r) => Write(RecordsPath, r);

        static void Clean(Pilot p)
        {
            p.name = string.IsNullOrEmpty(p.name) ? "Piloto" : p.name.Length > 20 ? p.name.Substring(0, 20) : p.name;
            if (string.IsNullOrEmpty(Champ.TagOf(p.tag))) p.tag = Champ.TagOf(p.name);
            if (string.IsNullOrEmpty(p.tag)) p.tag = "P";
            if (!ColorUtility.TryParseHtmlString(p.color, out _)) p.color = Champ.Colors[0];
            p.skill = Mathf.Clamp(p.skill <= 0 ? 50 : p.skill, 1, 100);
        }
    }
}
