using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Stabby.World;
using UnityEngine;

namespace Stabby.Viewer
{
    public sealed class WorldView : MonoBehaviour
    {
        public int Seed = 4;
        public int Size = 512;
        public int ChunkCells = 32;
        public Material Material;
        public float TargetQuadScreen = 0.01f;
        public int BuildBudget = 8;
        public int ApplyVertices = 150000;
        public float ApplyMs = 4f;
        public bool Blocking;
        public bool UseMapFile = true;
        public Color Sky = new Color(0.62f, 0.74f, 0.86f);

        static readonly float[] Steps = { 0.25f, 0.5f, 1, 2, 4, 8 };
        static readonly Color SeaColor = new Color(0.16f, 0.36f, 0.55f), LakeColor = new Color(0.22f, 0.47f, 0.68f),
            RiverColor = new Color(0.20f, 0.45f, 0.78f), Sand = new Color(0.78f, 0.72f, 0.52f), Rock = new Color(0.47f, 0.44f, 0.41f);

        public GeneratedWorld World { get; private set; }
        public TerrainDetail Detail { get; private set; }
        public bool Ready { get; private set; }
        public long GenerateMs, MeshMs;
        public string MapSource = "";
        public int VisibleChunks, VisibleTriangles, BuiltMeshes;
        readonly int[] lodCounts = new int[Steps.Length];
        readonly List<Chunk> chunks = new List<Chunk>();
        Task<Prepared> preparing;
        Prepared prepared;
        CancellationTokenSource cancel = new CancellationTokenSource();
        int outstanding, appliedChunks, appliedWater, appliedRivers, frameVertices;
        readonly Stopwatch frame = new Stopwatch();

        sealed class Source
        {
            public GeneratedWorld World;
            public TerrainDetail Detail;
            public Color[] Colors;
            public int Size, ChunkCells;
            public float Cm;
        }

        sealed class MeshData
        {
            public string Name;
            public Vector3[] Verts, Norms;
            public Color[] Cols;
            public int[] Tris;
        }

        sealed class ChunkData
        {
            public int X, Y;
            public MeshData Coarse, Water;
        }

        sealed class Prepared
        {
            public Source Src;
            public List<ChunkData> Chunks;
            public List<MeshData> Rivers;
            public long GenMs, MeshMs;
            public string Source = "";
        }

        sealed class Chunk
        {
            public Bounds Bounds;
            public Mesh[] Lods;
            public Task<MeshData>[] Pending;
            public int[] Tris;
            public int WaterTris;
            public MeshFilter Filter;
            public MeshRenderer Renderer, Water;
            public int X, Y, Lod = -1;
        }

        Source src;

        void Start()
        {
            if (preparing == null && prepared == null && !Ready) Build();
        }

        void Update()
        {
            frame.Restart();
            frameVertices = 0;
            Pump();
            var cam = Camera.main;
            if (cam != null && Ready) UpdateLod(cam);
        }

        public void Build()
        {
            var c = new WorldConfig { Seed = (ulong)Seed, Size = Size, CellMeters = WorldConfig.CellMetersFor(Size) };
            int cells = ChunkCells;
            string path = UseMapFile ? MapFile.PathFor(c.Seed, c.Size) : null;
            Begin(token =>
            {
                var sw = Stopwatch.StartNew();
                var defs = BiomeFile.Load(BiomeFile.DefaultPath);
                var (world, source) = path != null ? MapFile.LoadOrGenerate(path, BiomeFile.DefaultPath, c, defs, true) : (WorldGenerator.Generate(c, defs), $"generated in {sw.ElapsedMilliseconds} ms");
                long genMs = sw.ElapsedMilliseconds;
                var p = Prepare(world, defs, cells, token);
                p.GenMs = genMs;
                p.Source = source;
                return p;
            });
        }

        public void Build(GeneratedWorld world, IReadOnlyList<BiomeDef> defs)
        {
            int cells = ChunkCells;
            Begin(token => Prepare(world, defs, cells, token));
        }

        void Begin(Func<CancellationToken, Prepared> work)
        {
            Clear();
            Shader.SetGlobalColor("_StabbySky", Sky * 0.9f);
            Shader.SetGlobalColor("_StabbyGround", new Color(0.3f, 0.27f, 0.22f));
            if (Blocking) { prepared = work(cancel.Token); Pump(); }
            else preparing = Run(work);
        }

        public int Outstanding => Volatile.Read(ref outstanding);

        Task<T> Run<T>(Func<CancellationToken, T> work) where T : class
        {
            var token = cancel.Token;
            Interlocked.Increment(ref outstanding);
            return Task.Run(() =>
            {
                try { return token.IsCancellationRequested ? null : work(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
                finally { Interlocked.Decrement(ref outstanding); }
            });
        }

        bool Spend(int vertices)
        {
            if (Blocking) return true;
            if (frameVertices > 0 && (frameVertices + vertices > ApplyVertices || frame.Elapsed.TotalMilliseconds > ApplyMs)) return false;
            frameVertices += vertices;
            return true;
        }

        void OnDestroy() => cancel.Cancel();

        public void Clear()
        {
            for (int k = transform.childCount - 1; k >= 0; k--)
            {
                var go = transform.GetChild(k).gameObject;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            chunks.Clear();
            cancel.Cancel();
            cancel.Dispose();
            cancel = new CancellationTokenSource();
            preparing = null;
            prepared = null;
            appliedChunks = appliedWater = appliedRivers = 0;
            Ready = false;
        }

        void Pump()
        {
            if (preparing != null && preparing.IsCompleted)
            {
                if (preparing.IsFaulted) { UnityEngine.Debug.LogException(preparing.Exception); preparing = null; return; }
                prepared = preparing.Result;
                preparing = null;
            }
            if (prepared == null || Ready) return;
            if (appliedChunks == 0)
            {
                src = prepared.Src;
                World = src.World;
                Detail = src.Detail;
                Seed = (int)World.Config.Seed;
                Size = World.Config.Size;
                GenerateMs = prepared.GenMs;
                MapSource = prepared.Source;
                BuiltMeshes = 0;
            }
            var sw = Stopwatch.StartNew();
            var list = prepared.Chunks;
            while (appliedChunks < list.Count && Spend(list[appliedChunks].Coarse.Verts.Length))
                chunks.Add(BuildChunk(list[appliedChunks++]));
            while (appliedChunks == list.Count && appliedWater < list.Count)
            {
                var water = list[appliedWater].Water;
                if (water != null && !Spend(water.Verts.Length)) break;
                if (water != null) AttachWater(chunks[appliedWater], water);
                appliedWater++;
            }
            while (appliedWater == list.Count && appliedRivers < prepared.Rivers.Count && Spend(prepared.Rivers[appliedRivers].Verts.Length))
                Attach("Rivers", transform, ToMesh(prepared.Rivers[appliedRivers++]));
            if (appliedWater < list.Count || appliedRivers < prepared.Rivers.Count) { MeshMs += sw.ElapsedMilliseconds; return; }
            BuildSea();
            MeshMs = prepared.MeshMs + MeshMs + sw.ElapsedMilliseconds;
            prepared = null;
            Ready = true;
        }

        static Prepared Prepare(GeneratedWorld world, IReadOnlyList<BiomeDef> defs, int cells, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            var s = new Source { World = world, Size = world.Config.Size, ChunkCells = cells, Cm = world.Config.CellMeters };
            s.Detail = new TerrainDetail(world);
            s.Colors = BaseColors(world, defs);
            var list = new List<ChunkData>();
            int n = s.Size;
            for (int cy = 0; cy < n; cy += cells)
                for (int cx = 0; cx < n; cx += cells)
                    if (!token.IsCancellationRequested && HasLand(s, cx, cy))
                        list.Add(new ChunkData { X = cx, Y = cy, Coarse = TerrainMesh(s, cx, cy, Steps[Steps.Length - 1]), Water = WaterMesh(s, cx, cy) });
            token.ThrowIfCancellationRequested();
            return new Prepared { Src = s, Chunks = list, Rivers = RiverMeshes(s), MeshMs = sw.ElapsedMilliseconds };
        }

        public void UpdateLod(Camera cam)
        {
            if (!Ready) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            float k = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var eye = cam.transform.position;
            float cm = src.Cm;
            VisibleChunks = VisibleTriangles = 0;
            Array.Clear(lodCounts, 0, lodCounts.Length);
            foreach (var ch in chunks)
            {
                Collect(ch);
                float d = Mathf.Max(1f, Vector3.Distance(eye, ch.Bounds.ClosestPoint(eye)));
                bool vis = d < cam.farClipPlane && GeometryUtility.TestPlanesAABB(planes, ch.Bounds);
                ch.Renderer.enabled = vis;
                if (ch.Water != null) ch.Water.enabled = vis;
                if (!vis) continue;
                float allowed = TargetQuadScreen * d * k;
                int lod = 0;
                while (lod < Steps.Length - 1 && Steps[lod + 1] * cm <= allowed) lod++;
                if (ch.Lods[lod] == null && ch.Pending[lod] == null && (Blocking || Outstanding < BuildBudget)) Request(ch, lod);
                while (ch.Lods[lod] == null) lod++;
                if (lod != ch.Lod) { ch.Filter.sharedMesh = ch.Lods[lod]; ch.Lod = lod; }
                VisibleChunks++;
                VisibleTriangles += ch.Tris[lod] + ch.WaterTris;
                lodCounts[lod]++;
            }
        }

        void Request(Chunk ch, int lod)
        {
            var s = src;
            int x = ch.X, y = ch.Y;
            float step = Steps[lod];
            if (Blocking) { SetLod(ch, lod, TerrainMesh(s, x, y, step)); return; }
            ch.Pending[lod] = Run(_ => TerrainMesh(s, x, y, step));
        }

        void Collect(Chunk ch)
        {
            for (int l = 0; l < Steps.Length; l++)
            {
                var t = ch.Pending[l];
                if (t == null || !t.IsCompleted) continue;
                if (t.IsFaulted) { ch.Pending[l] = null; UnityEngine.Debug.LogException(t.Exception); continue; }
                var data = t.Result;
                if (data != null && !Spend(data.Verts.Length)) return;
                ch.Pending[l] = null;
                if (data != null) SetLod(ch, l, data);
            }
        }

        void SetLod(Chunk ch, int lod, MeshData data)
        {
            ch.Lods[lod] = ToMesh(data);
            ch.Tris[lod] = data.Tris.Length / 3;
            BuiltMeshes++;
        }

        public string Stats() =>
            $"seed {Seed} size {Size} cell {World?.Config.CellMeters:F0} m | {MapSource}, meshes {MeshMs} ms, {BuiltMeshes} built\n" +
            $"chunks {VisibleChunks}/{chunks.Count} visible, {VisibleTriangles / 1000f:F0}k tris, by LOD {string.Join("/", lodCounts)}";

        public float GroundHeight(Vector3 p)
        {
            if (World == null) return 0;
            float cm = World.Config.CellMeters, top = Size - 1;
            return Mathf.Max(0, Detail.HeightAt(Mathf.Clamp(p.x / cm, 0, top), Mathf.Clamp(p.z / cm, 0, top)));
        }

        static bool HasLand(Source s, int cx, int cy)
        {
            int n = s.Size;
            for (int y = cy; y <= Mathf.Min(cy + s.ChunkCells, n - 1); y++)
                for (int x = cx; x <= Mathf.Min(cx + s.ChunkCells, n - 1); x++)
                    if (!s.World.Sea[y * n + x]) return true;
            return false;
        }

        Chunk BuildChunk(ChunkData data)
        {
            var go = new GameObject($"Chunk {data.X / ChunkCells},{data.Y / ChunkCells}");
            go.transform.SetParent(transform, false);
            var ch = new Chunk { X = data.X, Y = data.Y, Lods = new Mesh[Steps.Length], Pending = new Task<MeshData>[Steps.Length], Tris = new int[Steps.Length] };
            int coarse = Steps.Length - 1;
            SetLod(ch, coarse, data.Coarse);
            float cm = src.Cm, w = ChunkCells * cm;
            var lb = ch.Lods[coarse].bounds;
            ch.Bounds = new Bounds(new Vector3(data.X * cm + w / 2, lb.center.y, data.Y * cm + w / 2), new Vector3(w, lb.size.y + 80f, w));
            ch.Filter = go.AddComponent<MeshFilter>();
            ch.Filter.sharedMesh = ch.Lods[coarse];
            ch.Lod = coarse;
            ch.Renderer = go.AddComponent<MeshRenderer>();
            ch.Renderer.sharedMaterial = Material;
            return ch;
        }

        void AttachWater(Chunk ch, MeshData water)
        {
            ch.Water = Attach("Water", ch.Filter.transform, ToMesh(water));
            ch.WaterTris = water.Tris.Length / 3;
        }

        MeshRenderer Attach(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            return r;
        }

        static Mesh ToMesh(MeshData d)
        {
            var mesh = new Mesh { name = d.Name, indexFormat = d.Verts.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(d.Verts);
            mesh.SetNormals(d.Norms);
            mesh.SetColors(d.Cols);
            mesh.SetTriangles(d.Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static MeshData TerrainMesh(Source s, int cx, int cy, float step)
        {
            int n = s.Size, m = Mathf.RoundToInt(s.ChunkCells / step), w = m + 1, g = m + 3;
            float cm = s.Cm, top = n - 1;
            var hs = new float[g * g];
            for (int j = 0; j < g; j++)
                for (int i = 0; i < g; i++)
                    hs[j * g + i] = s.Detail.HeightAt(Mathf.Clamp(cx + (i - 1) * step, 0, top), Mathf.Clamp(cy + (j - 1) * step, 0, top));

            int ringCount = 4 * m, total = w * w + ringCount;
            var verts = new Vector3[total];
            var norms = new Vector3[total];
            var cols = new Color[total];
            var tris = new int[m * m * 6 + ringCount * 12];
            for (int j = 0; j <= m; j++)
                for (int i = 0; i <= m; i++)
                {
                    float gx = Mathf.Min(cx + i * step, top), gy = Mathf.Min(cy + j * step, top);
                    int c = (j + 1) * g + i + 1, v = j * w + i;
                    float h = hs[c];
                    var nrm = new Vector3(hs[c - 1] - hs[c + 1], 2 * step * cm, hs[c - g] - hs[c + g]).normalized;
                    verts[v] = new Vector3(gx * cm, h, gy * cm);
                    norms[v] = nrm;
                    cols[v] = ColorAt(s, gx, gy, h, nrm);
                }
            int t = 0;
            for (int j = 0; j < m; j++)
                for (int i = 0; i < m; i++)
                {
                    int a = j * w + i;
                    tris[t++] = a; tris[t++] = a + w; tris[t++] = a + 1;
                    tris[t++] = a + 1; tris[t++] = a + w; tris[t++] = a + w + 1;
                }

            float drop = step * cm * 0.5f + 20f;
            var ring = new int[ringCount];
            int q = 0;
            for (int i = 0; i < m; i++) ring[q++] = i;
            for (int j = 0; j < m; j++) ring[q++] = j * w + m;
            for (int i = m; i > 0; i--) ring[q++] = m * w + i;
            for (int j = m; j > 0; j--) ring[q++] = j * w;
            int first = w * w;
            for (int k = 0; k < ringCount; k++)
            {
                int r = ring[k];
                verts[first + k] = verts[r] - new Vector3(0, drop, 0);
                norms[first + k] = norms[r];
                cols[first + k] = cols[r];
            }
            for (int k = 0; k < ringCount; k++)
            {
                int a = ring[k], b = ring[(k + 1) % ringCount], a2 = first + k, b2 = first + (k + 1) % ringCount;
                tris[t++] = a; tris[t++] = b; tris[t++] = a2;
                tris[t++] = b; tris[t++] = b2; tris[t++] = a2;
                tris[t++] = a; tris[t++] = a2; tris[t++] = b;
                tris[t++] = b; tris[t++] = a2; tris[t++] = b2;
            }
            return new MeshData { Name = $"Terrain {cx},{cy} x{step}", Verts = verts, Norms = norms, Cols = cols, Tris = tris };
        }

        static Color Linear(Color c) => new Color(ToLinear(c.r), ToLinear(c.g), ToLinear(c.b), c.a);

        static float ToLinear(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        static Color ColorAt(Source s, float gx, float gy, float h, Vector3 nrm)
        {
            if (h <= 0) return Linear(Color.Lerp(Sand, Sand * 0.6f, Mathf.Clamp01(-h / 30f)));
            int n = s.Size, x0 = Mathf.Min((int)gx, n - 2), y0 = Mathf.Min((int)gy, n - 2);
            float tx = gx - x0, ty = gy - y0;
            var bc = s.Colors;
            var c = Color.Lerp(Color.Lerp(bc[y0 * n + x0], bc[y0 * n + x0 + 1], tx),
                Color.Lerp(bc[(y0 + 1) * n + x0], bc[(y0 + 1) * n + x0 + 1], tx), ty);
            float slope = Mathf.Sqrt(Mathf.Max(0, 1 - nrm.y * nrm.y)) / Mathf.Max(nrm.y, 0.05f);
            c = Color.Lerp(c, Rock, Mathf.Clamp01((slope - 0.45f) / 0.4f));
            c = Color.Lerp(c, Sand, Mathf.Clamp01(1f - h / 6f));
            return Linear(c);
        }

        static MeshData Flat(string name, List<Vector3> verts, List<int> tris, Color color)
        {
            var cols = new Color[verts.Count];
            var up = new Vector3[verts.Count];
            var lin = Linear(color);
            for (int k = 0; k < cols.Length; k++) { cols[k] = lin; up[k] = Vector3.up; }
            return new MeshData { Name = name, Verts = verts.ToArray(), Norms = up, Cols = cols, Tris = tris.ToArray() };
        }

        static MeshData WaterMesh(Source s, int cx, int cy)
        {
            int n = s.Size;
            float cm = s.Cm, half = cm * 0.5f;
            var world = s.World;
            var hy = world.Water;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int y = cy; y < Mathf.Min(cy + s.ChunkCells, n); y++)
                for (int x = cx; x < Mathf.Min(cx + s.ChunkCells, n); x++)
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
            return verts.Count == 0 ? null : Flat($"Water {cx},{cy}", verts, tris, LakeColor);
        }

        const int RiverPieceVertices = 16000;

        static List<MeshData> RiverMeshes(Source s)
        {
            int n = s.Size;
            float cm = s.Cm;
            var world = s.World;
            var pieces = new List<MeshData>();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var r in world.Water.Rivers)
            {
                if (r.Cells.Count < 2) continue;
                if (verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(Flat($"Rivers {pieces.Count}", verts, tris, RiverColor));
                    verts.Clear();
                    tris.Clear();
                }
                var pts = new List<Vector4>();
                for (int k = 0; k < r.Cells.Count; k++)
                {
                    int c = r.Cells[k];
                    pts.Add(new Vector4(c % n * cm, Mathf.Max(r.Surface[k], world.Height[c]), c / n * cm, r.Width[k]));
                }
                for (int it = 0; it < 2; it++) pts = Chaikin(pts);
                int b = verts.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    var a = pts[Mathf.Max(k - 1, 0)];
                    var z = pts[Mathf.Min(k + 1, pts.Count - 1)];
                    var dir = new Vector3(z.x - a.x, 0, z.z - a.z).normalized;
                    var side = new Vector3(dir.z, 0, -dir.x) * Mathf.Max(pts[k].w * 0.5f, 6f);
                    var p = new Vector3(pts[k].x, 0, pts[k].z);
                    p.y = GroundAt(s, p, side, pts[k].y) + 1f;
                    verts.Add(p - side);
                    verts.Add(p + side);
                    if (k == 0) continue;
                    int v = b + 2 * k;
                    tris.Add(v - 2); tris.Add(v); tris.Add(v - 1);
                    tris.Add(v - 1); tris.Add(v); tris.Add(v + 1);
                }
            }
            if (verts.Count > 0) pieces.Add(Flat($"Rivers {pieces.Count}", verts, tris, RiverColor));
            return pieces;
        }

        static float GroundAt(Source s, Vector3 p, Vector3 side, float surface)
        {
            float cm = s.Cm, top = s.Size - 1, h = surface;
            for (int k = -1; k <= 1; k++)
            {
                var q = p + side * k;
                h = Mathf.Max(h, Mathf.Min(s.Detail.HeightAt(Mathf.Clamp(q.x / cm, 0, top), Mathf.Clamp(q.z / cm, 0, top)), surface + 3f));
            }
            return h;
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

        void BuildSea()
        {
            float span = Size * src.Cm, pad = span * 1.5f;
            var c = Linear(SeaColor);
            var mesh = new Mesh { name = "Sea" };
            mesh.vertices = new[]
            {
                new Vector3(-pad, 0, -pad), new Vector3(-pad, 0, span + pad),
                new Vector3(span + pad, 0, -pad), new Vector3(span + pad, 0, span + pad),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.colors = new[] { c, c, c, c };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            Attach("Sea", transform, mesh);
        }

        static Color[] BaseColors(GeneratedWorld w, IReadOnlyList<BiomeDef> defs)
        {
            var colors = new Color[w.Height.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                uint u = w.Biome != null && w.Biome[i] >= 0 ? defs[w.Biome[i]].Color : 0x8DAA5Eu;
                colors[i] = w.Sea[i] ? Sand : new Color(((u >> 16) & 255) / 255f, ((u >> 8) & 255) / 255f, (u & 255) / 255f);
            }
            return colors;
        }
    }
}
