using System.Collections;
using UnityEngine;

namespace Autorama
{
    // Fogos de artifício no fim do campeonato (seção 49 do guia): foguetes sobem de pontos da mesa e, ao apagar,
    // estouram numa esfera de faíscas da cor do foguete. Tudo em escala da pista (TrackMath.Scale).
    public partial class RaceController
    {
        const float FogosTempo = 6f, FogosIntervalo = 0.35f;
        const int FogosEstouro = 90;
        ParticleSystem fogos;
        Coroutine fogosRun;

        void CreateFogos()
        {
            var go = new GameObject("Fogos");
            go.transform.SetParent(transform, false);
            fogos = go.AddComponent<ParticleSystem>();
            fogos.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = fogos.main;
            main.playOnAwake = false; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.1f);
            main.startSize = 0.08f;
            main.gravityModifier = 0.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            var em = fogos.emission; em.enabled = false;
            var sh = fogos.shape; sh.enabled = false;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMaterial;

            var bgo = new GameObject("Estouro");
            bgo.transform.SetParent(go.transform, false);
            var burst = bgo.AddComponent<ParticleSystem>();
            burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var bm = burst.main;
            bm.playOnAwake = false; bm.loop = false;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.6f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 3.2f);
            bm.startSize = 0.1f;
            bm.gravityModifier = 0.35f;
            bm.simulationSpace = ParticleSystemSimulationSpace.World;
            bm.maxParticles = 4000;
            var be = burst.emission; be.rateOverTime = 0f;
            be.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)FogosEstouro) });
            var bs = burst.shape; bs.shapeType = ParticleSystemShapeType.Sphere; bs.radius = 0.05f;
            var drag = burst.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 1.2f; drag.limit = 100f;
            var col = burst.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            bgo.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMaterial;

            var sub = fogos.subEmitters; sub.enabled = true;
            sub.AddSubEmitter(burst, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritColor);
            fogos.Play();
        }

        // Chamado quando a última bateria do campeonato é salva.
        void Fogos()
        {
            if (!fogos) CreateFogos();
            if (fogosRun != null) StopCoroutine(fogosRun);
            fogosRun = StartCoroutine(FogosLoop());
        }

        IEnumerator FogosLoop()
        {
            float k = builder.Scale;
            var p0 = builder.transform.position;
            var r = builder.Info != null ? builder.Info.bounds : new Rect(0, 0, 10, 10);
            var bm = fogos.subEmitters.GetSubEmitterSystem(0).main;
            bm.startSpeedMultiplier = k;
            bm.startSizeMultiplier = 0.1f * k;
            for (float t = 0; t < FogosTempo; t += FogosIntervalo)
            {
                int n = Random.value < 0.3f ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    var pos = new Vector3(p0.x + Random.Range(r.xMin, r.xMax) * k, p0.y, p0.z + Random.Range(r.yMin, r.yMax) * k);
                    var vel = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(7f, 9f), Random.Range(-0.6f, 0.6f)) * k;
                    fogos.Emit(new ParticleSystem.EmitParams
                    {
                        position = pos, velocity = vel, startSize = 0.08f * k,
                        startColor = Color.HSVToRGB(Random.value, 0.85f, 1f),
                    }, 1);
                }
                yield return new WaitForSeconds(FogosIntervalo);
            }
            fogosRun = null;
        }
    }
}
