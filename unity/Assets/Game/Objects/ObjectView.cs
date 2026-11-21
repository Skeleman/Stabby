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

    public struct ObjectLod : IComponentData
    {
        public BatchMeshID L0, L1, L2;
        public float Lod1, Lod2, Fade, Shadow, Radius;
    }

    public struct ObjectSpawn
    {
        public float4x4 M;
        public float4 Color;
        public int Mesh, Def;
        public float Radius;
        public bool Shadow;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ObjectViewSystem : SystemBase
    {
        public const int Lods = 3, RegisterPerFrame = 24, SpawnBudget = 4000;
        public const float DespawnReach = 1f / (1f - Detail.Hysteresis), UnloadReach = 1.5f;

        sealed class MeshSet
        {
            public Vector3[][] Positions, Normals;
            public int[][] Indices;
            public AABB[] Bounds;
        }

        sealed class ChunkParts
        {
            public int Chunk;
            public long[] Keys;
            public int3[] Ranges;
            public NativeArray<ObjectSpawn> Parts;
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
        Material material;
        BatchMaterialID materialId;
        Entity castPrefab, flatPrefab;
        Task<ChunkParts> chunkTask;
        NativeArray<bool> loaded;
        NativeArray<ObjectSpawn>[] chunkParts;
        long[][] chunkKeys;
        NativeParallelHashMap<long, int3> ranges;
        NativeParallelHashSet<long> spawned;
        NativeParallelMultiHashMap<long, Entity> tileEntities;
        int liveEntities, chunksLoaded, chunksHeld, chunksUnloaded, partsHeld, peakParts, peakChunks;
        int[] liveByDef;
        ObjectKind[] kinds;
        int statusHash;
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
            if (chunkParts != null) foreach (var a in chunkParts) if (a.IsCreated) a.Dispose();
            Owned.Release(chunkTask, r => r.Parts.Dispose());
            if (ranges.IsCreated) ranges.Dispose();
            if (spawned.IsCreated) spawned.Dispose();
            if (tileEntities.IsCreated) tileEntities.Dispose();
            if (loaded.IsCreated) loaded.Dispose();
            if (meshes != null) foreach (var m in meshes) if (m != null) UnityEngine.Object.Destroy(m);
            if (material != null) UnityEngine.Object.Destroy(material);
        }

        static float4 Linear(uint rgb, float tint, int shape)
        {
            if (shape == ObjectPart.Rock) return new float4(tint, tint, tint, 0);
            var c = new float3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255f;
            c = math.select(math.pow((c + 0.055f) / 1.055f, 2.4f), c / 12.92f, c <= 0.04045f);
            return new float4(c * tint, shape);
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
            chunkKeys = new long[across * across][];
            float minTile = cm * 0.5f;
            int defs = lib.Objects.Count, count = 0;
            meshBase = new int[defs][];
            reach = new float[defs];
            tile = new float[defs];
            liveByDef = new int[defs];
            kinds = new ObjectKind[defs];
            var shapes = new List<int>();
            var keys = new List<ulong>();
            for (int d = 0; d < defs; d++)
            {
                var def = lib.Objects[d];
                meshBase[d] = new int[def.Parts.Count];
                for (int p = 0; p < def.Parts.Count; p++)
                {
                    meshBase[d][p] = count;
                    for (int v = 0; v < def.Variants; v++) { shapes.Add(def.Parts[p].Shape); keys.Add(ObjectMeshes.Key(def.Id, p, v)); count++; }
                }
                var kind = lib.KindOf(def);
                kinds[d] = kind;
                reach[d] = Detail.SimulationReach(2f * field.Footprint(d) * def.Placement.SizeMaxM, kind.FadePixels);
                float t = span;
                while (t > reach[d] / 3f && t * 0.5f >= minTile) t *= 0.5f;
                tile[d] = t;
            }
            meshTask = Task.Run(() => BuildMeshes(shapes, keys));
            return true;
        }

        static MeshSet BuildMeshes(List<int> shapes, List<ulong> keys)
        {
            int n = shapes.Count * Lods;
            var set = new MeshSet { Positions = new Vector3[n][], Normals = new Vector3[n][], Indices = new int[n][], Bounds = new AABB[shapes.Count] };
            for (int k = 0; k < shapes.Count; k++)
                for (int l = 0; l < Lods; l++)
                {
                    var m = ObjectMeshes.Build(shapes[k], keys[k], l);
                    int vc = m.VertexCount, at = k * Lods + l;
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
                    var idx = (int[])m.Indices.Clone();
                    if (outward < 0)
                    {
                        for (int t = 0; t < idx.Length; t += 3) (idx[t + 1], idx[t + 2]) = (idx[t + 2], idx[t + 1]);
                        for (int v = 0; v < vc; v++) nor[v] = -nor[v];
                    }
                    set.Positions[at] = pos; set.Normals[at] = nor; set.Indices[at] = idx;
                    if (l == 0) set.Bounds[k] = new AABB { Center = (lo + hi) * 0.5f, Extents = (hi - lo) * 0.5f };
                }
            return set;
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
            if (registered >= meshes.Length) return true;
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            for (int end = Math.Min(meshes.Length, registered + RegisterPerFrame); registered < end; registered++)
            {
                var m = new Mesh { name = "Object " + registered, vertices = meshSet.Positions[registered], normals = meshSet.Normals[registered], triangles = meshSet.Indices[registered] };
                m.RecalculateBounds();
                meshes[registered] = m;
                meshIds[registered] = graphics.RegisterMesh(m);
            }
            if (registered < meshes.Length) return false;
            material = new Material(Shader.Find("Stabby/Object")) { name = "Stabby objects", enableInstancing = true };
            materialId = graphics.RegisterMaterial(material);
            castPrefab = Prefab(ShadowCastingMode.On);
            flatPrefab = Prefab(ShadowCastingMode.Off);
            return true;
        }

        Entity Prefab(ShadowCastingMode shadows)
        {
            var e = EntityManager.CreateEntity();
            RenderMeshUtility.AddComponents(e, EntityManager, new RenderMeshDescription(shadows, true, MotionVectorGenerationMode.ForceNoMotion), new MaterialMeshInfo(materialId, meshIds[0]));
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
            var placed = field.Chunk(cx, cz);
            int n = 0;
            foreach (var o in placed) n += lib.Objects[o.Def].Parts.Count;
            var order = new NativeArray<SortKey>(n, Allocator.Persistent);
            var parts = new NativeArray<ObjectSpawn>(n, Allocator.Persistent);
            for (int i = 0, at = 0; i < placed.Length; i++)
            {
                var o = placed[i];
                long key = Key(o.Def, (int)(o.X / tile[o.Def]), (int)(o.Z / tile[o.Def]));
                for (int p = 0; p < lib.Objects[o.Def].Parts.Count; p++) order[at++] = new SortKey { Key = key, Item = (i << 8) | p };
            }
            order.Sort();
            int distinct = 0;
            for (int i = 0; i < n; i++) if (i == 0 || order[i].Key != order[i - 1].Key) distinct++;
            var keys = new long[distinct];
            var spans = new int3[distinct];
            for (int i = 0, d = -1; i < n; i++)
            {
                if (i == 0 || order[i].Key != order[i - 1].Key) { d++; keys[d] = order[i].Key; spans[d] = new int3(i, 0, c); }
                spans[d].y++;
                var o = placed[order[i].Item >> 8];
                int p = order[i].Item & 255;
                var def = lib.Objects[o.Def];
                var part = def.Parts[p];
                var root = float4x4.TRS(new float3(o.X, o.Y, o.Z), new quaternion(o.Qx, o.Qy, o.Qz, o.Qw), new float3(1));
                float tint = 0.86f + 0.28f * math.frac(o.Variant * 0.618034f + o.Size * 3.7f + o.X * 0.013f);
                var scale = new float3(part.ScaleX, part.ScaleY, part.ScaleZ) * o.Size;
                var local = float4x4.TRS(new float3(part.OffsetX, part.OffsetY, part.OffsetZ) * o.Size, quaternion.identity, scale);
                parts[i] = new ObjectSpawn
                {
                    M = math.mul(root, local), Color = Linear(part.Color, tint, part.Shape), Mesh = meshBase[o.Def][p] + o.Variant, Def = o.Def,
                    Radius = 0.5f * math.cmax(scale), Shadow = lib.PartShadows(def, part),
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
                chunkKeys[c] = null;
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
            if (best >= 0) chunkTask = Task.Run(() => Collect(best));
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
            int cast = 0;
            for (int k = 0; k < range.y; k++) if (parts[range.x + k].Shadow) cast++;
            var a = EntityManager.Instantiate(castPrefab, cast, Allocator.Temp);
            var b = EntityManager.Instantiate(flatPrefab, range.y - cast, Allocator.Temp);
            int ia = 0, ib = 0;
            for (int k = 0; k < range.y; k++)
            {
                var s = parts[range.x + k];
                var e = s.Shadow ? a[ia++] : b[ib++];
                var kind = kinds[s.Def];
                int m = s.Mesh * Lods;
                int lods = math.clamp(kind.Lods, 1, Lods);
                EntityManager.SetComponentData(e, new LocalToWorld { Value = s.M });
                EntityManager.SetComponentData(e, new RenderBounds { Value = meshSet.Bounds[s.Mesh] });
                EntityManager.SetComponentData(e, new MaterialMeshInfo(materialId, meshIds[m]));
                EntityManager.SetComponentData(e, new ObjectColor { Value = s.Color });
                EntityManager.SetComponentData(e, new ObjectFade { Value = new float4(1, s.Shadow ? 1 : 0, 0, 0) });
                EntityManager.SetComponentData(e, new ObjectLod
                {
                    L0 = meshIds[m], L1 = meshIds[m + math.min(1, lods - 1)], L2 = meshIds[m + math.min(2, lods - 1)],
                    Lod1 = kind.LodPixels.Length > 0 ? kind.LodPixels[0] : 0, Lod2 = kind.LodPixels.Length > 1 ? kind.LodPixels[1] : 0,
                    Fade = kind.FadePixels, Shadow = s.Shadow ? kind.ShadowPixels : float.MaxValue, Radius = s.Radius,
                });
                tileEntities.Add(key, e);
                liveByDef[s.Def]++;
            }
            spawned.Add(key);
            liveEntities += range.y;
            a.Dispose();
            b.Dispose();
        }

        protected override void OnUpdate()
        {
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
            int hash = HashCode.Combine(liveEntities, chunksHeld, chunksLoaded, chunksUnloaded, partsHeld, (int)(worstUpdate * 10));
            if (hash == statusHash) return;
            statusHash = hash;
            GameStatus.Lines["~objects view"] = $"objects: {liveEntities} entities in {spawned.Count()} tiles, {meshes.Length} meshes; chunks {chunksHeld} held (peak {peakChunks}), {chunksLoaded} loaded, {chunksUnloaded} unloaded; parts {partsHeld} held (peak {peakParts}); worst view update after terrain built {worstUpdate:F1} ms";
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(ObjectViewSystem))]
    public partial struct ObjectLodSystem : ISystem
    {
        ComponentLookup<MaterialMeshInfo> mmi;
        ComponentLookup<ObjectFade> fade;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ObjectLod>();
            mmi = state.GetComponentLookup<MaterialMeshInfo>();
            fade = state.GetComponentLookup<ObjectFade>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            mmi.Update(ref state);
            var view = DetailView.Current;
            if (view.PixelsPerMetre <= 0) return;
            fade.Update(ref state);
            state.Dependency = new LodJob { View = view, Mmi = mmi, Fade = fade }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IncludePrefab)]
        [WithNone(typeof(Prefab))]
        partial struct LodJob : IJobEntity
        {
            public DetailView View;
            [NativeDisableParallelForRestriction] public ComponentLookup<MaterialMeshInfo> Mmi;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectFade> Fade;

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
                if (m.MeshID.value != id.value) { m.MeshID = id; Mmi[e] = m; }
                bool shadow = Detail.Holds(px, lod.Shadow / View.Quality, Fade[e].Value.y > 0);
                var f = new float4(math.round(Detail.Crossfade(px, t.Far, 1.5f * t.Far) * 16f) / 16f, shadow ? 1 : 0, 0, 0);
                if (math.any(Fade[e].Value != f)) Fade[e] = new ObjectFade { Value = f };
            }
        }
    }
}
