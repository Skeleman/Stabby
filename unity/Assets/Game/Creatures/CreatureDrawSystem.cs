using Unity.Entities;
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
        GraphicsBuffer instances, bones, fur, args;
        GraphicsBuffer.IndirectDrawIndexedArgs[] commands = new GraphicsBuffer.IndirectDrawIndexedArgs[0];
        MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[0];
        int[] counts = new int[0];
        readonly Plane[] planes = new Plane[6];
        public int Drawn, Uploads;

        protected override void OnCreate()
        {
            Enabled = CreaturePath.Instanced;
        }

        protected override void OnDestroy()
        {
            instances?.Release();
            bones?.Release();
            fur?.Release();
            args?.Release();
            if (material != null) Object.Destroy(material);
        }

        static GraphicsBuffer Grow(GraphicsBuffer b, GraphicsBuffer.Target target, int count, int stride)
        {
            int old = b == null ? 0 : b.count;
            if (old >= count) return b;
            b?.Release();
            return new GraphicsBuffer(target, math.max(count, old == 0 ? 64 : old + old / 2), stride);
        }

        protected override void OnUpdate()
        {
            var handle = World.GetExistingSystem<CreatureAnimSystem>();
            if (handle == SystemHandle.Null) return;
            var build = World.GetExistingSystemManaged<CreatureBuildSystem>();
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
            if (anim.Pending && anim.Gpu.IsCompleted)
            {
                anim.Gpu.Complete();
                anim.Pending = false;
                Upload(ref anim, build);
            }
            if (!SystemAPI.TryGetSingleton<CreatureTally>(out var tally) || tally.Links == 0) System.Array.Clear(counts, 0, counts.Length);
            Draw(build);
        }

        float4 Plane(int i) => new float4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);

        void Upload(ref CreatureAnimSystem anim, CreatureBuildSystem build)
        {
            Uploads++;
            int n = anim.Instances.Length, groups = anim.Counts.Length;
            var meshes = build.Meshes;
            if (counts.Length != groups)
            {
                counts = new int[groups];
                commands = new GraphicsBuffer.IndirectDrawIndexedArgs[groups];
                blocks = new MaterialPropertyBlock[groups];
                for (int g = 0; g < groups; g++) blocks[g] = new MaterialPropertyBlock();
                args?.Release();
                args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, groups, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            }
            instances = Grow(instances, GraphicsBuffer.Target.Structured, math.max(n, 1), 32);
            bones = Grow(bones, GraphicsBuffer.Target.Raw, math.max(n, 1), 48);
            fur = Grow(fur, GraphicsBuffer.Target.Structured, math.max(anim.FurCount, 3), 16);
            if (n > 0)
            {
                instances.SetData(anim.Parts, 0, 0, n);
                bones.SetData(anim.Bones, 0, 0, n);
            }
            if (anim.FurCount > 0) fur.SetData(anim.Fur, 0, 0, anim.FurCount);
            material.SetBuffer(CreaturesId, instances);
            material.SetBuffer(BonesId, bones);
            material.SetBuffer(FurId, fur);
            for (int g = 0; g < groups; g++)
            {
                int m = g >> 1;
                bool known = m < meshes.Count && meshes[m] != null;
                counts[g] = known ? anim.Counts[g] : 0;
                commands[g] = new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = known ? meshes[m].GetIndexCount(0) : 0,
                    instanceCount = (uint)counts[g],
                    startIndex = known ? meshes[m].GetIndexStart(0) : 0,
                    baseVertexIndex = known ? meshes[m].GetBaseVertex(0) : 0,
                };
                blocks[g].SetInteger(StartId, anim.Starts[g]);
            }
            args.SetData(commands);
        }

        void Draw(CreatureBuildSystem build)
        {
            Drawn = 0;
            if (args == null || instances == null) return;
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
                Graphics.RenderMeshIndirect(rp, meshes[g >> 1], args, 1, g);
                Drawn += counts[g];
            }
        }
    }
}
