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
        public int ChunkCells = 64;
        public Material Material;
        // Chunk width as a fraction of screen width at which each finer LOD is kept.
        public float[] LodScreenFraction = { 0.6f, 0.3f, 0.15f, 0.07f };
        public Color Sky = new Color(0.62f, 0.74f, 0.86f);

        static readonly int[] Steps = { 1, 2, 4, 8, 16 };
        static readonly Color SeaColor = new Color(0.16f, 0.36f, 0.55f), LakeColor = new Color(0.22f, 0.47f, 0.68f),
            RiverColor = new Color(0.20f, 0.45f, 0.78f), Sand = new Color(0.78f, 0.72f, 0.52f), Rock = new Color(0.47f, 0.44f, 0.41f);

        public GeneratedWorld World { get; private set; }
        public long GenerateMs, MeshMs;
        public int VisibleChunks, VisibleTriangles;
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
            public int Lod = -1;
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

            var colors = CellColors(World, defs);
            var normals = CellNormals(World);
            int n = Size;
            for (int cy = 0; cy < n; cy += ChunkCells)
                for (int cx = 0; cx < n; cx += ChunkCells)
                    if (HasLand(cx, cy)) chunks.Add(BuildChunk(cx, cy, colors, normals));
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
            float width = ChunkCells * World.Config.CellMeters;
            VisibleChunks = VisibleTriangles = 0;
            System.Array.Clear(lodCounts, 0, lodCounts.Length);
            foreach (var ch in chunks)
            {
                float d = Mathf.Max(1f, Vector3.Distance(eye, ch.Bounds.ClosestPoint(eye)));
                bool vis = d < cam.farClipPlane && GeometryUtility.TestPlanesAABB(planes, ch.Bounds);
                ch.Renderer.enabled = vis;
                if (ch.Water != null) ch.Water.enabled = vis;
                if (!vis) continue;
                float frac = width / (d * k);
                int lod = Steps.Length - 1;
                for (int l = 0; l < LodScreenFraction.Length; l++)
                    if (frac >= LodScreenFraction[l]) { lod = l; break; }
                if (lod != ch.Lod) { ch.Filter.sharedMesh = ch.Lods[lod]; ch.Lod = lod; }
                VisibleChunks++;
                VisibleTriangles += ch.Tris[lod] + ch.WaterTris;
                lodCounts[lod]++;
            }
        }

        public string Stats() =>
            $"seed {Seed} size {Size} cell {World?.Config.CellMeters:F0} m | gen {GenerateMs} ms, meshes {MeshMs} ms\n" +
            $"chunks {VisibleChunks}/{chunks.Count} visible, {VisibleTriangles / 1000f:F0}k tris, by LOD {string.Join("/", lodCounts)}";

        public float GroundHeight(Vector3 p)
        {
            if (World == null) return 0;
            int n = Size;
            float cm = World.Config.CellMeters;
            int x = Mathf.Clamp(Mathf.RoundToInt(p.x / cm), 0, n - 1), y = Mathf.Clamp(Mathf.RoundToInt(p.z / cm), 0, n - 1);
            return Mathf.Max(0, World.Height[y * n + x]);
        }

        bool HasLand(int cx, int cy)
        {
            int n = Size;
            for (int y = cy; y <= Mathf.Min(cy + ChunkCells, n - 1); y++)
                for (int x = cx; x <= Mathf.Min(cx + ChunkCells, n - 1); x++)
                    if (!World.Sea[y * n + x]) return true;
            return false;
        }

        Chunk BuildChunk(int cx, int cy, Color[] colors, Vector3[] normals)
        {
            var go = new GameObject($"Chunk {cx / ChunkCells},{cy / ChunkCells}");
            go.transform.SetParent(transform, false);
            var ch = new Chunk { Lods = new Mesh[Steps.Length], Tris = new int[Steps.Length] };
            for (int l = 0; l < Steps.Length; l++)
            {
                ch.Lods[l] = TerrainMesh(cx, cy, Steps[l], colors, normals);
                ch.Tris[l] = ch.Lods[l].triangles.Length / 3;
            }
            ch.Bounds = ch.Lods[0].bounds;
            ch.Filter = go.AddComponent<MeshFilter>();
            ch.Filter.sharedMesh = ch.Lods[0];
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

        Mesh TerrainMesh(int cx, int cy, int step, Color[] colors, Vector3[] normals)
        {
            int n = Size, m = ChunkCells / step, w = m + 1;
            float cm = World.Config.CellMeters;
            var verts = new List<Vector3>(w * w + 4 * w);
            var norms = new List<Vector3>(verts.Capacity);
            var cols = new List<Color>(verts.Capacity);
            var tris = new List<int>(m * m * 6 + 4 * m * 12);
            for (int j = 0; j <= m; j++)
                for (int i = 0; i <= m; i++)
                {
                    int x = Mathf.Min(cx + i * step, n - 1), y = Mathf.Min(cy + j * step, n - 1), c = y * n + x;
                    verts.Add(new Vector3(x * cm, World.Height[c], y * cm));
                    norms.Add(normals[c]);
                    cols.Add(colors[c]);
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
                    var p = new Vector3(pts[k].x, GroundAt(pts[k].x, pts[k].z, pts[k].y) + 1f, pts[k].z);
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

        // The terrain mesh interpolates between cell centres, so a ribbon must clear the highest corner it spans.
        float GroundAt(float x, float z, float surface)
        {
            int n = Size;
            float cm = World.Config.CellMeters, h = surface;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x / cm), 0, n - 1), z0 = Mathf.Clamp(Mathf.FloorToInt(z / cm), 0, n - 1);
            for (int dz = 0; dz <= 1; dz++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    int c = Mathf.Min(z0 + dz, n - 1) * n + Mathf.Min(x0 + dx, n - 1);
                    if (!World.Sea[c]) h = Mathf.Max(h, Mathf.Min(World.Height[c], surface + 3f));
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

        static Vector3[] CellNormals(GeneratedWorld w)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var normals = new Vector3[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = w.Height[y * n + Mathf.Min(x + 1, n - 1)] - w.Height[y * n + Mathf.Max(x - 1, 0)];
                    float dz = w.Height[Mathf.Min(y + 1, n - 1) * n + x] - w.Height[Mathf.Max(y - 1, 0) * n + x];
                    normals[y * n + x] = new Vector3(-dx, 2 * cm, -dz).normalized;
                }
            return normals;
        }

        static Color[] CellColors(GeneratedWorld w, IReadOnlyList<BiomeDef> defs)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var colors = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float h = w.Height[i];
                    Color c;
                    if (w.Sea[i]) c = Color.Lerp(Sand, Sand * 0.6f, Mathf.Clamp01(-h / 30f));
                    else
                    {
                        uint u = w.Biome != null && w.Biome[i] >= 0 ? defs[w.Biome[i]].Color : 0x8DAA5Eu;
                        c = new Color(((u >> 16) & 255) / 255f, ((u >> 8) & 255) / 255f, (u & 255) / 255f);
                        float dx = w.Height[y * n + Mathf.Min(x + 1, n - 1)] - w.Height[y * n + Mathf.Max(x - 1, 0)];
                        float dz = w.Height[Mathf.Min(y + 1, n - 1) * n + x] - w.Height[Mathf.Max(y - 1, 0) * n + x];
                        float slope = Mathf.Sqrt(dx * dx + dz * dz) / (2 * cm);
                        c = Color.Lerp(c, Rock, Mathf.Clamp01((slope - 0.35f) / 0.4f));
                        c = Color.Lerp(c, Sand, Mathf.Clamp01(1f - h / 6f));
                    }
                    colors[i] = c.linear;
                }
            return colors;
        }
    }
}
