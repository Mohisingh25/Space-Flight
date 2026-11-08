using System.Collections.Generic;
using Apogee.Astro;
using Apogee.Rendering;
using Apogee.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Flight
{
    /// <summary>
    /// Real clouds around the vehicle: fair-weather cumulus at 1.2–3 km whose bases are flat and tops are
    /// sunlit, plus high cirrus at 8–11 km. The rocket climbs through them; puffs fade as the camera passes
    /// through. The field follows the vessel across the planet and only exists on worlds with weather.
    /// </summary>
    public class CloudField : MonoBehaviour
    {
        ParticleSystem ps;
        Material mat;
        CelestialBody body;
        Vector3d centreFixed;     // body-fixed unit vector of the field centre
        Quaternion frameFixed;
        const float Radius = 40000f;

        public void Init()
        {
            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 4000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 1e7f;
            main.startSpeed = 0f;
            var em = ps.emission;
            em.enabled = false;
            var r = GetComponent<ParticleSystemRenderer>();
            mat = MaterialLibrary.Custom("Cloud");
            mat.SetTexture("_MainTex", ProceduralTextures.Get("cloud"));
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.maxParticleSize = 20f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            gameObject.SetActive(false);
        }

        static bool HasWeather(CelestialBody b) { return b.name == "Earth" || b.name == "Titan"; }

        public void UpdateField(CelestialBody b, Vector3d originFrame, double t, Vector3 sunDir, Color sunColor, Color ambient)
        {
            double alt = originFrame.magnitude - b.radius;
            bool show = HasWeather(b) && alt < 60000;
            if (gameObject.activeSelf != show) gameObject.SetActive(show);
            if (!show) return;
            var spin = b.Spin(t);
            var sub = Vector3d.Rotate(Quaternion.Inverse(spin), originFrame).normalized;
            if (body != b || Vector3d.Angle(sub, centreFixed) * b.radius > Radius * 0.6) Regenerate(b, sub);
            var centreFrame = Vector3d.Rotate(spin, centreFixed * b.radius);
            transform.position = (centreFrame - originFrame).ToFloat();
            transform.rotation = spin * frameFixed;
            mat.SetVector("_SunColor", sunColor);
            mat.SetVector("_Ambient", ambient);
            mat.SetFloat("_SunUp", Vector3.Dot(transform.up, sunDir));
        }

        void Regenerate(CelestialBody b, Vector3d sub)
        {
            body = b;
            centreFixed = sub;
            var up = sub.ToFloat();
            var north = Vector3.ProjectOnPlane(Vector3.up, up);
            if (north.sqrMagnitude < 1e-6f) north = Vector3.forward;
            frameFixed = Quaternion.LookRotation(north.normalized, up);
            // deterministic per region, so the same sky returns when you come back
            int seed = Mathf.RoundToInt((float)(sub.x * 1000)) * 73856093 ^ Mathf.RoundToInt((float)(sub.z * 1000)) * 19349663;
            var rng = new System.Random(seed);
            float R = (float)b.radius;
            var parts = new List<ParticleSystem.Particle>();
            float tint = b.name == "Titan" ? 0f : 1f;
            Color baseCol = b.name == "Titan" ? new Color(1f, 0.75f, 0.45f) : Color.white;
            // cumulus clusters
            for (int c = 0; c < 230; c++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2), d = Mathf.Sqrt((float)rng.NextDouble()) * Radius;
                var ctr = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                float baseAlt = 1200f + (float)rng.NextDouble() * 1500f;
                float size = 350f + (float)rng.NextDouble() * 650f;
                int puffs = 8 + rng.Next(10);
                for (int k = 0; k < puffs; k++)
                {
                    var off = new Vector3(((float)rng.NextDouble() - 0.5f) * size * 2.2f, (float)rng.NextDouble() * size * 0.9f, ((float)rng.NextDouble() - 0.5f) * size * 2.2f);
                    var pos = ctr + off;
                    float dist = new Vector2(pos.x, pos.z).magnitude;
                    pos.y = baseAlt + off.y - dist * dist / (2f * R);
                    float ps0 = size * (0.7f + (float)rng.NextDouble() * 0.6f) * (1f - off.y / (size * 1.6f));
                    parts.Add(new ParticleSystem.Particle
                    {
                        position = pos,
                        startSize = ps0,
                        rotation = (float)rng.NextDouble() * 360f,
                        startColor = new Color(baseCol.r, baseCol.g, baseCol.b, 0.85f),
                        remainingLifetime = 1e7f,
                        startLifetime = 1e7f
                    });
                }
            }
            // high cirrus: wide, thin veils
            for (int c = 0; c < 90 * tint; c++)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2), d = Mathf.Sqrt((float)rng.NextDouble()) * Radius;
                var pos = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                float dist = pos.magnitude;
                pos.y = 8500f + (float)rng.NextDouble() * 2500f - dist * dist / (2f * R);
                parts.Add(new ParticleSystem.Particle
                {
                    position = pos,
                    startSize = 3000f + (float)rng.NextDouble() * 3000f,
                    rotation = (float)rng.NextDouble() * 360f,
                    startColor = new Color(1f, 1f, 1f, 0.22f),
                    remainingLifetime = 1e7f,
                    startLifetime = 1e7f
                });
            }
            var arr = parts.ToArray();
            ps.Clear();
            ps.SetParticles(arr, arr.Length);
            ps.Pause();
        }
    }
}
