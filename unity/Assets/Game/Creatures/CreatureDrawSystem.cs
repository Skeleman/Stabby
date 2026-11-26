using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CreatureBuildSystem))]
    [UpdateBefore(typeof(CreatureAnimSystem))]
    public partial class CreatureDrawSystem : SystemBase
    {
        static readonly int CreaturesId = Shader.PropertyToID("_Creatures");
        static readonly int BonesId = Shader.PropertyToID("_CreatureBones");
        static readonly int FurId = Shader.PropertyToID("_CreatureFur");
        static readonly int StartId = Shader.PropertyToID("_CreatureStart");
        Material material;
        CreatureBuildSystem build;
        readonly UploadRing<CreaturePart> instances = new UploadRing<CreaturePart>(GraphicsBuffer.Target.Structured, 32, "Creature instances");
        readonly UploadRing<float3x4> bones = new UploadRing<float3x4>(GraphicsBuffer.Target.Raw, 48, "Creature bones");
        readonly UploadRing<float4> fur = new UploadRing<float4>(GraphicsBuffer.Target.Structured, 16, "Creature fur");
        readonly UploadRing<GraphicsBuffer.IndirectDrawIndexedArgs> args = new UploadRing<GraphicsBuffer.IndirectDrawIndexedArgs>(GraphicsBuffer.Target.IndirectArguments, GraphicsBuffer.IndirectDrawIndexedArgs.size, "Creature args");
        NativeArray<uint4> meshInfo;
        NativeArray<int> packed;
        JobHandle pack;
        bool packing, empty;
        int furCount;
        MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[0];
        int[] counts = new int[0];
        readonly Plane[] planes = new Plane[6];
        public int Drawn, Uploads, Deferred;

        protected override void OnCreate()
        {
            Enabled = CreaturePath.Instanced;
        }

        protected override void OnDestroy()
        {
            JobPoll.Teardown(pack);
            instances.Release();
            bones.Release();
            fur.Release();
            args.Release();
            if (meshInfo.IsCreated) meshInfo.Dispose();
            if (packed.IsCreated) packed.Dispose();
            if (material != null) Object.Destroy(material);
        }

        protected override void OnUpdate()
        {
            Flush();
            var handle = World.GetExistingSystem<CreatureAnimSystem>();
            if (handle == SystemHandle.Null) return;
            build = World.GetExistingSystemManaged<CreatureBuildSystem>();
            ref var anim = ref World.Unmanaged.GetUnsafeSystemRef<CreatureAnimSystem>(handle);
            var cam = Camera.main;
            if (cam != null)
            {
                GeometryUtility.CalculateFrustumPlanes(cam, planes);
                anim.Frustum = new CreatureFrustum
                {
                    P0 = Plane(0), P1 = Plane(1), P2 = Plane(2), P3 = Plane(3), P4 = Plane(4), P5 = Plane(5), On = true,
                };
            }
            if (material == null)
            {
                var shader = Shader.Find("Stabby/Creature");
                if (shader == null) return;
                material = new Material(shader) { name = "Stabby creatures instanced" };
                material.EnableKeyword("STABBY_CREATURE_BUFFER");
            }
            if (!packing && anim.Pending && JobPoll.Done(anim.Gpu)) Schedule(ref anim);
            empty = !SystemAPI.TryGetSingleton<CreatureTally>(out var tally) || tally.Links == 0;
        }

        public void Submit()
        {
            Flush();
            if (empty) System.Array.Clear(counts, 0, counts.Length);
            Draw();
        }

        float4 Plane(int i) => new float4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);

        void Schedule(ref CreatureAnimSystem anim)
        {
            Uploads++;
            int groups = anim.Counts.Length;
            var meshes = build.Meshes;
            if (counts.Length != groups)
            {
                counts = new int[groups];
                blocks = new MaterialPropertyBlock[groups];
                for (int g = 0; g < groups; g++) blocks[g] = new MaterialPropertyBlock();
                if (meshInfo.IsCreated) meshInfo.Dispose();
                if (packed.IsCreated) packed.Dispose();
                meshInfo = new NativeArray<uint4>(groups, Allocator.Persistent);
                packed = new NativeArray<int>(2 * groups + 2, Allocator.Persistent);
            }
            for (int g = 0; g < groups; g++)
            {
                int m = g >> 1;
                bool known = m < meshes.Count && meshes[m] != null;
                meshInfo[g] = known ? new uint4(meshes[m].GetIndexCount(0), meshes[m].GetIndexStart(0), meshes[m].GetBaseVertex(0), 1) : uint4.zero;
            }
            furCount = anim.FurCount;
            anim.Room = anim.Bones.Capacity;
            pack = new Pack
            {
                Parts = anim.Parts, Bones = anim.Bones, Fur = anim.Fur, Starts = anim.Starts, Counts = anim.Counts, Totals = anim.Totals, Mesh = meshInfo, FurCount = furCount,
                Instances = instances.Lock(anim.Parts.Length), BonesOut = bones.Lock(math.max(anim.Bones.Length, 1)), FurOut = fur.Lock(math.max(furCount, 3)), Args = args.Lock(groups), Packed = packed,
            }.Schedule(anim.Gpu);
            JobHandle.ScheduleBatchedJobs();
            anim.Gpu = pack;
            anim.Pending = false;
            packing = true;
        }

        void Flush()
        {
            if (!packing) return;
            if (!JobPoll.Done(pack)) { Deferred++; return; }
            packing = false;
            int groups = counts.Length;
            instances.Unlock(packed[2 * groups]);
            bones.Unlock(packed[2 * groups + 1]);
            fur.Unlock(furCount);
            args.Unlock(groups);
            for (int g = 0; g < groups; g++)
            {
                counts[g] = packed[g];
                blocks[g].SetInteger(StartId, packed[groups + g]);
                blocks[g].SetBuffer(CreaturesId, instances.Current);
                blocks[g].SetBuffer(BonesId, bones.Current);
                blocks[g].SetBuffer(FurId, fur.Current);
            }
        }

        void Draw()
        {
            Drawn = 0;
            if (args.Current == null || build == null) return;
            var meshes = build.Meshes;
            var bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            for (int g = 0; g < counts.Length; g++)
            {
                if (counts[g] == 0 || (g >> 1) >= meshes.Count) continue;
                var rp = new RenderParams(material)
                {
                    worldBounds = bounds,
                    shadowCastingMode = (g & 1) != 0 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    matProps = blocks[g],
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                };
                Graphics.RenderMeshIndirect(rp, meshes[g >> 1], args.Current, 1, g);
                Drawn += counts[g];
            }
        }

        [BurstCompile]
        struct Pack : IJob
        {
            [ReadOnly] public NativeArray<CreaturePart> Parts;
            [ReadOnly] public NativeList<float3x4> Bones;
            [ReadOnly] public NativeArray<float4> Fur;
            [ReadOnly] public NativeArray<int> Starts, Counts, Totals;
            [ReadOnly] public NativeArray<uint4> Mesh;
            public int FurCount;
            public NativeArray<CreaturePart> Instances;
            public NativeArray<float3x4> BonesOut;
            public NativeArray<float4> FurOut;
            public NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs> Args;
            public NativeArray<int> Packed;

            public void Execute()
            {
                int groups = Counts.Length, n = 0;
                for (int g = 0; g < groups; g++)
                {
                    var m = Mesh[g];
                    int c = m.w != 0 ? Counts[g] : 0;
                    Packed[g] = c;
                    Packed[groups + g] = Starts[g];
                    n += Counts[g];
                    Args[g] = new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = m.x, instanceCount = (uint)c, startIndex = m.y, baseVertexIndex = m.z };
                }
                NativeArray<CreaturePart>.Copy(Parts, Instances, n);
                NativeArray<float3x4>.Copy(Bones.AsArray(), BonesOut, Totals[0]);
                NativeArray<float4>.Copy(Fur, FurOut, FurCount);
                Packed[2 * groups] = n;
                Packed[2 * groups + 1] = Totals[0];
            }
        }
    }
}
