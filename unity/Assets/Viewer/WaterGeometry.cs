using System.Collections.Generic;
using System.Threading.Tasks;
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
        public Vector4[] Uv1, Uv2, Uv3, Uv4;
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
        const float MouthReachM = 60f, MouthReachWidths = 3f, MouthFlare = 1.8f, MouthStepM = 8f;
        public const float SeaStep = 2f, SeaNearHalf = 64f, SeaGrowth = 1.05f;
        public const int LakeSwellSplit = 8, LakeSwellCells = 150;
        const float LakeSwellFullCells = 600f;
        public const float CurrentPerManning = 28f, CurrentMinSlope = 0.0005f, CurrentMax = 6f;
        public const float FallMinDrop = 0.3f;
        const float FallMargin = 0.3f, FallLipMin = 0.2f, FallLipMax = 0.75f, FallScour = 1.9f, FallScourDrop = 0.225f, FallScourQ = 0.54f, FallPoolMin = 0.6f, FallPoolMax = 2f, FallDischargePerArea = 2f, FallSink = 0.5f, LipBlend = 1.2f, FootGap = 0.5f, FootMax = 0.95f, PoolSpread = 2f, MistPerWidth = 7f, MistPerHeight = 10f, MistPerFlow = 0.5f, MistPerDrop = 0.25f, MistBase = 0.3f, MistMin = 0.5f, MistCap = 9f, MistCapPerDrop = 0.5f, MistCapPerWidth = 0.25f, MistFlowBase = 0.4f, MistJitter = 0.35f, MistPhase = 0.618034f, MistSpread = 0.8f, SprayStep = 3f, SprayPerDrop = 0.1f, SprayBase = 0.15f, SprayMin = 0.25f, SprayCap = 4f, SpraySpread = 0.95f, SprayAhead = 0.2f;
        public const float MistPad = 40f;
        const float SteamRadius = 0.55f, SteamRise = 2.2f, ShoreOvershoot = 0.25f, ColumnRadius = 0.9f;
        public const float ColumnPerRadius = 14f;
        const int SteamPuffs = 2, JetPuffs = 7, ColumnSteamPuffs = 3;
        const int CurtainRowsMin = 7, CurtainRowsMax = 24, MistMax = 6, SprayMax = 12, MistPuffs = 3;
        const int CurtainColsMin = 2, CurtainColsMax = 9;
        const float CurtainRowStep = 2f, CurtainColStep = 4f, HugClear = 0.4f, HugRamp = 2f, HugClearPerM = 0.04f, HugUnder = 0.3f, HugReach = 2f, HugReachPerM = 0.6f, HugStep = 0.5f, HugSteps = 12f, HugGap = 0.3f, HugBank = 0.3f, FootBendPerDrop = 0.25f, FootBendMin = 0.3f, FootBendMax = 1.5f, FootFade = 1f;
        const int FootBendRows = 4;
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
            if (d.Uv4 != null) mesh.SetUVs(4, d.Uv4);
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

        public static MeshParts Lakes(GeneratedWorld world, int cx, int cy, int cells) => Lakes(world, cx, cy, cells, false);

        public static MeshParts Lakes(GeneratedWorld world, int cx, int cy, int cells, bool shared)
        {
            int n = world.Config.Size, x1 = Mathf.Min(cx + cells, n), y1 = Mathf.Min(cy + cells, n), row = x1 - cx + 1;
            float cm = world.Config.CellMeters;
            int wet = 0, swelling = 0;
            for (int y = cy; y < y1; y++)
                for (int x = cx; x < x1; x++)
                    if (LakeHeight(world, x, y) != float.MinValue) { wet++; if (SwellOf(world, x, y) > 0) swelling++; }
            if (wet == 0) return null;
            int side = LakeSwellSplit + 1;
            var sheet = SheetBuffers.Rent(4 * wet + side * side * swelling, 6 * (wet - swelling) + 6 * LakeSwellSplit * LakeSwellSplit * swelling, row * (y1 - cy + 1));
            try { return Lakes(world, cx, cy, x1, y1, row, cm, sheet, shared); }
            finally { SheetBuffers.Return(sheet); }
        }

        static MeshParts Lakes(GeneratedWorld world, int cx, int cy, int x1, int y1, int row, float cm, SheetBuffers sheet, bool shared)
        {
            int n = world.Config.Size;
            var corner = sheet.Corners;
            int Corner(int X, int Y)
            {
                ref int v = ref corner[(Y - cy) * row + X - cx];
                if (v >= 0) return v;
                float h = 0, a = 0, b = 0, c = 0, swell = 1;
                int count = 0, liquid = -1;
                for (int j = Y - 1; j <= Y; j++)
                    for (int i = X - 1; i <= X; i++)
                    {
                        if (i < 0 || j < 0 || i >= n || j >= n) { swell = 0; continue; }
                        float ch = LakeHeight(world, i, j);
                        swell = Mathf.Min(swell, ch == float.MinValue ? 0 : SwellOf(world, i, j));
                        if (ch == float.MinValue) continue;
                        int id = LakeAt(world, i, j);
                        if (id < 0) continue;
                        var lk = world.Water.Lakes[id];
                        var m = WaterTint.MixAt(lk, n, X - 0.5f, Y - 0.5f);
                        h += ch; a += m.A; b += m.B; c += m.C; count++;
                        if (liquid < 0) liquid = lk.Liquid;
                    }
                count = Mathf.Max(count, 1);
                return v = sheet.Add(new Vector3((X - 0.5f) * cm, h / count, (Y - 0.5f) * cm), new Vector4(0, 0, 0, Mathf.Max(liquid, 0)), new Vector4(a / count, b / count, c / count, 0), new Vector4(swell, 0, 0, 0));
            }
            for (int y = cy; y < y1; y++)
                for (int x = cx; x < x1; x++)
                {
                    if (LakeHeight(world, x, y) == float.MinValue) continue;
                    int v00 = Corner(x, y), v01 = Corner(x, y + 1), v10 = Corner(x + 1, y), v11 = Corner(x + 1, y + 1);
                    if (SwellOf(world, x, y) > 0) sheet.Grid(v00, v01, v10, v11, LakeSwellSplit, cm / LakeSwellSplit, x, y);
                    else sheet.Quad(v00, v01, v10, v11);
                }
            return shared ? sheet.Bare($"Water {cx},{cy}") : sheet.Piece($"Water {cx},{cy}", LakeKind);
        }

        public static void ShareLakeConstants(MeshParts[] parts)
        {
            int most = 0;
            foreach (var p in parts) if (p != null) most = Mathf.Max(most, p.Verts.Length);
            var up = new Vector3[most];
            var cols = new Color[most];
            for (int k = 0; k < most; k++) { up[k] = Vector3.up; cols[k] = LakeKind; }
            var uvs = new Vector2[most];
            var zero = new Vector4[most];
            foreach (var p in parts)
                if (p != null) { p.Norms = up; p.Cols = cols; p.Uvs = uvs; p.Uv4 = zero; }
        }

        static float SwellOf(GeneratedWorld world, int x, int y)
        {
            int id = LakeOf(world.Water, y * world.Config.Size + x);
            if (id < 0) return 0;
            int size = world.Water.Lakes[id].Cells.Count;
            return size < LakeSwellCells ? 0 : Mathf.Min(1, Mathf.Sqrt(size / LakeSwellFullCells));
        }

        sealed class SheetBuffers
        {
            static readonly List<SheetBuffers> free = new List<SheetBuffers>();

            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<int> tris = new List<int>();
            readonly List<Vector4> kinds = new List<Vector4>(), tints = new List<Vector4>(), swells = new List<Vector4>();
            readonly Dictionary<long, int> edges = new Dictionary<long, int>();
            public int[] Corners = new int[0];
            int[] at = new int[0];

            public static SheetBuffers Rent(int vertCount, int triCount, int cornerCount)
            {
                SheetBuffers pick = null;
                lock (free)
                {
                    foreach (var b in free)
                        if (pick == null || (b.verts.Capacity >= vertCount ? pick.verts.Capacity < vertCount || b.verts.Capacity < pick.verts.Capacity : b.verts.Capacity > pick.verts.Capacity)) pick = b;
                    if (pick != null) free.Remove(pick);
                }
                pick ??= new SheetBuffers();
                pick.Reserve(vertCount, triCount, cornerCount);
                return pick;
            }

            public static void Return(SheetBuffers b) { lock (free) free.Add(b); }

            void Reserve(int vertCount, int triCount, int cornerCount)
            {
                verts.Clear(); tris.Clear(); kinds.Clear(); tints.Clear(); swells.Clear(); edges.Clear();
                if (verts.Capacity < vertCount) { verts.Capacity = vertCount; kinds.Capacity = vertCount; tints.Capacity = vertCount; swells.Capacity = vertCount; }
                if (tris.Capacity < triCount) tris.Capacity = triCount;
                if (Corners.Length < cornerCount) Corners = new int[cornerCount];
                for (int k = 0; k < cornerCount; k++) Corners[k] = -1;
            }

            public int Add(Vector3 p, Vector4 kind, Vector4 tint, Vector4 swell = default)
            {
                verts.Add(p); kinds.Add(kind); tints.Add(tint); swells.Add(swell);
                return verts.Count - 1;
            }

            public void Quad(int v00, int v01, int v10, int v11)
            {
                tris.Add(v00); tris.Add(v01); tris.Add(v10);
                tris.Add(v10); tris.Add(v01); tris.Add(v11);
            }

            public void Grid(int v00, int v01, int v10, int v11, int split, float spacing, int cx, int cy)
            {
                int row = split + 1;
                if (at.Length < row * row) at = new int[row * row];
                for (int j = 0; j <= split; j++)
                    for (int i = 0; i <= split; i++)
                    {
                        bool edge = i == 0 || j == 0 || i == split || j == split;
                        long key = ((long)(cx * split + i) << 40) | ((long)(cy * split + j) << 16) | (long)kinds[v00].w;
                        if (edge && edges.TryGetValue(key, out int shared)) { at[j * row + i] = shared; continue; }
                        float u = i / (float)split, w = j / (float)split;
                        at[j * row + i] = verts.Count;
                        if (edge) edges[key] = verts.Count;
                        verts.Add(Bilerp(verts, v00, v10, v01, v11, u, w));
                        kinds.Add(kinds[v00]);
                        tints.Add(Bilerp(tints, v00, v10, v01, v11, u, w));
                        var sw = Bilerp(swells, v00, v10, v01, v11, u, w);
                        swells.Add(new Vector4(sw.x, 0, spacing, 0));
                    }
                for (int j = 0; j < split; j++)
                    for (int i = 0; i < split; i++)
                    {
                        int a = j * row + i;
                        Quad(at[a], at[a + row], at[a + 1], at[a + row + 1]);
                    }
            }

            static Vector3 Bilerp(List<Vector3> l, int a, int b, int c, int d, float u, float w) => Vector3.Lerp(Vector3.Lerp(l[a], l[b], u), Vector3.Lerp(l[c], l[d], u), w);

            static Vector4 Bilerp(List<Vector4> l, int a, int b, int c, int d, float u, float w) => Vector4.Lerp(Vector4.Lerp(l[a], l[b], u), Vector4.Lerp(l[c], l[d], u), w);

            public MeshParts Piece(string name, Color kind)
            {
                if (verts.Count == 0) return null;
                return Flat(name, verts, null, tris, kind, uv1: kinds.ToArray(), uv2: swells.ToArray(), uv3: tints.ToArray());
            }

            public MeshParts Bare(string name) => new MeshParts { Name = name, Verts = verts.ToArray(), Tris = tris.ToArray(), Uv1 = kinds.ToArray(), Uv2 = swells.ToArray(), Uv3 = tints.ToArray() };
        }

        static Vector4 Tint(WaterMix m) => new Vector4(m.A, m.B, m.C, 0);

        static Vector4 Flowing(Vector4 tint, float current) { tint.w = current; return tint; }

        public static float Current(float depth, float slope) =>
            Mathf.Min(CurrentPerManning * (float)FMath.Pow(Mathf.Max(depth, 0.05f), 2.0 / 3.0) * Mathf.Sqrt(Mathf.Max(slope, CurrentMinSlope)), CurrentMax);

        static int LakeAt(GeneratedWorld world, int x, int y)
        {
            int n = world.Config.Size, id = LakeOf(world.Water, y * n + x);
            for (int k = 0; k < 8 && id < 0; k++)
            {
                int nx = x + Stabby.World.Grid.DX[k], ny = y + Stabby.World.Grid.DY[k];
                if (nx >= 0 && ny >= 0 && nx < n && ny < n) id = LakeOf(world.Water, ny * n + nx);
            }
            return id;
        }

        static int LakeOf(Hydrology hy, int i) => hy.LakeId[i] >= 0 || hy.LakeCover == null ? hy.LakeId[i] : hy.LakeCover[i];

        static bool Sprays(GeneratedWorld world, int liquid) => world.Materials == null || (world.Materials.Liquids[liquid].Spray != LiquidDef.SprayNone && world.Materials.Liquids[liquid].Spray != LiquidDef.SprayShimmer && world.Materials.Liquids[liquid].SprayAmount > 0);

        static float SprayAmount(GeneratedWorld world, int liquid) => world.Materials == null ? 1 : world.Materials.Liquids[liquid].SprayAmount;

        public static float LakeHeight(GeneratedWorld world, int x, int y)
        {
            int n = world.Config.Size, c = y * n + x;
            var hy = world.Water;
            if (world.Sea[c]) return float.MinValue;
            if (hy.LakeId[c] >= 0) return Mathf.Max(hy.WaterSurface[c], world.Height[c]) + LakeLift;
            int own = LakeOf(hy, c);
            if (own >= 0) return hy.Lakes[own].Level + LakeLift;
            float h = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + Stabby.World.Grid.DX[k], ny = y + Stabby.World.Grid.DY[k];
                if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                int o = ny * n + nx;
                int id = LakeOf(hy, o);
                if (!world.Sea[o] && id >= 0 && world.Height[c] >= hy.Lakes[id].Level) h = Mathf.Max(h, hy.Lakes[id].Level + LakeLift);
            }
            return h;
        }

        sealed class RiverBuffers
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Color> Cols = new List<Color>();
            public readonly List<Vector2> Chan = new List<Vector2>();
            public readonly List<Vector4> Reach = new List<Vector4>(), Fall = new List<Vector4>(), Tints = new List<Vector4>(), Curt = new List<Vector4>();
            public readonly List<int> Tris = new List<int>();
            public Vector4 Tint;
            public float Fade;

            public void Clear() { Verts.Clear(); Cols.Clear(); Chan.Clear(); Reach.Clear(); Fall.Clear(); Tints.Clear(); Curt.Clear(); Tris.Clear(); }

            public void Append(RiverBuffers o)
            {
                int b = Verts.Count;
                Verts.AddRange(o.Verts); Cols.AddRange(o.Cols); Chan.AddRange(o.Chan); Reach.AddRange(o.Reach); Fall.AddRange(o.Fall); Tints.AddRange(o.Tints); Curt.AddRange(o.Curt);
                foreach (int t in o.Tris) Tris.Add(b + t);
            }

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
                Curt.Add(new Vector4(0, 0, 0, Fade));
                Curt.Add(new Vector4(0, 0, 0, Fade));
                if (!join) return;
                int v = Verts.Count - 2;
                Tris.Add(v - 2); Tris.Add(v); Tris.Add(v - 1);
                Tris.Add(v - 1); Tris.Add(v); Tris.Add(v + 1);
            }

            public void Row(Vector3 p, Vector3 dir, float half, float leftY, float rightY, float slope, Vector4 reach, Vector4 fall, bool join)
            {
                var side = new Vector3(dir.z, 0, -dir.x) * half;
                var flow = new Color(dir.x * 0.5f + 0.5f, dir.z * 0.5f + 0.5f, slope, 0);
                var l = p - side;
                var r = p + side;
                l.y = leftY;
                r.y = rightY;
                int v = Add(l, flow, new Vector2(-1, half), reach, fall, Vector4.zero);
                Add(p, flow, new Vector2(0, half), reach, fall, Vector4.zero);
                Add(r, flow, new Vector2(1, half), reach, fall, Vector4.zero);
                if (!join) return;
                Tris.Add(v - 3); Tris.Add(v); Tris.Add(v - 2);
                Tris.Add(v - 2); Tris.Add(v); Tris.Add(v + 1);
                Tris.Add(v - 2); Tris.Add(v + 1); Tris.Add(v - 1);
                Tris.Add(v - 1); Tris.Add(v + 1); Tris.Add(v + 2);
            }

            int Add(Vector3 p, Color col, Vector2 chan, Vector4 reach, Vector4 fall, Vector4 curt)
            {
                Verts.Add(p);
                Cols.Add(col);
                Chan.Add(chan);
                Reach.Add(reach);
                Tints.Add(Tint);
                Fall.Add(fall);
                Curt.Add(curt);
                return Verts.Count - 1;
            }

            public void Edge(FallCurtain f, float u, float slope, Vector4 reach, Vector4 fall, List<int> lip)
            {
                var side = new Vector3(f.DirZ, 0, -f.DirX) * (0.5f * f.Width);
                lip.Add(Add(new Vector3(f.LipX, f.LipY, f.LipZ) + side * u, new Color(f.DirX * 0.5f + 0.5f, f.DirZ * 0.5f + 0.5f, slope, 0), new Vector2(u, 0.5f * f.Width), reach, fall, Vector4.zero));
            }

            public void Fan(int l, int r, List<int> row, bool down)
            {
                int m = row.Count / 2;
                for (int i = 0; i + 1 < row.Count; i++)
                {
                    int apex = i < m ? l : r;
                    if (down) { Tris.Add(apex); Tris.Add(row[i]); Tris.Add(row[i + 1]); }
                    else { Tris.Add(row[i]); Tris.Add(apex); Tris.Add(row[i + 1]); }
                }
                if (down) { Tris.Add(l); Tris.Add(row[m]); Tris.Add(r); }
                else { Tris.Add(row[m]); Tris.Add(l); Tris.Add(r); }
            }

            int Band(FallCurtain f, int prev, int cols, float y, float h, float blend, float[] off, int o0, int o1, float t, Color flow, Color top, Vector4 reach, Vector4 fall, bool both, float foot = 1, float ahead = 0)
            {
                float half = 0.5f * f.Width;
                var fwd = new Vector3(f.DirX, 0, f.DirZ);
                var side = new Vector3(f.DirZ, 0, -f.DirX) * half;
                var lip = new Vector3(f.LipX, f.LipY, f.LipZ);
                float w = blend > 0 ? Mathf.Clamp01(y / blend) : 0;
                var curt = new Vector4(w * foot, y / h, h, f.Discharge);
                var col = Color.Lerp(top, flow, w);
                int b = Verts.Count;
                for (int c = 0; c < cols; c++)
                {
                    float sc = f.SideAt(c, cols), o = Mathf.Lerp(off[o0 + c], off[o1 + c], t) + ahead;
                    Add(lip + side * sc + fwd * (f.Bow * sc * sc + o) - Vector3.up * y, col, Vector2.Lerp(new Vector2(sc, half), new Vector2(2f * c / (cols - 1) - 1, half * f.Span), w), reach, fall, curt);
                    if (c == 0) continue;
                    int v = b + c, a = prev + c;
                    Tris.Add(a - 1); Tris.Add(v - 1); Tris.Add(a);
                    Tris.Add(a); Tris.Add(v - 1); Tris.Add(v);
                    if (!both) continue;
                    Tris.Add(a - 1); Tris.Add(a); Tris.Add(v - 1);
                    Tris.Add(a); Tris.Add(v); Tris.Add(v - 1);
                }
                return b;
            }

            static int FootRowOf(float y, float h, int rows) => Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(y / h) * (rows - 1)), 1, rows - 1);

            static float FootRowT(float y, float h, int rows, int j)
            {
                float y0 = (j - 1f) * (j - 1f) / ((rows - 1f) * (rows - 1f)) * h, y1 = (float)j * j / ((rows - 1f) * (rows - 1f)) * h;
                return Mathf.Clamp01((y - y0) / Mathf.Max(y1 - y0, 1e-4f));
            }

            public float Curtain(FallCurtain f, float slope, Vector4 reach, Vector4 pool, float along, TerrainDetail detail, MistBuffers mist, List<int> lip, List<int> foot, List<int> bend, float room = float.MaxValue)
            {
                float h = f.Drop + FallSink, half = 0.5f * f.Width, blend = Mathf.Min(LipBlend, 0.5f * f.Drop);
                var fwd = new Vector3(f.DirX, 0, f.DirZ);
                var side = new Vector3(f.DirZ, 0, -f.DirX) * half;
                var top = new Color(f.DirX * 0.5f + 0.5f, f.DirZ * 0.5f + 0.5f, slope, 0);
                var flow = new Color(f.DirX * 0.5f + 0.5f, f.DirZ * 0.5f + 0.5f, 1, 0);
                var fall = new Vector4(pool.x, pool.y, pool.z, 1);
                int rows = CurtainRowsOf(h), cols = CurtainColsOf(f.RunWidth);
                var off = CurtainOffsets(f, detail);
                int prev = Verts.Count;
                for (int c = 0; c < cols; c++)
                    lip.Add(Add(new Vector3(f.LipX, f.LipY, f.LipZ) + side * f.SideAt(c, cols), top, new Vector2(f.SideAt(c, cols), half), reach, pool, new Vector4(0, 0, h, f.Discharge)));
                if (f.Bow > 0) prev = Band(f, prev, cols, 0, h, 0, off, 0, 0, 0, top, top, reach, fall, false);
                float ext = Mathf.Clamp(FootBendPerDrop * f.Drop, FootBendMin, FootBendMax), yb = f.Drop - Mathf.Min(ext, 0.5f * f.Drop);
                ext = f.Drop - yb;
                float far = float.MinValue, lean;
                for (int c = 0, jd = FootRowOf(f.Drop, h, rows); c < cols; c++)
                {
                    float sc = f.SideAt(c, cols);
                    far = Mathf.Max(far, f.Bow * sc * sc + Mathf.Lerp(off[(jd - 1) * cols + c], off[jd * cols + c], FootRowT(f.Drop, h, rows, jd)));
                }
                lean = Mathf.Clamp(room - far, 0, ext);
                for (int j = 1; j < rows; j++)
                {
                    float u1 = j / (rows - 1f), y = u1 * u1 * h;
                    if (y > yb - 0.05f * ext) break;
                    prev = Band(f, prev, cols, y, h, blend, off, j * cols, j * cols, 0, flow, top, reach, fall, true);
                }
                for (int s = 0; s <= FootBendRows; s++)
                {
                    float q = s / (float)FootBendRows, y = yb + ext * q;
                    int j = FootRowOf(y, h, rows);
                    prev = Band(f, prev, cols, y, h, blend, off, (j - 1) * cols, j * cols, FootRowT(y, h, rows, j), flow, top, reach, fall, true, Mathf.Clamp01((1 - q) / FootFade), lean * (1 - Mathf.Sqrt(Mathf.Max(0, 1 - q * q))));
                    for (int c = 0; c < cols; c++) bend.Add(prev + c);
                }
                for (int c = 0; c < cols; c++) foot.Add(prev + c);
                float shift = 0;
                for (int c = 0; c < cols; c++) shift += off[(rows - 1) * cols + c];
                shift = Mathf.Max(0, shift / cols - FallThrowAt(h));
                var push = fwd * shift + side * f.Mid + fwd * (f.Bow * f.Mid * f.Mid);
                mist?.Foot(f, along, h, push, fwd, side);
                return shift;
            }

            public MeshParts Piece(string name)
            {
                return Flat(name, Verts, Cols, Tris, default, Chan.ToArray(), Reach.ToArray(), Fall.ToArray(), Tints.ToArray(), Curt.ToArray());
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
                float scale = MistFlowBase + flowQ;
                float spray = Mathf.Clamp(SprayPerDrop * f.Drop * scale + SprayBase, SprayMin, SprayCap);
                int sprays = Mathf.Clamp(Mathf.RoundToInt((Mathf.RoundToInt(f.RunWidth / SprayStep) + 1) * Amount), 2, SprayMax);
                for (int m = 0; m < sprays; m++)
                    Puff(At(f, basePos, fwd, side, ((m + 0.5f) / sprays * 2 - 1) * SpraySpread * f.Span) + fwd * (SprayAhead * spray), fwd, flowQ, Mathf.Repeat(m * MistPhase + seed, 1f), new Vector2(spray, f.Drop), 0);
                int mists = Mathf.Clamp(Mathf.RoundToInt((f.RunWidth / MistPerWidth + f.Drop / MistPerHeight + MistPerFlow * Mathf.Sqrt(f.Discharge)) * Amount), 1, MistMax);
                float radius = Mathf.Min(Mathf.Clamp(MistPerDrop * f.Drop * scale + MistBase, MistMin, MistCap), MistCapPerDrop * f.Drop + MistCapPerWidth * f.RunWidth);
                for (int m = 0; m < mists * MistPuffs; m++)
                {
                    float phase = Mathf.Repeat(m * MistPhase + seed + 0.29f, 1f), mu = ((m / MistPuffs + 0.5f) / mists * 2 - 1 + (phase - 0.5f) * MistJitter * 2 / mists) * MistSpread * f.Span;
                    Puff(At(f, basePos, fwd, side, mu) + fwd * (0.3f * radius), fwd, flowQ, phase, new Vector2(radius, f.Drop), 1);
                }
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

            public void Append(MistBuffers o)
            {
                int b = Verts.Count;
                Verts.AddRange(o.Verts); Cols.AddRange(o.Cols); Size.AddRange(o.Size); Corner.AddRange(o.Corner);
                foreach (int t in o.Tris) Tris.Add(b + t);
            }

            public MeshParts Piece(string name)
            {
                var m = Flat(name, Verts, Cols, Tris, default, Size.ToArray(), Corner.ToArray());
                Verts.Clear(); Cols.Clear(); Size.Clear(); Corner.Clear(); Tris.Clear();
                return m;
            }
        }

        public static void Springs(ObjectField field, List<MeshParts> mists, List<MeshParts> water, System.Func<float, float, float, Vector3> ground = null)
        {
            var mist = new MistBuffers();
            var pools = new List<float>();
            var liquid = field.LiquidPools();
            int next = 0;
            foreach (var o in field.Discoveries())
            {
                if (!field.IsVent(o)) continue;
                int kind = field.VentKind(o);
                field.SpringPoolsAt(o, pools);
                for (int k = 0; k < pools.Count; k += ObjectField.SpringStride)
                {
                    var c = new Vector3(pools[k], liquid[next++].Level, pools[k + 1]);
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
                }
                if (mist.Verts.Count >= MistPieceVertices) mists.Add(mist.Piece($"Spring steam {mists.Count}"));
            }
            if (mist.Verts.Count > 0) mists.Add(mist.Piece($"Spring steam {mists.Count}"));
            var sheet = new SheetBuffers();
            foreach (var p in liquid)
            {
                var def = field.Materials?.Liquids[p.Liquid];
                Pool(p, def != null && def.Opacity > 0.5f, p.Liquid != 0 && def != null && def.GroundTint > 0 ? ground : null, sheet);
            }
            var piece = sheet.Piece("Liquid pools", SpringKind);
            if (piece != null) water.Add(piece);
        }

        static void Pool(LiquidPool p, bool opaque, System.Func<float, float, float, Vector3> ground, SheetBuffers sheet)
        {
            int nx = p.Nx, nz = p.Nz;
            var index = new int[nx * nz];
            for (int k = 0; k < index.Length; k++) index[k] = -1;
            int Node(int i, int j)
            {
                int k = j * nx + i;
                if (index[k] >= 0) return index[k];
                float x = p.NodeX(i), z = p.NodeZ(j), rim = 0;
                if (!p.Wet[k])
                {
                    float sx = 0, sz = 0, gd = p.Ground[k];
                    int count = 0;
                    for (int nj = Mathf.Max(0, j - 1); nj <= Mathf.Min(nz - 1, j + 1); nj++)
                        for (int ni = Mathf.Max(0, i - 1); ni <= Mathf.Min(nx - 1, i + 1); ni++)
                        {
                            int m = nj * nx + ni;
                            if (!p.Wet[m]) continue;
                            float gw = p.Ground[m], t = gd >= p.Level && gd > gw ? Mathf.Min(1f, (p.Level - gw) / (gd - gw) + ShoreOvershoot) : 0.5f, wx = p.NodeX(ni), wz = p.NodeZ(nj);
                            sx += wx + (x - wx) * t; sz += wz + (z - wz) * t; count++;
                        }
                    if (count > 0) { x = sx / count; z = sz / count; }
                    rim = opaque ? 1 : 0;
                }
                var c = ground == null ? Vector3.zero : ground(x, z, p.Level);
                return index[k] = sheet.Add(new Vector3(x, p.Level, z), new Vector4(rim, 0, 0, p.Liquid), new Vector4(c.x, c.y, c.z, 0));
            }
            for (int j = 0; j + 1 < nz; j++)
                for (int i = 0; i + 1 < nx; i++)
                {
                    int k = j * nx + i;
                    if (p.Wet[k] || p.Wet[k + 1] || p.Wet[k + nx] || p.Wet[k + nx + 1]) sheet.Quad(Node(i, j), Node(i, j + 1), Node(i + 1, j), Node(i + 1, j + 1));
                }
        }

        public static List<MeshParts> Rivers(GeneratedWorld world, TerrainDetail detail, List<MeshParts> mists)
        {
            var pieces = new List<MeshParts>();
            var buf = new RiverBuffers();
            var mist = mists == null ? null : new MistBuffers();
            var lines = NearField.Lines(world);
            var built = new RiverBuffers[lines.Count];
            var sprays = new MistBuffers[lines.Count];
            Parallel.For(0, lines.Count, i =>
            {
                built[i] = new RiverBuffers();
                sprays[i] = mists == null ? null : new MistBuffers();
                River(world, detail, lines[i], built[i], sprays[i]);
            });
            for (int i = 0; i < lines.Count; i++)
            {
                if (buf.Verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(buf.Piece($"Rivers {pieces.Count}"));
                    buf.Clear();
                }
                if (mist != null && mist.Verts.Count >= MistPieceVertices) mists.Add(mist.Piece($"Fall mist {mists.Count}"));
                buf.Append(built[i]);
                mist?.Append(sprays[i]);
            }
            if (buf.Verts.Count > 0) pieces.Add(buf.Piece($"Rivers {pieces.Count}"));
            if (mist != null && mist.Verts.Count > 0) mists.Add(mist.Piece($"Fall mist {mists.Count}"));
            return pieces;
        }

        static void River(GeneratedWorld world, TerrainDetail detail, RiverLine r, RiverBuffers buf, MistBuffers mist)
        {
            var spray = Sprays(world, r.Liquid) ? mist : null;
            if (spray != null) { spray.Amount = SprayAmount(world, r.Liquid); spray.Liquid = r.Liquid; }
            var pts = new List<Vector4>();
            for (int k = 0; k < r.X.Length; k++)
                pts.Add(new Vector4(r.X[k], r.S[k], r.Z[k], 2 * r.W[k]));
            int last = pts.Count - 1;
            var slopes = new float[pts.Count];
            var currents = new float[pts.Count];
            for (int k = 0; k < pts.Count; k++)
            {
                var u = pts[Mathf.Max(k - SlopeReach, 0)];
                var d = pts[Mathf.Min(k + SlopeReach, last)];
                float run = new Vector2(d.x - u.x, d.z - u.z).magnitude;
                slopes[k] = Mathf.Clamp01((run > 0 ? Mathf.Max(0, u.y - d.y) / run : 0) / SlopeRange);
                currents[k] = Current(r.D[k], run > 0 ? (u.y - d.y) / run : 0);
            }
            int coast = pts.Count;
            if (world.Sea != null)
                while (coast > 0 && world.Sea[NearField.CellOf(pts[coast - 1].x, pts[coast - 1].z, world.Config.CellMeters, world.Config.Size)]) coast--;
            bool mouth = coast < pts.Count;
            float coastS = 0, total = 0;
            for (int k = 1; k < pts.Count; k++)
            {
                total += new Vector2(pts[k].x - pts[k - 1].x, pts[k].z - pts[k - 1].z).magnitude;
                if (k == coast) coastS = total;
            }
            float endHalf = Mathf.Max(pts[last].w * 0.5f, 6f), reach = Mathf.Max(MouthReachM, MouthReachWidths * endHalf);
            float s = 0;
            var pool = NoPool;
            var held = new List<int>();
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
                buf.Tint = Flowing(Tint(r.Mix[k]), currents[k]);
                if (k > 0 && FallAt(r, k, out var f, out float lt))
                {
                    buf.Tint = Flowing(Tint(WaterMix.Lerp(r.Mix[k - 1], r.Mix[k], lt)), currents[k]);
                    var fd = new Vector3(f.DirX, 0, f.DirZ);
                    var prev = new Vector3(pts[k - 1].x, 0, pts[k - 1].z);
                    float sl = s - seg * (1 - lt), th = FallThrowAt(f.Drop), ft = Mathf.Min(lt + (th + FootGap) / Mathf.Max(seg, 1e-3f), FootMax);
                    var lr = new Vector4(rv.x, rv.y, sl, rv.w);
                    var parts = Split(f);
                    System.Array.Sort(parts, (x, y) => x.U0.CompareTo(y.U0));
                    var lip = new List<int>();
                    var foot = new List<int>();
                    var bendRows = new List<int>();
                    int up = buf.Verts.Count - 2, t0 = buf.Tris.Count;
                    if (parts[0].U0 > -0.999f) buf.Edge(f, -1, slopes[k - 1], lr, pool, lip);
                    float shove = 0;
                    foreach (var part in parts) shove = Mathf.Max(shove, buf.Curtain(part, slopes[k - 1], lr, pool, sl, detail, spray, lip, foot, bendRows, (FootMax - lt) * seg - FootGap));
                    if (parts[parts.Length - 1].U1 < 0.999f) buf.Edge(f, 1, slopes[k - 1], lr, pool, lip);
                    buf.Fan(up, up + 1, lip, true);
                    held.AddRange(buf.Tris.GetRange(t0, buf.Tris.Count - t0));
                    buf.Tris.RemoveRange(t0, buf.Tris.Count - t0);
                    th += shove;
                    foreach (int v in foot) th = Mathf.Max(th, Vector3.Dot(buf.Verts[v] - new Vector3(f.LipX, buf.Verts[v].y, f.LipZ), fd));
                    ft = Mathf.Min(lt + (th + FootGap) / Mathf.Max(seg, 1e-3f), FootMax);
                    pool = new Vector4(sl + th, f.Drop, f.PoolRadius, 0);
                    foreach (int v in bendRows)
                    {
                        buf.Fall[v] = new Vector4(pool.x, pool.y, pool.z, 1);
                        buf.Cols[v] = new Color(fd.x * 0.5f + 0.5f, fd.z * 0.5f + 0.5f, slopes[k], 0);
                    }
                    var footRow = Vector3.Lerp(prev, new Vector3(p.x, 0, p.z), ft);
                    footRow.y = f.BaseY;
                    buf.Tint = Flowing(Tint(WaterMix.Lerp(r.Mix[k - 1], r.Mix[k], ft)), currents[k]);
                    buf.Row(footRow, fd, Mathf.Lerp(0.5f * f.Width, half, (ft - lt) / (1 - lt)), slopes[k], new Vector4(rv.x, rv.y, s - seg * (1 - ft), rv.w), pool, false);
                    buf.Fan(buf.Verts.Count - 2, buf.Verts.Count - 1, foot, false);
                }
                buf.Tint = Flowing(Tint(r.Mix[k]), currents[k]);
                buf.Fade = mouth ? MouthFade(s, coastS, total + reach) : 0;
                buf.Tint = Vector4.Lerp(buf.Tint, Vector4.zero, buf.Fade);
                buf.Row(p, dir, half, slopes[k], rv, pool, join);
            }
            if (mouth && pts.Count > 1)
            {
                var end = pts[last];
                var prev = pts[last - 1];
                var dir = new Vector3(end.x - prev.x, 0, end.z - prev.z).normalized;
                int steps = Mathf.Max(2, Mathf.CeilToInt(reach / MouthStepM));
                var tint = Flowing(Tint(r.Mix[last]), currents[last]);
                for (int j = 1; j <= steps; j++)
                {
                    float u = j / (float)steps, along = u * reach;
                    var p = new Vector3(end.x, end.y + RiverLift, end.z) + dir * along;
                    buf.Fade = MouthFade(s + along, coastS, total + reach);
                    buf.Tint = Vector4.Lerp(tint, Vector4.zero, buf.Fade);
                    buf.Row(p, dir, endHalf * Mathf.Lerp(1, MouthFlare, u), 0, new Vector4(0, 0, s + along, r.Liquid), pool, true);
                }
            }
            buf.Fade = 0;
            buf.Tris.AddRange(held);
        }

        static float MouthFade(float s, float from, float to) => to > from ? Mathf.SmoothStep(0, 1, (s - from) / (to - from)) : 1;

        public static List<MeshParts> CaveBrooks(GeneratedWorld world, CaveBrook[] brooks)
        {
            var pieces = new List<MeshParts>();
            if (brooks == null || brooks.Length == 0) return pieces;
            var buf = new RiverBuffers();
            int liquid = world.Materials == null ? 0 : Mathf.Max(0, world.Materials.Liquid("water"));
            foreach (var b in brooks)
            {
                if (buf.Verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(buf.Piece($"Cave brooks {pieces.Count}"));
                    buf.Clear();
                }
                var q = b.Points;
                int rows = b.Rows, st = CaveBrook.Stride, cut = b.Fall > 0 ? b.Fall : rows;
                float s = 0;
                for (int k = 0; k < rows; k++)
                {
                    int a = Mathf.Max(k - 1, k < cut ? 0 : cut) * st, z = Mathf.Min(k + 1, (k < cut ? cut : rows) - 1) * st, o = k * st;
                    float run = new Vector2(q[z] - q[a], q[z + 2] - q[a + 2]).magnitude, drop = q[a + 1] - q[z + 1];
                    var dir = new Vector3(q[z] - q[a], 0, q[z + 2] - q[a + 2]).normalized;
                    float sl = s;
                    if (k > 0) s += new Vector2(q[o] - q[o - st], q[o + 2] - q[o - st + 2]).magnitude;
                    float slope = run > 0 ? drop / run : 0, half = q[o + 3];
                    buf.Tint = Flowing(Tint(default), Current(Stabby.World.CaveBrooks.DepthM, slope));
                    if (k == b.Fall) { CaveFall(buf, b, k, sl, liquid); break; }
                    buf.Row(new Vector3(q[o], q[o + 1], q[o + 2]), dir, half, q[o + 4], q[o + 5], Mathf.Clamp01(slope / SlopeRange), new Vector4(0, 0, s, liquid), NoPool, k > 0);
                }
            }
            if (buf.Verts.Count > 0) pieces.Add(buf.Piece($"Cave brooks {pieces.Count}"));
            return pieces;
        }

        static void CaveFall(RiverBuffers buf, CaveBrook b, int k, float sl, int liquid)
        {
            var q = b.Points;
            int st = CaveBrook.Stride, l = (k - 1) * st, o = k * st, up = buf.Verts.Count - 3;
            var fd = new Vector3(q[o] - q[l], 0, q[o + 2] - q[l + 2]).normalized;
            float drop = q[l + 1] - q[o + 1], th = FallThrowAt(drop), width = 2f * q[l + 3];
            var f = new FallCurtain
            {
                K = k - 1, LipX = q[l], LipZ = q[l + 2], LipY = q[l + 1],
                BaseX = q[l] + fd.x * th, BaseZ = q[l + 2] + fd.z * th, BaseY = q[o + 1],
                Width = width, DirX = fd.x, DirZ = fd.z, PoolRadius = b.PoolR,
                Discharge = width * Stabby.World.CaveBrooks.DepthM, Liquid = liquid, U0 = -1, U1 = 1,
            };
            var reach = new Vector4(0, 0, sl, liquid);
            var lip = new List<int>();
            var foot = new List<int>();
            var bend = new List<int>();
            th += buf.Curtain(f, 1, reach, NoPool, sl, null, null, lip, foot, bend);
            buf.Fan(up, up + 2, lip, true);
            foreach (int v in foot) th = Mathf.Max(th, Vector3.Dot(buf.Verts[v] - new Vector3(f.LipX, buf.Verts[v].y, f.LipZ), fd));
            var pool = new Vector4(sl + th, drop, b.PoolR, 0);
            foreach (int v in bend)
            {
                buf.Fall[v] = new Vector4(pool.x, pool.y, pool.z, 1);
                buf.Cols[v] = new Color(fd.x * 0.5f + 0.5f, fd.z * 0.5f + 0.5f, 0, 0);
            }
        }

        public static MeshParts Sea(int size, float cm)
        {
            float span = size * cm, reach = span * 2.5f;
            var axis = new List<float>();
            for (float x = 0; x <= SeaNearHalf; x += SeaStep) axis.Add(x);
            for (float x = SeaNearHalf, d = SeaStep; x < reach;) { d *= SeaGrowth; x += d; axis.Add(x); }
            var ax = new float[axis.Count * 2 - 1];
            for (int k = 0; k < axis.Count; k++) { ax[axis.Count - 1 + k] = axis[k]; ax[axis.Count - 1 - k] = -axis[k]; }
            int m = ax.Length;
            var verts = new List<Vector3>(m * m);
            var fall = new Vector4[m * m];
            for (int j = 0; j < m; j++)
                for (int i = 0; i < m; i++)
                {
                    float sx = Mathf.Max(ax[Mathf.Min(i + 1, m - 1)] - ax[i], ax[i] - ax[Mathf.Max(i - 1, 0)]);
                    float sz = Mathf.Max(ax[Mathf.Min(j + 1, m - 1)] - ax[j], ax[j] - ax[Mathf.Max(j - 1, 0)]);
                    fall[verts.Count] = new Vector4(1, 1, Mathf.Max(sx, sz), 0);
                    verts.Add(new Vector3(ax[i], 0, ax[j]));
                }
            var tris = new List<int>((m - 1) * (m - 1) * 6);
            for (int j = 0; j < m - 1; j++)
                for (int i = 0; i < m - 1; i++)
                {
                    int a = j * m + i;
                    tris.Add(a); tris.Add(a + m); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + m); tris.Add(a + m + 1);
                }
            return Flat("Sea", verts, null, tris, SeaKind, uv2: fall);
        }

        static MeshParts Flat(string name, List<Vector3> verts, List<Color> perVertex, List<int> tris, Color kind, Vector2[] uvs = null, Vector4[] uv1 = null, Vector4[] uv2 = null, Vector4[] uv3 = null, Vector4[] uv4 = null)
        {
            var cols = perVertex != null ? perVertex.ToArray() : new Color[verts.Count];
            var up = new Vector3[verts.Count];
            for (int k = 0; k < up.Length; k++) { up[k] = Vector3.up; if (perVertex == null) cols[k] = kind; }
            int n = verts.Count;
            return new MeshParts { Name = name, Verts = verts.ToArray(), Norms = up, Cols = cols, Tris = tris.ToArray(), Uvs = uvs ?? new Vector2[n], Uv1 = uv1 ?? new Vector4[n], Uv2 = uv2 ?? new Vector4[n], Uv3 = uv3 ?? new Vector4[n], Uv4 = uv4 ?? new Vector4[n] };
        }
    }
}
