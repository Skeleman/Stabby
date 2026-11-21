using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    [MaterialProperty("_BaseColor")]
    public struct ObjectColor : IComponentData { public float4 Value; }

    [MaterialProperty("_Fade")]
    public struct ObjectFade : IComponentData { public float4 Value; }

    [MaterialProperty("_Leaf")]
    public struct ObjectLeaf : IComponentData { public float4 Value; }

    [MaterialProperty("_Imp")]
    public struct ObjectImp : IComponentData { public float Value; }

    public struct ObjectLod : IComponentData
    {
        public BatchMeshID L0, L1, L2, Imp, Blend;
        public float Lod1, Lod2, Fade, Shadow, Radius, Sway, Flutter, Impostor;
    }

    public struct ObjectSpawn
    {
        public float4x4 M;
        public float4 Color, Leaf;
        public int Mesh, Def;
        public float Radius, Ground, Size;
        public bool Shadow;
        public ulong Id;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ObjectViewSystem : SystemBase
    {
        public const int Lods = 3, Stride = Lods + 2, RegisterPerFrame = 24, SpawnBudget = 4000, Frames = 8, Cell = 64;
        public const float DespawnReach = 1f / (1f - Detail.Hysteresis), UnloadReach = 1.5f;

        sealed class MeshSet
        {
            public Vector3[][] Positions, Normals;
            public Vector4[][] Uv0, Uv1;
            public int[][] Indices;
            public AABB[] Bounds;
            public Vector4[] Centers;
            public int[] Slots;
            public int SlotCount;
        }

        struct ChunkParts
        {
            public int Chunk;
            public NativeArray<long> Keys;
            public NativeArray<int3> Ranges;
            public NativeArray<ObjectSpawn> Parts;

            public void Dispose()
            {
                Keys.Dispose();
                Ranges.Dispose();
                Parts.Dispose();
            }
        }

        ObjectField field;
        ObjectLibrary lib;
        float cm, worldM, span;
        int across;
        int[][] meshBase;
        float[] reach, tile;
        Task<MeshSet> meshTask;
        MeshSet meshSet;
        Mesh[] meshes;
        BatchMeshID[] meshIds;
        int registered;
        public const int FoliageLayer = 8;
        Material material, foliageMaterial;
        BatchMaterialID materialId, foliageMaterialId;
        Entity castPrefab, flatPrefab, castFoliagePrefab, flatFoliagePrefab;
        bool[] foliageMesh;
        Task<LeafAtlas.Pixels> atlasTask;
        Texture2D atlas;
        RenderTexture impA, impB;
        Task<ChunkParts> chunkTask;
        Func<ChunkParts> collectNext;
        int collecting;
        readonly List<ObjectInstance> placed = new List<ObjectInstance>();
        readonly int[] groupCounts = new int[4], groupAt = new int[4];
        readonly NativeArray<Entity>[] groupMade = new NativeArray<Entity>[4];
        readonly Entity[] groupPrefabs = new Entity[4];
        NativeArray<bool> loaded;
        NativeArray<ObjectSpawn>[] chunkParts;
        NativeArray<long>[] chunkKeys;
        NativeParallelHashMap<long, int3> ranges;
        NativeParallelHashSet<long> spawned;
        NativeParallelMultiHashMap<long, Entity> tileEntities;
        int liveEntities, chunksLoaded, chunksHeld, chunksUnloaded, partsHeld, peakParts, peakChunks;
        int[] liveByDef;
        ObjectKind[] kinds;
        float worstUpdate;

        public static int Pending { get; private set; } = 1;
        public static int LiveEntities { get; private set; }
        public static int PartsHeld { get; private set; }
        public static int ChunksHeld { get; private set; }
        public static int ChunksUnloaded { get; private set; }
        static int[] liveByDefShared;
        static ObjectLibrary libShared;

        public static string PerDef()
        {
            if (libShared == null || liveByDefShared == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int d = 0; d < liveByDefShared.Length; d++) sb.Append(d == 0 ? "" : ", ").Append(libShared.Objects[d].Id).Append(' ').Append(liveByDefShared[d]);
            return sb.ToString();
        }

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) { Enabled = false; return; }
            ranges = new NativeParallelHashMap<long, int3>(1024, Allocator.Persistent);
            spawned = new NativeParallelHashSet<long>(1024, Allocator.Persistent);
            tileEntities = new NativeParallelMultiHashMap<long, Entity>(4096, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (GameStatus.Live.Remove("~objects view")) GameStatus.Lines["~objects view"] = Status();
            if (chunkParts != null) foreach (var a in chunkParts) if (a.IsCreated) a.Dispose();
            if (chunkKeys != null) foreach (var a in chunkKeys) if (a.IsCreated) a.Dispose();
            Owned.Release(chunkTask, r => r.Dispose());
            if (ranges.IsCreated) ranges.Dispose();
            if (spawned.IsCreated) spawned.Dispose();
            if (tileEntities.IsCreated) tileEntities.Dispose();
            DisposeHidden();
            if (loaded.IsCreated) loaded.Dispose();
            if (meshes != null) foreach (var m in meshes) if (m != null) UnityEngine.Object.Destroy(m);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (foliageMaterial != null) UnityEngine.Object.Destroy(foliageMaterial);
            if (atlas != null) UnityEngine.Object.Destroy(atlas);
            if (impA != null) { impA.Release(); UnityEngine.Object.Destroy(impA); }
            if (impB != null) { impB.Release(); UnityEngine.Object.Destroy(impB); }
        }

        static float4 Linear(uint rgb, float tint, int shape)
        {
            if (ObjectPart.IsStone(shape)) return new float4(tint, tint, tint, 0);
            return new float4(Decode(rgb) * tint, shape);
        }

        static float3 Decode(uint rgb)
        {
            var c = new float3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255f;
            return math.select(math.pow((c + 0.055f) / 1.055f, 2.4f), c / 12.92f, c <= 0.04045f);
        }

        bool Prepare()
        {
            var cols = World.GetExistingSystemManaged<ObjectColumnsSystem>();
            var map = World.GetExistingSystemManaged<ClientMapSystem>()?.Map;
            if (cols?.Field == null || map == null) return false;
            field = cols.Field;
            lib = field.Library;
            cm = map.Config.CellMeters;
            worldM = map.Config.Size * cm;
            span = WorldChunks.Size * cm;
            across = field.ChunksAcross;
            loaded = new NativeArray<bool>(across * across, Allocator.Persistent);
            chunkParts = new NativeArray<ObjectSpawn>[across * across];
            chunkKeys = new NativeArray<long>[across * across];
            collectNext = () => Collect(collecting);
            float minTile = cm * 0.5f;
            int defs = lib.Objects.Count, count = 0;
            meshBase = new int[defs][];
            reach = new float[defs];
            tile = new float[defs];
            liveByDef = new int[defs];
            kinds = new ObjectKind[defs];
            var shapes = new List<int>();
            var keys = new List<ulong>();
            var parts = new List<ObjectPart>();
            var impostors = new List<bool>();
            for (int d = 0; d < defs; d++)
            {
                var def = lib.Objects[d];
                var kind = lib.KindOf(def);
                meshBase[d] = new int[def.Parts.Count];
                for (int p = 0; p < def.Parts.Count; p++)
                {
                    meshBase[d][p] = count;
                    for (int v = 0; v < def.Variants; v++)
                    {
                        shapes.Add(def.Parts[p].Shape); parts.Add(def.Parts[p]); keys.Add(ObjectMeshes.Key(def.Id, p, v));
                        impostors.Add(kind.ImpostorPixels > 0 && ObjectPart.IsFoliage(def.Parts[p].Shape));
                        count++;
                    }
                }
                kinds[d] = kind;
                reach[d] = Detail.SimulationReach(2f * field.Footprint(d) * def.Placement.SizeMaxM, kind.FadePixels);
                float t = span;
                while (t > reach[d] / 3f && t * 0.5f >= minTile) t *= 0.5f;
                tile[d] = t;
            }
            foliageMesh = new bool[shapes.Count];
            bool anyFoliage = false;
            for (int k = 0; k < shapes.Count; k++) anyFoliage |= foliageMesh[k] = ObjectPart.IsFoliage(shapes[k]);
            meshTask = Task.Run(() => BuildMeshes(shapes, parts, keys, impostors));
            if (anyFoliage) atlasTask = Task.Run(() => LeafAtlas.Build());
            return true;
        }

        static MeshSet BuildMeshes(List<int> shapes, List<ObjectPart> parts, List<ulong> keys, List<bool> impostors)
        {
            int n = shapes.Count * Stride;
            var set = new MeshSet
            {
                Positions = new Vector3[n][], Normals = new Vector3[n][], Uv0 = new Vector4[n][], Uv1 = new Vector4[n][], Indices = new int[n][], Bounds = new AABB[shapes.Count],
                Centers = new Vector4[shapes.Count], Slots = new int[shapes.Count],
            };
            for (int k = 0; k < shapes.Count; k++)
            {
                for (int l = 0; l < Lods; l++)
                {
                    var m = ObjectMeshes.Build(parts[k], keys[k], l);
                    int vc = m.VertexCount, at = k * Stride + l;
                    var pos = new Vector3[vc];
                    var nor = new Vector3[vc];
                    Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity, mid = Vector3.zero;
                    for (int v = 0; v < vc; v++)
                    {
                        pos[v] = new Vector3(m.Positions[3 * v], m.Positions[3 * v + 1], m.Positions[3 * v + 2]);
                        nor[v] = new Vector3(m.Normals[3 * v], m.Normals[3 * v + 1], m.Normals[3 * v + 2]);
                        lo = Vector3.Min(lo, pos[v]); hi = Vector3.Max(hi, pos[v]); mid += pos[v];
                    }
                    mid /= Mathf.Max(1, vc);
                    float outward = 0;
                    for (int v = 0; v < vc; v++) outward += Vector3.Dot(nor[v], pos[v] - mid);
                    var idx = m.Indices;
                    if (m.Uvs != null)
                    {
                        var u0 = new Vector4[vc];
                        var u1 = new Vector4[vc];
                        for (int v = 0; v < vc; v++)
                        {
                            u0[v] = new Vector4(m.Uvs[4 * v], m.Uvs[4 * v + 1], m.Uvs[4 * v + 2], m.Uvs[4 * v + 3]);
                            u1[v] = new Vector4(m.Winds[4 * v], m.Winds[4 * v + 1], m.Winds[4 * v + 2], m.Winds[4 * v + 3]);
                        }
                        set.Uv0[at] = u0;
                        set.Uv1[at] = u1;
                    }
                    else
                    {
                        if (m.Tones != null)
                        {
                            var t0 = new Vector4[vc];
                            for (int v = 0; v < vc; v++) t0[v] = new Vector4(m.Tones[4 * v], m.Tones[4 * v + 1], m.Tones[4 * v + 2], m.Tones[4 * v + 3]);
                            set.Uv0[at] = t0;
                        }
                        if (outward < 0)
                        {
                            for (int t = 0; t < idx.Length; t += 3) (idx[t + 1], idx[t + 2]) = (idx[t + 2], idx[t + 1]);
                            for (int v = 0; v < vc; v++) nor[v] = -nor[v];
                        }
                    }
                    set.Positions[at] = pos; set.Normals[at] = nor; set.Indices[at] = idx;
                    if (l == 0) set.Bounds[k] = new AABB { Center = (lo + hi) * 0.5f, Extents = (hi - lo) * (m.Uvs != null ? 0.56f : 0.5f) };
                }
                set.Slots[k] = -1;
                if (impostors[k] && set.Uv0[k * Stride] != null) Impostor(set, k);
            }
            return set;
        }

        static void Impostor(MeshSet set, int k)
        {
            var pos = set.Positions[k * Stride];
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (var p in pos) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            var c = (lo + hi) * 0.5f;
            float r = 0;
            foreach (var p in pos) r = Mathf.Max(r, new Vector2(p.x - c.x, p.z - c.z).magnitude);
            float s = Mathf.Max(2f * r, hi.y - lo.y) * 1.02f;
            int slot = set.SlotCount++;
            set.Slots[k] = slot;
            set.Centers[k] = new Vector4(c.x, c.y, c.z, s);
            var b = set.Bounds[k];
            b.Extents = math.max(b.Extents, new float3(0.5f * s, 0.5f * s, 0.5f * s));
            b.Center = c;
            set.Bounds[k] = b;
            var qp = new Vector3[4];
            var qn = new Vector3[4];
            var q0 = new Vector4[4];
            var q1 = new Vector4[4];
            for (int v = 0; v < 4; v++)
            {
                float x = v == 0 || v == 3 ? -1 : 1, y = v < 2 ? -1 : 1;
                qp[v] = c + new Vector3(x * 0.5f * s, y * 0.5f * s, 0);
                qn[v] = Vector3.forward;
                q0[v] = new Vector4(x, y, slot, 2);
                q1[v] = set.Centers[k];
            }
            int at = k * Stride + Lods;
            set.Positions[at] = qp; set.Normals[at] = qn; set.Uv0[at] = q0; set.Uv1[at] = q1; set.Indices[at] = new[] { 0, 1, 2, 0, 2, 3 };
            int l2 = k * Stride + Lods - 1, vc = set.Positions[l2].Length;
            set.Positions[at + 1] = Concat(set.Positions[l2], qp);
            set.Normals[at + 1] = Concat(set.Normals[l2], qn);
            set.Uv0[at + 1] = Concat(set.Uv0[l2], q0);
            set.Uv1[at + 1] = Concat(set.Uv1[l2], q1);
            var idx = new int[set.Indices[l2].Length + 6];
            set.Indices[l2].CopyTo(idx, 0);
            for (int i = 0; i < 6; i++) idx[set.Indices[l2].Length + i] = vc + set.Indices[at][i];
            set.Indices[at + 1] = idx;
        }

        static T[] Concat<T>(T[] a, T[] b)
        {
            var r = new T[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        void Bake(CommandBuffer cmd)
        {
            int size = Cell;
            while ((size / Cell) * (size / Cell) < meshSet.SlotCount * Frames) size *= 2;
            int cols = size / Cell;
            float fill = (Cell - 4f) / Cell;
            impA = Target(size, "Stabby impostor albedo");
            impB = Target(size, "Stabby impostor normal");
            var depth = new RenderTexture(size, size, 24, RenderTextureFormat.Depth) { name = "Stabby impostor depth" };
            depth.Create();
            cmd.SetRenderTarget(new RenderTargetIdentifier[] { impA, impB }, depth.depthBuffer);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.SetGlobalVector("_StabbyBakeGrid", new Vector4(cols, fill, 0, 0));
            int pass = foliageMaterial.FindPass("Bake");
            for (int k = 0; k < meshSet.Slots.Length; k++)
            {
                if (meshSet.Slots[k] < 0) continue;
                cmd.SetGlobalVector("_StabbyBakeCenter", meshSet.Centers[k]);
                for (int f = 0; f < Frames; f++)
                {
                    float a = f * 2f * Mathf.PI / Frames;
                    int cell = meshSet.Slots[k] * Frames + f;
                    cmd.SetGlobalVector("_StabbyBakeFrame", new Vector4(Mathf.Sin(a), Mathf.Cos(a), cell % cols, cell / cols));
                    cmd.DrawMesh(meshes[k * Stride], Matrix4x4.identity, foliageMaterial, 0, pass);
                }
            }
            cmd.GenerateMips(impA);
            cmd.GenerateMips(impB);
            Graphics.ExecuteCommandBuffer(cmd);
            depth.Release();
            UnityEngine.Object.Destroy(depth);
            foliageMaterial.SetTexture("_ImpA", impA);
            foliageMaterial.SetTexture("_ImpB", impB);
            foliageMaterial.SetVector("_ImpGrid", new Vector4(cols, fill, Frames, 0));
        }

        static RenderTexture Target(int size, string name)
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(size, size, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0) { useMipMap = true, autoGenerateMips = false })
            {
                name = name, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        bool Register()
        {
            if (meshSet == null)
            {
                if (meshTask == null || !meshTask.IsCompleted) return false;
                if (meshTask.IsFaulted) { GameStatus.Failure = meshTask.Exception.ToString(); meshTask = null; return false; }
                meshSet = meshTask.Result;
                meshTask = null;
                meshes = new Mesh[meshSet.Positions.Length];
                meshIds = new BatchMeshID[meshes.Length];
            }
            if (atlasTask != null)
            {
                if (!atlasTask.IsCompleted) return false;
                if (atlasTask.IsFaulted) { GameStatus.Failure = atlasTask.Exception.ToString(); atlasTask = null; return false; }
                atlas = LeafAtlas.Create(atlasTask.Result);
                atlasTask = null;
            }
            if (registered >= meshes.Length) return true;
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            for (int end = Math.Min(meshes.Length, registered + RegisterPerFrame); registered < end; registered++)
            {
                if (meshSet.Positions[registered] == null) continue;
                var m = new Mesh { name = "Object " + registered, vertices = meshSet.Positions[registered], normals = meshSet.Normals[registered], triangles = meshSet.Indices[registered] };
                if (meshSet.Uv0[registered] != null)
                {
                    m.SetUVs(0, meshSet.Uv0[registered]);
                    if (meshSet.Uv1[registered] != null) m.SetUVs(1, meshSet.Uv1[registered]);
                }
                m.RecalculateBounds();
                meshes[registered] = m;
                meshIds[registered] = graphics.RegisterMesh(m);
            }
            if (registered < meshes.Length) return false;
            castPrefab = Prefab(ShadowCastingMode.On, materialId, false);
            flatPrefab = Prefab(ShadowCastingMode.Off, materialId, false);
            if (atlas != null)
            {
                foliageMaterial = new Material(Shader.Find("Stabby/Foliage")) { name = "Stabby foliage", enableInstancing = true };
                foliageMaterial.SetTexture("_LeafAtlas", atlas);
                if (meshSet.SlotCount > 0)
                {
                    using var cmd = new CommandBuffer { name = "Stabby impostor bake" };
                    Bake(cmd);
                }
                foliageMaterialId = graphics.RegisterMaterial(foliageMaterial);
                castFoliagePrefab = Prefab(ShadowCastingMode.On, foliageMaterialId, true);
                flatFoliagePrefab = Prefab(ShadowCastingMode.Off, foliageMaterialId, true);
            }
            return true;
        }

        Entity Prefab(ShadowCastingMode shadows, BatchMaterialID mat, bool foliage)
        {
            var e = EntityManager.CreateEntity();
            RenderMeshUtility.AddComponents(e, EntityManager, new RenderMeshDescription(shadows, true, MotionVectorGenerationMode.ForceNoMotion, foliage ? FoliageLayer : 0), new MaterialMeshInfo(mat, meshIds[0]));
            if (foliage) EntityManager.AddComponentData(e, new ObjectLeaf { Value = new float4(0.2f, 0.3f, 0.1f, 2) });
            if (foliage) EntityManager.AddComponentData(e, new ObjectImp());
            EntityManager.AddComponentData(e, new ObjectColor { Value = new float4(1, 1, 1, 0) });
            EntityManager.AddComponentData(e, new ObjectFade { Value = new float4(1, 1, 0, 0) });
            EntityManager.AddComponentData(e, new ObjectLod());
            EntityManager.AddComponent<Prefab>(e);
            return e;
        }

        long Key(int def, int tx, int tz) => ((long)def << 40) | ((long)tx << 20) | (long)tz;

        ChunkParts Collect(int c)
        {
            int cx = c % across, cz = c / across;
            field.PlaceInto(cx, cz, placed);
            int n = 0;
            for (int i = 0; i < placed.Count; i++) n += lib.Objects[placed[i].Def].Parts.Count;
            var order = new NativeArray<SortKey>(n, Allocator.Persistent);
            var parts = new NativeArray<ObjectSpawn>(n, Allocator.Persistent);
            for (int i = 0, at = 0; i < placed.Count; i++)
            {
                var o = placed[i];
                long key = Key(o.Def, (int)(o.X / tile[o.Def]), (int)(o.Z / tile[o.Def]));
                for (int p = 0; p < lib.Objects[o.Def].Parts.Count; p++) order[at++] = new SortKey { Key = key, Item = (i << 8) | p };
            }
            order.Sort();
            int distinct = 0;
            for (int i = 0; i < n; i++) if (i == 0 || order[i].Key != order[i - 1].Key) distinct++;
            var keys = new NativeArray<long>(distinct, Allocator.Persistent);
            var spans = new NativeArray<int3>(distinct, Allocator.Persistent);
            for (int i = 0, d = -1; i < n; i++)
            {
                if (i == 0 || order[i].Key != order[i - 1].Key) { d++; keys[d] = order[i].Key; spans[d] = new int3(i, 0, c); }
                spans[d] += new int3(0, 1, 0);
                var o = placed[order[i].Item >> 8];
                int p = order[i].Item & 255;
                var def = lib.Objects[o.Def];
                var part = def.Parts[p];
                var root = float4x4.TRS(new float3(o.X, o.Y, o.Z), new quaternion(o.Qx, o.Qy, o.Qz, o.Qw), new float3(1));
                float tint = 0.86f + 0.28f * math.frac(o.Variant * 0.618034f + o.Size * 3.7f + o.X * 0.013f);
                var scale = new float3(part.ScaleX, part.ScaleY, part.ScaleZ) * o.Size;
                var local = float4x4.TRS(new float3(part.OffsetX, part.OffsetY, part.OffsetZ) * o.Size, quaternion.identity, scale);
                float leafTint = 0.88f + 0.24f * math.frac(o.Variant * 0.381966f + o.Z * 0.0171f + o.Size * 1.3f);
                float hue = 2f * math.frac(o.Variant * 0.7548777f + o.X * 0.0093f + o.Z * 0.0061f) - 1f;
                var leaf = ObjectPart.IsFoliage(part.Shape) ? new float4(Decode(part.LeafColor) * leafTint * (1f + hue * new float3(0.16f, 0.06f, -0.13f)), 2) : float4.zero;
                parts[i] = new ObjectSpawn
                {
                    M = math.mul(root, local), Color = Linear(part.Color, tint, part.Shape), Leaf = leaf, Mesh = meshBase[o.Def][p] + o.Variant, Def = o.Def,
                    Radius = 0.5f * math.cmax(scale), Shadow = lib.PartShadows(def, part), Ground = o.Y + def.Placement.Sink * o.Size, Size = o.Size,
                    Id = o.Id,
                };
            }
            order.Dispose();
            return new ChunkParts { Chunk = c, Keys = keys, Ranges = spans, Parts = parts };
        }

        struct SortKey : IComparable<SortKey>
        {
            public long Key;
            public int Item;
            public int CompareTo(SortKey o) => Key != o.Key ? Key.CompareTo(o.Key) : Item.CompareTo(o.Item);
        }

        float RectDistance(float3 cam, float x0, float z0, float size)
        {
            float dx = math.max(0, math.max(x0 - cam.x, cam.x - (x0 + size)));
            float dz = math.max(0, math.max(z0 - cam.z, cam.z - (z0 + size)));
            return math.sqrt(dx * dx + dz * dz);
        }

        void LoadChunks(float3 cam, float maxReach)
        {
            if (chunkTask != null)
            {
                if (!chunkTask.IsCompleted) return;
                if (chunkTask.IsFaulted) { GameStatus.Failure = chunkTask.Exception.ToString(); chunkTask = null; return; }
                var r = chunkTask.Result;
                chunkTask = null;
                for (int i = 0; i < r.Keys.Length; i++)
                    if (!ranges.TryAdd(r.Keys[i], r.Ranges[i])) r.Keys[i] = -1;
                r.Ranges.Dispose();
                chunkParts[r.Chunk] = r.Parts;
                chunkKeys[r.Chunk] = r.Keys;
                loaded[r.Chunk] = true;
                chunksLoaded++;
                chunksHeld++;
                partsHeld += r.Parts.Length;
                peakParts = math.max(peakParts, partsHeld);
                peakChunks = math.max(peakChunks, chunksHeld);
            }
            for (int c = 0; c < loaded.Length; c++)
            {
                if (!loaded[c] || RectDistance(cam, (c % across) * span, (c / across) * span, span) <= maxReach * UnloadReach) continue;
                foreach (long key in chunkKeys[c]) if (key >= 0) ranges.Remove(key);
                partsHeld -= chunkParts[c].Length;
                chunkParts[c].Dispose();
                chunkKeys[c].Dispose();
                loaded[c] = false;
                chunksHeld--;
                chunksUnloaded++;
            }
            int best = -1;
            float bestD = maxReach;
            for (int c = 0; c < loaded.Length; c++)
            {
                if (loaded[c]) continue;
                float d = RectDistance(cam, (c % across) * span, (c / across) * span, span);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best >= 0) { collecting = best; chunkTask = Task.Run(collectNext); }
            Pending = best >= 0 || chunkTask != null ? 1 : 0;
        }

        void Despawn(float3 cam)
        {
            var keys = spawned.ToNativeArray(Allocator.Temp);
            var doomed = new NativeList<Entity>(Allocator.Temp);
            foreach (long key in keys)
            {
                int d = (int)(key >> 40), tx = (int)((key >> 20) & 0xFFFFF), tz = (int)(key & 0xFFFFF);
                if (RectDistance(cam, tx * tile[d], tz * tile[d], tile[d]) <= reach[d] * DespawnReach) continue;
                int count = doomed.Length;
                if (tileEntities.TryGetFirstValue(key, out var e, out var it))
                    do doomed.Add(e); while (tileEntities.TryGetNextValue(out e, ref it));
                count = doomed.Length - count;
                tileEntities.Remove(key);
                spawned.Remove(key);
                liveByDef[d] -= count;
            }
            if (doomed.Length > 0) EntityManager.DestroyEntity(doomed.AsArray());
            liveEntities -= doomed.Length;
            keys.Dispose();
            doomed.Dispose();
        }

        int Spawn(float3 cam)
        {
            int budget = SpawnBudget, waiting = 0;
            for (int d = 0; d < tile.Length; d++)
            {
                float t = tile[d], r = reach[d];
                int tiles = (int)math.ceil(worldM / t);
                int x0 = math.max(0, (int)((cam.x - r) / t)), x1 = math.min(tiles - 1, (int)((cam.x + r) / t));
                int z0 = math.max(0, (int)((cam.z - r) / t)), z1 = math.min(tiles - 1, (int)((cam.z + r) / t));
                for (int tz = z0; tz <= z1; tz++)
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        if (RectDistance(cam, tx * t, tz * t, t) > r) continue;
                        long key = Key(d, tx, tz);
                        if (spawned.Contains(key)) continue;
                        int c = math.min(across - 1, (int)(tz * t / span)) * across + math.min(across - 1, (int)(tx * t / span));
                        if (!loaded[c]) { waiting++; continue; }
                        if (!ranges.TryGetValue(key, out var range)) { spawned.Add(key); continue; }
                        if (budget <= 0) { waiting++; continue; }
                        SpawnTile(key, chunkParts[range.z], range);
                        budget -= range.y;
                    }
            }
            return waiting;
        }

        void SpawnTile(long key, NativeArray<ObjectSpawn> parts, int3 range)
        {
            var counts = groupCounts;
            System.Array.Clear(counts, 0, 4);
            System.Array.Clear(groupAt, 0, 4);
            int shown = 0;
            for (int k = 0; k < range.y; k++) if (!Hidden(parts[range.x + k].Id)) { shown++; counts[Group(parts[range.x + k])]++; }
            var prefabs = groupPrefabs;
            prefabs[0] = flatPrefab; prefabs[1] = castPrefab; prefabs[2] = flatFoliagePrefab; prefabs[3] = castFoliagePrefab;
            var made = groupMade;
            for (int g = 0; g < 4; g++) made[g] = EntityManager.Instantiate(prefabs[g], counts[g], Allocator.Temp);
            var at = groupAt;
            for (int k = 0; k < range.y; k++)
            {
                var s = parts[range.x + k];
                if (Hidden(s.Id)) continue;
                int group = Group(s);
                var e = made[group][at[group]++];
                var kind = kinds[s.Def];
                int m = s.Mesh * Stride;
                bool imp = group >= 2 && kind.ImpostorPixels > 0 && meshSet.Slots[s.Mesh] >= 0;
                int lods = math.clamp(kind.Lods, 1, Lods);
                bool foliage = group >= 2;
                EntityManager.SetComponentData(e, new LocalToWorld { Value = s.M });
                EntityManager.SetComponentData(e, new RenderBounds { Value = meshSet.Bounds[s.Mesh] });
                EntityManager.SetComponentData(e, new MaterialMeshInfo(foliage ? foliageMaterialId : materialId, meshIds[m]));
                EntityManager.SetComponentData(e, new ObjectColor { Value = s.Color });
                EntityManager.SetComponentData(e, new ObjectFade { Value = new float4(1, s.Shadow ? 1 : 0, s.Ground, s.Size) });
                if (foliage) EntityManager.SetComponentData(e, new ObjectLeaf { Value = s.Leaf });
                EntityManager.SetComponentData(e, new ObjectLod
                {
                    L0 = meshIds[m], L1 = meshIds[m + math.min(1, lods - 1)], L2 = meshIds[m + math.min(2, lods - 1)],
                    Lod1 = kind.LodPixels.Length > 0 ? kind.LodPixels[0] : 0, Lod2 = kind.LodPixels.Length > 1 ? kind.LodPixels[1] : 0,
                    Fade = kind.FadePixels, Shadow = s.Shadow ? kind.ShadowPixels : float.MaxValue, Radius = s.Radius,
                    Sway = kind.Wind ? kind.SwayPixels : float.MaxValue, Flutter = kind.Wind ? kind.FlutterPixels : float.MaxValue,
                    Imp = imp ? meshIds[m + Lods] : default, Blend = imp ? meshIds[m + Lods + 1] : default, Impostor = imp ? kind.ImpostorPixels : 0,
                });
                tileEntities.Add(key, e);
                liveByDef[s.Def]++;
            }
            spawned.Add(key);
            liveEntities += shown;
            foreach (var arr in made) arr.Dispose();
        }

        int Group(in ObjectSpawn s) => (s.Shadow ? 1 : 0) + (foliageMaterial != null && foliageMesh[s.Mesh] ? 2 : 0);

        bool Warm()
        {
            if (material == null)
            {
                material = new Material(Shader.Find("Stabby/Object")) { name = "Stabby objects", enableInstancing = true };
                return false;
            }
            if (materialId == BatchMaterialID.Null)
            {
                materialId = World.GetExistingSystemManaged<EntitiesGraphicsSystem>().RegisterMaterial(material);
                return false;
            }
            return true;
        }

        protected override void OnUpdate()
        {
            if (!Warm()) return;
            if (field == null && !Prepare()) return;
            if (!Register()) return;
            var cam = Camera.main;
            if (cam == null) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            float3 pos = DetailView.Refresh(cam).Camera;
            float maxReach = 0;
            foreach (float r in reach) maxReach = math.max(maxReach, r);
            LoadChunks(pos, maxReach);
            Despawn(pos);
            int waiting = Spawn(pos);
            if (waiting > 0) Pending = 1;
            LiveEntities = liveEntities;
            PartsHeld = partsHeld;
            ChunksHeld = chunksHeld;
            ChunksUnloaded = chunksUnloaded;
            liveByDefShared = liveByDef;
            libShared = lib;
            if (GameStatus.TerrainBuilt && !GameStatus.AfterTool)
            {
                worstUpdate = math.max(worstUpdate, (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency);
            }
            if (!GameStatus.Live.ContainsKey("~objects view")) GameStatus.Live["~objects view"] = Status;
        }

        string Status() => $"objects: {liveEntities} entities in {spawned.Count()} tiles, {meshes.Length} meshes; chunks {chunksHeld} held (peak {peakChunks}), {chunksLoaded} loaded, {chunksUnloaded} unloaded; parts {partsHeld} held (peak {peakParts}); worst view update after terrain built {worstUpdate:F1} ms";
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(ObjectViewSystem))]
    public partial struct ObjectLodSystem : ISystem
    {
        ComponentLookup<MaterialMeshInfo> mmi;
        ComponentLookup<ObjectFade> fade;
        ComponentLookup<ObjectLeaf> leaf;
        ComponentLookup<ObjectImp> imp;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ObjectLod>();
            mmi = state.GetComponentLookup<MaterialMeshInfo>();
            fade = state.GetComponentLookup<ObjectFade>();
            leaf = state.GetComponentLookup<ObjectLeaf>();
            imp = state.GetComponentLookup<ObjectImp>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            mmi.Update(ref state);
            var view = DetailView.Current;
            if (view.PixelsPerMetre <= 0) return;
            fade.Update(ref state);
            leaf.Update(ref state);
            imp.Update(ref state);
            state.Dependency = new LodJob { View = view, Mmi = mmi, Fade = fade, Leaf = leaf, Imp = imp }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IncludePrefab)]
        [WithNone(typeof(Prefab))]
        partial struct LodJob : IJobEntity
        {
            public DetailView View;
            [NativeDisableParallelForRestriction] public ComponentLookup<MaterialMeshInfo> Mmi;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectFade> Fade;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectLeaf> Leaf;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectImp> Imp;

            void Execute(Entity e, in LocalToWorld ltw, in ObjectLod lod)
            {
                float px = View.ScreenSize(2f * lod.Radius, ltw.Value.c3.xyz);
                var t = new Detail.Thresholds(lod.Lod1, lod.Lod2, lod.Fade).Scaled(View.Quality);
                bool on = px >= t.Far;
                if (Mmi.IsComponentEnabled(e) != on) Mmi.SetComponentEnabled(e, on);
                if (!on) return;
                var m = Mmi[e];
                int was = m.MeshID.value == lod.L0.value ? 0 : m.MeshID.value == lod.L1.value ? 1 : 2;
                int tier = math.min(Detail.Tier(px, t, was), 2);
                var id = tier == 0 ? lod.L0 : tier == 1 ? lod.L1 : lod.L2;
                if (lod.Impostor > 0)
                {
                    float ip = lod.Impostor * View.Impostor / math.max(View.Quality, 0.1f);
                    float b = tier == 2 ? math.round((1f - Detail.Crossfade(px, ip, 1.5f * ip)) * 16f) / 16f : 0;
                    if (b >= 1) id = lod.Imp;
                    else if (b > 0) id = lod.Blend;
                    if (Imp[e].Value != b) Imp[e] = new ObjectImp { Value = b };
                }
                if (m.MeshID.value != id.value) { m.MeshID = id; Mmi[e] = m; }
                var old = Fade[e].Value;
                bool shadow = Detail.Holds(px, lod.Shadow / View.Quality, old.y > 0);
                var f = new float4(math.round(Detail.Crossfade(px, t.Far, 1.5f * t.Far) * 16f) / 16f, shadow ? 1 : 0, old.z, old.w);
                if (math.any(old != f)) Fade[e] = new ObjectFade { Value = f };
                if (!Leaf.HasComponent(e)) return;
                float sway = lod.Sway / View.Quality, flutter = lod.Flutter / View.Quality;
                float w = math.round((Detail.Crossfade(px, sway, 1.5f * sway) + Detail.Crossfade(px, flutter, 1.5f * flutter)) * 16f) / 16f;
                var lv = Leaf[e].Value;
                if (lv.w != w) { lv.w = w; Leaf[e] = new ObjectLeaf { Value = lv }; }
            }
        }
    }
}
