using System;
using UnityEngine;

namespace Apogee.Simulation
{
    /// <summary>Double-precision 3D vector for orbital mechanics (positions up to billions of km).</summary>
    [Serializable]
    public struct Vector3d
    {
        public double x, y, z;

        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public Vector3d(Vector3 v) { x = v.x; y = v.y; z = v.z; }

        public static readonly Vector3d zero = new Vector3d(0, 0, 0);
        public static readonly Vector3d up = new Vector3d(0, 1, 0);

        public double sqrMagnitude { get { return x * x + y * y + z * z; } }
        public double magnitude { get { return Math.Sqrt(x * x + y * y + z * z); } }
        public Vector3d normalized
        {
            get
            {
                double m = magnitude;
                return m > 1e-300 ? new Vector3d(x / m, y / m, z / m) : zero;
            }
        }

        public Vector3 ToFloat() { return new Vector3((float)x, (float)y, (float)z); }

        public static Vector3d operator +(Vector3d a, Vector3d b) { return new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3d operator -(Vector3d a, Vector3d b) { return new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3d operator -(Vector3d a) { return new Vector3d(-a.x, -a.y, -a.z); }
        public static Vector3d operator *(Vector3d a, double d) { return new Vector3d(a.x * d, a.y * d, a.z * d); }
        public static Vector3d operator *(double d, Vector3d a) { return new Vector3d(a.x * d, a.y * d, a.z * d); }
        public static Vector3d operator /(Vector3d a, double d) { return new Vector3d(a.x / d, a.y / d, a.z / d); }
        public static implicit operator Vector3d(Vector3 v) { return new Vector3d(v.x, v.y, v.z); }

        public static double Dot(Vector3d a, Vector3d b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3d Cross(Vector3d a, Vector3d b)
        {
            return new Vector3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        public static double Angle(Vector3d a, Vector3d b)
        {
            double d = Dot(a.normalized, b.normalized);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d)));
        }

        /// <summary>Rotates by a float quaternion (attitude / planet spin).</summary>
        public static Vector3d Rotate(Quaternion q, Vector3d v)
        {
            double qx = q.x, qy = q.y, qz = q.z, qw = q.w;
            double tx = 2 * (qy * v.z - qz * v.y), ty = 2 * (qz * v.x - qx * v.z), tz = 2 * (qx * v.y - qy * v.x);
            return new Vector3d(v.x + qw * tx + (qy * tz - qz * ty), v.y + qw * ty + (qz * tx - qx * tz), v.z + qw * tz + (qx * ty - qy * tx));
        }

        public override string ToString() { return "(" + x.ToString("0.###") + ", " + y.ToString("0.###") + ", " + z.ToString("0.###") + ")"; }
    }
}
