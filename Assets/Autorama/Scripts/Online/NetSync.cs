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

        public static NetCar Pack(CarState c) => new NetCar
        {
            s = c.s, v = c.v, hold = c.hold, T = c.T, slip = c.slip, held = c.held, contact = c.contact, side = c.side, mode = (int)c.mode,
            gp = GasPct(c.gas), tb = c.turbo, bo = c.boost,
            fx = c.flyPos.x, fy = c.flyPos.y, fvx = c.flyVel.x, fvy = c.flyVel.y, flyH = c.flyH, flyW = c.flyW, flyZ = c.flyZ,
            bx = c.backPos.x, by = c.backPos.y, backH = c.backH, backY = c.backY, backT = c.backT,
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

        // Distância (em células, ao longo do trilho) a partir da qual o carrinho do próprio jogador deixa a predição e vai para o estado do host.
        // A predição anda à frente do host por causa da latência; só um desvio grande (batida, saída de pista) vale a correção.
        public const float OwnTolerance = 1.5f;

        // own: o carrinho que este jogador controla. Ele mantém o botão e, se a predição está perto do host, também a posição e a velocidade.
        public static void Apply(NetCar n, CarState c, bool own)
        {
            bool keepMotion = own && (int)c.mode == n.mode && c.lane == n.lane && Mathf.Abs(c.s - n.s) < OwnTolerance;
            c.lane = n.lane; c.crossBase = n.cb; c.lapDirty = n.ld;
            if (!keepMotion)
            {
                c.s = n.s; c.v = n.v; c.hold = n.hold; c.T = n.T; c.slip = n.slip; c.side = n.side; c.mode = (CarMode)n.mode;
                c.flyPos = new Vector2(n.fx, n.fy); c.flyVel = new Vector2(n.fvx, n.fvy); c.flyH = n.flyH; c.flyW = n.flyW; c.flyZ = n.flyZ;
                c.backPos = new Vector2(n.bx, n.by); c.backH = n.backH; c.backY = n.backY; c.backT = n.backT;
            }
            if (!own) { c.held = n.held; c.gas = GasOf(n.gp); c.boost = n.bo; }
            c.turbo = n.tb;                                 // o tanque é sempre o do host
            c.contact = n.contact; c.hitFx = n.hitFx; c.exitS = n.exitS; c.lapStart = n.lapStart;
            c.cross = n.cross; c.laps = n.laps; c.offs = n.offs; c.hits = n.hits;
            c.lastLap = Opt(n.lastLap); c.best = Opt(n.best); c.finish = Opt(n.finish);
        }

        public static void Apply(NetSnap n, RaceSim sim, int ownLane)
        {
            sim.Mirror((RaceSim.Phase)n.ph, n.t, n.w, n.mi, n.rt);
            for (int k = 0; k < sim.N && k < n.cars.Count; k++) Apply(n.cars[k], sim.cars[k], k == ownLane);
        }
    }
}
