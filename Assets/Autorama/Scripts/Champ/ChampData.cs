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
    // Campeonato de 4 carros (seção 25): só pistas de 4, pelo menos 4 pilotos, e na bateria k correm k, k+1, k+2 e k+3
    // nos trilhos 1 a 4. De novo, cada piloto corre uma vez em cada trilho. Os dois tipos de pista nunca se misturam.
    // Os arquivos usam listas (o JsonUtility não lê dicionário); o HTML guarda os mesmos dados em mapas.

    [Serializable]
    public class Pilot
    {
        public string id, name, tag, color;      // color: "#RRGGBB"
        public bool ai;                          // o computador dirige
        public int skill = 50;                   // qualidade do piloto IA, 1–100
        public string uid;                       // online: o id fixo do jogador (jogador.json ou "steam:…"); vazio no offline
        public CarSetup carro;                   // o ajuste do carro (seção 47); no campeonato, o da etapa em andamento (seção 48)
        public Color Color => ColorUtility.TryParseHtmlString(color, out var c) ? c : Color.red;
        public Pilot Copy() => (Pilot)MemberwiseClone();
    }

    [Serializable] public class Stage { public string name; public int laps = 5; public int secs = 120; public string hash; public TrackSave data; }   // secs: campeonato por tempo

    [Serializable]
    public class HeatResult
    {
        public int laps, offs, hits;
        public float time, best = -1f;           // best −1: nenhuma volta completa
        public bool dnf, rec, raceRec, pb;       // não terminou; bateu recorde de volta, de corrida ou o melhor pessoal
        public float dist;                       // corrida por tempo: voltas andadas com a fração da última
        public float fin = -1f;                  // a chegada (−1: não chegou); é com ela que se compara o recorde de corrida
    }

    [Serializable]
    public class Heat
    {
        public int s;                            // etapa
        public int[] p = new int[2];             // pilotos: [trilho 1, trilho 2] (ou os 4 trilhos); −1 = trilho vazio (por tempo)
        public HeatResult[] res;                 // vazio até salvar (o JsonUtility grava null como [])
        public bool Saved => res != null && res.Length == p.Length && res.Length > 0;
    }

    [Serializable] public class ChampRules { public float vmax, ramp, grip; public bool collide; public bool turbo = true; public float reslot = 0.3f; }   // turbo: seção 37 (campeonato antigo, sem o campo: ligado)

    [Serializable]
    public class Championship
    {
        public string id, name;
        public int faixas = 2;                   // 2 ou 4 carros por bateria: todas as etapas são pistas desse tipo
        public bool byTime;                      // corrida por tempo em todas as etapas (seção 26)
        public long created, finished;
        public int Faixas => TrackMath.Faixas(faixas);
        public ChampRules rules = new ChampRules();
        public List<Pilot> pilots = new List<Pilot>();
        public List<Stage> stages = new List<Stage>();
        public List<Heat> heats = new List<Heat>();
        public int carStage = -1;                // a última etapa com o ajuste do carro já escolhido (seção 48)
    }

    [Serializable] public class ChampDraft { public string name = ""; public int faixas = 2; public bool byTime; public List<string> pilots = new List<string>(); public List<Stage> stages = new List<Stage>(); }
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

    // online: feito num campeonato online (o tempo veio do host, que pode ter trapaceado) · host: o nome do host
    [Serializable] public class Rec { public float t = -1f; public Pilot pilot; public long date; public string champ; public int lane; public bool online; public string host; public bool Has => t >= 0f; }
    [Serializable] public class RaceRec { public int laps; public Rec rec = new Rec(); }
    [Serializable] public class PilotBest { public string pilot; public float lap = -1f; public List<RaceRec> race = new List<RaceRec>(); }

    [Serializable]
    public class TrackRecords
    {
        public string hash, name;
        public Rec lap = new Rec();
        public Rec lane1 = new Rec(), lane2 = new Rec(), lane3 = new Rec(), lane4 = new Rec(), lane5 = new Rec(), lane6 = new Rec(), lane7 = new Rec(), lane8 = new Rec();   // 3 a 6: só nas pistas de 4 e 6
        public List<RaceRec> race = new List<RaceRec>();
        public List<PilotBest> pb = new List<PilotBest>();

        public Rec Lane(int k) => k == 0 ? lane1 : k == 1 ? lane2 : k == 2 ? (lane3 ??= new Rec()) : k == 3 ? (lane4 ??= new Rec()) : k == 4 ? (lane5 ??= new Rec()) : k == 5 ? (lane6 ??= new Rec()) : k == 6 ? (lane7 ??= new Rec()) : (lane8 ??= new Rec());
        public void SetLane(int k, Rec r) { if (k == 0) lane1 = r; else if (k == 1) lane2 = r; else if (k == 2) lane3 = r; else if (k == 3) lane4 = r; else if (k == 4) lane5 = r; else if (k == 5) lane6 = r; else if (k == 6) lane7 = r; else lane8 = r; }
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

    // jogador.json: o id fixo deste computador no online (o melhor pessoal online fica preso a ele).
    [Serializable] public class PlayerFile { public string uid; }

    // ---------- Regras e contas ----------

    public class StageRow { public int i, laps, runs, offs, hits, pts = -1; public float time, best = -1f, dist; public bool fast; }
    public class StandRow { public int i, pts, wins, podiums, fast; public float time; }

    public static class Champ
    {
        public static readonly int[] Points = { 10, 8, 6, 5, 4, 3, 2, 1 };   // por posição na etapa; +1 para a melhor volta
        public const float Cutoff = 30f;          // segundos depois da bandeirada do primeiro
        public const int MaxPilots = 8;
        public static readonly string[] Colors = { "#E0362C", "#2563EB", "#F2B705", "#16A34A", "#9333EA", "#F97316", "#0EA5B7", "#E04B95" };
        public static readonly string[] ColorNames = { "Vermelho", "Azul", "Amarelo", "Verde", "Roxo", "Laranja", "Ciano", "Rosa" };

        // faixas: carrinhos por bateria (2 ou 4). Precisa de pelo menos esse tanto de pilotos (MinPilots).
        // byTime: uma bateria já passa por todos os trilhos (manches), então cada piloto corre uma vez por etapa.
        // Com mais pilotos que trilhos, eles se dividem em grupos equilibrados na ordem do cadastro (5 em 4 trilhos:
        // 3 + 2; 6 em 2: 2 + 2 + 2). Os trilhos que sobram ficam vazios (−1).
        // seed: sorteio da largada (seção 45). Com ele, em cada etapa a ordem dos pilotos no rodízio, a ordem das baterias e
        // (por tempo) os grupos e os trilhos são sorteados; o rodízio continua pondo cada piloto uma vez em cada trilho.
        // O mesmo seed dá as mesmas baterias: no online todos montam igual. Sem seed, a ordem do cadastro.
        public static List<Heat> MakeHeats(int nPilots, int nStages, int faixas = 2, bool byTime = false, int? seed = null)
        {
            int L = TrackMath.Faixas(faixas);
            var list = new List<Heat>();
            var rng = seed.HasValue ? new System.Random(seed.Value) : null;
            if (byTime)
            {
                int g = Mathf.Max(1, Mathf.CeilToInt(nPilots / (float)L));
                for (int s = 0; s < nStages; s++)
                {
                    int next = 0;
                    var order = Shuffled(nPilots, rng);
                    for (int gi = 0; gi < g; gi++)
                    {
                        int size = nPilots / g + (gi < nPilots % g ? 1 : 0);
                        var p = new int[L];
                        for (int l = 0; l < L; l++) p[l] = l < size ? order[next + l] : -1;
                        next += size;
                        if (rng != null) Shuffle(p, rng);
                        list.Add(new Heat { s = s, p = p });
                    }
                }
                return list;
            }
            // Por voltas: H = max(pilotos, trilhos) baterias por etapa. Na bateria k, o trilho l recebe o piloto (k + l) mod H;
            // se esse número passa do total de pilotos, o trilho fica vazio. Cada piloto corre uma vez em cada trilho.
            // Com pilotos ≥ trilhos é o rodízio de sempre; com 3 pilotos na pista de 4 são 4 baterias de 3 carros.
            int H = Mathf.Max(nPilots, L);
            for (int s = 0; s < nStages; s++)
            {
                var slot = Shuffled(H, rng);                 // posição no rodízio → piloto (≥ nPilots: trilho vazio)
                var ks = Shuffled(H, rng);                   // ordem das baterias
                foreach (int k in ks)
                {
                    var p = new int[L];
                    for (int l = 0; l < L; l++) { int i = slot[(k + l) % H]; p[l] = i < nPilots ? i : -1; }
                    list.Add(new Heat { s = s, p = p });
                }
            }
            return list;
        }

        static int[] Shuffled(int n, System.Random rng)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            if (rng != null) Shuffle(a, rng);
            return a;
        }

        static void Shuffle(int[] a, System.Random rng)
        {
            for (int i = a.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        }

        public static int MinPilots(int faixas, bool byTime = false) => 2;      // trilho vazio resolve a pista de 4 com 2 ou 3
        public static int HeatsPerStage(int nPilots, int faixas, bool byTime) => byTime ? Mathf.Max(1, Mathf.CeilToInt(nPilots / (float)TrackMath.Faixas(faixas))) : Mathf.Max(nPilots, TrackMath.Faixas(faixas));

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
                for (int k = 0; k < h.p.Length; k++)
                {
                    if (h.p[k] < 0) continue;                // trilho vazio
                    var r = h.res[k]; var row = rows[h.p[k]];
                    row.laps += r.laps; row.time += r.time; row.runs++; row.offs += r.offs; row.hits += r.hits; row.dist += r.dist;
                    if (r.best >= 0f && (row.best < 0f || r.best < row.best)) row.best = r.best;
                }
            }
            rows.RemoveAll(r => r.runs == 0);
            // Por voltas: mais voltas, depois menos tempo. Por tempo: mais distância (voltas com a fração).
            if (c.byTime) rows.Sort((a, b) => b.dist.CompareTo(a.dist));
            else rows.Sort((a, b) => a.laps != b.laps ? b.laps.CompareTo(a.laps) : a.time.CompareTo(b.time));
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
        // A pista de 4 carros começa com "f4|": a mesma forma em 2 e em 4 faixas são pistas diferentes, e as de 2 não mudam.
        public static string TrackHash(TrackSave t)
        {
            var sb = new StringBuilder((t.Faixas >= 4 ? $"f{t.Faixas}|" : "") + $"largada:0:{t.StartZ}");
            foreach (var p in t.pecas)
            {
                var def = PieceCatalog.ById(p.tipo);
                int dir = def != null && def.Sided ? (p.sentido == "esquerda" ? -1 : 1) : 0;
                int rise = p.altura == "sobe" ? 1 : p.altura == "desce" ? -1 : 0;
                sb.Append('|').Append(p.tipo).Append(':').Append(dir).Append(':').Append(rise);
                if (def != null && def.IsJump && p.vao > 1) sb.Append(':').Append(p.vao);     // vão 1 (o antigo): identidade igual
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
        public static int EstimateMin(List<Stage> stages, int nPilots, float vmax, int faixas = 2, bool byTime = false)
        {
            float sec = 0f;
            int L = TrackMath.Faixas(faixas);
            foreach (var s in stages)
            {
                if (byTime) { sec += HeatsPerStage(nPilots, faixas, true) * (s.secs + L * (RaceSim.Count + 1f) + (L - 1) * RaceSim.SwitchTime + 10f); continue; }
                float len = 0f;
                try { len = TrackLayout.Analyze(s.data.Origin, TrackLayout.Build(s.data.Origin, s.data.Pieces(), s.data.Tail, s.data.StartZ), s.data.Tail).centerLength * TrackMath.Scale(s.data.Faixas); } catch { }
                sec += HeatsPerStage(nPilots, faixas, false) * (s.laps * len / (0.6f * vmax * TrackMath.Scale(faixas)) + 10f);
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
            if (s.hasActive && (a == null || a.pilots.Count < Champ.MinPilots(a.faixas, a.byTime) || a.stages.Count == 0
                || a.heats.Count != Champ.HeatsPerStage(a.pilots.Count, a.faixas, a.byTime) * a.stages.Count)) s.hasActive = false;
            if (s.hasActive) { a.faixas = a.Faixas; foreach (var h in a.heats) if (h.p == null || h.p.Length != a.faixas) { s.hasActive = false; break; } }
            s.draft.faixas = TrackMath.Faixas(s.draft.faixas);
            if (s.hasActive) foreach (var p in a.pilots) Clean(p);
            return s;
        }
        public static bool SaveStore(ChampStore s) => Write(StorePath, s);

        public static RecordsFile LoadRecords() => Read<RecordsFile>(RecordsPath);

        // O id fixo do jogador no online: criado uma vez e guardado em jogador.json (32 letras e números).
        static string uid;
        public static string PlayerUid()
        {
            if (!string.IsNullOrEmpty(uid)) return uid;
            string path = Path.Combine(TrackLibrary.Root, "jogador.json");
            var f = Read<PlayerFile>(path);
            if (string.IsNullOrEmpty(f.uid) || f.uid.Length < 16) { f.uid = Guid.NewGuid().ToString("N"); Write(path, f); }
            return uid = f.uid;
        }
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
