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
        public int[] Tris;
    }

    public static class WaterGeometry
    {
        const int RiverPieceVertices = 16000;
        public const float RiverLift = 0.1f, LakeLift = 0.5f;
        const float SlopeRange = 0.1f;
        const int SlopeReach = 8;
        static readonly Color SeaKind = new Color(0.5f, 0.5f, 0, 1), LakeKind = new Color(0.5f, 0.5f, 0, 0.5f);

        public static Mesh ToMesh(MeshParts d)
        {
            var mesh = new Mesh { name = d.Name, indexFormat = d.Verts.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(d.Verts);
            mesh.SetNormals(d.Norms);
            mesh.SetColors(d.Cols);
            if (d.Uvs != null) mesh.SetUVs(0, d.Uvs);
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
            var tris = new List<int>();
            foreach (var r in NearField.Lines(world))
            {
                if (verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(Flat($"Rivers {pieces.Count}", verts, cols, tris, default));
                    verts.Clear();
                    cols.Clear();
                    tris.Clear();
                }
                var pts = new List<Vector4>();
                for (int k = 0; k < r.X.Length; k++)
                    pts.Add(new Vector4(r.X[k], r.S[k], r.Z[k], 2 * r.W[k]));
                int b = verts.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    var a = pts[Mathf.Max(k - 1, 0)];
                    var z = pts[Mathf.Min(k + 1, pts.Count - 1)];
                    var dir = new Vector3(z.x - a.x, 0, z.z - a.z).normalized;
                    var side = new Vector3(dir.z, 0, -dir.x) * Mathf.Max(pts[k].w * 0.5f, 6f);
                    var p = new Vector3(pts[k].x, 0, pts[k].z);
                    p.y = pts[k].y + RiverLift;
                    verts.Add(p - side);
                    verts.Add(p + side);
                    var u = pts[Mathf.Max(k - SlopeReach, 0)];
                    var d = pts[Mathf.Min(k + SlopeReach, pts.Count - 1)];
                    float run = new Vector2(d.x - u.x, d.z - u.z).magnitude;
                    float slope = run > 0 ? Mathf.Max(0, u.y - d.y) / run : 0;
                    var flow = new Color(dir.x * 0.5f + 0.5f, dir.z * 0.5f + 0.5f, Mathf.Clamp01(slope / SlopeRange), 0);
                    cols.Add(flow);
                    cols.Add(flow);
                    if (k == 0) continue;
                    int v = b + 2 * k;
                    tris.Add(v - 2); tris.Add(v); tris.Add(v - 1);
                    tris.Add(v - 1); tris.Add(v); tris.Add(v + 1);
                }
            }
            if (verts.Count > 0) pieces.Add(Flat($"Rivers {pieces.Count}", verts, cols, tris, default));
            return pieces;
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
