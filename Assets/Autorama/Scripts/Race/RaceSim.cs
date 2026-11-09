using System;
using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Ajustes da corrida (salvos entre sessões).
    [Serializable]
    public class RaceSettings
    {
        public int laps = 5;
        public float vmax = 14f;        // células por segundo com o acelerador no fundo
        public float ramp = 0.5f;       // segundos segurando até o acelerador chegar a 100%
        public float grip = 29f;        // força lateral que a curva plana aguenta
        public bool collide = true;
        public string track = "builder";
        public bool bot;                // corrida livre: o jogador 2 é um piloto IA
        public int botSkill = 60;       // qualidade dele, 1–100
        public int cameraMode = 0;      // 0 atual, 1 3D, 2 third person, 3 auto dois carros
        public int cameraTarget = 0;    // alvo do third person: 0 jogador 1, 1 jogador 2
        public int cameraDistance = 100; // escala do enquadramento: 70% mais perto, 160% mais longe
        public int halfGas = 60;        // tecla de meia aceleração (Q · O): até onde o acelerador vai, em %
        public bool bot34 = true;       // pista de 4 carros, corrida livre: os trilhos 3 e 4 são robôs (qualidade botSkill)
        public int cars4 = 4;           // pista de 4, corrida livre: quantos carrinhos (2, 3 ou 4); os trilhos de cima ficam vazios
        public bool byTime;             // corrida por tempo (seção 26): dura "secs" e se divide em uma manche por trilho
        public int secs = 120;          // duração total da corrida por tempo, em segundos

        const string Key = "autorama.corrida";

        public static RaceSettings Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(Key, "");
                if (json != "") return JsonUtility.FromJson<RaceSettings>(json) ?? new RaceSettings();
            }
            catch { }
            return new RaceSettings();
        }

        public void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }
    }

    public struct LanePoint
    {
        public Vector2 pos;
        public float heading;    // graus, anti-horário
        public float y;          // altura da superfície no trilho (mundo)
        public float k;          // curvatura: +1/raio na curva à direita, 0 na reta
        public float grip;       // multiplicador de aderência (1,7 na inclinada)
        public float slope;      // níveis por célula (rampa)
        public float z;          // nível de altura
        public int piece;
    }

    // Um trilho como polilinha com distância acumulada. Cada ponto sabe a curvatura, a aderência, a altura e a peça.
    // O carrinho troca de lado a cada cruzamento. Com um número ímpar de cruzamentos, ele só volta ao trilho de
    // partida depois de duas voltas: aí o caminho cobre duas voltas e passa duas vezes pela bandeirada (marks).
    public class LanePath
    {
        readonly List<Vector2> pts = new List<Vector2>();
        readonly List<float> ys = new List<float>(), ks = new List<float>(), gm = new List<float>(), zs = new List<float>(), gs = new List<float>(), cum = new List<float>();
        readonly List<int> pis = new List<int>();
        public readonly List<float> marks = new List<float>();     // distâncias da bandeirada dentro do caminho
        public float Total => cum.Count > 0 ? cum[cum.Count - 1] : 0f;

        // scale: unidades do mundo por célula (2 na pista de 4 carros). O caminho fica no mundo: posições, alturas,
        // distâncias e curvatura; a rampa continua em níveis por célula (o ângulo é o mesmo nas duas pistas).
        public LanePath(List<Segment> segs, float offset, float scale = 1f)
        {
            int crosses = 0;
            foreach (var s in segs) if (s.def.IsCross) crosses++;
            int laps = crosses % 2 == 1 ? 2 : 1;
            var lapStart = new List<int>();
            float off = offset;
            for (int lap = 0; lap < laps; lap++)
            {
                lapStart.Add(Mathf.Max(0, pts.Count - 1));
                for (int si = 0; si < segs.Count; si++)
                {
                    var s = segs[si];
                    var d = s.def;
                    float len = d.CenterLength;
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.08f));
                    float kap = d.IsCurve ? s.piece.dir / ((d.Radius - s.piece.dir * off) * scale) : 0f;
                    float slope = (s.zOut - s.zIn) / len;
                    for (int j = lap == 0 && si == 0 ? 0 : 1; j <= n; j++)
                    {
                        float t = j / (float)n;
                        float o = d.LaneOffsetAt(off, t);
                        var q = TrackMath.PoseAt(s.a, d, s.piece.dir, t);
                        pts.Add(TrackMath.Offset(q, o) * scale);
                        ys.Add(TrackLayout.SurfaceY(s, t, o) * scale);
                        ks.Add(kap); gm.Add(d.GripMultiplier); zs.Add(s.ZAt(t)); gs.Add(slope); pis.Add(si);
                    }
                    if (d.IsCross) off = -off;
                }
            }
            cum.Add(0f);
            for (int i = 1; i < pts.Count; i++) cum.Add(cum[i - 1] + Vector2.Distance(pts[i], pts[i - 1]));
            // A largada é reta: a bandeirada fica LineS depois do começo de cada volta.
            foreach (int i in lapStart) marks.Add(cum[i] + RaceSim.LineS * scale);
        }

        // Quantas vezes já passou pela bandeirada depois de andar d pelo caminho (0 no grid).
        public int LineCount(float d)
        {
            int cycles = Mathf.FloorToInt(d / Total);
            float r = d - cycles * Total;
            int n = cycles * marks.Count;
            foreach (float m in marks) if (m <= r) n++;
            return n;
        }

        // Fração da volta depois da última bandeirada (0 na linha, perto de 1 chegando nela de novo).
        public float LapFrac(float d)
        {
            float r = Mathf.Repeat(d, Total);
            int n = marks.Count;
            for (int i = n - 1; i >= -1; i--)
            {
                float m0 = i >= 0 ? marks[i] : marks[n - 1] - Total;          // última bandeirada antes de r
                if (m0 > r) continue;
                float m1 = i + 1 < n ? marks[i + 1] : marks[0] + Total;
                return Mathf.Clamp01((r - m0) / (m1 - m0));
            }
            return 0f;
        }

        public LanePoint At(float d)
        {
            d = Mathf.Repeat(d, Total);
            int lo = 0, hi = cum.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= d) lo = m; else hi = m; }
            float t = (d - cum[lo]) / Mathf.Max(cum[hi] - cum[lo], 1e-5f);
            Vector2 a = pts[lo], b = pts[hi], delta = b - a;
            return new LanePoint
            {
                pos = Vector2.Lerp(a, b, t),
                heading = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg,
                y = Mathf.Lerp(ys[lo], ys[hi], t),
                k = ks[hi], grip = gm[hi], slope = gs[hi], z = Mathf.Lerp(zs[lo], zs[hi], t), piece = pis[hi],
            };
        }
    }

    public enum CarMode { OnSlot, Off, Returning }

    public class CarState
    {
        public float s = RaceSim.GridS, v, hold, T, slip;
        public bool held;
        public float gas = 1f;   // até onde o acelerador vai enquanto está segurando (0–1): gatilho do controle ou tecla de meia
        public int side = 1;
        public CarMode mode;
        // fora da pista
        public Vector2 flyPos, flyVel;
        public float flyH, flyW, flyZ;
        // voltando ao trilho
        public Vector2 backPos;
        public float backH, backY, backT;
        // placar
        public float exitS, lapStart;
        public int cross = -1, laps, offs, hits;
        // corrida por tempo: trilho atual, ajuste da contagem de bandeiradas ao trocar de trilho e volta que pegou a troca
        public int lane, crossBase;
        public bool lapDirty;
        public float? lastLap, best, finish;
        // batidas
        public bool contact;
        public float hitFx;
        public Vector2? push;
        // piloto IA
        public float aiM = -1f, aiT, offT;
    }

    // A física do modo corrida, igual à do construtor em HTML (seção 15 do guia). Sem nada de cena: só números.
    public class RaceSim
    {
        public const float GridS = 0.75f;      // carrinho parado no grid (distância desde o início da largada)
        public const float LineS = 1.325f;     // bandeirada
        public const float Pin = 0.22f;        // guia na frente: a traseira escorrega em volta dela
        public const float Count = 2.4f;       // três luzes vermelhas
        const float Accel = 3.5f, Brake = 1.8f, Brake0 = 0.5f, SlopeG = 8f, Fall = 3f;
        const float SlipGain = 6f, SlipDecay = 1.5f, YawMax = 38f;
        const float CarHL = 0.3f, CarHW = 0.16f, Restitution = 0.35f;
        const float RubKick = 0.15f, RubKickV = 0.03f, RubSlip = 8f, RubDrag = 1.2f, HitSlip = 0.12f, HitSlipSide = 0.14f, HitSpin = 60f;

        public enum Phase { Idle, Countdown, Running, Done, Switch }     // Switch: troca de trilho entre manches (corrida por tempo)
        public Phase phase { get; private set; }
        public float t { get; private set; }
        public int winner { get; private set; } = -1;
        public bool closed { get; private set; }
        public RaceSettings set;                // na corrida livre, os ajustes salvos; no campeonato, as regras dele
        public float? cutoff;                   // campeonato: a bateria acaba esse tanto depois da bandeirada do primeiro
        public float? bestT;                    // melhor volta da bateria até agora
        public const int MaxCars = 4;
        public readonly int[] aiSkill = new int[MaxCars];   // 0: gente; 1–100: piloto IA com essa qualidade
        public readonly LanePath[] paths = new LanePath[MaxCars];
        public CarState[] cars { get; private set; } = { new CarState(), new CarState() };
        public readonly bool[] present = { true, true, true, true };   // trilho vazio na bateria (campeonato por tempo com menos pilotos que trilhos)

        // ---------- Corrida por tempo (seção 26) ----------
        // A prova dura set.secs e tem uma manche por trilho (N). No fim de cada manche os carrinhos param, cada um passa
        // para o trilho seguinte (1→2, …, último→1) no mesmo ponto da volta, e uma nova contagem larga a manche.
        // Vence quem andou mais: Dist(k) = bandeiradas + fração da volta.
        public const float SwitchTime = 2.5f;   // pausa mostrando a troca de trilho
        public bool ByTime => set != null && set.byTime;
        public int Manche { get; private set; }               // 0 … N−1
        public float MancheLen => set.secs / (float)N;
        public float resumeT;                                    // tempo de prova guardado durante a pausa e a contagem da manche
        float switchT;
        public LanePath PathOf(int k) => paths[cars[k].lane];
        public float Dist(int k) { var c = cars[k]; return c.cross + PathOf(k).LapFrac(c.s); }
        public float TimeLeft => ByTime ? Mathf.Max(0f, set.secs - RaceTime) : 0f;
        public float RaceTime => phase == Phase.Countdown || phase == Phase.Switch ? resumeT : t;
        public int N => cars.Length;           // carrinhos na pista: 2 ou 4 (um por trilho)
        public float Scale { get; private set; } = 1f;   // unidades do mundo por célula (2 na pista de 4)

        public event Action<Vector3> Spark;     // posição da batida no mundo
        public event Action ScoreChanged;
        public event Action<int, float> LapDone;      // carro, tempo da volta (antes de atualizar best e bestT)

        public RaceSim(RaceSettings settings) { set = settings; }

        public bool Ready => paths[0] != null && paths[0].Total > 0f;

        // faixas 4: quatro carrinhos, um por trilho, com o caminho no mundo (escala 2). Seção 25 do guia.
        public void SetTrack(List<Segment> segs, bool isClosed, int faixas = 2)
        {
            var lanes = TrackMath.LanesOf(faixas);
            Scale = TrackMath.Scale(faixas);
            if (cars.Length != lanes.Length)
            {
                cars = new CarState[lanes.Length];
                for (int k = 0; k < cars.Length; k++) cars[k] = new CarState();
            }
            for (int k = 0; k < MaxCars; k++) paths[k] = k < lanes.Length ? new LanePath(segs, lanes[k], Scale) : null;
            closed = isClosed;
            Reset();
        }

        public float GridPos => GridS * Scale;     // onde o carrinho para no grid, no mundo

        public void Reset()
        {
            phase = Phase.Idle; t = 0f; winner = -1; bestT = null; Manche = 0; resumeT = 0f; switchT = 0f;
            for (int k = 0; k < N; k++) { bool held = cars[k].held; cars[k] = new CarState { held = held, s = GridPos, lane = k }; }
            System.Array.Clear(pairContact, 0, pairContact.Length);
            ScoreChanged?.Invoke();
        }

        // Campeonato online: o visitante copia do host a fase, o relógio e quem ganhou (a física dos carrinhos vem em NetSync).
        public void Mirror(Phase p, float time, int win, int manche = 0, float resume = 0f) { phase = p; t = time; winner = win; Manche = manche; resumeT = resume; }

        public void StartRace()
        {
            if (!closed || !Ready) return;
            Reset();
            phase = Phase.Countdown;
        }

        // ---------- Passo de simulação ----------

        public void Step(float dt)
        {
            if (Ready && phase != Phase.Idle) for (int k = 0; k < N; k++) if (aiSkill[k] > 0 && present[k]) AiDrive(k, dt);
            bool stopped = ByTime && (phase == Phase.Done || phase == Phase.Switch);     // fim da manche: ninguém acelera
            // Acelerador: sobe enquanto segura, zera ao soltar.
            foreach (var c in cars)
            {
                // O acelerador sobe até o nível pedido no mesmo ritmo de sempre (100% em "ramp" segundos) e desce na hora.
                // Com o nível em 1 (teclado, IA) é exatamente o T = hold / ramp de antes: a física e os recordes não mudam.
                if (c.held && c.finish == null && !stopped)
                {
                    c.hold += dt;
                    float g = Mathf.Clamp01(c.gas);
                    c.T = c.T > g ? g : Mathf.Min(g, Mathf.Min(1f, c.hold / set.ramp));
                }
                else { c.hold = 0f; c.T = 0f; }
                c.hitFx = Mathf.Max(0f, c.hitFx - dt);
            }
            if (!Ready) return;
            if (phase == Phase.Countdown)
            {
                t += dt;
                if (t >= Count) { phase = Phase.Running; t = resumeT; }     // 1ª manche: 0; as outras continuam o relógio
                return;
            }
            if (phase == Phase.Idle) return;
            if (phase == Phase.Switch)
            {
                if ((switchT += dt) >= SwitchTime) RotateLanes();
                return;
            }
            if (phase == Phase.Running) t += dt;
            if (phase == Phase.Running && ByTime && t >= (Manche + 1) * MancheLen) { EndManche(); return; }
            // Campeonato: quem ainda não chegou tem um tempo limite depois da bandeirada do primeiro.
            if (phase == Phase.Running && cutoff.HasValue && winner >= 0 && t - cars[winner].finish.Value >= cutoff.Value) phase = Phase.Done;

            for (int k = 0; k < N; k++)
            {
                if (!present[k]) continue;
                var c = cars[k];
                var P = PathOf(k);
                if (c.mode == CarMode.OnSlot)
                {
                    // Motor: busca T × velocidade máxima; soltou, freia. Rampa: subir freia, descer embala.
                    var here = P.At(c.s);
                    float a = (c.T == 0f ? -(Brake * c.v + Brake0) : Accel * (c.T * set.vmax - c.v)) - SlopeG * here.slope;
                    c.v = Mathf.Max(0f, c.v + a * dt);
                    c.s += c.v * dt;
                    // Força lateral v²/r contra a aderência (maior na inclinada). O excesso enche a derrapagem.
                    var q = P.At(c.s);
                    if (q.k != 0f) c.side = q.k > 0f ? 1 : -1;
                    float ratio = c.v * c.v * Mathf.Abs(q.k) / (set.grip * q.grip);
                    c.slip = ratio > 1f ? c.slip + (ratio - 1f) * SlipGain * dt : Mathf.Max(0f, c.slip - SlipDecay * dt);
                    if (c.slip >= 1f) Deslot(k, null);
                    else CountLap(k);
                }
                else if (c.mode == CarMode.Off)
                {
                    // Fora do trilho: desliza, gira e cai até o chão.
                    float damp = Mathf.Exp(-2.4f * dt);
                    c.flyVel *= damp;
                    c.flyW *= Mathf.Exp(-2.8f * dt);
                    c.flyPos += c.flyVel * dt;
                    c.flyH += c.flyW * dt;
                    c.flyZ = Mathf.Max(0f, c.flyZ - Fall * dt);
                }
                else
                {
                    c.backT += dt / 0.6f;
                    if (c.backT >= 1f) { c.mode = CarMode.OnSlot; c.v = 0f; c.slip = 0f; }
                }
            }
            if (set.collide) Collide(dt);
        }

        // Fim da manche: na última, a prova acaba e vence quem andou mais; nas outras, pausa para trocar de trilho.
        void EndManche()
        {
            t = (Manche + 1) * MancheLen;
            if (Manche >= N - 1)
            {
                float best = float.NegativeInfinity;
                for (int k = 0; k < N; k++) if (present[k] && Dist(k) > best) { best = Dist(k); winner = k; }
                phase = Phase.Done;
            }
            else { phase = Phase.Switch; switchT = 0f; resumeT = t; }
            for (int k = 0; k < N; k++) { cars[k].held = false; cars[k].T = 0f; cars[k].hold = 0f; }
            ScoreChanged?.Invoke();
        }

        // Cada carrinho vai para o trilho seguinte, no mesmo ponto da volta (fração do caminho), parado e no trilho.
        // A contagem de bandeiradas continua de onde estava (crossBase) e a volta em andamento não vale como tempo.
        void RotateLanes()
        {
            for (int k = 0; k < N; k++)
            {
                var c = cars[k];
                var from = PathOf(k);
                float s0 = c.mode == CarMode.OnSlot ? c.s : c.exitS;      // quem estava fora volta de onde saiu
                float f = Mathf.Repeat(s0, from.Total) / from.Total;
                c.lane = (c.lane + 1) % N;
                var to = PathOf(k);
                c.s = f * to.Total;
                c.crossBase = c.cross - (to.LineCount(c.s) - 1);
                c.v = 0f; c.slip = 0f; c.mode = CarMode.OnSlot; c.backT = 0f;
                c.lapDirty = c.cross >= 0;
            }
            Manche++;
            phase = Phase.Countdown; t = 0f;
            ScoreChanged?.Invoke();
        }

        void Deslot(int k, Vector2? push)
        {
            var c = cars[k];
            var P = PathOf(k);
            var body = SlotPose(k, out _);
            var q = P.At(c.s);
            Vector2 tangent = TrackMath.Dir(q.heading);
            Vector2 outward = TrackMath.Dir(q.heading + c.side * 90f);      // para fora da curva
            c.flyPos = body.pos;
            c.flyH = body.heading;
            c.flyZ = q.z;
            c.flyVel = (tangent + outward * 0.35f) * c.v;
            if (push.HasValue) c.flyVel += push.Value;                       // empurrão de uma batida
            c.flyW = -c.side * (220f + c.v * 30f);                           // gira no sentido da curva
            c.mode = CarMode.Off;
            c.exitS = c.s; c.v = 0f; c.slip = 0f; c.offs++;
            ScoreChanged?.Invoke();
        }

        // Recolocar: volta ao trilho um pouco antes de onde saiu, parado.
        public void Reslot(int k)
        {
            var c = cars[k];
            if (c.mode != CarMode.Off) return;
            c.backPos = c.flyPos; c.backH = c.flyH; c.backY = c.flyZ * TrackMath.LevelHeight * Scale;
            c.s = Mathf.Max(c.crossBase == 0 && c.lane == k ? GridPos : 0f, c.exitS - 0.4f * Scale);
            c.backT = 0f;
            c.mode = CarMode.Returning;
        }

        void CountLap(int k)
        {
            var c = cars[k];
            int cr = PathOf(k).LineCount(c.s) - 1 + c.crossBase;      // -1 no grid; 0 ao largar; 1 ao completar a primeira volta
            if (cr <= c.cross) return;
            c.cross = cr;
            if (cr < 1 || c.finish != null || phase != Phase.Running) return;
            float lap = t - c.lapStart;
            c.lapStart = t; c.laps = cr;
            // A volta que pegou a troca de trilho é metade num trilho e metade no outro: conta, mas não vale como tempo.
            if (c.lapDirty) { c.lapDirty = false; ScoreChanged?.Invoke(); return; }
            c.lastLap = lap;
            LapDone?.Invoke(k, lap);
            if (c.best == null || lap < c.best) c.best = lap;
            if (bestT == null || lap < bestT) bestT = lap;
            if (ByTime) { ScoreChanged?.Invoke(); return; }      // por tempo não há chegada: acaba no relógio
            if (cr >= set.laps)
            {
                c.finish = t;
                if (winner < 0) winner = k;
                bool all = true;
                for (int i = 0; i < N; i++) if (present[i] && cars[i].finish == null) all = false;
                if (all) phase = Phase.Done;
            }
            ScoreChanged?.Invoke();
        }

        // ---------- Pose dos carrinhos ----------

        // No trilho, o carrinho gira em volta da guia: a traseira sai para fora da curva conforme derrapa.
        Pose2 SlotPose(int k, out float y)
        {
            var c = cars[k];
            var q = PathOf(k).At(c.s);
            float hh = q.heading - c.side * Mathf.Pow(c.slip, 1.4f) * YawMax;
            Vector2 guide = q.pos + TrackMath.Dir(q.heading) * Pin;
            y = q.y;
            return new Pose2(guide - TrackMath.Dir(hh) * Pin, hh);
        }

        // Pose para desenhar: posição na mesa, altura do mundo e inclinação (graus) na rampa.
        public Pose2 CarPose(int k, out float y, out float pitch)
        {
            var c = cars[k];
            pitch = 0f;
            if (c.mode == CarMode.Off) { y = c.flyZ * TrackMath.LevelHeight * Scale; return new Pose2(c.flyPos, c.flyH); }
            var to = SlotPose(k, out y);
            var P = PathOf(k);
            float y0 = P.At(c.s - 0.15f).y, y1 = P.At(c.s + 0.15f).y;
            pitch = Mathf.Atan2(y1 - y0, 0.3f) * Mathf.Rad2Deg;
            if (c.mode == CarMode.Returning)
            {
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(c.backT));
                y = Mathf.Lerp(c.backY, y, e);
                pitch *= e;
                return new Pose2(Vector2.Lerp(c.backPos, to.pos, e), Mathf.LerpAngle(c.backH, to.heading, e));
            }
            return to;
        }

        public float CarZ(int k)
        {
            var c = cars[k];
            return c.mode == CarMode.Off ? c.flyZ : PathOf(k).At(c.s).z;
        }

        public int CarPiece(int k) => PathOf(k).At(cars[k].s).piece;

        Vector2 WorldVelocity(int k)
        {
            var c = cars[k];
            if (c.mode == CarMode.Off) return c.flyVel;
            if (c.mode != CarMode.OnSlot) return Vector2.zero;
            return TrackMath.Dir(PathOf(k).At(c.s).heading) * c.v;
        }

        // ---------- Colisão entre os carrinhos ----------

        // Cada par de carrinhos é testado (1 par na pista de 2, 6 na de 4). "contact" de cada carrinho fica ligado
        // se ele encosta em algum outro; a primeira batida de cada par solta faísca e conta.
        readonly bool[,] pairContact = new bool[MaxCars, MaxCars];

        void Collide(float dt)
        {
            for (int i = 0; i < N; i++) cars[i].contact = false;
            for (int i = 0; i < N; i++)
                for (int j = i + 1; j < N; j++)
                    pairContact[i, j] = CollidePair(i, j, dt, pairContact[i, j]);
            for (int k = 0; k < N; k++)
            {
                var c = cars[k];
                if (c.mode == CarMode.OnSlot && c.slip >= 1f) Deslot(k, c.push);
                c.push = null;
            }
        }

        // Devolve se o par está encostado agora.
        bool CollidePair(int ia, int ib, float dt, bool wasTouching)
        {
            CarState A = cars[ia], B = cars[ib];
            if (!present[ia] || !present[ib] || A.mode == CarMode.Returning || B.mode == CarMode.Returning || Mathf.Abs(CarZ(ia) - CarZ(ib)) > 0.6f)
                return false;                           // na mão do fiscal, ou um na ponte e outro embaixo
            var pa = CarPose(ia, out float ya, out _);
            var pb = CarPose(ib, out _, out _);
            if (!Overlap(pa, pb, out Vector2 n, out float depth)) return false;

            bool first = !wasTouching;
            A.contact = B.contact = true;
            float vRel = Vector2.Dot(WorldVelocity(ib) - WorldVelocity(ia), n);     // < 0: aproximando
            if (first)
            {
                var mid = (pa.pos + pb.pos) / 2f;
                Spark?.Invoke(new Vector3(mid.x, ya + 0.08f, mid.y));
                A.hitFx = B.hitFx = 0.6f; A.hits++; B.hits++;
                ScoreChanged?.Invoke();
            }
            if (A.mode == CarMode.OnSlot && B.mode == CarMode.OnSlot)
            {
                // Raspando lado a lado: os dois ganham derrapagem e perdem velocidade enquanto encostam.
                if (first) { float kick = RubKick + RubKickV * Mathf.Max(A.v, B.v); A.slip += kick; B.slip += kick; }
                A.slip += depth * RubSlip * dt; B.slip += depth * RubSlip * dt;
                float drag = Mathf.Exp(-RubDrag * dt); A.v *= drag; B.v *= drag;
            }
            else
            {
                // Pelo menos um solto: choque com troca de impulso (massas iguais).
                if (vRel < 0f)
                {
                    float j = -(1f + Restitution) * vRel / 2f;
                    Vector2 half = (pb.pos - pa.pos) / 2f;
                    Impulse(ia, -j * n, half);
                    Impulse(ib, j * n, -half);
                }
                float share = A.mode == CarMode.Off && B.mode == CarMode.Off ? 0.5f : 1f;
                if (A.mode == CarMode.Off) A.flyPos -= n * depth * share;
                if (B.mode == CarMode.Off) B.flyPos += n * depth * share;
            }
            return true;
        }

        // r: do centro do carrinho até o ponto de contato (define para que lado ele gira).
        void Impulse(int k, Vector2 imp, Vector2 r)
        {
            var c = cars[k];
            if (c.mode == CarMode.Off)
            {
                c.flyVel += imp;
                c.flyW += Mathf.Sign(r.x * imp.y - r.y * imp.x) * imp.magnitude * HitSpin;
            }
            else if (c.mode == CarMode.OnSlot)
            {
                // O trilho segura: só a parte do empurrão na direção da pista vira velocidade; a de lado vira derrapagem.
                Vector2 tan = TrackMath.Dir(PathOf(k).At(c.s).heading);
                float along = Vector2.Dot(imp, tan);
                float across = Mathf.Abs(imp.x * -tan.y + imp.y * tan.x);
                c.v = Mathf.Max(0f, c.v + along);
                c.slip += HitSlip + across * HitSlipSide;
                c.push = imp;
            }
        }

        // Teste de eixos separados entre as duas carrocerias (retângulos girados).
        static bool Overlap(Pose2 a, Pose2 b, out Vector2 normal, out float depth)
        {
            Vector2[] axes = { TrackMath.Dir(a.heading), TrackMath.Dir(a.heading + 90f), TrackMath.Dir(b.heading), TrackMath.Dir(b.heading + 90f) };
            Vector2 d = b.pos - a.pos;
            normal = Vector2.zero; depth = float.MaxValue;
            foreach (var ax in axes)
            {
                float o = Radius(a, ax) + Radius(b, ax) - Mathf.Abs(Vector2.Dot(d, ax));
                if (o <= 0f) return false;
                if (o < depth) { depth = o; normal = Vector2.Dot(d, ax) < 0f ? -ax : ax; }
            }
            return true;
        }

        static float Radius(Pose2 p, Vector2 ax) =>
            CarHL * Mathf.Abs(Vector2.Dot(TrackMath.Dir(p.heading), ax)) + CarHW * Mathf.Abs(Vector2.Dot(TrackMath.Dir(p.heading + 90f), ax));

        // ---------- Piloto IA ----------
        // Acelera enquanto estiver abaixo da velocidade com que ainda freia a tempo para a próxima curva.
        // A coragem m é a fração do limite da curva que ele aceita: de 0,82 (qualidade 1) a 1,15 (qualidade 100).
        // A cada 1,2 s sorteia de novo, com uma variação que diminui com a qualidade. Às vezes erra: entra a 130–140%
        // do limite e sai da pista; a chance vai de 12,5% por sorteio (qualidade 1) a 0,5% (qualidade 100).
        // Fora da pista, aperta Recolocar depois de um tempo de reação, menor nos melhores.
        const float AiLook = 3.5f, AiBrake = 20f, AiEvery = 1.2f;
        static float AiBase(float sk) => 0.82f + 0.33f * sk;
        static float AiSpread(float sk) => 0.02f + 0.1f * (1f - sk);
        static float AiMiss(float sk) => 0.005f + 0.12f * Mathf.Pow(1f - sk, 1.5f);
        static float AiReslotDelay(float sk) => 1.4f - 0.9f * sk;

        void AiDrive(int k, float dt)
        {
            var c = cars[k];
            float sk = aiSkill[k] / 100f;
            if (phase == Phase.Countdown) { c.held = true; return; }        // segura na luz vermelha, como gente
            if ((c.aiT -= dt) <= 0f || c.aiM < 0f)
            {
                c.aiM = UnityEngine.Random.value < AiMiss(sk) ? 1.3f + 0.1f * UnityEngine.Random.value
                                                               : AiBase(sk) + (UnityEngine.Random.value * 2f - 1f) * AiSpread(sk);
                c.aiT = AiEvery;
            }
            if (c.mode == CarMode.Off) { c.held = false; if ((c.offT += dt) >= AiReslotDelay(sk)) { c.offT = 0f; Reslot(k); } return; }
            if (c.mode != CarMode.OnSlot || c.finish != null) { c.held = false; return; }
            var P = PathOf(k);
            // Pista de 4 (Scale 2): as curvas são 2× mais longas e a derrapagem passa o dobro do tempo enchendo.
            // A parte da coragem acima do limite (m² − 1, que é o que enche a barra) é dividida pela escala.
            // Na pista de 2 (Scale 1) não muda nada.
            float m = c.aiM;
            if (m > 1f && Scale > 1f && m < 1.3f) m = Mathf.Sqrt(1f + (m * m - 1f) / Scale);
            float need = float.PositiveInfinity;
            for (float d = 0f; d <= AiLook; d += 0.1f)
            {
                var q = P.At(c.s + d);
                if (q.k == 0f) continue;
                float v = Mathf.Sqrt(set.grip * q.grip / Mathf.Abs(q.k)) * m;
                need = Mathf.Min(need, Mathf.Sqrt(v * v + 2f * AiBrake * d));
            }
            c.held = c.v < need;
        }

        // Melhor volta da bateria só entre gente: piloto IA não bate recorde da pista.
        public float HumanBest()
        {
            float b = float.PositiveInfinity;
            for (int k = 0; k < N; k++) if (present[k] && aiSkill[k] == 0 && cars[k].best.HasValue) b = Mathf.Min(b, cars[k].best.Value);
            return b;
        }

        // Velocidade limite de uma curva para o ajuste de aderência (para o texto de ajuda).
        public float CornerLimit(float radius, float gripMultiplier) => Mathf.Sqrt(set.grip * gripMultiplier * radius);
    }
}
