using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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

        public static void Solve(ref CreatureRig rig, float3 rootPos, quaternion rootRot, float phase, float move, float clock,
            NativeArray<float3> pos, NativeArray<quaternion> rot, NativeArray<float3> end, NativeArray<quaternion> endRot)
        {
            for (int i = 0; i < rig.Parts.Length; i++)
            {
                ref var part = ref rig.Parts[i];
                float3 start;
                quaternion r;
                if (part.Parent < 0) { start = rootPos + math.mul(rootRot, part.Socket); r = math.mul(rootRot, part.Rest); }
                else { start = end[part.Parent] + math.mul(endRot[part.Parent], part.Socket); r = math.mul(endRot[part.Parent], part.Rest); }
                float p = math.frac(phase + part.Phase);
                float3 sway = Sway(part.Role, part.Side, clock);
                for (int j = 0; j < part.Count; j++)
                {
                    ref var seg = ref rig.Segments[part.First + j];
                    float3 a = math.lerp(seg.Idle, Cycle(seg.Forward, seg.Back, seg.Lift, p), move) + sway;
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

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureRoot>();
            state.RequireForUpdate<Ground>();
            world = state.GetComponentLookup<LocalToWorld>();
            fade = state.GetComponentLookup<ObjectFade>();
            mmi = state.GetComponentLookup<MaterialMeshInfo>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var view = DetailView.Current;
            if (view.PixelsPerMetre <= 0) return;
            world.Update(ref state);
            fade.Update(ref state);
            mmi.Update(ref state);
            state.Dependency = new AnimJob
            {
                Ground = SystemAPI.GetSingleton<Ground>(), View = view, Hold = CreatureView.Current, Dt = math.min(SystemAPI.Time.DeltaTime, 0.1f),
                World = world, Fade = fade, Mmi = mmi,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct AnimJob : IJobEntity
        {
            public Ground Ground;
            public DetailView View;
            public CreatureView Hold;
            public float Dt;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToWorld> World;
            [NativeDisableParallelForRestriction] public ComponentLookup<ObjectFade> Fade;
            [NativeDisableParallelForRestriction] public ComponentLookup<MaterialMeshInfo> Mmi;

            void Wander(ref CreatureRoot c, ref CreatureRig rig)
            {
                if (Hold.Override != 0)
                {
                    c.Move = Hold.Move;
                    c.Phase = Hold.Phase;
                    c.Speed = 0;
                    c.Clock = 0;
                    return;
                }
                c.Timer -= Dt;
                if (c.Timer <= 0)
                {
                    c.Walking = c.Random.NextFloat() < 0.6f;
                    c.Timer = c.Random.NextFloat(3, 9);
                    c.Turn = c.Random.NextFloat(-0.6f, 0.6f);
                }
                float2 fwd = new float2(math.sin(c.Heading), math.cos(c.Heading));
                float2 toHome = c.Home - c.Position.xz;
                float steer = math.clamp(0.02f * (fwd.y * toHome.x - fwd.x * toHome.y), -0.8f, 0.8f);
                c.Move = math.lerp(c.Move, c.Walking ? 1 : 0, math.min(1, 1.2f * Dt));
                c.Heading += (c.Turn * 0.4f + steer) * Dt * c.Move;
                c.Speed = c.Move * c.WalkSpeed;
                c.Position.xz += fwd * (c.Speed * Dt);
                c.Phase = math.frac(c.Phase + c.Speed / rig.Stride * Dt);
                c.Clock += Dt;
            }

            void Show(DynamicBuffer<CreatureLink> links, int from, int count, bool on)
            {
                for (int i = from; i < from + count; i++) Mmi.SetComponentEnabled(links[i].Value, on);
            }

            void Execute(ref CreatureRoot c, in DynamicBuffer<CreatureLink> links)
            {
                ref var rig = ref c.Rig.Value;
                Wander(ref c, ref rig);
                float gy = Ground.HeightAt(c.Position.x, c.Position.z);
                float3 centre = new float3(c.Position.x, gy + rig.Stand, c.Position.z);
                float q = math.max(View.Quality, 0.1f);
                float px = View.ScreenSize(rig.Length, centre);
                float far = rig.FadePx / q;
                bool visible = px >= far;
                c.Visible = visible;
                int half = links.Length / 2;
                float low = rig.LowPx / q;
                c.High = Detail.Holds(px, low, c.High);
                float blend = c.High ? 1 - math.round(Detail.Crossfade(px, low, 1.5f * low) * 16f) / 16f : 1;
                bool highOn = visible && blend < 1, lowOn = visible && blend > 0;
                if (highOn != c.HighOn) { c.HighOn = highOn; Show(links, 0, half, highOn); }
                if (lowOn != c.LowOn) { c.LowOn = lowOn; Show(links, half, half, lowOn); }
                if (!visible) return;
                c.Shadow = Detail.Holds(px, rig.ShadowPx / q, c.Shadow);
                float fadeX = math.round(Detail.Crossfade(px, far, 1.5f * far) * 16f) / 16f;
                float highX = 1 - blend, lowX = blend >= 1 ? fadeX : -blend;
                float anim = Detail.Crossfade(px, rig.AnimPx / q, 1.5f * rig.AnimPx / q);
                float ik = Detail.Crossfade(px, rig.IkPx / q, 1.5f * rig.IkPx / q);

                float3 fwd3 = new float3(math.sin(c.Heading), 0, math.cos(c.Heading));
                float3 halfLen = fwd3 * (0.5f * rig.Length);
                float front = Ground.HeightAt(c.Position.x + halfLen.x, c.Position.z + halfLen.z);
                float rear = Ground.HeightAt(c.Position.x - halfLen.x, c.Position.z - halfLen.z);
                float pitch = math.clamp(math.atan2(front - rear, rig.Length), -0.5f, 0.5f);
                c.Pitch = Hold.Override != 0 ? pitch : math.lerp(c.Pitch, pitch, math.min(1, 4 * Dt));

                float move = c.Move * anim;
                float bob = move * 0.02f * rig.Stand * math.cos(4 * math.PI * c.Phase);
                float3 rootPos = new float3(c.Position.x, gy + rig.Stand - c.Sink + bob, c.Position.z);
                quaternion rootRot = math.mul(quaternion.RotateY(c.Heading), quaternion.RotateX(-c.Pitch));

                int n = rig.Segments.Length, parts = rig.Parts.Length;
                var pos = new NativeArray<float3>(n, Allocator.Temp);
                var rot = new NativeArray<quaternion>(n, Allocator.Temp);
                var end = new NativeArray<float3>(parts, Allocator.Temp);
                var endRot = new NativeArray<quaternion>(parts, Allocator.Temp);
                var floor = new NativeArray<float>(n, Allocator.Temp);
                CreaturePose.Solve(ref rig, rootPos, rootRot, c.Phase, move, c.Clock, pos, rot, end, endRot);

                var unrot = math.inverse(rootRot);
                float drop = 0;
                for (int i = 0; i < parts; i++)
                {
                    ref var part = ref rig.Parts[i];
                    for (int j = 0; j < part.Count; j++) floor[part.First + j] = gy;
                    if (part.Role != CreaturePartDef.Leg || part.Count < 2) continue;
                    float3 foot = end[i];
                    float lift = math.mul(unrot, foot - rootPos).y + rig.Stand;
                    float g = Ground.HeightAt(foot.x, foot.z);
                    drop = math.max(drop, foot.y + c.Sink - lift - g);
                    for (int j = 0; j < part.Count; j++) floor[part.First + j] = g;
                    float shift = math.clamp(g + lift - foot.y, -0.45f * part.Reach, 0.45f * part.Reach) * ik;
                    if (math.abs(shift) > 1e-4f) CreaturePose.Reach(ref part, rootRot, foot + new float3(0, shift, 0), pos, rot, foot);
                }
                float sink = math.clamp(drop * ik, 0, 0.3f * rig.Stand);
                c.Sink = Hold.Override != 0 ? sink : math.lerp(c.Sink, sink, math.min(1, 3 * Dt));

                float contact = 0.5f * rig.Stand;
                var trunkAt = rootPos + math.mul(rootRot, new float3(0, 0, -0.5f * rig.Length + rig.Rear));
                float shadow = c.Shadow ? 1 : 0;
                for (int tier = 0; tier < 2; tier++)
                {
                    if (tier == 0 ? !highOn : !lowOn) continue;
                    int b = tier * half;
                    float x = tier == 0 ? highX : lowX;
                    Write(links[b], float4x4.TRS(trunkAt, rootRot, links[b].Scale), new float4(x, shadow, gy, contact));
                    for (int s = 0; s < n && s + 1 < half; s++)
                        Write(links[b + s + 1], float4x4.TRS(pos[s], rot[s], links[b + s + 1].Scale), new float4(x, shadow, floor[s], contact));
                }
            }

            void Write(CreatureLink link, float4x4 m, float4 f)
            {
                World[link.Value] = new LocalToWorld { Value = m };
                var old = Fade[link.Value].Value;
                if (math.any(math.abs(old - f) > 1e-3f)) Fade[link.Value] = new ObjectFade { Value = f };
            }
        }
    }
}
