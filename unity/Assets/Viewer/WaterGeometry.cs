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
        const float RiverLift = 0.1f;

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
            var hy = world.Water;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int y = cy; y < Mathf.Min(cy + cells, n); y++)
                for (int x = cx; x < Mathf.Min(cx + cells, n); x++)
                {
                    int c = y * n + x;
                    if (world.Sea[c] || hy.LakeId[c] < 0) continue;
                    float h = Mathf.Max(hy.WaterSurface[c], world.Height[c]) + 0.5f;
                    int b = verts.Count;
                    verts.Add(new Vector3(x * cm - half, h, y * cm - half));
                    verts.Add(new Vector3(x * cm - half, h, y * cm + half));
                    verts.Add(new Vector3(x * cm + half, h, y * cm - half));
                    verts.Add(new Vector3(x * cm + half, h, y * cm + half));
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 3);
                }
            return verts.Count == 0 ? null : Flat($"Water {cx},{cy}", verts, tris, TerrainLook.Lake);
        }

        public static List<MeshParts> Rivers(GeneratedWorld world)
        {
            float cm = world.Config.CellMeters;
            var pieces = new List<MeshParts>();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var r in world.Water.Rivers)
            {
                if (r.Cells.Count < 2) continue;
                if (verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(Flat($"Rivers {pieces.Count}", verts, tris, TerrainLook.River));
                    verts.Clear();
                    tris.Clear();
                }
                var pts = new List<Vector4>();
                for (int k = 0; k < r.Cells.Count; k++)
                    pts.Add(new Vector4(r.X[k] * cm, Hydrology.Stage(world.Config, r, k), r.Y[k] * cm, r.Width[k]));
                for (int it = 0; it < 2; it++) pts = Chaikin(pts);
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
                    if (k == 0) continue;
                    int v = b + 2 * k;
                    tris.Add(v - 2); tris.Add(v); tris.Add(v - 1);
                    tris.Add(v - 1); tris.Add(v); tris.Add(v + 1);
                }
            }
            if (verts.Count > 0) pieces.Add(Flat($"Rivers {pieces.Count}", verts, tris, TerrainLook.River));
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
            return Flat("Sea", verts, new List<int> { 0, 1, 2, 2, 1, 3 }, TerrainLook.Sea);
        }

        static MeshParts Flat(string name, List<Vector3> verts, List<int> tris, float3 srgb)
        {
            var cols = new Color[verts.Count];
            var up = new Vector3[verts.Count];
            var l = TerrainLook.ToLinear(srgb);
            var lin = new Color(l.x, l.y, l.z);
            for (int k = 0; k < cols.Length; k++) { cols[k] = lin; up[k] = Vector3.up; }
            return new MeshParts { Name = name, Verts = verts.ToArray(), Norms = up, Cols = cols, Tris = tris.ToArray() };
        }

        static List<Vector4> Chaikin(List<Vector4> p)
        {
            var o = new List<Vector4>(p.Count * 2) { p[0] };
            for (int k = 0; k + 1 < p.Count; k++)
            {
                o.Add(Vector4.Lerp(p[k], p[k + 1], 0.25f));
                o.Add(Vector4.Lerp(p[k], p[k + 1], 0.75f));
            }
            o.Add(p[p.Count - 1]);
            return o;
        }
    }
}
