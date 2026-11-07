using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Apogee.Rendering
{
    /// <summary>
    /// Accumulates procedural geometry into a multi-submesh Mesh. Shapes are added through a
    /// transform matrix so complex parts (engines with pipes, towers) can be assembled from primitives.
    /// </summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<List<int>> tris = new List<List<int>>();

        public Matrix4x4 transform = Matrix4x4.identity;

        public int VertexCount { get { return verts.Count; } }

        List<int> Sub(int i)
        {
            while (tris.Count <= i) tris.Add(new List<int>());
            return tris[i];
        }

        int AddVertex(Vector3 p, Vector3 n, Vector2 uv)
        {
            verts.Add(transform.MultiplyPoint3x4(p));
            normals.Add(transform.MultiplyVector(n).normalized);
            uvs.Add(uv);
            return verts.Count - 1;
        }

        /// <summary>
        /// Surface of revolution around local Y. Profile points are (radius, y) ordered bottom → top for an
        /// outward-facing surface (top → bottom for an inward-facing one, e.g. inside a nozzle bell).
        /// Corners sharper than ~35° get split normals so edges stay crisp.
        /// </summary>
        public void Lathe(IList<Vector2> profile, int segments, int submesh, float uRepeat = 1f, float vScale = 1f, float startAngle = 0f, float sweep = 360f, float vOffset = 0f)
        {
            if (profile.Count < 2) return;
            var segNormals = new List<Vector2>();
            var segLen = new List<float>();
            for (int i = 0; i < profile.Count - 1; i++)
            {
                Vector2 t = profile[i + 1] - profile[i];
                float len = t.magnitude;
                segLen.Add(len);
                segNormals.Add(len > 1e-6f ? new Vector2(t.y, -t.x) / len : Vector2.zero);
            }

            // Build rings: each ring = (profile index, normal in r/y plane, v coordinate)
            var ringIdx = new List<int>();
            var ringN = new List<Vector2>();
            var ringV = new List<float>();
            var ringSeg = new List<int>(); // segment that starts at this ring (-1 if none)
            float v = vOffset;
            for (int i = 0; i < profile.Count; i++)
            {
                Vector2 nPrev = i > 0 ? segNormals[i - 1] : Vector2.zero;
                Vector2 nNext = i < segNormals.Count ? segNormals[i] : Vector2.zero;
                if (i > 0) v += segLen[i - 1] * vScale;
                bool smooth = nPrev != Vector2.zero && nNext != Vector2.zero && Vector2.Dot(nPrev, nNext) > 0.82f;
                if (i == 0 || i == profile.Count - 1 || smooth)
                {
                    Vector2 n = i == 0 ? nNext : i == profile.Count - 1 ? nPrev : (nPrev + nNext).normalized;
                    ringIdx.Add(i); ringN.Add(n); ringV.Add(v); ringSeg.Add(i < segNormals.Count ? i : -1);
                }
                else
                {
                    ringIdx.Add(i); ringN.Add(nPrev); ringV.Add(v); ringSeg.Add(-1);
                    ringIdx.Add(i); ringN.Add(nNext); ringV.Add(v); ringSeg.Add(i < segNormals.Count ? i : -1);
                }
            }

            var sub = Sub(submesh);
            var ringStart = new int[ringIdx.Count];
            for (int r = 0; r < ringIdx.Count; r++)
            {
                ringStart[r] = verts.Count;
                Vector2 pr = profile[ringIdx[r]];
                for (int k = 0; k <= segments; k++)
                {
                    float u = (float)k / segments;
                    float ang = (startAngle + u * sweep) * Mathf.Deg2Rad;
                    float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                    AddVertex(new Vector3(pr.x * c, pr.y, pr.x * s), new Vector3(ringN[r].x * c, ringN[r].y, ringN[r].x * s), new Vector2(u * uRepeat, ringV[r]));
                }
            }
            // Quads between a ring that starts segment i and the next ring belonging to profile index i+1.
            for (int r = 0; r < ringIdx.Count - 1; r++)
            {
                int seg = ringSeg[r];
                if (seg < 0 || segLen[seg] < 1e-6f) continue;
                int rn = r + 1;
                while (rn < ringIdx.Count && ringIdx[rn] != seg + 1) rn++;
                if (rn >= ringIdx.Count) continue;
                for (int k = 0; k < segments; k++)
                {
                    int a = ringStart[r] + k, b = ringStart[r] + k + 1;
                    int d = ringStart[rn] + k, c2 = ringStart[rn] + k + 1;
                    sub.Add(a); sub.Add(d); sub.Add(c2);
                    sub.Add(a); sub.Add(c2); sub.Add(b);
                }
            }
        }

        /// <summary>Simple cylinder (optionally capped) between y0 and y1.</summary>
        public void Cylinder(float r, float y0, float y1, int segments, int submesh, bool caps = true, float uRepeat = 1f, float vScale = 1f)
        {
            var prof = new List<Vector2>();
            if (caps) prof.Add(new Vector2(0f, y0));
            prof.Add(new Vector2(r, y0));
            prof.Add(new Vector2(r, y1));
            if (caps) prof.Add(new Vector2(0f, y1));
            Lathe(prof, segments, submesh, uRepeat, vScale);
        }

        /// <summary>Cylinder between two arbitrary points (pipes, struts).</summary>
        public void Tube(Vector3 a, Vector3 b, float r, int segments, int submesh)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-5f) return;
            var saved = transform;
            transform = saved * Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, dir / len), Vector3.one);
            Cylinder(r, 0f, len, segments, submesh, true, 1f, 1f);
            transform = saved;
        }

        /// <summary>Axis-aligned (in current transform) box with per-face normals.</summary>
        public void Box(Vector3 center, Vector3 size, int submesh)
        {
            Box(center, size, Quaternion.identity, submesh);
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rot, int submesh)
        {
            var sub = Sub(submesh);
            Vector3 h = size * 0.5f;
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int f = 0; f < 6; f++)
            {
                Vector3 nn = n[f];
                Vector3 u = Mathf.Abs(nn.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(nn, u);
                Vector3 c = center + rot * Vector3.Scale(nn, h);
                Vector3 du = rot * Vector3.Scale(u, h);
                Vector3 dw = rot * Vector3.Scale(w, h);
                Vector3 wn = rot * nn;
                float su = Mathf.Abs(Vector3.Dot(u, size)), sw = Mathf.Abs(Vector3.Dot(w, size));
                int i0 = AddVertex(c - du - dw, wn, new Vector2(0, 0));
                int i1 = AddVertex(c + du - dw, wn, new Vector2(0, su));
                int i2 = AddVertex(c + du + dw, wn, new Vector2(sw, su));
                int i3 = AddVertex(c - du + dw, wn, new Vector2(sw, 0));
                // Winding so the face is visible from outside (along +normal).
                sub.Add(i0); sub.Add(i1); sub.Add(i2);
                sub.Add(i0); sub.Add(i2); sub.Add(i3);
            }
        }

        /// <summary>Flat convex polygon in the XY plane, extruded symmetrically along Z.</summary>
        public void ExtrudedPolygon(IList<Vector2> poly, float thickness, int submesh)
        {
            var sub = Sub(submesh);
            float hz = thickness * 0.5f;
            int n = poly.Count;
            // Front (+Z) and back (-Z) faces
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? hz : -hz;
                Vector3 nn = side == 0 ? Vector3.forward : Vector3.back;
                int start = verts.Count;
                for (int i = 0; i < n; i++) AddVertex(new Vector3(poly[i].x, poly[i].y, z), nn, poly[i]);
                for (int i = 1; i < n - 1; i++)
                {
                    if (side == 0) { sub.Add(start); sub.Add(start + i); sub.Add(start + i + 1); }
                    else { sub.Add(start); sub.Add(start + i + 1); sub.Add(start + i); }
                }
            }
            // Edges
            float area = 0f;
            for (int i = 0; i < n; i++) { var p = poly[i]; var q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            bool ccw = area > 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % n];
                Vector2 e = q - p;
                Vector3 nn = ccw ? new Vector3(e.y, -e.x, 0f).normalized : new Vector3(-e.y, e.x, 0f).normalized;
                int a = AddVertex(new Vector3(p.x, p.y, hz), nn, new Vector2(0, 0));
                int b = AddVertex(new Vector3(q.x, q.y, hz), nn, new Vector2(1, 0));
                int c = AddVertex(new Vector3(q.x, q.y, -hz), nn, new Vector2(1, 1));
                int d = AddVertex(new Vector3(p.x, p.y, -hz), nn, new Vector2(0, 1));
                if (ccw) { sub.Add(a); sub.Add(d); sub.Add(c); sub.Add(a); sub.Add(c); sub.Add(b); }
                else { sub.Add(a); sub.Add(b); sub.Add(c); sub.Add(a); sub.Add(c); sub.Add(d); }
            }
        }

        /// <summary>Single quad (two-sided optional) — used for panels, windows and decals.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int submesh, bool twoSided = false)
        {
            var sub = Sub(submesh);
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            int i0 = AddVertex(a, n, new Vector2(0, 0));
            int i1 = AddVertex(b, n, new Vector2(1, 0));
            int i2 = AddVertex(c, n, new Vector2(1, 1));
            int i3 = AddVertex(d, n, new Vector2(0, 1));
            // Unity treats clockwise triangles (seen from the front) as front faces.
            sub.Add(i0); sub.Add(i1); sub.Add(i2);
            sub.Add(i0); sub.Add(i2); sub.Add(i3);
            if (twoSided)
            {
                int j0 = AddVertex(a, -n, new Vector2(0, 0));
                int j1 = AddVertex(b, -n, new Vector2(1, 0));
                int j2 = AddVertex(c, -n, new Vector2(1, 1));
                int j3 = AddVertex(d, -n, new Vector2(0, 1));
                sub.Add(j0); sub.Add(j2); sub.Add(j1);
                sub.Add(j0); sub.Add(j3); sub.Add(j2);
            }
        }

        public int SubmeshCount { get { return tris.Count; } }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh();
            m.name = name;
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.subMeshCount = Mathf.Max(1, tris.Count);
            for (int i = 0; i < tris.Count; i++) m.SetTriangles(tris[i], i, false);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }
}
