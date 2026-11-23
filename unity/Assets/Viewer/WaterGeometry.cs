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
        public Vector4[] Uv1, Uv2, Uv3;
        public int[] Tris;
    }

    public struct FallCurtain
    {
        public int K;
        public float LipX, LipZ, LipY;
        public float BaseX, BaseZ, BaseY;
        public float Width, Bow;
        public float DirX, DirZ;
        public float PoolRadius;
        public float Discharge;
        public int Liquid;
        public float U0, U1;
        public float[] Runs;
        public float Drop => LipY - BaseY;
        public float Mid => 0.5f * (U0 + U1);
        public float Span => 0.5f * (U1 - U0);
        public float RunWidth => Width * Span;
        public float SideAt(int c, int cols) => Mid + Span * (2f * c / (cols - 1) - 1);
    }

    public static class WaterGeometry
    {
        const int RiverPieceVertices = 16000, MistPieceVertices = 2048;
        public const float RiverLift = 0.1f, LakeLift = 0.5f;
        const float SlopeRange = 0.1f;
        const int SlopeReach = 8, TurnReach = 6, PlungeReach = 16;
        public const float FallMinDrop = 0.3f;
        const float FallMargin = 0.3f, FallLipMin = 0.2f, FallLipMax = 0.75f, FallScour = 1.9f, FallScourDrop = 0.225f, FallScourQ = 0.54f, FallPoolMin = 0.6f, FallPoolMax = 2f, FallDischargePerArea = 2f, FallSink = 0.5f, FootGap = 0.5f, FootMax = 0.95f, PoolSpread = 2f, MistPerWidth = 7f, MistPerHeight = 10f, MistPerFlow = 0.5f, MistPerPool = 0.5f, MistPerDrop = 0.25f, MistPhase = 0.618034f, MistSpread = 0.8f, SprayStep = 3f, SprayPerPool = 0.35f, SprayPerDrop = 0.12f, SpraySpread = 0.95f, SprayAhead = 0.2f, VeilWidth = 1.1f, VeilHeight = 0.4f, VeilAhead = 1f;
        public const float MistPad = 40f;
        const float SteamRadius = 0.55f, SteamRise = 2.2f, SpringOverlap = 0.3f, ColumnRadius = 0.9f;
        public const float ColumnPerRadius = 14f;
        const int SteamPuffs = 2, SpringSides = 32, JetPuffs = 7, ColumnSteamPuffs = 3;
        const int CurtainRowsMin = 7, CurtainRowsMax = 24, MistMax = 6, SprayMax = 12;
        const int CurtainColsMin = 2, CurtainColsMax = 9;
        const float CurtainRowStep = 2f, CurtainColStep = 4f, HugClear = 0.4f, HugRamp = 2f, HugClearPerM = 0.04f, HugUnder = 0.3f, HugReach = 2f, HugReachPerM = 0.6f, HugStep = 0.5f, HugSteps = 12f, HugGap = 0.3f, HugBank = 0.3f;
        static readonly Vector4 NoPool = new Vector4(-1e5f, 0, 0, 0);
        static readonly Color SeaKind = new Color(0.5f, 0.5f, 0, 1), LakeKind = new Color(0.5f, 0.5f, 0, 0.5f), SpringKind = new Color(0.5f, 0.5f, 0, 0);

        public static Mesh ToMesh(MeshParts d)
        {
            var mesh = new Mesh { name = d.Name, indexFormat = d.Verts.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(d.Verts);
            mesh.SetNormals(d.Norms);
            mesh.SetColors(d.Cols);
            if (d.Uvs != null) mesh.SetUVs(0, d.Uvs);
            if (d.Uv1 != null) mesh.SetUVs(1, d.Uv1);
            if (d.Uv2 != null) mesh.SetUVs(2, d.Uv2);
            if (d.Uv3 != null) mesh.SetUVs(3, d.Uv3);
            mesh.SetTriangles(d.Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static bool FallAt(RiverLine r, int k, out FallCurtain f, out float lipT)
        {
            f = default;
            lipT = 0;
            if (k < 1 || k >= r.X.Length) return false;
            int at = r.Lip == null ? -1 : System.Array.IndexOf(r.Lip, k - 1);
            if (at >= 0)
            {
                var rf = r.Falls[at];
                f = new FallCurtain
                {
                    K = k - 1, LipX = rf.LipX, LipZ = rf.LipZ, LipY = rf.LipY + RiverLift,
                    BaseX = rf.LipX + rf.DirX * FallThrowAt(rf.Drop), BaseZ = rf.LipZ + rf.DirZ * FallThrowAt(rf.Drop), BaseY = rf.BaseY + RiverLift,
                    Width = 2 * rf.SheetHalf, Bow = rf.ShelfBow, DirX = rf.DirX, DirZ = rf.DirZ,
                    PoolRadius = rf.PoolRadius, Discharge = rf.Discharge, Liquid = r.Liquid, U0 = -1, U1 = 1,
                };
                if (rf.LipPts != null)
                {
                    var runs = Hydrology.LipRuns(rf);
                    f.Runs = new float[runs.Count];
                    for (int i = 0; i < runs.Count; i++) f.Runs[i] = runs[i] / rf.SheetHalf;
                }
                return true;
            }
            if (r.P[k] <= 0) return false;
            float drop = r.S[k - 1] - r.S[k];
            var d = new Vector2(r.X[k] - r.X[k - 1], r.Z[k] - r.Z[k - 1]);
            if (drop < FallMinDrop || d.sqrMagnitude < 1e-6f) return false;
            lipT = Mathf.Clamp((drop + FallMargin - r.D[k - 1] - r.P[k - 1]) / Mathf.Max(drop + r.P[k] - r.P[k - 1], 1e-3f), FallLipMin, FallLipMax);
            d.Normalize();
            float lx = Mathf.Lerp(r.X[k - 1], r.X[k], lipT), lz = Mathf.Lerp(r.Z[k - 1], r.Z[k], lipT), th = FallThrowAt(drop), w = Mathf.Lerp(r.W[k - 1], r.W[k], lipT);
            float width = Hydrology.SheetWidth(2 * w), discharge = FallDischargePerArea * w * Mathf.Lerp(r.D[k - 1], r.D[k], lipT);
            f = new FallCurtain
            {
                K = k - 1, LipX = lx, LipZ = lz, LipY = r.S[k - 1] + RiverLift,
                BaseX = lx + d.x * th, BaseZ = lz + d.y * th, BaseY = r.S[k] + RiverLift,
                Width = width, DirX = d.x, DirZ = d.y,
                PoolRadius = PoolRadiusOf(drop, width, discharge),
                Discharge = discharge, Liquid = r.Liquid, U0 = -1, U1 = 1,
            };
            return true;
        }

        public static float PoolRadiusOf(float drop, float width, float discharge)
        {
            float scour = FallScour * Mathf.Pow(Mathf.Max(drop, 0), FallScourDrop) * Mathf.Pow(Mathf.Max(discharge, 0) / width, FallScourQ);
            return Mathf.Clamp(0.5f * width + scour, FallPoolMin * width, FallPoolMax * width);
        }

        public static FallCurtain[] Split(FallCurtain f)
        {
            if (f.Runs == null || f.Runs.Length < 4) return new[] { f };
            var parts = new FallCurtain[f.Runs.Length / 2];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = f;
                parts[i].U0 = f.Runs[2 * i];
                parts[i].U1 = f.Runs[2 * i + 1];
            }
            return parts;
        }

        public static int CurtainRowsOf(float height) => Mathf.Clamp(Mathf.RoundToInt(height / CurtainRowStep), CurtainRowsMin, CurtainRowsMax);

        public static float FallThrowAt(float height) => Hydrology.FallThrow(height);

        public static int CurtainColsOf(float width) => Mathf.Clamp(Mathf.RoundToInt(width / CurtainColStep) + 1, CurtainColsMin, CurtainColsMax);

        public static float[] CurtainOffsets(FallCurtain f, TerrainDetail detail)
        {
            float h = f.Drop + FallSink, half = 0.5f * f.Width;
            int rows = CurtainRowsOf(h), cols = CurtainColsOf(f.RunWidth);
            var off = new float[rows * cols];
            for (int j = 0; j < rows; j++)
            {
                float u = j / (rows - 1f), y = u * u * h;
                for (int c = 0; c < cols; c++)
                {
                    float u2 = f.SideAt(c, cols), sc = u2 * half, a = f.Bow * u2 * u2, from = j > 0 ? off[(j - 1) * cols + c] : 0;
                    off[j * cols + c] = HugAt(detail, f, f.LipX + f.DirZ * sc + f.DirX * a, f.LipZ - f.DirX * sc + f.DirZ * a, y, from);
                }
            }
            var tmp = new float[rows * cols];
            Swell(off, tmp, rows, cols, cols, 1);
            Swell(off, tmp, cols, rows, 1, cols);
            for (int c = 0; c < cols; c++) off[c] = 0;
            return off;
        }

        static float HugAt(TerrainDetail detail, FallCurtain f, float x, float z, float y, float from)
        {
            float free = FallThrowAt(y), start = Mathf.Max(free, from);
            if (detail == null || y <= 0) return start;
            float floor = Mathf.Max(f.LipY - y - HugClear * Mathf.Clamp01(y / HugRamp) - HugClearPerM * y, f.BaseY - HugUnder);
            float ground = detail.Sample(x + f.DirX * start, z + f.DirZ * start);
            if (ground <= floor || ground > f.LipY + HugBank) return start;
            float far = free + HugReachPerM * y + HugReach, step = Mathf.Max(HugStep, (far - start) / HugSteps), prev = start;
            for (float t = start + step; t <= far + 1e-3f; t += step)
            {
                if (detail.Sample(x + f.DirX * t, z + f.DirZ * t) > floor) { prev = t; continue; }
                float lo = prev, hi = t;
                for (int k = 0; k < 4; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (detail.Sample(x + f.DirX * mid, z + f.DirZ * mid) > floor) lo = mid;
                    else hi = mid;
                }
                return hi + HugGap;
            }
            return start;
        }

        static void Swell(float[] a, float[] tmp, int lines, int len, int lineStride, int step)
        {
            for (int l = 0; l < lines; l++)
            {
                int o = l * lineStride;
                for (int i = 0; i < len; i++)
                {
                    float m = a[o + i * step];
                    if (i > 0) m = Mathf.Max(m, a[o + (i - 1) * step]);
                    if (i < len - 1) m = Mathf.Max(m, a[o + (i + 1) * step]);
                    tmp[o + i * step] = m;
                }
                for (int i = 0; i < len; i++)
                {
                    float p = tmp[o + Mathf.Max(i - 1, 0) * step], q = tmp[o + Mathf.Min(i + 1, len - 1) * step];
                    a[o + i * step] = 0.25f * (p + 2 * tmp[o + i * step] + q);
                }
            }
        }

        public static MeshParts Lakes(GeneratedWorld world, int cx, int cy, int cells)
        {
            int n = world.Config.Size, x1 = Mathf.Min(cx + cells, n), y1 = Mathf.Min(cy + cells, n), row = x1 - cx + 1;
            float cm = world.Config.CellMeters;
            var corner = new int[row * (y1 - cy + 1)];
            for (int k = 0; k < corner.Length; k++) corner[k] = -1;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var kinds = new List<Vector4>();
            var tints = new List<Vector4>();
            int Corner(int X, int Y)
            {
                ref int v = ref corner[(Y - cy) * row + X - cx];
                if (v >= 0) return v;
                float h = 0, a = 0, b = 0, c = 0;
                int count = 0, liquid = -1;
                for (int j = Y - 1; j <= Y; j++)
                    for (int i = X - 1; i <= X; i++)
                    {
                        if (i < 0 || j < 0 || i >= n || j >= n) continue;
                        float ch = LakeHeight(world, i, j);
                        if (ch == float.MinValue) continue;
                        int id = LakeAt(world, i, j);
                        if (id < 0) continue;
                        var lk = world.Water.Lakes[id];
                        var m = WaterTint.MixAt(lk, n, X - 0.5f, Y - 0.5f);
                        h += ch; a += m.A; b += m.B; c += m.C; count++;
                        if (liquid < 0) liquid = lk.Liquid;
                    }
                v = verts.Count;
                count = Mathf.Max(count, 1);
                verts.Add(new Vector3((X - 0.5f) * cm, h / count, (Y - 0.5f) * cm));
                kinds.Add(new Vector4(0, 0, 0, Mathf.Max(liquid, 0)));
                tints.Add(new Vector4(a / count, b / count, c / count, 0));
                return v;
            }
            for (int y = cy; y < y1; y++)
                for (int x = cx; x < x1; x++)
                {
                    if (LakeHeight(world, x, y) == float.MinValue) continue;
                    int v00 = Corner(x, y), v01 = Corner(x, y + 1), v10 = Corner(x + 1, y), v11 = Corner(x + 1, y + 1);
                    tris.Add(v00); tris.Add(v01); tris.Add(v10);
                    tris.Add(v10); tris.Add(v01); tris.Add(v11);
                }
            if (verts.Count == 0) return null;
            var lake = Flat($"Water {cx},{cy}", verts, null, tris, LakeKind);
            lake.Uvs = new Vector2[verts.Count];
            lake.Uv1 = kinds.ToArray();
            lake.Uv2 = new Vector4[verts.Count];
            lake.Uv3 = tints.ToArray();
            return lake;
        }

        static Vector4 Tint(WaterMix m) => new Vector4(m.A, m.B, m.C, 0);

        static int LakeAt(GeneratedWorld world, int x, int y)
        {
            int n = world.Config.Size, id = world.Water.LakeId[y * n + x];
            for (int k = 0; k < 8 && id < 0; k++)
            {
                int nx = x + Stabby.World.Grid.DX[k], ny = y + Stabby.World.Grid.DY[k];
                if (nx >= 0 && ny >= 0 && nx < n && ny < n) id = world.Water.LakeId[ny * n + nx];
            }
            return id;
        }

        static bool Sprays(GeneratedWorld world, int liquid) => world.Materials == null || (world.Materials.Liquids[liquid].Spray != LiquidDef.SprayNone && world.Materials.Liquids[liquid].SprayAmount > 0);

        static float SprayAmount(GeneratedWorld world, int liquid) => world.Materials == null ? 1 : world.Materials.Liquids[liquid].SprayAmount;

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
            public readonly List<Vector4> Reach = new List<Vector4>(), Fall = new List<Vector4>(), Tints = new List<Vector4>();
            public readonly List<int> Tris = new List<int>();
            public Vector4 Tint;

            public void Clear() { Verts.Clear(); Cols.Clear(); Chan.Clear(); Reach.Clear(); Fall.Clear(); Tints.Clear(); Tris.Clear(); }

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
                Tints.Add(Tint);
                Tints.Add(Tint);
                Fall.Add(fall);
                Fall.Add(fall);
                if (!join) return;
                int v = Verts.Count - 2;
                Tris.Add(v - 2); Tris.Add(v); Tris.Add(v - 1);
                Tris.Add(v - 1); Tris.Add(v); Tris.Add(v + 1);
            }

            public void Shelf(FallCurtain f, float slope, Vector4 reach, Vector4 fall)
            {
                float half = 0.5f * f.Width;
                var fwd = new Vector3(f.DirX, 0, f.DirZ);
                var side = new Vector3(f.DirZ, 0, -f.DirX) * half;
                var lip = new Vector3(f.LipX, f.LipY, f.LipZ);
                var flow = new Color(f.DirX * 0.5f + 0.5f, f.DirZ * 0.5f + 0.5f, slope, 0);
                int b = Verts.Count, cols = CurtainColsOf(f.RunWidth);
                for (int c = 0; c < cols; c++)
                {
                    float u = f.SideAt(c, cols);
                    Verts.Add(lip + side * u);
                    Verts.Add(lip + side * u + fwd * (f.Bow * u * u));
                    for (int e = 0; e < 2; e++)
                    {
                        Cols.Add(flow);
                        Chan.Add(new Vector2(2f * c / (cols - 1) - 1, half * f.Span));
                        Reach.Add(reach);
                        Tints.Add(Tint);
                        Fall.Add(fall);
                    }
                    if (c == 0) continue;
                    int v = b + 2 * c;
                    Tris.Add(v - 2); Tris.Add(v - 1); Tris.Add(v);
                    Tris.Add(v); Tris.Add(v - 1); Tris.Add(v + 1);
                }
            }

            public float Curtain(FallCurtain f, float along, TerrainDetail detail, MistBuffers mist)
            {
                float h = f.Drop + FallSink, half = 0.5f * f.Width;
                var fwd = new Vector3(f.DirX, 0, f.DirZ);
                var side = new Vector3(f.DirZ, 0, -f.DirX) * half;
                var lip = new Vector3(f.LipX, f.LipY, f.LipZ);
                var flow = new Color(f.DirX * 0.5f + 0.5f, f.DirZ * 0.5f + 0.5f, 1, 0);
                var reach = new Vector4(f.Discharge, f.PoolRadius, along, f.Liquid);
                int b = Verts.Count, rows = CurtainRowsOf(h), cols = CurtainColsOf(f.RunWidth);
                var off = CurtainOffsets(f, detail);
                for (int j = 0; j < rows; j++)
                {
                    float u = j / (rows - 1f), y = u * u * h;
                    var fv = new Vector4(y / h, h, along, 1);
                    for (int c = 0; c < cols; c++)
                    {
                        float sc = f.SideAt(c, cols);
                        Verts.Add(lip + side * sc + fwd * (f.Bow * sc * sc + off[j * cols + c]) - Vector3.up * y);
                        Cols.Add(flow);
                        Chan.Add(new Vector2(2f * c / (cols - 1) - 1, half * f.Span));
                        Reach.Add(reach);
                        Tints.Add(Tint);
                        Fall.Add(fv);
                        if (j == 0 || c == 0) continue;
                        int v = b + j * cols + c, a = v - cols;
                        Tris.Add(a - 1); Tris.Add(v - 1); Tris.Add(a);
                        Tris.Add(a); Tris.Add(v - 1); Tris.Add(v);
                        Tris.Add(a - 1); Tris.Add(a); Tris.Add(v - 1);
                        Tris.Add(a); Tris.Add(v); Tris.Add(v - 1);
                    }
                }
                float shift = 0;
                for (int c = 0; c < cols; c++) shift += off[(rows - 1) * cols + c];
                shift = Mathf.Max(0, shift / cols - FallThrowAt(h));
                var push = fwd * shift + side * f.Mid + fwd * (f.Bow * f.Mid * f.Mid);
                mist?.Foot(f, along, h, push, fwd, side);
                return shift;
            }

            public MeshParts Piece(string name)
            {
                var m = Flat(name, Verts, Cols, Tris, default);
                m.Uvs = Chan.ToArray();
                m.Uv1 = Reach.ToArray();
                m.Uv2 = Fall.ToArray();
                m.Uv3 = Tints.ToArray();
                return m;
            }
        }

        sealed class MistBuffers
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Color> Cols = new List<Color>();
            public readonly List<Vector2> Size = new List<Vector2>();
            public readonly List<Vector4> Corner = new List<Vector4>();
            public readonly List<int> Tris = new List<int>();
            public float Amount = 1;
            public int Liquid;

            public void Foot(FallCurtain f, float along, float h, Vector3 push, Vector3 fwd, Vector3 side)
            {
                float flowQ = Mathf.Clamp01(Mathf.Log(1 + f.Discharge, 2) / 9), seed = along * 0.0137f + f.Mid * 0.37f;
                var basePos = new Vector3(f.BaseX, f.BaseY, f.BaseZ) + push;
                float spray = Mathf.Clamp(SprayPerPool * f.PoolRadius + SprayPerDrop * f.Drop, 1.2f, 6f);
                int sprays = Mathf.Clamp(Mathf.RoundToInt((Mathf.RoundToInt(f.RunWidth / SprayStep) + 1) * Amount), 2, SprayMax);
                for (int m = 0; m < sprays; m++)
                    Puff(At(f, basePos, fwd, side, ((m + 0.5f) / sprays * 2 - 1) * SpraySpread * f.Span) + fwd * (SprayAhead * spray), fwd, flowQ, Mathf.Repeat(m * MistPhase + seed, 1f), new Vector2(spray, f.Drop), 0);
                int mists = Mathf.Clamp(Mathf.RoundToInt((f.RunWidth / MistPerWidth + f.Drop / MistPerHeight + MistPerFlow * Mathf.Sqrt(f.Discharge)) * Amount), 1, MistMax);
                float radius = Mathf.Clamp(MistPerPool * f.PoolRadius + MistPerDrop * f.Drop, 2.5f, 14f);
                for (int m = 0; m < mists; m++)
                {
                    var c = At(f, basePos, fwd, side, ((m + 0.5f) / mists * 2 - 1) * MistSpread * f.Span) + fwd * (0.3f * radius);
                    float phase = Mathf.Repeat(m * MistPhase + seed + 0.29f, 1f);
                    Puff(c, fwd, flowQ, phase, new Vector2(radius, f.Drop), 1);
                    Puff(c, fwd, flowQ, Mathf.Repeat(phase + 0.5f, 1f), new Vector2(radius, f.Drop), 1);
                }
                Puff(basePos + fwd * VeilAhead, fwd, flowQ, Mathf.Repeat(seed + 0.5f, 1f), new Vector2(0.5f * VeilWidth * f.RunWidth, VeilHeight * h), 2);
            }

            public void Vent(Vector3 c, float phase, float radius, float rise) => Puff(c, Vector3.right, 0, phase, new Vector2(radius, rise), 3);

            public void Column(Vector3 c, float duty, float offset, float steam, float phase, float radius, float height, float interval)
            {
                int q = Verts.Count;
                var col = new Color(duty, offset, steam, phase);
                for (int corner = 0; corner < 4; corner++)
                {
                    Verts.Add(c);
                    Cols.Add(col);
                    Size.Add(new Vector2(radius, height));
                    Corner.Add(new Vector4((corner & 1) * 2 - 1, (corner >> 1) * 2 - 1, 4, interval));
                }
                Tris.Add(q); Tris.Add(q + 2); Tris.Add(q + 1);
                Tris.Add(q + 1); Tris.Add(q + 2); Tris.Add(q + 3);
            }

            static Vector3 At(FallCurtain f, Vector3 basePos, Vector3 fwd, Vector3 side, float mu)
            {
                float at = f.Mid + mu;
                return basePos + side * mu + fwd * (f.Bow * (at * at - f.Mid * f.Mid));
            }

            void Puff(Vector3 c, Vector3 fwd, float flowQ, float phase, Vector2 size, int kind)
            {
                int q = Verts.Count;
                var col = new Color(fwd.x * 0.5f + 0.5f, fwd.z * 0.5f + 0.5f, flowQ, phase);
                for (int corner = 0; corner < 4; corner++)
                {
                    Verts.Add(c);
                    Cols.Add(col);
                    Size.Add(size);
                    Corner.Add(new Vector4((corner & 1) * 2 - 1, (corner >> 1) * 2 - 1, kind, kind < 3 ? Liquid : 0));
                }
                Tris.Add(q); Tris.Add(q + 2); Tris.Add(q + 1);
                Tris.Add(q + 1); Tris.Add(q + 2); Tris.Add(q + 3);
            }

            public MeshParts Piece(string name)
            {
                var m = Flat(name, Verts, Cols, Tris, default);
                m.Uvs = Size.ToArray();
                m.Uv1 = Corner.ToArray();
                Verts.Clear(); Cols.Clear(); Size.Clear(); Corner.Clear(); Tris.Clear();
                return m;
            }
        }

        public static void Springs(ObjectField field, List<MeshParts> mists, List<MeshParts> water)
        {
            var mist = new MistBuffers();
            var pools = new List<float>();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var tags = new List<float>();
            var rims = new List<float>();
            foreach (var o in field.Discoveries())
            {
                if (!field.IsVent(o)) continue;
                int kind = field.VentKind(o);
                field.SpringPoolsAt(o, pools);
                float u = Structures.SpringU * o.Size;
                for (int k = 0; k < pools.Count; k += ObjectField.SpringStride)
                {
                    var c = new Vector3(pools[k], pools[k + 4], pools[k + 1]);
                    float r = pools[k + 2], seed = Mathf.Repeat(c.x * 0.0137f + c.z * 0.0291f, 1f);
                    int puffs = kind == ObjectField.MudPot ? 1 : SteamPuffs;
                    for (int m = 0; m < puffs; m++) mist.Vent(c, Mathf.Repeat(seed + m * MistPhase, 1f), SteamRadius * r, SteamRise * r);
                    if (kind == ObjectField.Geyser)
                    {
                        Structures.GeyserCycle(field.VentSeed(o), out float interval, out float duration, out float offset);
                        float duty = duration / interval, start = offset / interval, h = ColumnPerRadius * r;
                        for (int m = 0; m < JetPuffs + ColumnSteamPuffs; m++)
                            mist.Column(c, duty, start, m < JetPuffs ? 0 : 1, Mathf.Repeat(seed + (m < JetPuffs ? (float)m / JetPuffs : m * MistPhase), 1f), ColumnRadius * r, h, interval);
                    }
                    Disc(c, r + SpringOverlap * u, field.LiquidOf(o), field.Materials, verts, tris, tags, rims);
                }
                if (mist.Verts.Count >= MistPieceVertices) mists.Add(mist.Piece($"Spring steam {mists.Count}"));
            }
            if (mist.Verts.Count > 0) mists.Add(mist.Piece($"Spring steam {mists.Count}"));
            var bogs = field.BogPools();
            int peat = field.Materials == null ? 0 : Mathf.Max(0, field.Materials.Liquid("peat"));
            for (int k = 0; k < bogs.Length; k += ObjectField.BogStride)
                Disc(new Vector3(bogs[k], bogs[k + 3], bogs[k + 1]), ObjectField.BogMargin(bogs[k + 2]), peat, field.Materials, verts, tris, tags, rims);
            if (verts.Count == 0) return;
            var pool = Flat("Liquid pools", verts, null, tris, SpringKind);
            pool.Uvs = new Vector2[verts.Count];
            pool.Uv1 = new Vector4[verts.Count];
            pool.Uv2 = new Vector4[verts.Count];
            for (int k = 0; k < verts.Count; k++) pool.Uv1[k] = new Vector4(rims[k], 0, 0, tags[k]);
            water.Add(pool);
        }

        static void Disc(Vector3 c, float edge, int liquid, MaterialLibrary lib, List<Vector3> verts, List<int> tris, List<float> tags, List<float> rims)
        {
            int b = verts.Count;
            bool opaque = lib != null && lib.Liquids[liquid].Opacity > 0.5f;
            for (int s = 0; s <= SpringSides; s++) { tags.Add(liquid); rims.Add(s > 0 && opaque ? 1 : 0); }
            verts.Add(c);
            for (int s = 0; s < SpringSides; s++)
            {
                float a = s * (2 * Mathf.PI / SpringSides);
                verts.Add(c + new Vector3(Mathf.Cos(a) * edge, 0, Mathf.Sin(a) * edge));
                tris.Add(b); tris.Add(b + 1 + (s + 1) % SpringSides); tris.Add(b + 1 + s);
            }
        }

        public static List<MeshParts> Rivers(GeneratedWorld world, TerrainDetail detail, List<MeshParts> mists)
        {
            var pieces = new List<MeshParts>();
            var buf = new RiverBuffers();
            var mist = mists == null ? null : new MistBuffers();
            foreach (var r in NearField.Lines(world))
            {
                if (buf.Verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(buf.Piece($"Rivers {pieces.Count}"));
                    buf.Clear();
                }
                if (mist != null && mist.Verts.Count >= MistPieceVertices) mists.Add(mist.Piece($"Fall mist {mists.Count}"));
                var spray = Sprays(world, r.Liquid) ? mist : null;
                if (spray != null) { spray.Amount = SprayAmount(world, r.Liquid); spray.Liquid = r.Liquid; }
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
                    if (pool.x > NoPool.x) half = Mathf.Max(half, pool.z * (1 - Mathf.Clamp01((s - pool.x) / (PoolSpread * pool.z))));
                    var rv = new Vector4(Mathf.Clamp(bend * half * 3, -1, 1), plunge, s, r.Liquid);
                    bool join = k > 0;
                    buf.Tint = Tint(r.Mix[k]);
                    if (k > 0 && FallAt(r, k, out var f, out float lt))
                    {
                        buf.Tint = Tint(WaterMix.Lerp(r.Mix[k - 1], r.Mix[k], lt));
                        var fd = new Vector3(f.DirX, 0, f.DirZ);
                        var prev = new Vector3(pts[k - 1].x, 0, pts[k - 1].z);
                        float sl = s - seg * (1 - lt), th = FallThrowAt(f.Drop), ft = Mathf.Min(lt + (th + FootGap) / Mathf.Max(seg, 1e-3f), FootMax);
                        var lipRow = new Vector3(f.LipX, f.LipY, f.LipZ);
                        buf.Row(lipRow, fd, 0.5f * f.Width, slopes[k - 1], new Vector4(rv.x, rv.y, sl, rv.w), pool, true);
                        float shove = 0;
                        foreach (var part in Split(f))
                        {
                            if (f.Bow > 0) buf.Shelf(part, slopes[k - 1], new Vector4(rv.x, rv.y, sl, rv.w), pool);
                            shove = Mathf.Max(shove, buf.Curtain(part, sl, detail, spray));
                        }
                        th += shove;
                        ft = Mathf.Min(lt + (th + FootGap) / Mathf.Max(seg, 1e-3f), FootMax);
                        pool = new Vector4(sl + th, f.Drop, f.PoolRadius, 0);
                        var footRow = Vector3.Lerp(prev, new Vector3(p.x, 0, p.z), ft);
                        footRow.y = f.BaseY;
                        buf.Tint = Tint(WaterMix.Lerp(r.Mix[k - 1], r.Mix[k], ft));
                        buf.Row(footRow, fd, Mathf.Lerp(0.5f * f.Width, half, (ft - lt) / (1 - lt)), slopes[k], new Vector4(rv.x, rv.y, s - seg * (1 - ft), rv.w), pool, false);
                    }
                    buf.Tint = Tint(r.Mix[k]);
                    buf.Row(p, dir, half, slopes[k], rv, pool, join);
                }
            }
            if (buf.Verts.Count > 0) pieces.Add(buf.Piece($"Rivers {pieces.Count}"));
            if (mist != null && mist.Verts.Count > 0) mists.Add(mist.Piece($"Fall mist {mists.Count}"));
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
