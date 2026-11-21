using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Mathematics;
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
            public float3[] Colors;
            public StrataLook Strata;
            public int Size, ChunkCells;
            public float Cm;
        }

        sealed class ChunkData
        {
            public int X, Y;
            public MeshParts Coarse, Water;
        }

        sealed class Prepared
        {
            public Source Src;
            public List<ChunkData> Chunks;
            public List<MeshParts> Rivers;
            public long GenMs, MeshMs;
            public string Source = "";
        }

        sealed class Chunk
        {
            public Bounds Bounds;
            public Mesh[] Lods;
            public Task<MeshParts>[] Pending;
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
            string path = UseMapFile ? MapFile.PathFor(c.Seed, c.Size) : null, materials = ContentFiles.MaterialsPath;
            Begin(token =>
            {
                var sw = Stopwatch.StartNew();
                var defs = ContentFiles.Biomes();
                var lib = ContentFiles.Materials(materials);
                var (world, source) = path != null ? MapFile.LoadOrGenerate(path, c, defs, lib, true) : (WorldGenerator.Generate(c, defs, lib), $"generated in {sw.ElapsedMilliseconds} ms");
                long genMs = sw.ElapsedMilliseconds;
                var p = Prepare(world, defs, materials, cells, token);
                p.GenMs = genMs;
                p.Source = source;
                return p;
            });
        }

        public void Build(GeneratedWorld world, IReadOnlyList<BiomeDef> defs)
        {
            int cells = ChunkCells;
            string materials = ContentFiles.MaterialsPath;
            Begin(token => Prepare(world, defs, materials, cells, token));
        }

        void Begin(Func<CancellationToken, Prepared> work)
        {
            Clear();
            Shader.SetGlobalColor("_StabbySky", Sky * 0.45f);
            Shader.SetGlobalColor("_StabbyGround", new Color(0.14f, 0.14f, 0.13f));
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
                TerrainLook.ApplyStrata(src.Strata);
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
                Attach("Rivers", transform, WaterGeometry.ToMesh(prepared.Rivers[appliedRivers++]));
            if (appliedWater < list.Count || appliedRivers < prepared.Rivers.Count) { MeshMs += sw.ElapsedMilliseconds; return; }
            BuildSea();
            MeshMs = prepared.MeshMs + MeshMs + sw.ElapsedMilliseconds;
            prepared = null;
            Ready = true;
        }

        static Prepared Prepare(GeneratedWorld world, IReadOnlyList<BiomeDef> defs, string materials, int cells, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            var s = new Source { World = world, Size = world.Config.Size, ChunkCells = cells, Cm = world.Config.CellMeters };
            s.Detail = new TerrainDetail(world);
            s.Colors = TerrainLook.BaseColors(world, defs);
            s.Strata = TerrainLook.Strata(world, ContentFiles.Materials(materials), defs);
            var list = new List<ChunkData>();
            int n = s.Size;
            for (int cy = 0; cy < n; cy += cells)
                for (int cx = 0; cx < n; cx += cells)
                    if (!token.IsCancellationRequested && HasLand(s, cx, cy))
                        list.Add(new ChunkData { X = cx, Y = cy, Coarse = TerrainMesh(s, cx, cy, Steps[Steps.Length - 1]), Water = WaterGeometry.Lakes(world, cx, cy, cells) });
            token.ThrowIfCancellationRequested();
            return new Prepared { Src = s, Chunks = list, Rivers = WaterGeometry.Rivers(world), MeshMs = sw.ElapsedMilliseconds };
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

        void SetLod(Chunk ch, int lod, MeshParts data)
        {
            ch.Lods[lod] = WaterGeometry.ToMesh(data);
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
            var ch = new Chunk { X = data.X, Y = data.Y, Lods = new Mesh[Steps.Length], Pending = new Task<MeshParts>[Steps.Length], Tris = new int[Steps.Length] };
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

        void AttachWater(Chunk ch, MeshParts water)
        {
            ch.Water = Attach("Water", ch.Filter.transform, WaterGeometry.ToMesh(water));
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

        static MeshParts TerrainMesh(Source s, int cx, int cy, float step)
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
            var uvs = new Vector2[total];
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
                    (cols[v], uvs[v]) = ColorAt(s, gx, gy, h, nrm);
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
                uvs[first + k] = uvs[r];
            }
            for (int k = 0; k < ringCount; k++)
            {
                int a = ring[k], b = ring[(k + 1) % ringCount], a2 = first + k, b2 = first + (k + 1) % ringCount;
                tris[t++] = a; tris[t++] = b; tris[t++] = a2;
                tris[t++] = b; tris[t++] = b2; tris[t++] = a2;
                tris[t++] = a; tris[t++] = a2; tris[t++] = b;
                tris[t++] = b; tris[t++] = a2; tris[t++] = b2;
            }
            return new MeshParts { Name = $"Terrain {cx},{cy} x{step}", Verts = verts, Norms = norms, Cols = cols, Uvs = uvs, Tris = tris };
        }

        static (Color, Vector2) ColorAt(Source s, float gx, float gy, float h, Vector3 nrm)
        {
            int n = s.Size, x0 = Mathf.Min((int)gx, n - 2), y0 = Mathf.Min((int)gy, n - 2), a = y0 * n + x0;
            var bc = s.Colors;
            var off = s.Strata.Offsets;
            var c = TerrainLook.Shade(TerrainLook.Bilinear(bc[a], bc[a + 1], bc[a + n], bc[a + n + 1], gx - x0, gy - y0), h, nrm.y);
            return (new Color(c.x, c.y, c.z), new Vector2(TerrainLook.Bilinear(off[a], off[a + 1], off[a + n], off[a + n + 1], gx - x0, gy - y0), c.w));
        }

        void BuildSea() => Attach("Sea", transform, WaterGeometry.ToMesh(WaterGeometry.Sea(Size, src.Cm)));
    }
}
