using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Stabby.Game
{
    public static class CreaturePose
    {
        public static float3 Cycle(float3 forward, float3 back, float3 lift, float p)
        {
            if (p < 0.5f) return math.lerp(forward, back, 2 * p);
            float t = 2 * p - 1;
            return math.lerp(back, forward, math.smoothstep(0, 1, t)) + lift * math.sin(math.PI * t);
        }

        static float3 Sway(int role, float side, float clock)
        {
            if (role == CreaturePartDef.Neck) return new float3(0.04f * math.sin(clock * 0.55f), 0.03f * math.sin(clock * 0.31f + 1), 0);
            if (role == CreaturePartDef.Head) return new float3(0.05f * math.sin(clock * 0.43f + 2), 0.06f * math.sin(clock * 0.27f), 0);
            if (role == CreaturePartDef.Ear)
            {
                float flick = math.pow(math.max(0, math.sin(clock * 0.9f + side * 2.1f)), 12);
                return new float3(0.3f * flick, side * (0.08f * math.sin(clock * 0.7f + side) - 0.25f * flick), 0);
            }
            if (role == CreaturePartDef.Tail) return new float3(0.06f * math.sin(clock * 1.9f), 0.2f * math.sin(clock * 2.6f), 0);
            return 0;
        }

        public static float Run(ref CreatureRig rig, float move) => math.saturate((move - 1) / math.max(rig.RunRatio - 1, 1e-3f));

        public static void Solve(ref CreatureRig rig, float3 rootPos, quaternion rootRot, float phase, float move, float run, float alert, float down, float clock, float swayScale,
            NativeArray<float3> pos, NativeArray<quaternion> rot, NativeArray<float3> end, NativeArray<quaternion> endRot)
        {
            for (int i = 0; i < rig.Parts.Length; i++)
            {
                ref var part = ref rig.Parts[i];
                float3 start;
                quaternion r;
                if (part.Parent < 0) { start = rootPos + math.mul(rootRot, part.Socket); r = math.mul(rootRot, part.Rest); }
                else { start = end[part.Parent] + math.mul(endRot[part.Parent], part.Socket); r = math.mul(endRot[part.Parent], part.Rest); }
                float p = math.frac(phase + math.lerp(part.Phase, part.RunPhase, run));
                float3 sway = Sway(part.Role, part.Side, clock) * ((1 - 0.8f * alert) * swayScale);
                float gait = math.min(move, 1);
                for (int j = 0; j < part.Count; j++)
                {
                    ref var seg = ref rig.Segments[part.First + j];
                    float3 stand = math.lerp(math.lerp(seg.Idle, seg.Graze, down), seg.Alert, alert);
                    float3 cycle = math.lerp(Cycle(seg.Forward, seg.Back, seg.Lift, p), Cycle(seg.RunForward, seg.RunBack, seg.RunLift, p), run);
                    float3 a = math.lerp(stand, cycle, gait) + sway;
                    r = math.mul(r, quaternion.EulerZXY(a));
                    pos[part.First + j] = start;
                    rot[part.First + j] = r;
                    start += math.mul(r, new float3(0, 0, seg.Length));
                }
                end[i] = start;
                endRot[i] = r;
            }
        }

        public static quaternion FromTo(float3 u, float3 v) => math.normalize(new quaternion(new float4(math.cross(u, v), 1 + math.dot(u, v))));

        public static void Reach(ref CreaturePartRig part, quaternion rootRot, float3 target, NativeArray<float3> pos, NativeArray<quaternion> rot, float3 foot)
        {
            int f = part.First;
            float3 h = pos[f], k = pos[f + 1];
            float a = math.length(k - h), b = math.length(foot - k);
            float3 ht = target - h;
            float span = math.length(ht);
            float3 u = ht / math.max(span, 1e-5f);
            float d = math.clamp(span, math.abs(a - b) + 1e-3f, (a + b) * 0.999f);
            float3 kh = k - h, v = kh - u * math.dot(kh, u);
            float side = math.length(v);
            v = side > 1e-5f ? v / side : math.mul(rootRot, new float3(0, 0, 1));
            float ca = math.clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1);
            float3 k2 = h + u * (a * ca) + v * (a * math.sqrt(1 - ca * ca));
            float3 t2 = h + u * d;
            var q0 = FromTo(math.normalizesafe(kh), math.normalizesafe(k2 - h));
            var q1 = FromTo(math.normalizesafe(foot - k), math.normalizesafe(t2 - k2));
            rot[f] = math.mul(q0, rot[f]);
            for (int j = f + 1; j < f + part.Count; j++)
            {
                pos[j] = k2 + math.mul(q1, pos[j] - k);
                rot[j] = math.mul(q1, rot[j]);
            }
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(ObjectViewSystem))]
    [UpdateBefore(typeof(EntitiesGraphicsSystem))]
    public partial struct CreatureAnimSystem : ISystem
    {
        ComponentLookup<LocalToWorld> world;
        ComponentLookup<ObjectFade> fade;
        ComponentLookup<MaterialMeshInfo> mmi;
        ComponentLookup<CreatureState> states;
        ComponentLookup<CreatureFur> furs;
        EntityQuery roots;
        public NativeList<CreatureGpu> Instances;
        public NativeArray<CreatureGpu> Sorted;
        public NativeArray<float4> Fur;
        public NativeArray<int> Starts, Counts;
        public JobHandle Gpu;
        public bool Pending;
        public int Reused, FurCount;
        public CreatureFrustum Frustum;
        NativeArray<float3> scratchPos, scratchEnd;
        NativeArray<quaternion> scratchRot, scratchEndRot;
        NativeArray<float> scratchFloor;
        float carry;
        int skipped;
        const int SegCap = 64, PartCap = 32;

        public void OnCreate(ref SystemState state)
        {
            int threads = JobsUtility.ThreadIndexCount;
            scratchPos = new NativeArray<float3>(threads * SegCap, Allocator.Persistent);
            scratchRot = new NativeArray<quaternion>(threads * SegCap, Allocator.Persistent);
            scratchFloor = new NativeArray<float>(threads * SegCap, Allocator.Persistent);
            scratchEnd = new NativeArray<float3>(threads * PartCap, Allocator.Persistent);
            scratchEndRot = new NativeArray<quaternion>(threads * PartCap, Allocator.Persistent);
            state.RequireForUpdate<CreatureRoot>();
            state.RequireForUpdate<Ground>();
            world = state.GetComponentLookup<LocalToWorld>();
            fade = state.GetComponentLookup<ObjectFade>();
            mmi = state.GetComponentLookup<MaterialMeshInfo>();
            states = state.GetComponentLookup<CreatureState>(true);
            furs = state.GetComponentLookup<CreatureFur>(true);
            roots = SystemAPI.QueryBuilder().WithAll<CreatureRoot, CreatureLink>().Build();
            Instances = new NativeList<CreatureGpu>(1, Allocator.Persistent);
            Sorted = new NativeArray<CreatureGpu>(1, Allocator.Persistent);
            Fur = new NativeArray<float4>(3, Allocator.Persistent);
            Starts = new NativeArray<int>(1, Allocator.Persistent);
            Counts = new NativeArray<int>(1, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            scratchPos.Dispose(state.Dependency);
            scratchRot.Dispose(state.Dependency);
            scratchFloor.Dispose(state.Dependency);
            scratchEnd.Dispose(state.Dependency);
            scratchEndRot.Dispose(state.Dependency);
            var dep = JobHandle.CombineDependencies(state.Dependency, Gpu);
            Instances.Dispose(dep);
            Sorted.Dispose(dep);
            Fur.Dispose(dep);
            Starts.Dispose(dep);
            Counts.Dispose(dep);
        }

        static void Fit<T>(ref NativeArray<T> a, int n) where T : struct
        {
            if (a.Length >= n) return;
            int size = math.max(n, a.Length + a.Length / 2);
            a.Dispose();
            a = new NativeArray<T>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var view = DetailView.Current;
            if (view.PixelsPerMetre <= 0) return;
            world.Update(ref state);
            fade.Update(ref state);
            mmi.Update(ref state);
            states.Update(ref state);
            furs.Update(ref state);
            var hold = CreatureView.Current;
            bool present = !SystemAPI.TryGetSingleton<CreaturePresenter>(out var presenter) || presenter.Index == hold.Show;
            bool gpu = SystemAPI.TryGetSingleton<CreatureTally>(out var tally) && tally.Instanced;
            if (gpu)
            {
                if (Pending) { Reused++; carry += SystemAPI.Time.DeltaTime; skipped++; return; }
                int count = roots.CalculateEntityCount();
                Instances.Clear();
                if (Instances.Capacity < tally.Links) Instances.Capacity = tally.Links + tally.Links / 2;
                Fit(ref Sorted, Instances.Capacity);
                Fit(ref Fur, 3 * count);
                Fit(ref Starts, tally.Groups);
                Fit(ref Counts, tally.Groups);
                FurCount = 3 * count;
            }
            state.Dependency = new AnimJob
            {
                Ground = SystemAPI.GetSingleton<Ground>(), View = view, Hold = hold, Present = present, Dt = math.min(SystemAPI.Time.DeltaTime + carry, 0.1f * (1 + skipped)),
                World = world, Fade = fade, Mmi = mmi, States = states,
                Pos = scratchPos, Rot = scratchRot, Floor = scratchFloor, End = scratchEnd, EndRot = scratchEndRot,
                Gpu = gpu, Furs = furs, Out = Instances.AsParallelWriter(), FurOut = Fur, Frustum = Frustum,
            }.ScheduleParallel(JobHandle.CombineDependencies(state.Dependency, Gpu));
            carry = 0;
            skipped = 0;
            if (!gpu) return;
            state.Dependency = new Compact { Instances = Instances, Sorted = Sorted, Starts = Starts, Counts = Counts, Groups = tally.Groups }.Schedule(state.Dependency);
            Gpu = state.Dependency;
            Pending = true;
        }

        [BurstCompile]
        struct Compact : IJob
        {
            [ReadOnly] public NativeList<CreatureGpu> Instances;
            public NativeArray<CreatureGpu> Sorted;
            public NativeArray<int> Starts, Counts;
            public int Groups;

            public void Execute()
            {
                for (int g = 0; g < Counts.Length; g++) Counts[g] = 0;
                for (int i = 0; i < Instances.Length; i++) Counts[Instances[i].Group]++;
                int at = 0;
                for (int g = 0; g < Groups; g++) { Starts[g] = at; at += Counts[g]; }
                var cursor = new NativeArray<int>(Groups, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int g = 0; g < Groups; g++) cursor[g] = Starts[g];
                for (int i = 0; i < Instances.Length; i++)
                {
                    var x = Instances[i];
                    Sorted[cursor[x.Group]++] = x;
                }
            }
        }

        [BurstCompile]
        partial struct AnimJob : IJobEntity
        {
            public Ground Ground;
            public DetailView View;
            public CreatureView Hold;
            public float Dt;
            public bool Present, Gpu;
            public CreatureFrustum Frustum;
            [ReadOnly] public ComponentLookup<CreatureState> States;
            [ReadOnly] public ComponentLookup<CreatureFur> Furs;
            public NativeList<CreatureGpu>.ParallelWriter Out;
            [NativeDisableParallelForRestriction] public NativeArray<float4> FurOut;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToWorld> World;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectFade> Fade;
            [NativeDisableParallelForRestriction] public ComponentLookup<MaterialMeshInfo> Mmi;
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<float3> Pos, End;
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<quaternion> Rot, EndRot;
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<float> Floor;

            NativeArray<T> Scratch<T>(NativeArray<T> pool, int cap, int n, bool fits) where T : struct =>
                fits ? pool.GetSubArray(JobsUtility.ThreadIndex * cap, n) : new NativeArray<T>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            void Follow(ref CreatureRoot c, ref CreatureRig rig, in CreatureState s)
            {
                c.Position.x = s.Position.x;
                c.Position.z = s.Position.y;
                c.Heading = s.Heading;
                c.WalkSpeed = s.WalkSpeed;
                if (Hold.Override != 0)
                {
                    c.Move = Hold.Run > 0 ? 1 + Hold.Run * (rig.RunRatio - 1) : Hold.Move;
                    c.Alert = Hold.Alert;
                    c.Down = Hold.Down;
                    c.Phase = Hold.Phase;
                    c.Clock = 0;
                    return;
                }
                c.Move = s.Move;
                c.Alert = s.Alert;
                c.Down = math.lerp(c.Down, s.Act == Drives.Graze || s.Act == Drives.Drink ? 1 : 0, math.min(1, 1.5f * Dt));
                float stride = math.lerp(rig.Stride, rig.RunStride, CreaturePose.Run(ref rig, c.Move));
                c.Phase = math.frac(c.Phase + c.Move * c.WalkSpeed / stride * Dt);
                c.Clock += Dt;
            }

            void Show(DynamicBuffer<CreatureLink> links, int from, int count, bool on)
            {
                if (Gpu) return;
                for (int i = from; i < from + count; i++) Mmi.SetComponentEnabled(links[i].Value, on);
            }

            void Execute(Entity e, [EntityIndexInQuery] int index, ref CreatureRoot c, in DynamicBuffer<CreatureLink> links)
            {
                ref var rig = ref c.Rig.Value;
                bool present = Present && States.HasComponent(c.Ghost);
                if (present) Follow(ref c, ref rig, States[c.Ghost]);
                c.Wake = math.min(1, c.Wake + Dt / Detail.CrossfadeSeconds);
                float gy = Ground.HeightAt(c.Position.x, c.Position.z);
                float3 centre = new float3(c.Position.x, gy + rig.Stand, c.Position.z);
                float q = math.max(View.Quality, 0.1f);
                float px = View.ScreenSize(rig.Length, centre);
                var d = View.Decide(rig.Rule, rig.Length, centre, c.Decision);
                c.Decision = d;
                float far = rig.FadePx / q;
                bool visible = present && px >= far;
                c.Visible = visible;
                int half = links.Length / 2;
                float low = rig.LowPx / q;
                float blend = d.Geometry == 0 ? 1 - math.round(Detail.Crossfade(px, low, 1.5f * low) * 16f) / 16f : 1;
                bool highOn = visible && blend < 1, lowOn = visible && blend > 0;
                if (highOn != c.HighOn) { c.HighOn = highOn; Show(links, 0, half, highOn); }
                if (lowOn != c.LowOn) { c.LowOn = lowOn; Show(links, half, half, lowOn); }
                if (!visible) return;
                c.Shadow = d.Casts;
                if (Gpu)
                {
                    if (!c.Shadow && Frustum.Outside(centre, rig.Length)) return;
                    var fur = Furs[e];
                    FurOut[3 * index] = fur.Base;
                    FurOut[3 * index + 1] = fur.Accent;
                    FurOut[3 * index + 2] = fur.Dark;
                }
                float fadeX = math.round(Detail.Crossfade(px, far, 1.5f * far) * 16f) / 16f;
                float wake = Hold.Override != 0 ? 1 : Detail.Crossfade(c.Wake, 0, 1);
                float highX = (1 - blend) * wake, lowX = blend >= 1 ? fadeX * wake : -blend * wake;
                float anim = d.Animation <= Detail.AnimDecimated ? Detail.Crossfade(px, rig.AnimPx / q, 1.5f * rig.AnimPx / q) : 0;
                float ik = d.Animation == Detail.AnimFull ? Detail.Crossfade(px, rig.IkPx / q, 1.5f * rig.IkPx / q) : 0;

                float2 fw = new float2(math.sin(c.Heading), math.cos(c.Heading)), side = new float2(fw.y, -fw.x) * rig.Track;
                float2 at = c.Position.xz, fore = at + fw * rig.Fore, hind = at + fw * rig.Hind;
                float fy = 0.5f * (Ground.HeightAt(fore.x + side.x, fore.y + side.y) + Ground.HeightAt(fore.x - side.x, fore.y - side.y));
                float hy = 0.5f * (Ground.HeightAt(hind.x + side.x, hind.y + side.y) + Ground.HeightAt(hind.x - side.x, hind.y - side.y));
                float span = math.max(rig.Fore - rig.Hind, 0.1f);
                gy = hy + (fy - hy) * (-rig.Hind / span);
                float pitch = math.clamp(math.atan2(fy - hy, span), -0.5f, 0.5f);
                c.Pitch = Hold.Override != 0 ? pitch : math.lerp(c.Pitch, pitch, math.min(1, 4 * Dt));

                float run = CreaturePose.Run(ref rig, c.Move) * anim;
                float move = math.min(c.Move, 1) * anim;
                float bob = rig.Stand * (move * 0.02f * math.cos(4 * math.PI * c.Phase) + run * 0.07f * math.sin(2 * math.PI * c.Phase));
                float rock = run * 0.12f * math.cos(2 * math.PI * c.Phase);
                float3 rootPos = new float3(c.Position.x, gy + rig.Stand - c.Sink + bob, c.Position.z);
                quaternion rootRot = math.mul(quaternion.RotateY(c.Heading), quaternion.RotateX(-c.Pitch + rock));

                int n = rig.Segments.Length, parts = rig.Parts.Length;
                bool fits = n <= SegCap && parts <= PartCap;
                var pos = Scratch(Pos, SegCap, n, fits);
                var rot = Scratch(Rot, SegCap, n, fits);
                var floor = Scratch(Floor, SegCap, n, fits);
                float sink = 0;
                if (anim <= 0 && ik <= 0)
                {
                    for (int s = 0; s < n; s++)
                    {
                        pos[s] = rootPos + math.mul(rootRot, rig.RestPos[s]);
                        rot[s] = math.mul(rootRot, rig.RestRot[s]);
                        floor[s] = gy;
                    }
                }
                else
                {
                    var end = Scratch(End, PartCap, parts, fits);
                    var endRot = Scratch(EndRot, PartCap, parts, fits);
                    CreaturePose.Solve(ref rig, rootPos, rootRot, c.Phase, move, run, c.Alert * anim, c.Down * anim, c.Clock, anim, pos, rot, end, endRot);
                    sink = Legs(ref rig, rootPos, rootRot, gy, c.Sink, ik, pos, rot, end, floor);
                }
                c.Sink = Hold.Override != 0 ? sink : math.lerp(c.Sink, sink, math.min(1, 3 * Dt));

                float contact = 0.5f * rig.Stand;
                var trunkAt = rootPos + math.mul(rootRot, new float3(0, 0, -0.5f * rig.Length + rig.Rear));
                float shadow = c.Shadow ? 1 : 0;
                for (int tier = 0; tier < 2; tier++)
                {
                    if (tier == 0 ? !highOn : !lowOn) continue;
                    int b = tier * half;
                    float x = tier == 0 ? highX : lowX;
                    Write(links[b], float4x4.TRS(trunkAt, rootRot, links[b].Scale), new float4(x, shadow, gy, contact), index);
                    for (int s = 0; s < n && s + 1 < half; s++)
                        Write(links[b + s + 1], float4x4.TRS(pos[s], rot[s], links[b + s + 1].Scale), new float4(x, shadow, floor[s], contact), index);
                }
            }

            float Legs(ref CreatureRig rig, float3 rootPos, quaternion rootRot, float gy, float oldSink, float ik,
                NativeArray<float3> pos, NativeArray<quaternion> rot, NativeArray<float3> end, NativeArray<float> floor)
            {
                if (ik <= 0)
                {
                    for (int s = 0; s < floor.Length; s++) floor[s] = gy;
                    return 0;
                }
                var unrot = math.inverse(rootRot);
                float drop = 0;
                for (int i = 0; i < rig.Parts.Length; i++)
                {
                    ref var part = ref rig.Parts[i];
                    for (int j = 0; j < part.Count; j++) floor[part.First + j] = gy;
                    if (part.Role != CreaturePartDef.Leg || part.Count < 2) continue;
                    float3 foot = end[i];
                    float lift = math.mul(unrot, foot - rootPos).y + rig.Stand;
                    float g = Ground.HeightAt(foot.x, foot.z);
                    drop = math.max(drop, foot.y + oldSink - lift - g);
                    float shift = math.clamp(g + lift - foot.y, -0.45f * part.Reach, 0.45f * part.Reach) * ik;
                    if (math.abs(shift) > 1e-4f) CreaturePose.Reach(ref part, rootRot, foot + new float3(0, shift, 0), pos, rot, foot);
                    for (int j = 0; j < part.Count - 1; j++) floor[part.First + j] = Ground.HeightAt(pos[part.First + j].x, pos[part.First + j].z);
                    floor[part.First + part.Count - 1] = g;
                }
                return math.clamp(drop * ik, 0, 0.3f * rig.Stand);
            }

            void Write(CreatureLink link, float4x4 m, float4 f, int index)
            {
                if (Gpu)
                {
                    Out.AddNoResize(new CreatureGpu
                    {
                        R0 = new float4(m.c0.x, m.c1.x, m.c2.x, m.c3.x), R1 = new float4(m.c0.y, m.c1.y, m.c2.y, m.c3.y), R2 = new float4(m.c0.z, m.c1.z, m.c2.z, m.c3.z),
                        Fade = f, Fur = index, Group = 2 * link.Mesh + (f.y > 0.5f ? 1 : 0),
                    });
                    return;
                }
                World[link.Value] = new LocalToWorld { Value = m };
                var old = Fade[link.Value].Value;
                if (math.any(math.abs(old - f) > 1e-3f)) Fade[link.Value] = new ObjectFade { Value = f };
            }
        }
    }
}
