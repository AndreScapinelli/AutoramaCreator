using UnityEngine;
using UnityEngine.Rendering;

namespace Autorama
{
    // Efeitos dos carrinhos (seção 56 do guia): rastro do turbo, fumaça e marca de pneu na derrapagem.
    // Só leem o CarState (boost, slip, mode), que o online já sincroniza: a física não muda.
    public partial class RaceController
    {
        const float SmokeSlip = 0.25f, SmokeRate = 140f, SmokeMinV = 0.3f;
        const float SkidSlip = 0.15f, SkidLoad = 0.85f, SkidW = 0.035f, SkidLift = 0.003f, SkidStep = 0.02f;
        const float TrailTime = 0.35f, VfxJump = 1f;
        const int SkidMax = 6000;
        const float SkidFlush = 0.1f;            // a malha das marcas sobe para a placa no máximo 10 vezes por segundo
        static readonly Vector3[] RearWheels = { new Vector3(-0.185f, 0f, 0.145f), new Vector3(-0.185f, 0f, -0.145f) };

        TrailRenderer[] turboTrails;
        ParticleSystem smoke;
        Mesh skidMesh;
        Vector3[] skidV;
        Color32[] skidC;
        int skidNext;
        bool skidDirty;
        float skidFlushT;
        Vector3[,] skidLast;
        bool[,] skidHas;
        float[] smokeAcc;
        Vector3[] vfxPrev;
        CarMode[] vfxMode;
        float[] vfxHit;
        const float DustLifeMin = 2f, DustLifeMax = 2.8f, DustRate = 30f;
        const int HitSparks = 10, OffSparks = 12, OffDust = 30;

        void CreateVfx()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            int n = RaceSim.MaxCars;
            turboTrails = new TrailRenderer[n];
            skidLast = new Vector3[n, 2]; skidHas = new bool[n, 2];
            smokeAcc = new float[n]; vfxPrev = new Vector3[n]; vfxMode = new CarMode[n]; vfxHit = new float[n];

            // Turbo: chama branca que vira laranja e some, saindo da traseira.
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0.4f), new GradientColorKey(new Color(1f, 0.2f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            for (int k = 0; k < n; k++)
            {
                var go = new GameObject("Turbo");
                go.transform.SetParent(cars[k], false);
                go.transform.localPosition = new Vector3(-0.27f, 0.07f, 0f);
                var tr = go.AddComponent<TrailRenderer>();
                tr.time = TrailTime;
                tr.minVertexDistance = 0.03f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.18f), new Keyframe(1f, 0f));
                tr.colorGradient = grad;
                tr.sharedMaterial = mat;
                tr.shadowCastingMode = ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.emitting = false;
                turboTrails[k] = tr;
            }

            // Fumaça: um sistema só, em espaço do mundo, com Emit por pneu (como as faíscas).
            var sgo = new GameObject("Fumaça");
            sgo.transform.SetParent(transform, false);
            smoke = sgo.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.playOnAwake = false; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            var em = smoke.emission; em.enabled = false;
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.02f;
            var sol = smoke.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 3f)));
            var col = smoke.colorOverLifetime; col.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = sg;
            var sr = sgo.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = new Material(mat) { mainTexture = SmokeTexture() };
            sr.shadowCastingMode = ShadowCastingMode.Off;
            smoke.Play();

            // Marcas de pneu: uma malha só, com SkidMax faixas num buffer circular (as mais velhas dão lugar às novas).
            var mgo = new GameObject("Marcas de pneu");
            mgo.transform.SetParent(transform, false);
            skidMesh = new Mesh { name = "Marcas" };
            skidMesh.MarkDynamic();
            skidV = new Vector3[SkidMax * 4];
            skidC = new Color32[SkidMax * 4];
            var tris = new int[SkidMax * 6];
            for (int i = 0; i < SkidMax; i++)
            {
                int v = i * 4, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            skidMesh.vertices = skidV;
            skidMesh.colors32 = skidC;
            skidMesh.triangles = tris;
            skidMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            mgo.AddComponent<MeshFilter>().sharedMesh = skidMesh;
            var mr = mgo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            sim.Resets += ClearVfx;
        }

        static Texture2D SmokeTexture()
        {
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        void ClearVfx()
        {
            if (smoke == null) return;
            foreach (var tr in turboTrails) { tr.emitting = false; tr.Clear(); }
            smoke.Clear();
            System.Array.Clear(skidC, 0, skidC.Length);
            System.Array.Clear(skidHas, 0, skidHas.Length);
            skidNext = 0; skidDirty = true;
        }

        // Chamado em PlaceCars, depois de posicionar o carrinho k.
        void UpdateVfx(int k, CarState st)
        {
            var car = cars[k];
            float dt = Time.deltaTime;
            bool live = !Paused && st.mode == CarMode.OnSlot;

            // Teleporte (volta ao trilho, grid): sem rastro nem marca ligando os dois pontos.
            if ((car.position - vfxPrev[k]).sqrMagnitude > VfxJump * VfxJump)
            {
                turboTrails[k].Clear();
                skidHas[k, 0] = skidHas[k, 1] = false;
            }
            vfxPrev[k] = car.position;

            // Batida e saída de pista: pelo estado (hitFx subiu, mode virou Off), para valer também no cliente online.
            var at = car.position + Vector3.up * 0.08f;
            if (st.hitFx > vfxHit[k] + 0.1f && sparks) sparks.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true }, HitSparks);
            if (st.mode == CarMode.Off && vfxMode[k] != CarMode.Off)
            {
                if (sparks) sparks.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true }, OffSparks);
                for (int i = 0; i < OffDust; i++)
                {
                    var dir = Random.insideUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.5f;
                    smoke.Emit(new ParticleSystem.EmitParams { position = at + dir * 0.1f, velocity = dir * 0.8f, startLifetime = Random.Range(DustLifeMin, DustLifeMax), applyShapeToPosition = true }, 1);
                }
            }
            // Rodando fora da pista: poeira enquanto ainda desliza.
            if (st.mode == CarMode.Off && !Paused && st.flyVel.sqrMagnitude > 0.09f)
            {
                smokeAcc[k] += DustRate * dt;
                int count = (int)smokeAcc[k];
                smokeAcc[k] -= count;
                for (int i = 0; i < count; i++)
                    smoke.Emit(new ParticleSystem.EmitParams { position = car.position + Vector3.up * 0.04f, startLifetime = Random.Range(DustLifeMin, DustLifeMax), applyShapeToPosition = true }, 1);
            }
            vfxHit[k] = st.hitFx; vfxMode[k] = st.mode;

            turboTrails[k].emitting = live && st.boost && st.turbo > 0f && !st.air;

            bool slide = live && !st.air && st.v > SmokeMinV;
            float sf = slide ? Mathf.Clamp01((st.slip - SmokeSlip) / (1f - SmokeSlip)) : 0f;
            if (sf > 0f)
            {
                smokeAcc[k] += SmokeRate * sf * dt;
                int count = (int)smokeAcc[k];
                smokeAcc[k] -= count;
                for (int i = 0; i < count; i++)
                    smoke.Emit(new ParticleSystem.EmitParams { position = car.TransformPoint(RearWheels[i & 1]) + Vector3.up * 0.06f, applyShapeToPosition = true }, 1);
            }

            bool skid = slide && (st.slip > SkidSlip || st.load > SkidLoad);
            // No limite da curva (load perto de 1) a marca é fraca; derrapando (slip), forte.
            byte alpha = (byte)Mathf.Min(240f, 60f + 80f * Mathf.Clamp01((st.load - SkidLoad) / (1f - SkidLoad)) + 150f * Mathf.Clamp01((st.slip - SkidSlip) / (0.6f - SkidSlip)));
            for (int w = 0; w < 2; w++)
            {
                if (!skid) { skidHas[k, w] = false; continue; }
                var p = car.TransformPoint(RearWheels[w]) + car.up * SkidLift;
                if (!skidHas[k, w]) { skidLast[k, w] = p; skidHas[k, w] = true; continue; }
                var a = skidLast[k, w];
                if ((p - a).sqrMagnitude < SkidStep * SkidStep) continue;
                var side = Vector3.Cross(car.up, p - a).normalized * SkidW;
                int v = skidNext * 4;
                skidV[v] = a - side; skidV[v + 1] = a + side; skidV[v + 2] = p - side; skidV[v + 3] = p + side;
                var c = new Color32(12, 12, 12, alpha);
                skidC[v] = skidC[v + 1] = skidC[v + 2] = skidC[v + 3] = c;
                skidNext = (skidNext + 1) % SkidMax;
                skidLast[k, w] = p;
                skidDirty = true;
            }
        }

        void FlushVfx()
        {
            if (!skidDirty || skidMesh == null) return;
            skidFlushT += Time.unscaledDeltaTime;
            if (skidFlushT < SkidFlush) return;
            skidFlushT = 0f;
            skidMesh.vertices = skidV;
            skidMesh.colors32 = skidC;
            skidDirty = false;
        }
    }
}
