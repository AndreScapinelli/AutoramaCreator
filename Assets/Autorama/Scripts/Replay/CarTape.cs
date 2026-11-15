using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // Gravação da corrida (seção 55 do guia). Não sabe o que é fantasma nem volta: guarda poses no mundo e devolve
    // a pose de qualquer instante. Serve ao fantasma e a qualquer repetição que vier depois (melhores momentos…).

    // Uma amostra: tudo o que a tela precisa para pôr o carrinho no lugar. t em segundos do relógio da prova.
    public struct CarFrame
    {
        public float t, x, z, y, heading, pitch, bank, v;
        public bool off;      // fora da pista (o carrinho tomba)
    }

    // A trilha de um carrinho: amostras em ordem de tempo.
    public class CarTape
    {
        public readonly List<CarFrame> frames = new List<CarFrame>();

        public float Length => frames.Count == 0 ? 0f : frames[frames.Count - 1].t;

        public void Add(CarFrame f) => frames.Add(f);

        // A pose no instante t, interpolada entre as duas amostras em volta (o rumo pelo menor ângulo).
        public CarFrame At(float t)
        {
            int n = frames.Count;
            if (n == 0) return default;
            if (t <= frames[0].t) return frames[0];
            if (t >= frames[n - 1].t) return frames[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (frames[m].t <= t) lo = m; else hi = m; }
            var a = frames[lo]; var b = frames[hi];
            float e = (t - a.t) / Mathf.Max(b.t - a.t, 1e-6f);
            return new CarFrame
            {
                t = t,
                x = Mathf.Lerp(a.x, b.x, e), z = Mathf.Lerp(a.z, b.z, e), y = Mathf.Lerp(a.y, b.y, e),
                heading = Mathf.LerpAngle(a.heading, b.heading, e), pitch = Mathf.LerpAngle(a.pitch, b.pitch, e),
                bank = Mathf.Lerp(a.bank, b.bank, e), v = Mathf.Lerp(a.v, b.v, e), off = a.off,
            };
        }

        // O trecho de t0 a t1, com o tempo contado a partir de t0. Leva uma amostra antes e uma depois, para a
        // interpolação cobrir as pontas.
        public CarTape Slice(float t0, float t1)
        {
            var r = new CarTape();
            int first = 0;
            while (first + 1 < frames.Count && frames[first + 1].t <= t0) first++;
            for (int i = first; i < frames.Count; i++)
            {
                var f = frames[i];
                f.t -= t0;
                r.frames.Add(f);
                if (frames[i].t >= t1) break;
            }
            return r;
        }

        public string ToBase64()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(frames.Count);
                foreach (var f in frames)
                {
                    w.Write(f.t); w.Write(f.x); w.Write(f.z); w.Write(f.y);
                    w.Write(f.heading); w.Write(f.pitch); w.Write(f.bank); w.Write(f.v); w.Write(f.off);
                }
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        public static CarTape FromBase64(string s)
        {
            var r = new CarTape();
            if (string.IsNullOrEmpty(s)) return r;
            using var rd = new BinaryReader(new MemoryStream(Convert.FromBase64String(s)));
            int n = rd.ReadInt32();
            for (int i = 0; i < n; i++)
                r.frames.Add(new CarFrame
                {
                    t = rd.ReadSingle(), x = rd.ReadSingle(), z = rd.ReadSingle(), y = rd.ReadSingle(),
                    heading = rd.ReadSingle(), pitch = rd.ReadSingle(), bank = rd.ReadSingle(), v = rd.ReadSingle(), off = rd.ReadBoolean(),
                });
            return r;
        }
    }

    // Grava todos os carrinhos presentes enquanto a prova corre, a 30 amostras por segundo do relógio da prova.
    // Prova nova (o relógio volta): começa do zero.
    public class RaceRecorder
    {
        public const float Rate = 30f;
        public readonly CarTape[] tapes = new CarTape[RaceSim.MaxCars];
        float last = -1f;

        public RaceRecorder() { Clear(); }

        public void Clear()
        {
            for (int k = 0; k < tapes.Length; k++) tapes[k] = new CarTape();
            last = -1f;
        }

        // force: grava mesmo antes de 1/30 s (no instante exato em que uma volta fecha).
        public void Sample(RaceSim sim, bool force = false)
        {
            if (sim.phase != RaceSim.Phase.Running && !force) return;      // a última volta fecha junto com a prova
            if (sim.t < last) Clear();
            if (last >= 0f && !force && sim.t - last < 1f / Rate - 1e-4f) return;
            if (sim.t == last) return;
            last = sim.t;
            for (int k = 0; k < sim.N; k++)
            {
                if (!sim.present[k]) continue;
                var c = sim.cars[k];
                var p = sim.CarPose(k, out float y, out float pitch);
                tapes[k].Add(new CarFrame
                {
                    t = sim.t, x = p.pos.x, z = p.pos.y, y = y, heading = p.heading, pitch = pitch,
                    bank = sim.CarBank(k), v = c.mode == CarMode.OnSlot ? c.v : 0f, off = c.mode == CarMode.Off,
                });
            }
        }
    }
}
