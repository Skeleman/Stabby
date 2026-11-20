using System.Collections.Generic;
using System.Diagnostics;
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
        // Largest quad edge allowed, as a fraction of screen width; picks each chunk's LOD.
        public float TargetQuadScreen = 0.01f;
        public int BuildBudget = 8;
        public Color Sky = new Color(0.62f, 0.74f, 0.86f);

        // Vertex spacing in cells per LOD.
        static readonly float[] Steps = { 0.25f, 0.5f, 1, 2, 4, 8 };
        static readonly Color SeaColor = new Color(0.16f, 0.36f, 0.55f), LakeColor = new Color(0.22f, 0.47f, 0.68f),
            RiverColor = new Color(0.20f, 0.45f, 0.78f), Sand = new Color(0.78f, 0.72f, 0.52f), Rock = new Color(0.47f, 0.44f, 0.41f);

        public GeneratedWorld World { get; private set; }
        public TerrainDetail Detail { get; private set; }
        public long GenerateMs, MeshMs;
        public int VisibleChunks, VisibleTriangles, BuiltMeshes;
        Color[] baseColors;
        readonly int[] lodCounts = new int[Steps.Length];
        readonly List<Chunk> chunks = new List<Chunk>();

        sealed class Chunk
        {
            public Bounds Bounds;
            public Mesh[] Lods;
            public int[] Tris;
            public int WaterTris;
            public MeshFilter Filter;
            public MeshRenderer Renderer, Water;
            public int X, Y, Lod = -1;
        }

        void Start()
        {
            if (chunks.Count == 0) Build();
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam != null) UpdateLod(cam);
        }

        public void Build()
        {
            Clear();
            Shader.SetGlobalColor("_StabbySky", Sky * 0.9f);
            Shader.SetGlobalColor("_StabbyGround", new Color(0.3f, 0.27f, 0.22f));
            var sw = Stopwatch.StartNew();
            var defs = BiomeFile.Load(BiomeFile.DefaultPath);
            var c = new WorldConfig { Seed = (ulong)Seed, Size = Size, CellMeters = 60f * 512 / Size };
            World = WorldGenerator.Generate(c, defs);
            GenerateMs = sw.ElapsedMilliseconds;
            sw.Restart();

            Detail = new TerrainDetail(World);
            baseColors = BaseColors(World, defs);
            BuiltMeshes = 0;
            int n = Size;
            for (int cy = 0; cy < n; cy += ChunkCells)
                for (int cx = 0; cx < n; cx += ChunkCells)
                    if (HasLand(cx, cy)) chunks.Add(BuildChunk(cx, cy));
            BuildSea();
            BuildRivers();
            MeshMs = sw.ElapsedMilliseconds;
        }

        public void Clear()
        {
            for (int k = transform.childCount - 1; k >= 0; k--)
            {
                var go = transform.GetChild(k).gameObject;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            chunks.Clear();
        }

        public void UpdateLod(Camera cam)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            float k = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var eye = cam.transform.position;
            float cm = World.Config.CellMeters;
            int budget = BuildBudget;
            VisibleChunks = VisibleTriangles = 0;
            System.Array.Clear(lodCounts, 0, lodCounts.Length);
            foreach (var ch in chunks)
            {
                float d = Mathf.Max(1f, Vector3.Distance(eye, ch.Bounds.ClosestPoint(eye)));
                bool vis = d < cam.farClipPlane && GeometryUtility.TestPlanesAABB(planes, ch.Bounds);
                ch.Renderer.enabled = vis;
                if (ch.Water != null) ch.Water.enabled = vis;
                if (!vis) continue;
                float allowed = TargetQuadScreen * d * k;
                int lod = 0;
                while (lod < Steps.Length - 1 && Steps[lod + 1] * cm <= allowed) lod++;
                if (ch.Lods[lod] == null && budget > 0) { budget--; EnsureLod(ch, lod); }
                while (ch.Lods[lod] == null) lod++;
                if (lod != ch.Lod) { ch.Filter.sharedMesh = ch.Lods[lod]; ch.Lod = lod; }
                VisibleChunks++;
                VisibleTriangles += ch.Tris[lod] + ch.WaterTris;
                lodCounts[lod]++;
            }
        }

        public string Stats() =>
            $"seed {Seed} size {Size} cell {World?.Config.CellMeters:F0} m | gen {GenerateMs} ms, meshes {MeshMs} ms, {BuiltMeshes} built\n" +
            $"chunks {VisibleChunks}/{chunks.Count} visible, {VisibleTriangles / 1000f:F0}k tris, by LOD {string.Join("/", lodCounts)}";

        public float GroundHeight(Vector3 p)
        {
            if (World == null) return 0;
            float cm = World.Config.CellMeters, top = Size - 1;
            return Mathf.Max(0, Detail.HeightAt(Mathf.Clamp(p.x / cm, 0, top), Mathf.Clamp(p.z / cm, 0, top)));
        }

        bool HasLand(int cx, int cy)
        {
            int n = Size;
            for (int y = cy; y <= Mathf.Min(cy + ChunkCells, n - 1); y++)
                for (int x = cx; x <= Mathf.Min(cx + ChunkCells, n - 1); x++)
                    if (!World.Sea[y * n + x]) return true;
            return false;
        }

        Chunk BuildChunk(int cx, int cy)
        {
            var go = new GameObject($"Chunk {cx / ChunkCells},{cy / ChunkCells}");
            go.transform.SetParent(transform, false);
            var ch = new Chunk { X = cx, Y = cy, Lods = new Mesh[Steps.Length], Tris = new int[Steps.Length] };
            int coarse = Steps.Length - 1;
            EnsureLod(ch, coarse);
            float cm = World.Config.CellMeters, w = ChunkCells * cm;
            var lb = ch.Lods[coarse].bounds;
            ch.Bounds = new Bounds(new Vector3(cx * cm + w / 2, lb.center.y, cy * cm + w / 2), new Vector3(w, lb.size.y + 80f, w));
            ch.Filter = go.AddComponent<MeshFilter>();
            ch.Filter.sharedMesh = ch.Lods[coarse];
            ch.Lod = coarse;
            ch.Renderer = go.AddComponent<MeshRenderer>();
            ch.Renderer.sharedMaterial = Material;

            var water = WaterMesh(cx, cy);
            if (water != null)
            {
                var wgo = new GameObject("Water");
                wgo.transform.SetParent(go.transform, false);
                wgo.AddComponent<MeshFilter>().sharedMesh = water;
                ch.Water = wgo.AddComponent<MeshRenderer>();
                ch.Water.sharedMaterial = Material;
                ch.WaterTris = water.triangles.Length / 3;
            }
            return ch;
        }

        void EnsureLod(Chunk ch, int lod)
        {
            if (ch.Lods[lod] != null) return;
            ch.Lods[lod] = TerrainMesh(ch.X, ch.Y, Steps[lod]);
            ch.Tris[lod] = ch.Lods[lod].triangles.Length / 3;
            BuiltMeshes++;
        }

        Mesh TerrainMesh(int cx, int cy, float step)
        {
            int n = Size, m = Mathf.RoundToInt(ChunkCells / step), w = m + 1, g = m + 3;
            float cm = World.Config.CellMeters, top = n - 1;
            var hs = new float[g * g];
            for (int j = 0; j < g; j++)
                for (int i = 0; i < g; i++)
                    hs[j * g + i] = Detail.HeightAt(Mathf.Clamp(cx + (i - 1) * step, 0, top), Mathf.Clamp(cy + (j - 1) * step, 0, top));

            var verts = new List<Vector3>(w * w + 4 * m);
            var norms = new List<Vector3>(verts.Capacity);
            var cols = new List<Color>(verts.Capacity);
            var tris = new List<int>(m * m * 6 + 4 * m * 12);
            for (int j = 0; j <= m; j++)
                for (int i = 0; i <= m; i++)
                {
                    float gx = Mathf.Min(cx + i * step, top), gy = Mathf.Min(cy + j * step, top);
                    int c = (j + 1) * g + i + 1;
                    float h = hs[c];
                    var nrm = new Vector3(hs[c - 1] - hs[c + 1], 2 * step * cm, hs[c - g] - hs[c + g]).normalized;
                    verts.Add(new Vector3(gx * cm, h, gy * cm));
                    norms.Add(nrm);
                    cols.Add(ColorAt(gx, gy, h, nrm));
                }
            for (int j = 0; j < m; j++)
                for (int i = 0; i < m; i++)
                {
                    int a = j * w + i;
                    tris.Add(a); tris.Add(a + w); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + w); tris.Add(a + w + 1);
                }

            // Skirts hide cracks where neighbouring chunks sit at different LODs.
            float drop = step * cm * 0.5f + 20f;
            var ring = new List<int>();
            for (int i = 0; i < m; i++) ring.Add(i);
            for (int j = 0; j < m; j++) ring.Add(j * w + m);
            for (int i = m; i > 0; i--) ring.Add(m * w + i);
            for (int j = m; j > 0; j--) ring.Add(j * w);
            int first = verts.Count;
            foreach (int r in ring)
            {
                verts.Add(verts[r] - new Vector3(0, drop, 0));
                norms.Add(norms[r]);
                cols.Add(cols[r]);
            }
            for (int k = 0; k < ring.Count; k++)
            {
                int a = ring[k], b = ring[(k + 1) % ring.Count], a2 = first + k, b2 = first + (k + 1) % ring.Count;
                tris.Add(a); tris.Add(b); tris.Add(a2);
                tris.Add(b); tris.Add(b2); tris.Add(a2);
                tris.Add(a); tris.Add(a2); tris.Add(b);
                tris.Add(b); tris.Add(a2); tris.Add(b2);
            }

            var mesh = new Mesh { name = $"Terrain {cx},{cy} x{step}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        Color ColorAt(float gx, float gy, float h, Vector3 nrm)
        {
            if (h <= 0) return Color.Lerp(Sand, Sand * 0.6f, Mathf.Clamp01(-h / 30f)).linear;
            int n = Size, x0 = Mathf.Min((int)gx, n - 2), y0 = Mathf.Min((int)gy, n - 2);
            float tx = gx - x0, ty = gy - y0;
            var c = Color.Lerp(Color.Lerp(baseColors[y0 * n + x0], baseColors[y0 * n + x0 + 1], tx),
                Color.Lerp(baseColors[(y0 + 1) * n + x0], baseColors[(y0 + 1) * n + x0 + 1], tx), ty);
            float slope = Mathf.Sqrt(Mathf.Max(0, 1 - nrm.y * nrm.y)) / Mathf.Max(nrm.y, 0.05f);
            c = Color.Lerp(c, Rock, Mathf.Clamp01((slope - 0.45f) / 0.4f));
            c = Color.Lerp(c, Sand, Mathf.Clamp01(1f - h / 6f));
            return c.linear;
        }

        Mesh WaterMesh(int cx, int cy)
        {
            int n = Size;
            float cm = World.Config.CellMeters, half = cm * 0.5f;
            var hy = World.Water;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            for (int y = cy; y < Mathf.Min(cy + ChunkCells, n); y++)
                for (int x = cx; x < Mathf.Min(cx + ChunkCells, n); x++)
                {
                    int c = y * n + x;
                    if (World.Sea[c] || hy.LakeId[c] < 0) continue;
                    float h = Mathf.Max(hy.WaterSurface[c], World.Height[c]) + 0.5f;
                    int b = verts.Count;
                    verts.Add(new Vector3(x * cm - half, h, y * cm - half));
                    verts.Add(new Vector3(x * cm - half, h, y * cm + half));
                    verts.Add(new Vector3(x * cm + half, h, y * cm - half));
                    verts.Add(new Vector3(x * cm + half, h, y * cm + half));
                    var col = LakeColor.linear;
                    for (int k = 0; k < 4; k++) cols.Add(col);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 3);
                }
            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = $"Water {cx},{cy}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            var up = new Vector3[verts.Count];
            for (int k = 0; k < up.Length; k++) up[k] = Vector3.up;
            mesh.normals = up;
            mesh.RecalculateBounds();
            return mesh;
        }

        void BuildRivers()
        {
            int n = Size;
            float cm = World.Config.CellMeters;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var r in World.Water.Rivers)
            {
                if (r.Cells.Count < 2) continue;
                var pts = new List<Vector4>();
                for (int k = 0; k < r.Cells.Count; k++)
                {
                    int c = r.Cells[k];
                    pts.Add(new Vector4(c % n * cm, Mathf.Max(r.Surface[k], World.Height[c]), c / n * cm, r.Width[k]));
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
                    p.y = GroundAt(p, side, pts[k].y) + 1f;
                    verts.Add(p - side);
                    verts.Add(p + side);
                    if (k == 0) continue;
                    int v = b + 2 * k;
                    tris.Add(v - 2); tris.Add(v); tris.Add(v - 1);
                    tris.Add(v - 1); tris.Add(v); tris.Add(v + 1);
                }
            }
            if (verts.Count == 0) return;
            var mesh = new Mesh { name = "Rivers", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            var cols = new Color[verts.Count];
            var up = new Vector3[verts.Count];
            for (int k = 0; k < cols.Length; k++) { cols[k] = RiverColor.linear; up[k] = Vector3.up; }
            mesh.colors = cols;
            mesh.normals = up;
            mesh.SetTriangles(tris, 0);
            var go = new GameObject("Rivers");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Material;
        }

        // A ribbon must clear the ground across its whole width, up to a few metres of bank.
        float GroundAt(Vector3 p, Vector3 side, float surface)
        {
            float cm = World.Config.CellMeters, top = Size - 1, h = surface;
            for (int k = -1; k <= 1; k++)
            {
                var q = p + side * k;
                h = Mathf.Max(h, Mathf.Min(Detail.HeightAt(Mathf.Clamp(q.x / cm, 0, top), Mathf.Clamp(q.z / cm, 0, top)), surface + 3f));
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
            float span = Size * World.Config.CellMeters, pad = span * 1.5f;
            var go = new GameObject("Sea");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "Sea" };
            mesh.vertices = new[]
            {
                new Vector3(-pad, 0, -pad), new Vector3(-pad, 0, span + pad),
                new Vector3(span + pad, 0, -pad), new Vector3(span + pad, 0, span + pad),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            var c = SeaColor.linear;
            mesh.colors = new[] { c, c, c, c };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Material;
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
