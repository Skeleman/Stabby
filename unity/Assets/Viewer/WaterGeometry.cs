using System.Collections.Generic;
using Stabby.World;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Viewer
{
    public sealed class MeshParts
    {
        public string Name;
        public Vector3[] Verts, Norms;
        public Color[] Cols;
        public Vector2[] Uvs;
        public Vector4[] Uv1;
        public int[] Tris;
    }

    public static class WaterGeometry
    {
        const int RiverPieceVertices = 16000;
        public const float RiverLift = 0.1f, LakeLift = 0.5f;
        const float SlopeRange = 0.1f;
        const int SlopeReach = 8, TurnReach = 6, PlungeReach = 16;
        static readonly Color SeaKind = new Color(0.5f, 0.5f, 0, 1), LakeKind = new Color(0.5f, 0.5f, 0, 0.5f);

        public static Mesh ToMesh(MeshParts d)
        {
            var mesh = new Mesh { name = d.Name, indexFormat = d.Verts.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(d.Verts);
            mesh.SetNormals(d.Norms);
            mesh.SetColors(d.Cols);
            if (d.Uvs != null) mesh.SetUVs(0, d.Uvs);
            if (d.Uv1 != null) mesh.SetUVs(1, d.Uv1);
            mesh.SetTriangles(d.Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static MeshParts Lakes(GeneratedWorld world, int cx, int cy, int cells)
        {
            int n = world.Config.Size;
            float cm = world.Config.CellMeters, half = cm * 0.5f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int y = cy; y < Mathf.Min(cy + cells, n); y++)
                for (int x = cx; x < Mathf.Min(cx + cells, n); x++)
                {
                    float h = LakeHeight(world, x, y);
                    if (h == float.MinValue) continue;
                    int b = verts.Count;
                    verts.Add(new Vector3(x * cm - half, h, y * cm - half));
                    verts.Add(new Vector3(x * cm - half, h, y * cm + half));
                    verts.Add(new Vector3(x * cm + half, h, y * cm - half));
                    verts.Add(new Vector3(x * cm + half, h, y * cm + half));
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 3);
                }
            return verts.Count == 0 ? null : Flat($"Water {cx},{cy}", verts, null, tris, LakeKind);
        }

        public static float LakeHeight(GeneratedWorld world, int x, int y)
        {
            int n = world.Config.Size, c = y * n + x;
            var hy = world.Water;
            if (world.Sea[c]) return float.MinValue;
            if (hy.LakeId[c] >= 0) return Mathf.Max(hy.WaterSurface[c], world.Height[c]) + LakeLift;
            float h = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + Stabby.World.Grid.DX[k], ny = y + Stabby.World.Grid.DY[k];
                if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                int o = ny * n + nx;
                if (!world.Sea[o] && hy.LakeId[o] >= 0 && world.Height[c] >= hy.WaterSurface[o]) h = Mathf.Max(h, hy.WaterSurface[o] + LakeLift);
            }
            return h;
        }

        public static List<MeshParts> Rivers(GeneratedWorld world)
        {
            var pieces = new List<MeshParts>();
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var chan = new List<Vector2>();
            var reach = new List<Vector4>();
            var tris = new List<int>();
            foreach (var r in NearField.Lines(world))
            {
                if (verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(RiverPiece($"Rivers {pieces.Count}", verts, cols, chan, reach, tris));
                    verts.Clear();
                    cols.Clear();
                    chan.Clear();
                    reach.Clear();
                    tris.Clear();
                }
                var pts = new List<Vector4>();
                for (int k = 0; k < r.X.Length; k++)
                    pts.Add(new Vector4(r.X[k], r.S[k], r.Z[k], 2 * r.W[k]));
                int b = verts.Count, last = pts.Count - 1;
                var slopes = new float[pts.Count];
                for (int k = 0; k < pts.Count; k++)
                {
                    var u = pts[Mathf.Max(k - SlopeReach, 0)];
                    var d = pts[Mathf.Min(k + SlopeReach, last)];
                    float run = new Vector2(d.x - u.x, d.z - u.z).magnitude;
                    slopes[k] = Mathf.Clamp01((run > 0 ? Mathf.Max(0, u.y - d.y) / run : 0) / SlopeRange);
                }
                float s = 0;
                for (int k = 0; k < pts.Count; k++)
                {
                    var a = pts[Mathf.Max(k - 1, 0)];
                    var z = pts[Mathf.Min(k + 1, last)];
                    var dir = new Vector3(z.x - a.x, 0, z.z - a.z).normalized;
                    float half = Mathf.Max(pts[k].w * 0.5f, 6f);
                    var side = new Vector3(dir.z, 0, -dir.x) * half;
                    var p = new Vector3(pts[k].x, 0, pts[k].z);
                    if (k > 0) s += new Vector2(p.x - pts[k - 1].x, p.z - pts[k - 1].z).magnitude;
                    p.y = pts[k].y + RiverLift;
                    verts.Add(p - side);
                    verts.Add(p + side);
                    var flow = new Color(dir.x * 0.5f + 0.5f, dir.z * 0.5f + 0.5f, slopes[k], 0);
                    cols.Add(flow);
                    cols.Add(flow);
                    chan.Add(new Vector2(-1, half));
                    chan.Add(new Vector2(1, half));
                    var i0 = pts[Mathf.Max(k - TurnReach, 0)];
                    var i1 = pts[Mathf.Min(k + TurnReach, last)];
                    float ix = p.x - i0.x, iz = p.z - i0.z, ox = i1.x - p.x, oz = i1.z - p.z;
                    float li = Mathf.Sqrt(ix * ix + iz * iz), lo = Mathf.Sqrt(ox * ox + oz * oz);
                    float bend = li * lo > 0 ? (ix * oz - iz * ox) / (li * lo) / (0.5f * (li + lo)) : 0;
                    float plunge = 0;
                    for (int j = Mathf.Max(k - PlungeReach, 0); j <= k; j++)
                        plunge = Mathf.Max(plunge, slopes[j] * (1 - (k - j) / (float)(PlungeReach + 1)));
                    var rv = new Vector4(Mathf.Clamp(bend * half * 3, -1, 1), plunge, s, 0);
                    reach.Add(rv);
                    reach.Add(rv);
                    if (k == 0) continue;
                    int v = b + 2 * k;
                    tris.Add(v - 2); tris.Add(v); tris.Add(v - 1);
                    tris.Add(v - 1); tris.Add(v); tris.Add(v + 1);
                }
            }
            if (verts.Count > 0) pieces.Add(RiverPiece($"Rivers {pieces.Count}", verts, cols, chan, reach, tris));
            return pieces;
        }

        static MeshParts RiverPiece(string name, List<Vector3> verts, List<Color> cols, List<Vector2> chan, List<Vector4> reach, List<int> tris)
        {
            var m = Flat(name, verts, cols, tris, default);
            m.Uvs = chan.ToArray();
            m.Uv1 = reach.ToArray();
            return m;
        }

        public static MeshParts Sea(int size, float cm)
        {
            float span = size * cm, pad = span * 1.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-pad, 0, -pad), new Vector3(-pad, 0, span + pad),
                new Vector3(span + pad, 0, -pad), new Vector3(span + pad, 0, span + pad),
            };
            return Flat("Sea", verts, null, new List<int> { 0, 1, 2, 2, 1, 3 }, SeaKind);
        }

        static MeshParts Flat(string name, List<Vector3> verts, List<Color> perVertex, List<int> tris, Color kind)
        {
            var cols = perVertex != null ? perVertex.ToArray() : new Color[verts.Count];
            var up = new Vector3[verts.Count];
            for (int k = 0; k < up.Length; k++) { up[k] = Vector3.up; if (perVertex == null) cols[k] = kind; }
            return new MeshParts { Name = name, Verts = verts.ToArray(), Norms = up, Cols = cols, Tris = tris.ToArray() };
        }
    }
}
