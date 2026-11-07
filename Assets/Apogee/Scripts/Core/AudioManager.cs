using System;
using System.Collections.Generic;
using UnityEngine;

namespace Apogee.Core
{
    public enum Sfx { Click, Snap, Error, Whoosh, Detach }
    public enum Ambience { None, Space, Hangar }

    /// <summary>
    /// Synthesises interface sounds and ambience at startup (no audio files needed) and plays them
    /// with the player's volume settings. Engine/flight audio is added with the flight systems in Phase 2.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        const int Rate = 44100;
        readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        AudioSource sfxSource, ambienceSource;
        AudioClip spaceClip, hangarClip;
        Ambience current = Ambience.None;
        float ambienceTarget;
        GameSettings settings;

        public void Init(GameSettings s)
        {
            settings = s;
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            ambienceSource = gameObject.AddComponent<AudioSource>();
            ambienceSource.loop = true;
            ambienceSource.playOnAwake = false;
            ambienceSource.volume = 0f;

            clips[Sfx.Click] = Make("click", 0.05f, Click);
            clips[Sfx.Snap] = Make("snap", 0.35f, Snap);
            clips[Sfx.Error] = Make("error", 0.32f, Error);
            clips[Sfx.Whoosh] = Make("whoosh", 0.35f, Whoosh);
            clips[Sfx.Detach] = Make("detach", 0.18f, Detach);
        }

        public void Play(Sfx s, float volume = 1f)
        {
            AudioClip c;
            if (settings == null || !clips.TryGetValue(s, out c)) return;
            sfxSource.PlayOneShot(c, Mathf.Clamp01(volume * settings.sfxVolume));
        }

        public void SetAmbience(Ambience a)
        {
            if (a == current) return;
            current = a;
            if (a == Ambience.None) { ambienceTarget = 0f; return; }
            if (a == Ambience.Space && spaceClip == null) spaceClip = MakeLoop("space", 12f, 22050, SpaceDrone);
            if (a == Ambience.Hangar && hangarClip == null) hangarClip = MakeLoop("hangar", 10f, 22050, HangarAir);
            ambienceSource.clip = a == Ambience.Space ? spaceClip : hangarClip;
            ambienceSource.volume = 0f;
            ambienceSource.Play();
            ambienceTarget = 1f;
        }

        void Update()
        {
            if (settings == null || ambienceSource == null) return;
            float target = ambienceTarget * settings.ambientVolume * 0.5f;
            ambienceSource.volume = Mathf.MoveTowards(ambienceSource.volume, target, Time.unscaledDeltaTime * 0.6f);
            if (ambienceTarget == 0f && ambienceSource.volume <= 0.001f && ambienceSource.isPlaying) ambienceSource.Stop();
        }

        // ------------------------------------------------------------------ synthesis

        delegate float Generator(float t, float dur, System.Random rng);

        static AudioClip Make(string name, float seconds, Generator g)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(g((float)i / Rate, seconds, rng), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Env(float t, float attack, float decay) { return (t < attack ? t / attack : 1f) * Mathf.Exp(-t / decay); }

        static float Click(float t, float d, System.Random r)
        {
            float f = Mathf.Lerp(2100f, 1300f, t / d);
            return Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, 0.001f, 0.012f) * 0.45f;
        }

        static float Snap(float t, float d, System.Random r)
        {
            float thump = Mathf.Sin(2f * Mathf.PI * 85f * t) * Env(t, 0.002f, 0.06f) * 0.7f;
            float ring = (Mathf.Sin(2f * Mathf.PI * 2230f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 3170f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 4410f * t)) * Env(t, 0.001f, 0.07f) * 0.12f;
            float noise = ((float)r.NextDouble() * 2f - 1f) * Env(t, 0.0005f, 0.012f) * 0.35f;
            return thump + ring + noise;
        }

        static float Error(float t, float d, System.Random r)
        {
            float f = t < 0.14f ? 330f : 220f;
            float lt = t < 0.14f ? t : t - 0.16f;
            if (t >= 0.14f && t < 0.16f) return 0f;
            float s = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t)) * 0.5f + Mathf.Sin(2f * Mathf.PI * f * t) * 0.5f;
            return s * Env(lt, 0.004f, 0.08f) * 0.22f;
        }

        static float Whoosh(float t, float d, System.Random r)
        {
            float x = t / d;
            float env = Mathf.Sin(Mathf.PI * x);
            return ((float)r.NextDouble() * 2f - 1f) * env * env * 0.12f;
        }

        static float Detach(float t, float d, System.Random r)
        {
            float f = Mathf.Lerp(900f, 300f, t / d);
            return Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, 0.002f, 0.05f) * 0.3f;
        }

        delegate void LoopFill(float[] data, int rate, System.Random rng);

        static AudioClip MakeLoop(string name, float seconds, int rate, LoopFill fill)
        {
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            fill(data, rate, new System.Random(name.GetHashCode()));
            // Crossfade the ends so the loop is seamless.
            int fade = rate / 2;
            for (int i = 0; i < fade; i++)
            {
                float a = (float)i / fade;
                data[i] = data[i] * a + data[n - fade + i] * (1f - a);
            }
            var trimmed = new float[n - fade];
            Array.Copy(data, trimmed, trimmed.Length);
            var clip = AudioClip.Create(name, trimmed.Length, 1, rate, false);
            clip.SetData(trimmed, 0);
            return clip;
        }

        static void SpaceDrone(float[] d, int rate, System.Random r)
        {
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / rate;
                float lfo = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.08f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 82.4f * t) * 0.22f * lfo
                           + Mathf.Sin(2f * Mathf.PI * 110.3f * t) * 0.12f * (1f - lfo) + Mathf.Sin(2f * Mathf.PI * 164.8f * t) * 0.05f;
                float white = (float)r.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.02f;
                lp2 += (lp - lp2) * 0.05f;
                d[i] = (tone * 0.5f + lp2 * 2.5f) * 0.5f;
            }
        }

        static void HangarAir(float[] d, int rate, System.Random r)
        {
            float lp = 0f, bp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / rate;
                float white = (float)r.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.06f;           // ventilation rumble
                bp += (white - bp) * 0.25f;
                float hum = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.06f + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.03f;
                float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.11f * t);
                d[i] = (lp * 0.9f * swell + (bp - lp) * 0.05f + hum) * 0.6f;
            }
        }
    }
}
