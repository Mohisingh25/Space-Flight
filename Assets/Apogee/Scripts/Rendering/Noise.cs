using System;

namespace Apogee.Rendering
{
    /// <summary>Thread-safe 3D gradient (Perlin) noise and fBm helpers used for planet and surface textures.</summary>
    public class Noise
    {
        readonly int[] perm = new int[512];

        public Noise(int seed)
        {
            var rng = new Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static double Fade(double t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        static double Grad(int hash, double x, double y, double z)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        /// <summary>Noise in roughly [-1, 1].</summary>
        public double Sample(double x, double y, double z)
        {
            int X = (int)Math.Floor(x) & 255, Y = (int)Math.Floor(y) & 255, Z = (int)Math.Floor(z) & 255;
            x -= Math.Floor(x); y -= Math.Floor(y); z -= Math.Floor(z);
            double u = Fade(x), v = Fade(y), w = Fade(z);
            int A = perm[X] + Y, AA = perm[A] + Z, AB = perm[A + 1] + Z;
            int B = perm[X + 1] + Y, BA = perm[B] + Z, BB = perm[B + 1] + Z;
            return Lerp(
                Lerp(Lerp(Grad(perm[AA], x, y, z), Grad(perm[BA], x - 1, y, z), u),
                     Lerp(Grad(perm[AB], x, y - 1, z), Grad(perm[BB], x - 1, y - 1, z), u), v),
                Lerp(Lerp(Grad(perm[AA + 1], x, y, z - 1), Grad(perm[BA + 1], x - 1, y, z - 1), u),
                     Lerp(Grad(perm[AB + 1], x, y - 1, z - 1), Grad(perm[BB + 1], x - 1, y - 1, z - 1), u), v), w);
        }

        /// <summary>Fractal Brownian motion, result roughly in [-1, 1].</summary>
        public double Fbm(double x, double y, double z, int octaves, double lacunarity = 2.0, double gain = 0.5)
        {
            double sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x, y, z) * amp;
                norm += amp;
                amp *= gain;
                x *= lacunarity; y *= lacunarity; z *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal — sharp mountain ridges, result in [0, 1].</summary>
        public double Ridged(double x, double y, double z, int octaves)
        {
            double sum = 0, amp = 0.5, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                double n = 1.0 - Math.Abs(Sample(x, y, z));
                sum += n * n * amp;
                norm += amp;
                amp *= 0.5;
                x *= 2.0; y *= 2.0; z *= 2.0;
            }
            return sum / norm;
        }
    }
}
