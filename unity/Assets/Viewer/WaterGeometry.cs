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
        public Vector4[] Uv1, Uv2;
        public int[] Tris;
    }

    public struct FallCurtain
    {
        public int K;
        public float LipX, LipZ, LipY;
        public float BaseX, BaseZ, BaseY;
        public float Width;
        public float DirX, DirZ;
        public float PoolRadius;
        public float Discharge;
        public float Drop => LipY - BaseY;
    }

    public static class WaterGeometry
    {
        const int RiverPieceVertices = 16000;
        public const float RiverLift = 0.1f, LakeLift = 0.5f;
        const float SlopeRange = 0.1f;
        const int SlopeReach = 8, TurnReach = 6, PlungeReach = 16;
        public const float FallMinDrop = 0.3f;
        const float FallMargin = 0.3f, FallThrow = 1.5f, FallLipMin = 0.2f, FallLipMax = 0.75f, FallPoolBase = 2f, FallPoolPerDrop = 1.8f, FallDischargePerArea = 2f, FallSink = 0.5f, FootGap = 0.5f, FootMax = 0.95f, MistPerWidth = 7f, MistLift = 0.35f;
        const int CurtainRows = 7, MistMax = 4;
        static readonly Vector4 NoPool = new Vector4(-1e5f, 0, 0, 0);
        static readonly Color SeaKind = new Color(0.5f, 0.5f, 0, 1), LakeKind = new Color(0.5f, 0.5f, 0, 0.5f);

        public static Mesh ToMesh(MeshParts d)
        {
            var mesh = new Mesh { name = d.Name, indexFormat = d.Verts.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(d.Verts);
            mesh.SetNormals(d.Norms);
            mesh.SetColors(d.Cols);
            if (d.Uvs != null) mesh.SetUVs(0, d.Uvs);
            if (d.Uv1 != null) mesh.SetUVs(1, d.Uv1);
            if (d.Uv2 != null) mesh.SetUVs(2, d.Uv2);
            mesh.SetTriangles(d.Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static bool FallAt(RiverLine r, int k, out FallCurtain f, out float lipT)
        {
            f = default;
            lipT = 0;
            if (k < 1 || k >= r.X.Length || r.P[k] <= 0) return false;
            float drop = r.S[k - 1] - r.S[k];
            var d = new Vector2(r.X[k] - r.X[k - 1], r.Z[k] - r.Z[k - 1]);
            if (drop < FallMinDrop || d.sqrMagnitude < 1e-6f) return false;
            lipT = Mathf.Clamp((drop + FallMargin - r.D[k - 1] - r.P[k - 1]) / Mathf.Max(drop + r.P[k] - r.P[k - 1], 1e-3f), FallLipMin, FallLipMax);
            d.Normalize();
            float lx = Mathf.Lerp(r.X[k - 1], r.X[k], lipT), lz = Mathf.Lerp(r.Z[k - 1], r.Z[k], lipT), th = FallThrowAt(drop), w = Mathf.Lerp(r.W[k - 1], r.W[k], lipT);
            f = new FallCurtain
            {
                K = k - 1, LipX = lx, LipZ = lz, LipY = r.S[k - 1] + RiverLift,
                BaseX = lx + d.x * th, BaseZ = lz + d.y * th, BaseY = r.S[k] + RiverLift,
                Width = 2 * Mathf.Max(w, 6f), DirX = d.x, DirZ = d.y,
                PoolRadius = FallPoolBase + FallPoolPerDrop * drop,
                Discharge = FallDischargePerArea * w * Mathf.Lerp(r.D[k - 1], r.D[k], lipT),
            };
            return true;
        }

        public static float FallThrowAt(float height) => FallThrow * Mathf.Sqrt(2 * Mathf.Max(height, 0) / 9.81f);

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

        sealed class RiverBuffers
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Color> Cols = new List<Color>();
            public readonly List<Vector2> Chan = new List<Vector2>();
            public readonly List<Vector4> Reach = new List<Vector4>(), Fall = new List<Vector4>();
            public readonly List<int> Tris = new List<int>();

            public void Clear() { Verts.Clear(); Cols.Clear(); Chan.Clear(); Reach.Clear(); Fall.Clear(); Tris.Clear(); }

            public void Row(Vector3 p, Vector3 dir, float half, float slope, Vector4 reach, Vector4 fall, bool join)
            {
                var side = new Vector3(dir.z, 0, -dir.x) * half;
                Verts.Add(p - side);
                Verts.Add(p + side);
                var flow = new Color(dir.x * 0.5f + 0.5f, dir.z * 0.5f + 0.5f, slope, 0);
                Cols.Add(flow);
                Cols.Add(flow);
                Chan.Add(new Vector2(-1, half));
                Chan.Add(new Vector2(1, half));
                Reach.Add(reach);
                Reach.Add(reach);
                Fall.Add(fall);
                Fall.Add(fall);
                if (!join) return;
                int v = Verts.Count - 2;
                Tris.Add(v - 2); Tris.Add(v); Tris.Add(v - 1);
                Tris.Add(v - 1); Tris.Add(v); Tris.Add(v + 1);
            }

            public void Curtain(FallCurtain f, float along)
            {
                float h = f.Drop + FallSink, half = 0.5f * f.Width;
                var fwd = new Vector3(f.DirX, 0, f.DirZ);
                var side = new Vector3(f.DirZ, 0, -f.DirX) * half;
                var lip = new Vector3(f.LipX, f.LipY, f.LipZ);
                var flow = new Color(f.DirX * 0.5f + 0.5f, f.DirZ * 0.5f + 0.5f, 1, 0);
                var reach = new Vector4(f.Discharge, f.PoolRadius, along, 0);
                int b = Verts.Count;
                for (int j = 0; j < CurtainRows; j++)
                {
                    float u = j / (CurtainRows - 1f), y = u * u * h;
                    var p = lip + fwd * FallThrowAt(y) - Vector3.up * y;
                    Verts.Add(p - side);
                    Verts.Add(p + side);
                    Cols.Add(flow);
                    Cols.Add(flow);
                    Chan.Add(new Vector2(-1, half));
                    Chan.Add(new Vector2(1, half));
                    Reach.Add(reach);
                    Reach.Add(reach);
                    var fv = new Vector4(y / h, h, along, 1);
                    Fall.Add(fv);
                    Fall.Add(fv);
                    if (j == 0) continue;
                    int v = b + 2 * j;
                    Tris.Add(v - 2); Tris.Add(v); Tris.Add(v - 1);
                    Tris.Add(v - 1); Tris.Add(v); Tris.Add(v + 1);
                    Tris.Add(v - 2); Tris.Add(v - 1); Tris.Add(v);
                    Tris.Add(v - 1); Tris.Add(v + 1); Tris.Add(v);
                }
                int mists = Mathf.Clamp(Mathf.RoundToInt(f.Width / MistPerWidth), 1, MistMax);
                float radius = Mathf.Clamp(0.5f * f.Drop + 0.3f * f.Width / mists, 2f, 14f);
                var foot = new Vector3(f.BaseX, f.BaseY + MistLift * radius, f.BaseZ) + fwd * (0.3f * radius);
                for (int m = 0; m < mists; m++)
                {
                    var c = foot + side * ((m + 0.5f) / mists * 2 - 1) * 0.8f;
                    int q = Verts.Count;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Verts.Add(c);
                        Cols.Add(flow);
                        Chan.Add(Vector2.zero);
                        Reach.Add(reach);
                        Fall.Add(new Vector4((corner & 1) * 2 - 1, (corner >> 1) * 2 - 1, radius, 2));
                    }
                    Tris.Add(q); Tris.Add(q + 2); Tris.Add(q + 1);
                    Tris.Add(q + 1); Tris.Add(q + 2); Tris.Add(q + 3);
                }
            }

            public MeshParts Piece(string name)
            {
                var m = Flat(name, Verts, Cols, Tris, default);
                m.Uvs = Chan.ToArray();
                m.Uv1 = Reach.ToArray();
                m.Uv2 = Fall.ToArray();
                return m;
            }
        }

        public static List<MeshParts> Rivers(GeneratedWorld world)
        {
            var pieces = new List<MeshParts>();
            var buf = new RiverBuffers();
            foreach (var r in NearField.Lines(world))
            {
                if (buf.Verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(buf.Piece($"Rivers {pieces.Count}"));
                    buf.Clear();
                }
                var pts = new List<Vector4>();
                for (int k = 0; k < r.X.Length; k++)
                    pts.Add(new Vector4(r.X[k], r.S[k], r.Z[k], 2 * r.W[k]));
                int last = pts.Count - 1;
                var slopes = new float[pts.Count];
                for (int k = 0; k < pts.Count; k++)
                {
                    var u = pts[Mathf.Max(k - SlopeReach, 0)];
                    var d = pts[Mathf.Min(k + SlopeReach, last)];
                    float run = new Vector2(d.x - u.x, d.z - u.z).magnitude;
                    slopes[k] = Mathf.Clamp01((run > 0 ? Mathf.Max(0, u.y - d.y) / run : 0) / SlopeRange);
                }
                float s = 0;
                var pool = NoPool;
                for (int k = 0; k < pts.Count; k++)
                {
                    var a = pts[Mathf.Max(k - 1, 0)];
                    var z = pts[Mathf.Min(k + 1, last)];
                    var dir = new Vector3(z.x - a.x, 0, z.z - a.z).normalized;
                    float half = Mathf.Max(pts[k].w * 0.5f, 6f), seg = 0;
                    var p = new Vector3(pts[k].x, 0, pts[k].z);
                    if (k > 0) s += seg = new Vector2(p.x - pts[k - 1].x, p.z - pts[k - 1].z).magnitude;
                    p.y = pts[k].y + RiverLift;
                    var i0 = pts[Mathf.Max(k - TurnReach, 0)];
                    var i1 = pts[Mathf.Min(k + TurnReach, last)];
                    float ix = p.x - i0.x, iz = p.z - i0.z, ox = i1.x - p.x, oz = i1.z - p.z;
                    float li = Mathf.Sqrt(ix * ix + iz * iz), lo = Mathf.Sqrt(ox * ox + oz * oz);
                    float bend = li * lo > 0 ? (ix * oz - iz * ox) / (li * lo) / (0.5f * (li + lo)) : 0;
                    float plunge = 0;
                    for (int j = Mathf.Max(k - PlungeReach, 0); j <= k; j++)
                        plunge = Mathf.Max(plunge, slopes[j] * (1 - (k - j) / (float)(PlungeReach + 1)));
                    var rv = new Vector4(Mathf.Clamp(bend * half * 3, -1, 1), plunge, s, 0);
                    bool join = k > 0;
                    if (k > 0 && FallAt(r, k, out var f, out float lt))
                    {
                        var fd = new Vector3(f.DirX, 0, f.DirZ);
                        var prev = new Vector3(pts[k - 1].x, 0, pts[k - 1].z);
                        float sl = s - seg * (1 - lt), th = FallThrowAt(f.Drop), ft = Mathf.Min(lt + (th + FootGap) / Mathf.Max(seg, 1e-3f), FootMax);
                        var lipRow = new Vector3(f.LipX, f.LipY, f.LipZ);
                        buf.Row(lipRow, fd, 0.5f * f.Width, slopes[k - 1], new Vector4(rv.x, rv.y, sl, 0), pool, true);
                        buf.Curtain(f, sl);
                        pool = new Vector4(sl + th, f.Drop, f.PoolRadius, 0);
                        var footRow = Vector3.Lerp(prev, new Vector3(p.x, 0, p.z), ft);
                        footRow.y = f.BaseY;
                        buf.Row(footRow, fd, Mathf.Lerp(0.5f * f.Width, half, (ft - lt) / (1 - lt)), slopes[k], new Vector4(rv.x, rv.y, s - seg * (1 - ft), 0), pool, false);
                    }
                    buf.Row(p, dir, half, slopes[k], rv, pool, join);
                }
            }
            if (buf.Verts.Count > 0) pieces.Add(buf.Piece($"Rivers {pieces.Count}"));
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
