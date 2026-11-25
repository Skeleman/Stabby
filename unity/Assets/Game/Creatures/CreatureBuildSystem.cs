using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(CreatureAnimSystem))]
    public partial class CreatureBuildSystem : SystemBase
    {
        sealed class Kit
        {
            public CreatureLibrary Lib;
            public List<CreatureShape> Shapes = new List<CreatureShape>();
            public List<float[]> Verts = new List<float[]>();
            public List<ushort[]> Index = new List<ushort[]>();
            public List<float[]> LowVerts = new List<float[]>();
            public List<ushort[]> LowIndex = new List<ushort[]>();
            public int[] First;
            public MeshPack Pack;
            public BlobAssetReference<CreatureRig>[] Rigs;

            public void Dispose()
            {
                Pack?.Dispose();
                Pack = null;
                if (Rigs == null) return;
                foreach (var r in Rigs) if (r.IsCreated) r.Dispose();
                Rigs = null;
            }
        }

        struct Arrival
        {
            public Entity Ghost;
            public CreatureState State;
            public BlobAssetReference<CreatureFit> Fit;
            public CreatureScale Fitted;
            public int[] Prefab;
            public float3[] Scale;
            public bool High, Low;
            public float4 Base, Accent, Dark;
            public float Length, Distance;
            public CreatureDef Def;
            public CreatureBody Body;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        };

        struct Plan
        {
            public Entity Root;
            public int Tier;
            public float4 Base, Accent, Dark;
            public float Size;
        }

        public static int Count { get; private set; }
        public static int PeakSpend, TierBuilds, TierDrops;
        public IReadOnlyList<Mesh> Meshes => meshes;

        Task<Kit> kitTask;
        Kit kit;
        MeshBatch batch;
        BatchMeshID[] meshIds;
        readonly List<Mesh> pumped = new List<Mesh>();
        readonly List<int> pumpedIds = new List<int>();
        Entity[] prefabs;
        Task<List<Arrival>> arriving;
        Material material;
        BatchMaterialID materialId;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Arrival> queue = new List<Arrival>();
        EntityQuery pending, vanished, presented, asking;
        EntityArchetype rootType;
        bool counts, instanced;
        int live;
        Entity tally;

        protected override void OnCreate()
        {
            counts = World.Name == GameSettings.ViewClient;
            instanced = CreaturePath.Instanced;
            if (counts) Count = 0;
            EntityManager.CreateSingleton(new CreaturePresenter { Index = Math.Max(0, Array.IndexOf(GameSettings.Clients, World.Name)) });
            tally = EntityManager.CreateSingleton(new CreatureTally { Instanced = instanced });
            pending = SystemAPI.QueryBuilder().WithAll<CreatureState>().WithNone<CreatureClaimed>().Build();
            vanished = SystemAPI.QueryBuilder().WithAll<CreaturePresented>().WithNone<CreatureState>().Build();
            presented = SystemAPI.QueryBuilder().WithAll<CreaturePresented>().Build();
            asking = SystemAPI.QueryBuilder().WithAll<CreatureRoot, CreatureLink, CreatureRetier>().Build();
            rootType = instanced
                ? EntityManager.CreateArchetype(typeof(CreatureRoot), typeof(CreatureLink), typeof(CreatureRetier), typeof(CreatureFur))
                : EntityManager.CreateArchetype(typeof(CreatureRoot), typeof(CreatureLink), typeof(CreatureRetier));
        }

        protected override void OnDestroy()
        {
            Owned.Release(kitTask, k => k.Dispose());
            batch?.Dispose();
            Owned.Release(arriving, Drop);
            Drop(queue);
            queue.Clear();
            using (var ps = presented.ToComponentDataArray<CreaturePresented>(Allocator.Temp))
                foreach (var s in ps) if (s.Fit.IsCreated) s.Fit.Dispose();
            EntityManager.RemoveComponent<CreaturePresented>(presented);
            kit?.Dispose();
            live = 0;
            foreach (var m in meshes) if (m != null) Object.Destroy(m);
            meshes.Clear();
            if (material != null) Object.Destroy(material);
            if (counts) Count = 0;
        }

        bool Warm()
        {
            if (material == null)
            {
                material = new Material(Shader.Find("Stabby/Creature")) { name = "Stabby creatures", enableInstancing = true };
                return false;
            }
            if (materialId == BatchMaterialID.Null && !instanced)
            {
                materialId = World.GetExistingSystemManaged<EntitiesGraphicsSystem>().RegisterMaterial(material);
                return false;
            }
            return true;
        }

        static void Drop(List<Arrival> list)
        {
            foreach (var a in list) if (a.Fit.IsCreated) a.Fit.Dispose();
        }

        bool Alive(Entity ghost) => EntityManager.Exists(ghost) && EntityManager.HasComponent<CreatureState>(ghost);

        protected override void OnUpdate()
        {
            if (prefabs == null)
            {
                if (kit != null)
                {
                    MakeMeshes();
                    return;
                }
                if (kitTask == null)
                {
                    var defs = World.GetExistingSystemManaged<ClientMapSystem>()?.Defs;
                    if (defs == null) return;
                    kitTask = Task.Run(() => MakeKit(defs));
                    return;
                }
                if (!kitTask.IsCompleted || !Warm()) return;
                if (kitTask.IsFaulted) { Debug.LogException(kitTask.Exception); Enabled = false; return; }
                kit = kitTask.Result;
                kitTask = null;
                meshIds = new BatchMeshID[kit.Pack.Count];
                batch = new MeshBatch(kit.Pack);
                MakeMeshes();
                return;
            }

            Vanish();

            if (arriving != null && arriving.IsCompleted)
            {
                if (arriving.IsFaulted) { Debug.LogException(arriving.Exception); Enabled = false; return; }
                queue.AddRange(arriving.Result);
                arriving = null;
            }

            var budget = new FrameBudget(FrameBudget.CreatureEntities);
            Retier(ref budget);
            if (queue.Count > 0) Arrive(ref budget);
            if (counts)
            {
                Count = live;
                PeakSpend = Math.Max(PeakSpend, budget.Spent);
            }
            if (arriving != null || pending.IsEmpty) return;

            var es = pending.ToEntityArray(Allocator.Temp);
            var states = pending.ToComponentDataArray<CreatureState>(Allocator.Temp);
            EntityManager.AddComponent<CreatureClaimed>(pending);
            List<Arrival> fresh = null;
            for (int i = 0; i < es.Length; i++)
            {
                var st = states[i];
                if (st.Body < 0 || st.Body >= kit.Lib.Bodies.Count) continue;
                fresh ??= new List<Arrival>();
                fresh.Add(new Arrival { Ghost = es[i], State = st });
            }
            es.Dispose();
            states.Dispose();
            if (fresh == null) return;
            arriving = StartPrepare(kit, fresh);
        }

        static Task<List<Arrival>> StartPrepare(Kit k, List<Arrival> fresh) => Task.Run(() =>
        {
            for (int i = 0; i < fresh.Count; i++) fresh[i] = Prepare(k, fresh[i]);
            return fresh;
        });

        void Vanish()
        {
            if (vanished.IsEmpty) return;
            var gone = vanished.ToComponentDataArray<CreaturePresented>(Allocator.Temp);
            var doomed = new NativeList<Entity>(gone.Length * 32, Allocator.Temp);
            int dropped = 0;
            foreach (var s in gone)
            {
                var links = EntityManager.GetBuffer<CreatureLink>(s.Root, true);
                if (instanced) dropped += links.Length;
                for (int i = 0; i < links.Length; i++)
                    if (links[i].Value != Entity.Null)
                    {
                        doomed.Add(links[i].Value);
                        dropped++;
                    }
                doomed.Add(s.Root);
                s.Fit.Dispose();
            }
            live -= gone.Length;
            gone.Dispose();
            EntityManager.RemoveComponent<CreaturePresented>(vanished);
            EntityManager.DestroyEntity(doomed.AsArray());
            doomed.Dispose();
            AddLinks(-dropped);
        }

        void AddLinks(int n)
        {
            var t = EntityManager.GetComponentData<CreatureTally>(tally);
            t.Links += n;
            EntityManager.SetComponentData(tally, t);
        }

        static Arrival Prepare(Kit k, Arrival a)
        {
            var def = k.Lib.Bodies[a.State.Body];
            var body = CreatureBody.Assemble(def, k.Lib, a.State.BodySeed);
            int first = k.First[a.State.Body], shapeCount = k.Shapes.Count, per = 1 + body.Segments.Count;
            a.Def = def;
            a.Body = body;
            a.Prefab = new int[2 * per];
            a.Scale = new float3[2 * per];
            var trunk = k.Shapes[first];
            for (int tier = 0; tier < 2; tier++)
            {
                int at = first + tier * shapeCount, o = tier * per;
                a.Prefab[o] = at;
                a.Scale[o] = new float3(body.Trunk.Radius, body.Trunk.Radius, body.Trunk.Length / math.max(trunk.Length, 1e-3f));
                for (int j = 0; j < body.Segments.Count; j++)
                {
                    var seg = body.Segments[j];
                    var shape = k.Shapes[first + seg.Shape];
                    a.Prefab[o + 1 + j] = at + seg.Shape;
                    a.Scale[o + 1 + j] = shape.Sphere ? new float3(seg.Radius) : new float3(seg.Radius, seg.Radius, seg.Length / math.max(shape.Length, 1e-3f));
                }
            }
            a.Base = Linear(body.Base);
            a.Accent = Linear(body.Accent);
            a.Dark = Linear(body.Dark);
            a.Length = body.Length;
            return a;
        }

        void Retier(ref FrameBudget budget)
        {
            if (instanced || asking.IsEmpty) return;
            var asks = asking.ToEntityArray(Allocator.Temp);
            var plans = new List<Plan>();
            var doomed = new NativeList<Entity>(Allocator.Temp);
            var settled = new NativeList<Entity>(Allocator.Temp);
            int delta = 0;
            foreach (var r in asks)
            {
                var c = EntityManager.GetComponentData<CreatureRoot>(r);
                var links = EntityManager.GetBuffer<CreatureLink>(r);
                int half = links.Length / 2;
                bool hasHigh = links[0].Value != Entity.Null, hasLow = links[half].Value != Entity.Null, waiting = false;
                var donor = hasHigh ? links[0].Value : links[half].Value;
                bool buildHigh = c.WantHigh && !hasHigh, buildLow = c.WantLow && !hasLow;
                if (donor != Entity.Null && (buildHigh || buildLow))
                {
                    var plan = new Plan
                    {
                        Root = r, Size = c.Scale.Length, Base = EntityManager.GetComponentData<CreatureFurBase>(donor).Value,
                        Accent = EntityManager.GetComponentData<CreatureFurAccent>(donor).Value, Dark = EntityManager.GetComponentData<CreatureFurDark>(donor).Value,
                    };
                    if (buildHigh && budget.Take(half)) { plan.Tier = 0; plans.Add(plan); hasHigh = c.HighOn = true; delta += half; }
                    else waiting |= buildHigh;
                    if (buildLow && budget.Take(half)) { plan.Tier = 1; plans.Add(plan); hasLow = c.LowOn = true; delta += half; }
                    else waiting |= buildLow;
                }
                for (int tier = 0; tier < 2; tier++)
                {
                    int b = tier * half;
                    bool want = tier == 0 ? c.WantHigh : c.WantLow, other = tier == 0 ? hasLow : hasHigh;
                    if (want || links[b].Value == Entity.Null) continue;
                    if (!other) { waiting = true; continue; }
                    for (int j = b; j < b + half; j++)
                    {
                        var l = links[j];
                        doomed.Add(l.Value);
                        l.Value = Entity.Null;
                        links[j] = l;
                    }
                    if (tier == 0) c.HighOn = false;
                    else c.LowOn = false;
                    delta -= half;
                }
                EntityManager.SetComponentData(r, c);
                if (!waiting) settled.Add(r);
            }
            asks.Dispose();
            foreach (var r in settled) EntityManager.SetComponentEnabled<CreatureRetier>(r, false);
            settled.Dispose();
            if (counts) TierDrops += doomed.Length;
            if (doomed.Length > 0) EntityManager.DestroyEntity(doomed.AsArray());
            doomed.Dispose();
            AddLinks(delta);
            if (plans.Count > 0) Build(plans);
        }

        void Build(List<Plan> plans)
        {
            var perPrefab = new int[prefabs.Length];
            int total = 0;
            foreach (var plan in plans)
            {
                var links = EntityManager.GetBuffer<CreatureLink>(plan.Root, true);
                int half = links.Length / 2;
                for (int j = plan.Tier * half; j < (plan.Tier + 1) * half; j++) perPrefab[links[j].Mesh]++;
                total += half;
            }
            var made = new NativeArray<Entity>[prefabs.Length];
            for (int p = 0; p < prefabs.Length; p++)
                if (perPrefab[p] > 0) made[p] = EntityManager.Instantiate(prefabs[p], perPrefab[p], Allocator.Temp);
            Array.Clear(perPrefab, 0, perPrefab.Length);
            var linkEntity = new NativeArray<Entity>(total, Allocator.TempJob);
            var linkScale = new NativeArray<float3>(total, Allocator.TempJob);
            var linkOwner = new NativeArray<int>(total, Allocator.TempJob);
            var fur = new NativeArray<float4>(3 * plans.Count, Allocator.TempJob);
            var size = new NativeArray<float>(plans.Count, Allocator.TempJob);
            int at = 0;
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                var links = EntityManager.GetBuffer<CreatureLink>(plan.Root);
                int half = links.Length / 2;
                for (int j = plan.Tier * half; j < (plan.Tier + 1) * half; j++)
                {
                    var l = links[j];
                    l.Value = made[l.Mesh][perPrefab[l.Mesh]++];
                    links[j] = l;
                    linkEntity[at] = l.Value;
                    linkScale[at] = l.Scale;
                    linkOwner[at] = i;
                    at++;
                }
                fur[3 * i] = plan.Base;
                fur[3 * i + 1] = plan.Accent;
                fur[3 * i + 2] = plan.Dark;
                size[i] = plan.Size;
            }
            foreach (var arr in made) if (arr.IsCreated) arr.Dispose();
            if (counts) TierBuilds += plans.Count;
            Dress(linkEntity, linkScale, linkOwner, fur, size);
        }

        void Dress(NativeArray<Entity> linkEntity, NativeArray<float3> linkScale, NativeArray<int> linkOwner, NativeArray<float4> fur, NativeArray<float> size)
        {
            Dependency = new DressJob
            {
                Links = linkEntity, Scales = linkScale, Owner = linkOwner, Fur = fur, Size = size,
                Ltw = GetComponentLookup<LocalToWorld>(), Base = GetComponentLookup<CreatureFurBase>(), Accent = GetComponentLookup<CreatureFurAccent>(),
                Dark = GetComponentLookup<CreatureFurDark>(), Bounds = GetComponentLookup<RenderBounds>(),
            }.Schedule(linkEntity.Length, 64, Dependency);
            linkEntity.Dispose(Dependency);
            linkScale.Dispose(Dependency);
            linkOwner.Dispose(Dependency);
            fur.Dispose(Dependency);
            size.Dispose(Dependency);
        }

        void Tiers(ref Arrival a, float3 cam, bool hasGround, in Ground ground)
        {
            if (instanced)
            {
                a.High = a.Low = true;
                return;
            }
            var view = DetailView.Current;
            float y = hasGround ? ground.HeightAt(a.State.Position.x, a.State.Position.y) : cam.y;
            float px = view.ScreenSize(a.Length, new float3(a.State.Position.x, y, a.State.Position.y));
            float low = a.Def.LowPixels / math.max(view.Quality, 0.1f);
            a.High = CreatureTier.NeedHigh(px, low, false);
            a.Low = CreatureTier.NeedLow(px, low, false) || !a.High;
        }

        void Arrive(ref FrameBudget budget)
        {
            float3 cam = DetailView.Current.Camera;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var a = queue[i];
                if (!Alive(a.Ghost))
                {
                    if (a.Fit.IsCreated) a.Fit.Dispose();
                    queue.RemoveAt(i);
                    continue;
                }
                var st = EntityManager.GetComponentData<CreatureState>(a.Ghost);
                a.State.Position = st.Position;
                a.State.Heading = st.Heading;
                a.Distance = math.distancesq(cam.xz, st.Position);
                queue[i] = a;
            }
            if (queue.Count == 0) return;
            queue.Sort((x, y) => x.Distance.CompareTo(y.Distance));

            int m = 0, links = 0;
            bool hasGround = SystemAPI.TryGetSingleton<Ground>(out var ground);
            while (m < queue.Count)
            {
                var a = queue[m];
                Tiers(ref a, cam, hasGround, ground);
                int per = a.Prefab.Length / 2, built = instanced ? 2 * per : (a.High ? per : 0) + (a.Low ? per : 0);
                if (!budget.Take(1 + built)) break;
                queue[m++] = a;
                links += built;
            }

            var perPrefab = new int[prefabs.Length];
            for (int i = 0; i < m; i++)
            {
                var a = queue[i];
                int per = a.Prefab.Length / 2;
                for (int j = 0; j < a.Prefab.Length; j++) if (j < per ? a.High : a.Low) perPrefab[a.Prefab[j]]++;
            }
            var made = new NativeArray<Entity>[prefabs.Length];
            for (int p = 0; p < prefabs.Length; p++)
                if (perPrefab[p] > 0 && !instanced) made[p] = EntityManager.Instantiate(prefabs[p], perPrefab[p], Allocator.Temp);
            var roots = EntityManager.CreateEntity(rootType, m, Allocator.Temp);

            var linkEntity = new NativeArray<Entity>(links, Allocator.TempJob);
            var linkScale = new NativeArray<float3>(links, Allocator.TempJob);
            var linkOwner = new NativeArray<int>(links, Allocator.TempJob);
            var fur = new NativeArray<float4>(3 * m, Allocator.TempJob);
            var size = new NativeArray<float>(m, Allocator.TempJob);
            var ready = new NativeArray<Entity>(m, Allocator.Temp);
            var marks = new NativeArray<CreaturePresented>(m, Allocator.Temp);
            Array.Clear(perPrefab, 0, perPrefab.Length);
            int at = 0;
            for (int i = 0; i < m; i++)
            {
                var a = queue[i];
                int body = a.State.Body;
                if (!kit.Rigs[body].IsCreated) kit.Rigs[body] = Rig(kit.Lib.Bodies[body], CreatureBody.Assemble(kit.Lib.Bodies[body], kit.Lib, 0));
                var rig = kit.Rigs[body];
                a.Fit = Fit(ref rig.Value, a.Def, a.Body, false, out a.Fitted);
                uint seed = a.State.Seed;
                EntityManager.SetComponentData(roots[i], new CreatureRoot
                {
                    Rig = rig, Fit = a.Fit, Scale = a.Fitted, Ghost = a.Ghost, Position = new float3(a.State.Position.x, 0, a.State.Position.y), Heading = a.State.Heading, WalkSpeed = a.State.WalkSpeed,
                    Visible = true, Shadow = true, HighOn = a.High, LowOn = a.Low, WantHigh = a.High, WantLow = a.Low,
                    Phase = (seed % 389) / 389f, Clock = (seed % 211) / 7f,
                });
                var buffer = EntityManager.GetBuffer<CreatureLink>(roots[i]);
                buffer.ResizeUninitialized(a.Prefab.Length);
                if (instanced) EntityManager.SetComponentData(roots[i], new CreatureFur { Base = a.Base, Accent = a.Accent, Dark = a.Dark });
                EntityManager.SetComponentEnabled<CreatureRetier>(roots[i], false);
                int half = a.Prefab.Length / 2;
                for (int j = 0; j < a.Prefab.Length; j++)
                {
                    int p = a.Prefab[j];
                    bool built = j < half ? a.High : a.Low;
                    var e = instanced || !built ? Entity.Null : made[p][perPrefab[p]++];
                    buffer[j] = new CreatureLink { Value = e, Scale = a.Scale[j], Mesh = p };
                    if (!built) continue;
                    linkEntity[at] = e;
                    linkScale[at] = a.Scale[j];
                    linkOwner[at] = i;
                    at++;
                }
                fur[3 * i] = a.Base;
                fur[3 * i + 1] = a.Accent;
                fur[3 * i + 2] = a.Dark;
                size[i] = a.Length;
                ready[i] = a.Ghost;
                marks[i] = new CreaturePresented { Root = roots[i], Fit = a.Fit };
            }
            EntityManager.AddComponent<CreaturePresented>(ready);
            for (int i = 0; i < m; i++) EntityManager.SetComponentData(ready[i], marks[i]);
            live += m;
            ready.Dispose();
            marks.Dispose();
            queue.RemoveRange(0, m);
            foreach (var arr in made) if (arr.IsCreated) arr.Dispose();
            roots.Dispose();
            AddLinks(links);
            if (instanced)
            {
                linkEntity.Dispose();
                linkScale.Dispose();
                linkOwner.Dispose();
                fur.Dispose();
                size.Dispose();
                return;
            }

            Dress(linkEntity, linkScale, linkOwner, fur, size);
        }

        [BurstCompile]
        struct DressJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Links;
            [ReadOnly] public NativeArray<float3> Scales;
            [ReadOnly] public NativeArray<int> Owner;
            [ReadOnly] public NativeArray<float4> Fur;
            [ReadOnly] public NativeArray<float> Size;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToWorld> Ltw;
            [NativeDisableParallelForRestriction] public ComponentLookup<CreatureFurBase> Base;
            [NativeDisableParallelForRestriction] public ComponentLookup<CreatureFurAccent> Accent;
            [NativeDisableParallelForRestriction] public ComponentLookup<CreatureFurDark> Dark;
            [NativeDisableParallelForRestriction] public ComponentLookup<RenderBounds> Bounds;

            public void Execute(int i)
            {
                var e = Links[i];
                int o = Owner[i];
                Ltw[e] = new LocalToWorld { Value = float4x4.TRS(0, quaternion.identity, Scales[i]) };
                Base[e] = new CreatureFurBase { Value = Fur[3 * o] };
                Accent[e] = new CreatureFurAccent { Value = Fur[3 * o + 1] };
                Dark[e] = new CreatureFurDark { Value = Fur[3 * o + 2] };
                Bounds[e] = new RenderBounds { Value = new Unity.Mathematics.AABB { Center = 0, Extents = new float3(1.2f * Size[o]) } };
            }
        }

        static Kit MakeKit(List<BiomeDef> defs)
        {
            var k = new Kit { Lib = ContentFiles.Creatures(defs) };
            k.First = new int[k.Lib.Bodies.Count];
            k.Rigs = new BlobAssetReference<CreatureRig>[k.Lib.Bodies.Count];
            var verts = new List<float>();
            var index = new List<ushort>();
            for (int b = 0; b < k.Lib.Bodies.Count; b++)
            {
                k.First[b] = k.Shapes.Count;
                var shapes = CreatureBody.Shapes(k.Lib.Bodies[b], k.Lib);
                for (int s = 0; s < shapes.Count; s++)
                {
                    CreatureMesh.Build(shapes[s], s == 0, false, verts, index);
                    k.Shapes.Add(shapes[s]);
                    k.Verts.Add(verts.ToArray());
                    k.Index.Add(index.ToArray());
                    CreatureMesh.Build(shapes[s], s == 0, true, verts, index);
                    k.LowVerts.Add(verts.ToArray());
                    k.LowIndex.Add(index.ToArray());
                }
            }
            k.Pack = Pack(k);
            return k;
        }

        static MeshPack Pack(Kit k)
        {
            var pack = new MeshPack(1 << 16, 1 << 16);
            try
            {
                int attr = pack.Layout(Layout), shapeCount = k.Shapes.Count;
                for (int s = 0; s < 2 * shapeCount; s++)
                {
                    var vs = s < shapeCount ? k.Verts[s] : k.LowVerts[s - shapeCount];
                    var ix = s < shapeCount ? k.Index[s] : k.LowIndex[s - shapeCount];
                    pack.Add("Creature " + s, s, attr, Layout.Length, CreatureMesh.Stride, vs, ix);
                }
            }
            catch
            {
                pack.Dispose();
                throw;
            }
            return pack;
        }

        static float Linear(float c) => c <= 0.04045f ? c / 12.92f : math.pow((c + 0.055f) / 1.055f, 2.4f);

        static float4 Linear(float[] c) => new float4(Linear(c[0]), Linear(c[1]), Linear(c[2]), 1);

        static float3 V(float[] a) => new float3(a[0], a[1], a[2]);

        static BlobAssetReference<CreatureRig> Rig(CreatureDef def, CreatureBody body)
        {
            var b = new BlobBuilder(Allocator.Persistent);
            ref var rig = ref b.ConstructRoot<CreatureRig>();
            rig.RunRatio = def.RunSpeed / def.WalkSpeed;
            rig.FadePx = def.FadePixels;
            rig.ShadowPx = def.ShadowPixels;
            rig.AnimPx = def.AnimPixels;
            rig.IkPx = def.IkPixels;
            rig.LowPx = def.LowPixels;
            rig.Rule = def.Rule();
            var parts = b.Allocate(ref rig.Parts, body.Parts.Count);
            for (int i = 0; i < body.Parts.Count; i++)
            {
                var p = body.Parts[i];
                parts[i] = new CreaturePartRig
                {
                    Parent = p.Parent, Role = p.Role, First = p.FirstSegment, Count = p.SegmentCount,
                    Rest = quaternion.EulerZXY(p.RestX, p.RestY, p.RestZ),
                    Phase = p.Phase, RunPhase = p.RunPhase, Side = p.Side,
                };
            }
            var segs = b.Allocate(ref rig.Segments, body.Segments.Count);
            for (int i = 0; i < body.Segments.Count; i++)
            {
                var s = body.Segments[i];
                segs[i] = new CreatureSegmentRig
                {
                    Idle = V(s.Idle), Forward = V(s.Reach), Back = V(s.Push), Lift = V(s.Lift),
                    RunForward = V(s.RunReach), RunBack = V(s.RunPush), RunLift = V(s.RunLift), Alert = V(s.Alert), Graze = V(s.Graze),
                };
            }
            b.Allocate(ref rig.RestRot, body.Segments.Count);
            var blob = b.CreateBlobAssetReference<CreatureRig>(Allocator.Persistent);
            b.Dispose();
            Fit(ref blob.Value, def, body, true, out _).Dispose();
            return blob;
        }

        static BlobAssetReference<CreatureFit> Fit(ref CreatureRig rig, CreatureDef def, CreatureBody body, bool rest, out CreatureScale scale)
        {
            var b = new BlobBuilder(Allocator.Persistent);
            ref var fit = ref b.ConstructRoot<CreatureFit>();
            var lengths = b.Allocate(ref fit.Lengths, body.Segments.Count);
            for (int i = 0; i < body.Segments.Count; i++) lengths[i] = body.Segments[i].Length;
            var sockets = b.Allocate(ref fit.Sockets, body.Parts.Count);
            for (int i = 0; i < body.Parts.Count; i++)
            {
                var p = body.Parts[i];
                float reach = 0;
                for (int j = 0; j < p.SegmentCount; j++) reach += body.Segments[p.FirstSegment + j].Length;
                sockets[i] = new float4(p.SocketX, p.SocketY, p.SocketZ, reach);
            }
            b.Allocate(ref fit.RestPos, body.Segments.Count);
            var blob = b.CreateBlobAssetReference<CreatureFit>(Allocator.Persistent);
            b.Dispose();
            scale = new CreatureScale { Length = body.Length, Rear = body.RearRadius, Stand = 0, Stride = 1, RunStride = def.RunStride * body.Size };
            Measure(ref rig, ref blob.Value, ref scale, body.Size, rest);
            return blob;
        }

        static void Measure(ref CreatureRig rig, ref CreatureFit fit, ref CreatureScale scale, float size, bool rest)
        {
            int n = rig.Segments.Length, parts = rig.Parts.Length;
            var pos = new NativeArray<float3>(n, Allocator.Persistent);
            var rot = new NativeArray<quaternion>(n, Allocator.Persistent);
            var end = new NativeArray<float3>(parts, Allocator.Persistent);
            var endRot = new NativeArray<quaternion>(parts, Allocator.Persistent);
            CreaturePose.Solve(ref rig, ref fit, 0, quaternion.identity, 0, 0, 0, 0, 0, 0, 0, pos, rot, end, endRot);
            for (int i = 0; i < n; i++)
            {
                fit.RestPos[i] = pos[i];
                if (rest) rig.RestRot[i] = rot[i];
            }
            CreaturePose.Solve(ref rig, ref fit, 0, quaternion.identity, 0, 0, 0, 0, 0, 0, 1, pos, rot, end, endRot);
            float footY = 0, travel = 0, fore = 0, hind = 0, track = 0;
            int legs = 0, fores = 0;
            for (int i = 0; i < parts; i++)
            {
                if (rig.Parts[i].Role != CreaturePartDef.Leg) continue;
                footY += end[i].y;
                track += math.abs(end[i].x);
                if (end[i].z > 0) { fore += end[i].z; fores++; }
                else hind += end[i].z;
                legs++;
            }
            bool paired = fores > 0 && fores < legs;
            scale.Fore = paired ? fore / fores : 0.5f * scale.Length;
            scale.Hind = paired ? hind / (legs - fores) : -0.5f * scale.Length;
            scale.Track = legs > 0 ? track / legs : 0;
            for (int i = 0; i < parts; i++)
            {
                ref var part = ref rig.Parts[i];
                if (part.Role != CreaturePartDef.Leg) continue;
                CreaturePose.Solve(ref rig, ref fit, 0, quaternion.identity, math.frac(-part.Phase), 1, 0, 0, 0, 0, 1, pos, rot, end, endRot);
                float front = end[i].z;
                CreaturePose.Solve(ref rig, ref fit, 0, quaternion.identity, math.frac(0.5f - part.Phase), 1, 0, 0, 0, 0, 1, pos, rot, end, endRot);
                travel += front - end[i].z;
            }
            if (legs > 0)
            {
                scale.Stand = -footY / legs + 0.015f * size;
                scale.Stride = math.max(0.1f, 2 * travel / legs);
            }
            pos.Dispose(); rot.Dispose(); end.Dispose(); endRot.Dispose();
        }

        void MakeMeshes()
        {
            var graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (batch.Pump(pumped, pumpedIds))
                for (int i = 0; i < pumped.Count; i++)
                {
                    meshes.Add(pumped[i]);
                    if (!instanced) meshIds[pumpedIds[i]] = graphics.RegisterMesh(pumped[i]);
                }
            if (!batch.Done) return;
            batch.Dispose();
            batch = null;
            kit.Pack.Dispose();
            kit.Pack = null;
            MakePrefabs();
        }

        void MakePrefabs()
        {
            int n = meshIds.Length;
            prefabs = new Entity[n];
            EntityManager.SetComponentData(tally, new CreatureTally { Instanced = instanced, Groups = 2 * n });
            if (instanced) return;
            var template = EntityManager.CreateEntity();
            RenderMeshUtility.AddComponents(template, EntityManager, new RenderMeshDescription(ShadowCastingMode.On, true, MotionVectorGenerationMode.ForceNoMotion, 0), new MaterialMeshInfo(materialId, meshIds[0]));
            EntityManager.AddComponentData(template, new ObjectFade { Value = new float4(1, 1, 0, 0.5f) });
            EntityManager.AddComponent(template, new ComponentTypeSet(ComponentType.ReadWrite<CreatureFurBase>(), ComponentType.ReadWrite<CreatureFurAccent>(), ComponentType.ReadWrite<CreatureFurDark>(), ComponentType.ReadWrite<Prefab>()));
            var all = new NativeArray<Entity>(n, Allocator.TempJob);
            all[0] = template;
            if (n > 1)
                using (var copies = EntityManager.Instantiate(template, n - 1, Allocator.Temp))
                    all.GetSubArray(1, n - 1).CopyFrom(copies);
            all.CopyTo(prefabs);
            var info = new NativeArray<MaterialMeshInfo>(n, Allocator.TempJob);
            var box = new NativeArray<Unity.Mathematics.AABB>(n, Allocator.TempJob);
            for (int s = 0; s < n; s++)
            {
                info[s] = new MaterialMeshInfo(materialId, meshIds[s]);
                box[s] = new AABB { Center = meshes[s].bounds.center, Extents = meshes[s].bounds.extents };
            }
            Dependency = new Assign { Entities = all, Info = info, Box = box, Infos = GetComponentLookup<MaterialMeshInfo>(), Bounds = GetComponentLookup<RenderBounds>() }.Schedule(n, Dependency);
            all.Dispose(Dependency);
            info.Dispose(Dependency);
            box.Dispose(Dependency);
        }

        [BurstCompile]
        struct Assign : IJobFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<MaterialMeshInfo> Info;
            [ReadOnly] public NativeArray<Unity.Mathematics.AABB> Box;
            public ComponentLookup<MaterialMeshInfo> Infos;
            public ComponentLookup<RenderBounds> Bounds;

            public void Execute(int i)
            {
                Infos[Entities[i]] = Info[i];
                Bounds[Entities[i]] = new RenderBounds { Value = Box[i] };
            }
        }
    }
}
