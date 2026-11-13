using System;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // Passa o estado de um carrinho do RaceSim para o snapshot e de volta (campeonato online, seção 23 do guia).
    // O host manda o estado de verdade; o visitante também roda o RaceSim (predição) e se corrige com ele.
    public static class NetSync
    {
        static float Opt(float? v) => v ?? -1f;

        // Nível do acelerador na rede: inteiro de 1 a 100. 0 (campo ausente, cliente antigo) vale 100%.
        public static int GasPct(float g) => Mathf.Clamp(Mathf.RoundToInt(g * 100f), 1, 100);
        public static float GasOf(int pct) => pct <= 0 ? 1f : Mathf.Clamp01(pct / 100f);
        static float? Opt(float v) => v < 0f ? (float?)null : v;

        // O relógio que o visitante manda no "in" e recebe de volta no snapshot (ms desde que o jogo abriu).
        public static int ClockMs => (int)(Time.realtimeSinceStartupAsDouble * 1000.0);

        public static NetCar Pack(CarState c) => new NetCar
        {
            s = c.s, v = c.v, hold = c.hold, T = c.T, slip = c.slip, held = c.held, contact = c.contact, side = c.side, mode = (int)c.mode,
            gp = GasPct(c.gas), tb = c.turbo, bo = c.boost,
            fx = c.flyPos.x, fy = c.flyPos.y, fvx = c.flyVel.x, fvy = c.flyVel.y, flyH = c.flyH, flyW = c.flyW, flyZ = c.flyZ,
            bx = c.backPos.x, by = c.backPos.y, backH = c.backH, backY = c.backY, backT = c.backT,
            ar = c.air, ay = c.airY, avy = c.airVy,
            exitS = c.exitS, lapStart = c.lapStart, hitFx = c.hitFx,
            cross = c.cross, laps = c.laps, offs = c.offs, hits = c.hits,
            lastLap = Opt(c.lastLap), best = Opt(c.best), finish = Opt(c.finish),
            lane = c.lane, cb = c.crossBase, ld = c.lapDirty,
        };

        public static NetSnap Pack(RaceSim sim, int heat)
        {
            var s = new NetSnap { hi = heat, ph = (int)sim.phase, t = sim.t, w = sim.winner, mi = sim.Manche, rt = sim.resumeT };
            foreach (var c in sim.cars) s.cars.Add(Pack(c));
            return s;
        }

        // ---------- Binário ----------
        // O snapshot vai em binário (base64 no campo "b"): ~66 bytes por carro no trilho, contra ~600 no JSON.
        // Os blocos "fora da pista" e "voltando" só vão quando o carro está nesse modo.
        const byte Format = 1;

        public static string Encode(NetSnap n)
        {
            var ms = new MemoryStream(32 + 70 * n.cars.Count);
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Format); w.Write((ushort)n.hi); w.Write((byte)n.ph); w.Write((sbyte)n.w); w.Write((byte)n.mi);
                w.Write(n.t); w.Write(n.rt); w.Write(n.sq); w.Write((byte)n.cars.Count);
                foreach (var c in n.cars)
                {
                    w.Write(c.s); w.Write(c.v); w.Write(c.hold); w.Write(c.T); w.Write(c.slip);
                    w.Write((byte)((c.held ? 1 : 0) | (c.contact ? 2 : 0) | (c.bo ? 4 : 0) | (c.ld ? 8 : 0) | (c.ar ? 16 : 0)));
                    w.Write((sbyte)c.side); w.Write((byte)c.mode); w.Write((byte)Mathf.Clamp(c.gp, 0, 100)); w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(c.tb) * 255f));
                    if (c.mode == (int)CarMode.Off) { w.Write(c.fx); w.Write(c.fy); w.Write(c.fvx); w.Write(c.fvy); w.Write(c.flyH); w.Write(c.flyW); w.Write(c.flyZ); }
                    if (c.mode == (int)CarMode.Returning) { w.Write(c.bx); w.Write(c.by); w.Write(c.backH); w.Write(c.backY); w.Write(c.backT); }
                    if (c.ar) { w.Write(c.ay); w.Write(c.avy); }     // só existe em pista com salto: o formato das outras não muda
                    w.Write(c.exitS); w.Write(c.lapStart); w.Write(c.hitFx);
                    w.Write((short)c.cross); w.Write((ushort)c.laps); w.Write((ushort)c.offs); w.Write((ushort)c.hits);
                    w.Write(c.lastLap); w.Write(c.best); w.Write(c.finish);
                    w.Write((byte)c.lane); w.Write((short)c.cb);
                    w.Write(c.ec); w.Write((ushort)Mathf.Clamp(c.ea, 0, 65535));
                }
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        // null: base64 inválido, formato de outra versão ou mensagem cortada.
        public static NetSnap Decode(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return null;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(b64))))
                {
                    if (r.ReadByte() != Format) return null;
                    var n = new NetSnap { hi = r.ReadUInt16(), ph = r.ReadByte(), w = r.ReadSByte(), mi = r.ReadByte(), t = r.ReadSingle(), rt = r.ReadSingle(), sq = r.ReadInt32() };
                    int count = r.ReadByte();
                    for (int i = 0; i < count; i++)
                    {
                        var c = new NetCar { s = r.ReadSingle(), v = r.ReadSingle(), hold = r.ReadSingle(), T = r.ReadSingle(), slip = r.ReadSingle() };
                        int f = r.ReadByte();
                        c.held = (f & 1) != 0; c.contact = (f & 2) != 0; c.bo = (f & 4) != 0; c.ld = (f & 8) != 0; c.ar = (f & 16) != 0;
                        c.side = r.ReadSByte(); c.mode = r.ReadByte(); c.gp = r.ReadByte(); c.tb = r.ReadByte() / 255f;
                        if (c.mode == (int)CarMode.Off) { c.fx = r.ReadSingle(); c.fy = r.ReadSingle(); c.fvx = r.ReadSingle(); c.fvy = r.ReadSingle(); c.flyH = r.ReadSingle(); c.flyW = r.ReadSingle(); c.flyZ = r.ReadSingle(); }
                        if (c.mode == (int)CarMode.Returning) { c.bx = r.ReadSingle(); c.by = r.ReadSingle(); c.backH = r.ReadSingle(); c.backY = r.ReadSingle(); c.backT = r.ReadSingle(); }
                        if (c.ar) { c.ay = r.ReadSingle(); c.avy = r.ReadSingle(); }
                        c.exitS = r.ReadSingle(); c.lapStart = r.ReadSingle(); c.hitFx = r.ReadSingle();
                        c.cross = r.ReadInt16(); c.laps = r.ReadUInt16(); c.offs = r.ReadUInt16(); c.hits = r.ReadUInt16();
                        c.lastLap = r.ReadSingle(); c.best = r.ReadSingle(); c.finish = r.ReadSingle();
                        c.lane = r.ReadByte(); c.cb = r.ReadInt16();
                        c.ec = r.ReadInt32(); c.ea = r.ReadUInt16();
                        n.cars.Add(c);
                    }
                    return n;
                }
            }
            catch (Exception) { return null; }
        }

        // ---------- Correção do carro próprio ----------

        // Diferença (em células, ao longo do trilho) a partir da qual o carrinho do próprio jogador deixa a predição e vai inteiro para o estado do host.
        public const float OwnTolerance = 1.5f;
        // Fração do erro corrigida a cada snapshot quando a predição está perto do host (correção suave, sem salto).
        public const float Blend = 0.25f;

        // own: o carrinho que este jogador controla. Ele mantém o botão e, se a predição está perto do host, também a posição e a velocidade.
        // hist: a predição dos últimos quadros. Com a latência conhecida (hist.lag), o host é comparado com a predição de lag atrás
        // (o snapshot já chega velho) e a diferença é corrigida aos poucos. Sem ela, vale a regra antiga: perto do host, fica a predição.
        public static void Apply(NetCar n, CarState c, bool own, OwnHistory hist = null)
        {
            bool keepMotion = false;
            if (own && (int)c.mode == n.mode && c.lane == n.lane)
            {
                if (hist != null && hist.lag >= 0f && hist.At(out float ps, out float pv))
                {
                    float ds = n.s - ps, dv = n.v - pv;
                    if (Mathf.Abs(ds) < OwnTolerance)
                    {
                        keepMotion = true;
                        ds *= Blend; dv *= Blend;
                        c.s += ds; c.v = Mathf.Max(0f, c.v + dv);
                        hist.Shift(ds, dv);
                    }
                }
                else keepMotion = Mathf.Abs(c.s - n.s) < OwnTolerance;
            }
            c.lane = n.lane; c.crossBase = n.cb; c.lapDirty = n.ld;
            if (!keepMotion)
            {
                c.s = n.s; c.v = n.v; c.hold = n.hold; c.T = n.T; c.slip = n.slip; c.side = n.side; c.mode = (CarMode)n.mode;
                c.flyPos = new Vector2(n.fx, n.fy); c.flyVel = new Vector2(n.fvx, n.fvy); c.flyH = n.flyH; c.flyW = n.flyW; c.flyZ = n.flyZ;
                c.backPos = new Vector2(n.bx, n.by); c.backH = n.backH; c.backY = n.backY; c.backT = n.backT;
                c.air = n.ar; c.airY = n.ay; c.airVy = n.avy;
                if (own) hist?.Clear();
            }
            if (!own) { c.held = n.held; c.gas = GasOf(n.gp); c.boost = n.bo; }
            c.turbo = n.tb;                                 // o tanque é sempre o do host
            c.contact = n.contact; c.hitFx = n.hitFx; c.exitS = n.exitS; c.lapStart = n.lapStart;
            c.cross = n.cross; c.laps = n.laps; c.offs = n.offs; c.hits = n.hits;
            c.lastLap = Opt(n.lastLap); c.best = Opt(n.best); c.finish = Opt(n.finish);
        }

        public static void Apply(NetSnap n, RaceSim sim, int ownLane, OwnHistory hist = null, int ownLane2 = -1, OwnHistory hist2 = null)
        {
            sim.Mirror((RaceSim.Phase)n.ph, n.t, n.w, n.mi, n.rt);
            for (int k = 0; k < sim.N && k < n.cars.Count; k++)
            {
                bool own2 = k == ownLane2 && k != ownLane;       // o segundo jogador do computador também é predito
                Apply(n.cars[k], sim.cars[k], k == ownLane || own2, k == ownLane ? hist : own2 ? hist2 : null);
            }
        }

        // Latência de ida e volta (s) pelo eco do relógio no carro próprio; −1 se o snapshot não traz eco.
        public static float Rtt(NetCar own, int nowMs)
        {
            if (own == null || own.ec <= 0) return -1f;
            int ms = nowMs - own.ec - own.ea;
            return ms >= 0 && ms < 5000 ? ms / 1000f : -1f;
        }
    }

    // A predição do carro próprio nos últimos ~2 s (um registro por quadro), para comparar com o snapshot que chega atrasado.
    public class OwnHistory
    {
        const int Max = 128;
        readonly float[] t = new float[Max], s = new float[Max], v = new float[Max];
        int head, count;
        public float lag = -1f;                             // quanto o snapshot chega atrasado (s): metade da ida e volta; −1 desconhecido

        public void Clear() { count = 0; }

        public void Add(float time, CarState c)
        {
            head = (head + 1) % Max;
            t[head] = time; s[head] = c.s; v[head] = c.v;
            if (count < Max) count++;
        }

        // A predição de "lag" segundos antes do último registro (a mais próxima). false: nada registrado.
        public bool At(out float ps, out float pv)
        {
            ps = pv = 0f;
            if (count == 0) return false;
            float want = t[head] - lag;
            int best = head;
            for (int i = 0; i < count; i++)
            {
                int k = (head - i + Max) % Max;
                best = k;
                if (t[k] <= want) break;
            }
            ps = s[best]; pv = v[best];
            return true;
        }

        // A correção aplicada vale para toda a história (senão o mesmo erro seria corrigido de novo no próximo snapshot).
        public void Shift(float ds, float dv)
        {
            for (int i = 0; i < count; i++) { int k = (head - i + Max) % Max; s[k] += ds; v[k] += dv; }
        }
    }
}
