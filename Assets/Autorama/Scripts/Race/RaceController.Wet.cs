using UnityEngine;
using UnityEngine.Rendering;

namespace Autorama
{
    // Pista molhada (seção 76 do guia): chuva caindo, a pista escura e brilhante, a luz mais fraca e spray d'água atrás
    // dos pneus. Só visual: a aderência menor vem da regra "molhada" (Career.Wet). Liga na prova molhada da carreira.
    public partial class RaceController
    {
        const float WetDark = 0.7f, WetSmooth = 0.82f, WetLight = 0.7f;
        const float RainRate = 900f, RainBox = 26f, RainSpeed = 14f, RainHeight = 4f;
        const float SprayMinV = 1.5f, SprayRate = 70f;

        bool wetOn;
        ParticleSystem rain, spray;
        MaterialPropertyBlock wetBlock;
        int wetRenderers = -1;
        float wetCheckT, sprayAcc;
        Light wetSun;
        float wetSunWas;

        bool WantWet => panel.CareerMode && (kScreen == KScreen.Race || kScreen == KScreen.Result) && KNode != null && KNode.Has("molhada");

        void CreateWet()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            var soft = new Material(mat) { mainTexture = SoftDot() };     // o spray é névoa: bolinha com borda suave

            // Chuva: riscos esticados caindo numa caixa que acompanha a câmera.
            var rgo = new GameObject("Chuva");
            rgo.transform.SetParent(transform, false);
            rain = rgo.AddComponent<ParticleSystem>();
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = rain.main;
            m.playOnAwake = false; m.loop = true;
            m.startLifetime = 1.4f;
            m.startSpeed = RainSpeed;
            m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
            m.startColor = new Color(0.8f, 0.87f, 1f, 0.35f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 4000;
            var em = rain.emission; em.rateOverTime = RainRate;
            var sh = rain.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(RainBox, 0.1f, RainBox);
            sh.rotation = new Vector3(90f, 0f, 0f);                  // a caixa emite para baixo
            var rr = rain.GetComponent<ParticleSystemRenderer>();
            rr.renderMode = ParticleSystemRenderMode.Stretch;
            rr.velocityScale = 0.035f; rr.lengthScale = 1f;
            rr.sharedMaterial = mat;
            rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;

            // Spray: névoa branca que sai das rodas de trás e se espalha.
            var sgo = new GameObject("Spray");
            sgo.transform.SetParent(transform, false);
            spray = sgo.AddComponent<ParticleSystem>();
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = spray.main;
            sm.playOnAwake = false; sm.loop = false;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            sm.startSpeed = 0f;
            sm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            sm.startColor = new Color(0.9f, 0.94f, 1f, 0.55f);
            sm.gravityModifier = 0.25f;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.maxParticles = 2500;
            var se = spray.emission; se.enabled = false;
            var col = spray.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = spray.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
            var spr = spray.GetComponent<ParticleSystemRenderer>();
            spr.sharedMaterial = soft;
            spr.shadowCastingMode = ShadowCastingMode.Off; spr.receiveShadows = false;

            wetBlock = new MaterialPropertyBlock();
        }

        // A cada quadro: liga e desliga, segue a câmera e solta o spray.
        void WetTick()
        {
            if (rain == null) CreateWet();
            bool want = WantWet;
            if (want != wetOn) SetWet(want);
            if (!wetOn) return;

            // A chuva cai sobre o ponto da mesa que a câmera olha (não passa colada na lente); na pista larga a caixa cresce.
            var c = cam.Cam.transform;
            float k = sim != null && sim.Ready ? Mathf.Sqrt(sim.Scale) : 1f;
            Vector3 look = c.forward.y < -0.05f ? c.position + c.forward * (c.position.y / -c.forward.y) : c.position + c.forward * 8f * k;
            float h = Mathf.Min(RainHeight * k, c.position.y * 0.6f);             // nasce abaixo da câmera
            rain.transform.position = new Vector3(look.x, h, look.z);
            var sh = rain.shape; sh.scale = new Vector3(RainBox * k, 0.1f, RainBox * k);
            var mm = rain.main; mm.startLifetime = h / RainSpeed;                  // some ao chegar na mesa

            // A pista se reconstrói ao trocar de prova: de vez em quando, molha as peças novas.
            if ((wetCheckT -= Time.unscaledDeltaTime) <= 0f) { wetCheckT = 0.5f; ApplyWetTrack(true); }

            // Spray: cada carro andando no trilho, nas duas rodas de trás, mais forte quanto mais rápido.
            if (sim == null || !sim.Ready || Paused || kPause) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < sim.N; i++)
            {
                var st = sim.cars[i];
                if (!sim.present[i] || st.mode != CarMode.OnSlot || st.air || st.v < SprayMinV * sim.Speed) continue;
                float rate = SprayRate * Mathf.Clamp01(st.v / (sim.set.vmax * sim.Speed));
                sprayAcc += rate * dt;
                int n = (int)sprayAcc;
                if (n <= 0) continue;
                sprayAcc -= n;
                var back = -cars[i].right;                               // a frente do carrinho é +X
                foreach (var w in RearWheels)
                {
                    var at = cars[i].TransformPoint(w);
                    for (int j = 0; j < n; j++)
                        spray.Emit(new ParticleSystem.EmitParams
                        {
                            position = at + Random.insideUnitSphere * 0.02f * sim.Scale,
                            velocity = (back * Random.Range(0.6f, 1.4f) + Vector3.up * Random.Range(0.4f, 1f) + Random.insideUnitSphere * 0.4f) * st.v * 0.12f,
                            startSize = Random.Range(0.12f, 0.26f) * Mathf.Sqrt(sim.Scale),
                        }, 1);
                }
            }
        }

        static Texture2D SoftDot()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        void SetWet(bool on)
        {
            wetOn = on;
            if (on) rain.Play(); else { rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); spray.Clear(); }
            ApplyWetTrack(on);
            // Céu fechado: a luz principal fica mais fraca.
            if (on)
            {
                wetSun = RenderSettings.sun != null ? RenderSettings.sun : FindFirstObjectByType<Light>();
                if (wetSun != null) { wetSunWas = wetSun.intensity; wetSun.intensity = wetSunWas * WetLight; }
            }
            else if (wetSun != null) { wetSun.intensity = wetSunWas; wetSun = null; }
        }

        // As peças da pista (filhas do construtor): mais escuras e brilhantes, como asfalto molhado.
        void ApplyWetTrack(bool on)
        {
            var rs = builder.GetComponentsInChildren<MeshRenderer>();
            if (on && rs.Length == wetRenderers) return;
            wetRenderers = on ? rs.Length : -1;
            foreach (var r in rs)
            {
                if (!on) { r.SetPropertyBlock(null); continue; }
                var m = r.sharedMaterial;
                if (m == null || !m.HasProperty("_Smoothness")) continue;
                wetBlock.Clear();
                var baseC = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                wetBlock.SetColor("_BaseColor", new Color(baseC.r * WetDark, baseC.g * WetDark, baseC.b * WetDark, baseC.a));
                wetBlock.SetFloat("_Smoothness", Mathf.Max(m.GetFloat("_Smoothness"), WetSmooth));
                r.SetPropertyBlock(wetBlock);
            }
        }
    }
}
