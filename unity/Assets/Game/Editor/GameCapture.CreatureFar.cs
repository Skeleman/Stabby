using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float FarSimSeconds = 25f, FarAwaySeconds = 25f, WalkSpeed = 30f;
        const int DenseSites = 5;
        const float WakeFov = 15f;
        static readonly int[] FarHerdCounts = { 2000, 10000 };
        static readonly bool FarDeerOnly = System.Environment.GetEnvironmentVariable("STABBY_FAR_MIX") == "deer";

        static BlobAssetReference<CreaturePlanData> PlanOf(EntityManager em)
        {
            using var q = em.CreateEntityQuery(typeof(CreaturePlanBlob));
            return q.CalculateEntityCount() == 1 ? q.GetSingleton<CreaturePlanBlob>().Value : default;
        }

        static void SetPlan(Unity.Entities.World server, BlobAssetReference<CreaturePlanData> plan)
        {
            var em = server.EntityManager;
            em.CompleteAllTrackedJobs();
            using var q = em.CreateEntityQuery(typeof(CreaturePlanBlob));
            q.SetSingleton(new CreaturePlanBlob { Value = plan });
        }

        static bool PlanHerd(BlobAssetReference<CreaturePlanData> planRef, int herd, double t, out float2 centre, out int members, out int body)
        {
            ref var plan = ref planRef.Value;
            centre = 0;
            members = 0;
            body = 0;
            int site = herd >> 4;
            if (site < 0 || site + 1 >= plan.SiteStart.Length) return false;
            float2 offset = 0;
            HerdRoute route = default;
            for (int i = plan.SiteStart[site]; i < plan.SiteStart[site + 1]; i++)
            {
                ref var row = ref plan.Spawns[i];
                if (CreaturePlan.HerdOf(row.Id) != herd) continue;
                offset += row.At - new float2(row.Route.X0, row.Route.Z0);
                route = row.Route;
                body = row.Body;
                members++;
            }
            if (members == 0) return false;
            route.At(t, out float x, out float z, out _);
            centre = new float2(x, z) + offset / members;
            return true;
        }

        static bool PickFarHerd(BlobAssetReference<CreaturePlanData> planRef, float2 player, double now, float despawn, out int herd, out Vector3 eye, out int members)
        {
            ref var plan = ref planRef.Value;
            herd = -1;
            eye = default;
            members = 0;
            foreach (float limit in ReliefLimits)
            {
                float best = 0;
                int last = int.MinValue;
                for (int i = 0; i < plan.Spawns.Length; i++)
                {
                    int h = CreaturePlan.HerdOf(plan.Spawns[i].Id);
                    if (h == last) continue;
                    last = h;
                    if (!PlanHerd(planRef, h, now, out var p, out int n, out int body) || n < 2) continue;
                    float d = math.distance(p, player);
                    if (d < 1.5f * despawn || d > 6f * despawn) continue;
                    PlanHerd(planRef, h, now + FarSimSeconds, out var p1, out _, out _);
                    PlanHerd(planRef, h, now + 2 * FarSimSeconds + FarAwaySeconds, out var p2, out _, out _);
                    float score = math.distance(p, p1) + math.distance(p1, p2) - d / (6f * despawn);
                    if (score <= best) continue;
                    if (Relief(p) > limit || Relief(p1) > limit) continue;
                    float stand = plan.Wary[body].Alert * 1.4f;
                    if (EyeFor(planRef, p, stand, math.normalizesafe(p - player, new float2(0, 1)), out var e) > 0) continue;
                    best = score;
                    herd = h;
                    eye = e;
                    members = n;
                }
                if (herd >= 0) return true;
            }
            return false;
        }

        static void AimAt(Camera cam, Vector3 eye, float2 at)
        {
            var target = new Vector3(at.x, GroundHeight(at.x, at.y) + 0.9f, at.y);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        }

        static (int Count, float Mean, float Min, float Max, float Length, float LowPx, float FadePx) WakeOf(Unity.Entities.World view, int herd)
        {
            var em = view.EntityManager;
            em.CompleteAllTrackedJobs();
            using var q = em.CreateEntityQuery(typeof(CreatureRoot));
            using var roots = q.ToComponentDataArray<CreatureRoot>(Allocator.Temp);
            int n = 0;
            float sum = 0, lo = 1, hi = 0, length = 0, low = 0, fade = 0;
            foreach (var c in roots)
            {
                if (!em.HasComponent<CreatureState>(c.Ghost) || CreaturePlan.HerdOf(em.GetComponentData<CreatureState>(c.Ghost).Id) != herd) continue;
                n++;
                sum += c.Wake;
                lo = math.min(lo, c.Wake);
                hi = math.max(hi, c.Wake);
                ref var rig = ref c.Rig.Value;
                length = rig.Length;
                low = rig.LowPx;
                fade = rig.FadePx;
            }
            return (n, n > 0 ? sum / n : 0, lo, hi, length, low, fade);
        }

        static int BrainCount(EntityManager em, float2 player, float despawn, out int beyond, out int herds)
        {
            em.CompleteAllTrackedJobs();
            using var q = em.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain));
            using var brains = q.ToComponentDataArray<CreatureBrain>(Allocator.Temp);
            var seen = new HashSet<int>();
            beyond = 0;
            foreach (var b in brains)
            {
                seen.Add(b.Herd);
                if (math.distance(b.Home, player) > despawn) beyond++;
            }
            herds = seen.Count;
            return brains.Length;
        }

        static void ClearCreatures(Unity.Entities.World server)
        {
            var em = server.EntityManager;
            em.CompleteAllTrackedJobs();
            using var q = em.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain));
            em.DestroyEntity(q);
            var host = server.GetExistingSystem<CreatureHostSystem>();
            if (host != SystemHandle.Null) server.Unmanaged.GetUnsafeSystemRef<CreatureHostSystem>(host).Herds.Clear();
        }

        static float2 DenseSpot(BlobAssetReference<CreaturePlanData> planRef, float2 home, float2 dir, float despawn, float siteMeters, float extent)
        {
            var ground = new CreatureGround { Plan = planRef };
            float2 best = math.clamp(home + dir * 8 * despawn, 3 * siteMeters, extent - 3 * siteMeters);
            int bestOpen = -1;
            for (int a = 0; a < 8; a++)
            {
                float ang = a * math.PI / 4;
                float2 d = new float2(dir.x * math.cos(ang) - dir.y * math.sin(ang), dir.x * math.sin(ang) + dir.y * math.cos(ang));
                for (int f = 8; f >= 4; f--)
                {
                    float2 c = math.clamp(home + d * f * despawn, 3 * siteMeters, extent - 3 * siteMeters);
                    int open = 0;
                    for (int z = -DenseSites / 2; z <= DenseSites / 2; z++)
                        for (int x = -DenseSites / 2; x <= DenseSites / 2; x++)
                            for (int q = 0; q < 4; q++)
                                if (!ground.Blocked(c.x + (x + 0.25f + 0.5f * (q & 1)) * siteMeters, c.y + (z + 0.25f + 0.5f * (q >> 1)) * siteMeters)) open++;
                    if (open > bestOpen) { bestOpen = open; best = c; }
                }
            }
            return best;
        }

        static BlobAssetReference<CreaturePlanData> SynthPlan(BlobAssetReference<CreaturePlanData> realRef, int herds, float2 dense, out int made, out int rowsMade, out int denseMade)
        {
            ref var real = ref realRef.Value;
            var byBody = new List<List<int2>>();
            for (int i = 0; i < real.Spawns.Length;)
            {
                int h = CreaturePlan.HerdOf(real.Spawns[i].Id), j = i, body = real.Spawns[i].Body;
                while (j < real.Spawns.Length && CreaturePlan.HerdOf(real.Spawns[j].Id) == h) j++;
                while (byBody.Count <= body) byBody.Add(new List<int2>());
                byBody[body].Add(new int2(i, j - i));
                i = j;
            }
            var kinds = new List<List<int2>>();
            foreach (var l in byBody) if (l.Count > 0 && (!FarDeerOnly || kinds.Count == 0)) kinds.Add(l);
            int target = 0;
            for (int i = 0; i < herds; i++) target += byBody[0][i % byBody[0].Count].y;
            int sites = real.Across * real.Across;
            var perSite = new List<CreatureSpawnRow>[sites];
            var slots = new int[sites];
            var rng = new Unity.Mathematics.Random(0x9E3779B9u);
            var ground = new CreatureGround { Plan = realRef };
            made = 0;
            rowsMade = 0;
            denseMade = 0;
            int dx = (int)(dense.x / real.SiteMeters), dz = (int)(dense.y / real.SiteMeters);
            for (int tries = 0; rowsMade < target && tries < herds * 40; tries++)
            {
                bool packing = tries < DenseSites * DenseSites * CreaturePlan.MaxBodies * 4;
                int site = packing
                    ? math.clamp(dz + rng.NextInt(-DenseSites / 2, DenseSites / 2 + 1), 0, real.Across - 1) * real.Across + math.clamp(dx + rng.NextInt(-DenseSites / 2, DenseSites / 2 + 1), 0, real.Across - 1)
                    : rng.NextInt(sites);
                if (slots[site] >= CreaturePlan.MaxBodies) continue;
                float2 c = new float2(site % real.Across + rng.NextFloat(), site / real.Across + rng.NextFloat()) * real.SiteMeters;
                if (ground.Blocked(c.x, c.y)) continue;
                var kind = kinds[made % kinds.Count];
                var t = kind[made / kinds.Count % kind.Count];
                float2 shift = c - new float2(real.Spawns[t.x].Route.X0, real.Spawns[t.x].Route.Z0);
                if (perSite[site] == null) perSite[site] = new List<CreatureSpawnRow>();
                for (int k = 0; k < t.y; k++)
                {
                    var row = real.Spawns[t.x + k];
                    row.Id = site << 8 | slots[site] << 4 | k;
                    row.At += shift;
                    for (int w = 0; w < HerdRoute.Points; w++) row.Route.Set(w, row.Route.X(w) + shift.x, row.Route.Z(w) + shift.y);
                    row.Needs.GrazeX += shift.x; row.Needs.GrazeZ += shift.y; row.Needs.BedX += shift.x; row.Needs.BedZ += shift.y;
                    row.Needs.Has &= unchecked((byte)~HerdNeeds.HasDrink);
                    perSite[site].Add(row);
                    rowsMade++;
                }
                if (packing) denseMade++;
                slots[site]++;
                made++;
            }

            var b = new BlobBuilder(Allocator.Temp);
            ref var root = ref b.ConstructRoot<CreaturePlanData>();
            root.Across = real.Across;
            root.SiteMeters = real.SiteMeters;
            root.Spread = real.Spread;
            root.MaxReach = real.MaxReach;
            root.RouteReach = real.RouteReach;
            root.Size = real.Size;
            root.CellMeters = real.CellMeters;
            var reach = b.Allocate(ref root.Reach, real.Reach.Length);
            var think = b.Allocate(ref root.Think, real.Think.Length);
            var length = b.Allocate(ref root.Length, real.Length.Length);
            var wary = b.Allocate(ref root.Wary, real.Wary.Length);
            var grade = b.Allocate(ref root.Grade, real.Grade.Length);
            for (int i = 0; i < grade.Length; i++) grade[i] = real.Grade[i];
            for (int i = 0; i < reach.Length; i++) reach[i] = real.Reach[i];
            for (int i = 0; i < think.Length; i++) think[i] = real.Think[i];
            for (int i = 0; i < length.Length; i++) length[i] = real.Length[i];
            for (int i = 0; i < wary.Length; i++) wary[i] = real.Wary[i];
            var drives = b.Allocate(ref root.Drives, real.Drives.Length);
            for (int i = 0; i < drives.Length; i++) drives[i] = real.Drives[i];
            var wet = b.Allocate(ref root.Wet, real.Wet.Length);
            for (int i = 0; i < wet.Length; i++) wet[i] = real.Wet[i];
            var start = b.Allocate(ref root.SiteStart, sites + 1);
            var rows = b.Allocate(ref root.Spawns, rowsMade);
            int at = 0;
            for (int s = 0; s < sites; s++)
            {
                start[s] = at;
                if (perSite[s] == null) continue;
                foreach (var row in perSite[s]) rows[at++] = row;
            }
            start[sites] = at;
            var blob = b.CreateBlobAssetReference<CreaturePlanData>(Allocator.Persistent);
            b.Dispose();
            return blob;
        }


        sealed class FarFrames
        {
            public double Host, Build, Main, Dt, WakeDt, QuietDt;
            public int Peak, Spawned, Frames, Slow, SlowBuild, Roots, BuildIn, BuildOut, PeakIn, PeakOut;
            readonly HashSet<int> seen = new HashSet<int>();

            public void Reset(EntityManager em, EntityManager view)
            {
                Host = Build = Main = Dt = WakeDt = QuietDt = 0;
                Peak = Spawned = Frames = Slow = SlowBuild = BuildIn = BuildOut = PeakIn = PeakOut = 0;
                Ids(em, seen, out _);
                Roots = RootCount(view);
            }

            public void Add(EntityManager em, EntityManager view, ProfilerRecorder host, ProfilerRecorder build, ProfilerRecorder main)
            {
                Host = math.max(Host, host.LastValue / 1e6);
                double b = build.LastValue / 1e6;
                int roots = RootCount(view), delta = roots - Roots;
                Roots = roots;
                int rin = math.max(delta, 0), rout = math.max(-delta, 0);
                PeakIn = math.max(PeakIn, rin);
                PeakOut = math.max(PeakOut, rout);
                if (b > Build) { Build = b; BuildIn = rin; BuildOut = rout; }
                if (b > 4) SlowBuild++;
                Main = math.max(Main, main.LastValue / 1e6);
                double dt = Time.unscaledDeltaTime * 1000.0;
                Dt = math.max(Dt, dt);
                if (dt > 33) Slow++;
                Frames++;
                Ids(em, seen, out int fresh);
                Peak = math.max(Peak, fresh);
                Spawned += fresh;
                if (fresh > 0 || delta != 0) WakeDt = math.max(WakeDt, dt);
                else QuietDt = math.max(QuietDt, dt);
            }

            public string Text => $"peak {Peak} server spawns in one frame (budget {CreatureHostSystem.SpawnBudget}), {Spawned} spawned over {Frames} frames, client roots peak +{PeakIn}/-{PeakOut} per frame; {Slow} frames over 33 ms, worst dt {WakeDt:F1} ms on frames with creature arrivals or departures vs {QuietDt:F1} ms on frames without; worst host {Host:F3} ms; worst client build {Build:F3} ms (that frame +{BuildIn}/-{BuildOut} roots), {SlowBuild} build frames over 4 ms; worst main thread {Main:F2} ms";
        }

        static void Ids(EntityManager em, HashSet<int> seen, out int fresh)
        {
            em.CompleteAllTrackedJobs();
            using var q = em.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain));
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            fresh = 0;
            var now = new HashSet<int>();
            foreach (var s in states)
            {
                now.Add(s.Id);
                if (!seen.Contains(s.Id)) fresh++;
            }
            seen.Clear();
            seen.UnionWith(now);
        }

        static int RootCount(EntityManager view)
        {
            using var q = view.CreateEntityQuery(typeof(CreatureRoot));
            return q.CalculateEntityCount();
        }

        static readonly float[] EyeSteps = { 1f, 1.25f, 1.5f, 1.8f }, RangeSteps = { 1f }, ReliefLimits = { 8f, 14f, 24f, float.MaxValue };

        static float Relief(float2 p)
        {
            float g = GroundHeight(p.x, p.y), top = g;
            for (int r = 1; r <= 3; r++)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * math.PI / 8;
                    top = Mathf.Max(top, GroundHeight(p.x + 20f * r * math.cos(a), p.y + 20f * r * math.sin(a)));
                }
            return top - g;
        }

        static float FirstHit(Vector3 eye, Vector3 end)
        {
            for (int i = 1; i <= 32; i++)
            {
                var q = Vector3.Lerp(eye, end, i / 32f);
                if (GroundHeight(q.x, q.z) > q.y - 0.25f) return i / 32f;
            }
            return 2f;
        }

        static int EyeFor(BlobAssetReference<CreaturePlanData> planRef, float2 p, float dist, float2 prefer, out Vector3 eye, float fov = WakeFov, float[] steps = null, int lifts = 2)
        {
            var ground = new CreatureGround { Plan = planRef };
            var field = ViewField();
            var target = new Vector3(p.x, GroundHeight(p.x, p.y) + 0.9f, p.y);
            int best = int.MaxValue;
            eye = default;
            foreach (float step in steps ?? EyeSteps)
                for (int lift = 0; lift < lifts; lift++)
                    for (int k = 0; k < 16; k++)
                    {
                        float a = (k >> 1) * math.PI / 8 * ((k & 1) == 0 ? 1 : -1);
                        float2 d = new float2(prefer.x * math.cos(a) - prefer.y * math.sin(a), prefer.x * math.sin(a) + prefer.y * math.cos(a));
                        float2 at = p + d * dist * step;
                        if (ground.Blocked(at.x, at.y)) continue;
                        var e = new Vector3(at.x, 0, at.y);
                        e.y = Mathf.Max(GroundHeight(at.x, at.y) + 3f + 5f * lift, target.y + 4f + 4f * lift);
                        int b = Blockers(field, e, target, fov);
                        if (b >= best) continue;
                        best = b;
                        eye = e;
                        if (b == 0) return 0;
                    }
            return best;
        }

        static ObjectField ViewField()
        {
            foreach (var w in Unity.Entities.World.All)
                if (w.Name == GameSettings.ViewClient) return w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
            return null;
        }

        static readonly List<ObjectColumn> sightColumns = new List<ObjectColumn>();

        static float PartTop(ObjectDef def)
        {
            float top = 0;
            foreach (var part in def.Parts) top = Mathf.Max(top, part.OffsetY + part.ScaleY);
            return top;
        }

        static int Blockers(ObjectField field, Vector3 eye, Vector3 target, float fov)
        {
            var flat = new Vector3(target.x - eye.x, 0, target.z - eye.z);
            float dist = flat.magnitude;
            var right = Vector3.Cross(Vector3.up, flat / Mathf.Max(dist, 0.01f));
            float vhalf = Mathf.Tan(0.5f * fov * Mathf.Deg2Rad) * (target - eye).magnitude, half = vhalf * 16f / 9f;
            var view = (target - eye).normalized;
            var up = Vector3.Cross(view, right);
            int blocked = 0;
            for (int v = -1; v <= 1; v++)
                for (int s = -2; s <= 2; s++)
                    if (FirstHit(eye, target + right * (0.45f * half * s) + up * (0.6f * vhalf * v)) < 0.8f) blocked += s == 0 && v == 0 ? 4 : 1;
            if (field == null) return blocked;
            float span = field.ChunkMeters, margin = 16f;
            int n = field.ChunksAcross;
            float minX = Mathf.Min(eye.x, target.x) - margin, maxX = Mathf.Max(eye.x, target.x) + margin;
            float minZ = Mathf.Min(eye.z, target.z) - margin, maxZ = Mathf.Max(eye.z, target.z) + margin;
            int cx0 = Mathf.Max(0, Mathf.FloorToInt(minX / span)), cx1 = Mathf.Min(n - 1, Mathf.FloorToInt(maxX / span));
            int cz0 = Mathf.Max(0, Mathf.FloorToInt(minZ / span)), cz1 = Mathf.Min(n - 1, Mathf.FloorToInt(maxZ / span));
            var a = new Vector2(eye.x, eye.z);
            var ab = new Vector2(flat.x, flat.z);
            float len2 = Mathf.Max(ab.sqrMagnitude, 0.0001f);
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                    foreach (var o in field.Chunk(cx, cz))
                    {
                        if (o.X < minX || o.X > maxX || o.Z < minZ || o.Z > maxZ) continue;
                        float r = field.Footprint(o.Def) * o.Size, top = o.Y + PartTop(field.Library.Objects[o.Def]) * o.Size;
                        sightColumns.Clear();
                        field.ColumnsOf(o, sightColumns);
                        foreach (var c in sightColumns) top = Mathf.Max(top, c.Top);
                        var pc = new Vector2(o.X, o.Z);
                        float t = Mathf.Clamp01(Vector2.Dot(pc - a, ab) / len2);
                        if (t > 0.97f) continue;
                        float y = Mathf.Lerp(eye.y, target.y, t);
                        if (Vector2.Distance(a + ab * t, pc) > r + 0.5f || y < o.Y - 0.5f || y > top + 0.5f) continue;
                        blocked += 2;
                    }
            return blocked;
        }

        static readonly bool SlopeOnly = System.Environment.GetEnvironmentVariable("STABBY_FAR_SLOPE_ONLY") == "1";

        static float GradeAt(float2 p, float r, out float2 down)
        {
            float gx = GroundHeight(p.x + r, p.y) - GroundHeight(p.x - r, p.y), gz = GroundHeight(p.x, p.y + r) - GroundHeight(p.x, p.y - r);
            down = -math.normalizesafe(new float2(gx, gz), new float2(0, 1));
            return math.length(new float2(gx, gz)) / (2 * r);
        }

        static float FootGap(CreatureRoot c, out float worstCentre)
        {
            ref var rig = ref c.Rig.Value;
            int n = rig.Segments.Length, parts = rig.Parts.Length;
            using var pos = new NativeArray<float3>(n, Allocator.Temp);
            using var rot = new NativeArray<quaternion>(n, Allocator.Temp);
            using var end = new NativeArray<float3>(parts, Allocator.Temp);
            using var endRot = new NativeArray<quaternion>(parts, Allocator.Temp);
            float2 fw = new float2(math.sin(c.Heading), math.cos(c.Heading)), side = new float2(fw.y, -fw.x) * rig.Track;
            float2 at = c.Position.xz, fore = at + fw * rig.Fore, hind = at + fw * rig.Hind;
            float fy = 0.5f * (GroundHeight(fore.x + side.x, fore.y + side.y) + GroundHeight(fore.x - side.x, fore.y - side.y));
            float hy = 0.5f * (GroundHeight(hind.x + side.x, hind.y + side.y) + GroundHeight(hind.x - side.x, hind.y - side.y));
            float span = math.max(rig.Fore - rig.Hind, 0.1f);
            float gy = hy + (fy - hy) * (-rig.Hind / span), centre = GroundHeight(at.x, at.y);
            float run = CreaturePose.Run(ref rig, c.Move), move = math.min(c.Move, 1);
            float bob = rig.Stand * (move * 0.02f * math.cos(4 * math.PI * c.Phase) + run * 0.07f * math.sin(2 * math.PI * c.Phase));
            float rock = run * 0.12f * math.cos(2 * math.PI * c.Phase);
            float worst = 0;
            worstCentre = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                float baseY = pass == 0 ? gy : centre, pitch = pass == 0 ? c.Pitch : 0, sink = pass == 0 ? c.Sink : 0;
                var rootPos = new float3(at.x, baseY + rig.Stand - sink + bob, at.y);
                var rootRot = math.mul(quaternion.RotateY(c.Heading), quaternion.RotateX(-pitch + rock));
                CreaturePose.Solve(ref rig, rootPos, rootRot, c.Phase, move, run, c.Alert, c.Down, c.Clock, 1, pos, rot, end, endRot);
                var unrot = math.inverse(rootRot);
                for (int i = 0; i < parts; i++)
                {
                    ref var part = ref rig.Parts[i];
                    if (part.Role != CreaturePartDef.Leg || part.Count < 2) continue;
                    float3 foot = end[i];
                    float lift = math.mul(unrot, foot - rootPos).y + rig.Stand;
                    float need = GroundHeight(foot.x, foot.z) + lift - foot.y;
                    float miss = math.max(math.abs(need) - 0.45f * part.Reach, 0);
                    if (pass == 0) worst = math.max(worst, miss); else worstCentre = math.max(worstCentre, math.abs(need));
                }
            }
            return worst;
        }

        static System.Collections.IEnumerator SlopeShot(Camera cam, string dir, List<string> lines, BlobAssetReference<CreaturePlanData> planRef, float2 home)
        {
            var server = ServerWorld();
            var view = ViewWorld();
            double now = server.Time.ElapsedTime;
            int herd = -1, last = int.MinValue, body = 0, members = 0, rows = planRef.Value.Spawns.Length;
            float best = float.MaxValue, planGrade = 0;
            float2 at = home;
            for (int i = 0; i < rows; i++)
            {
                int h = CreaturePlan.HerdOf(planRef.Value.Spawns[i].Id);
                if (h == last) continue;
                last = h;
                if (!PlanHerd(planRef, h, now, out var p, out int n, out int b) || n < 2) continue;
                float grade = GradeAt(p, 6f, out _), d = math.distance(p, home);
                if (grade < 0.2f || grade > 0.45f || d >= best) continue;
                best = d;
                planGrade = grade;
                herd = h;
                at = p;
                body = b;
                members = n;
            }
            if (herd < 0) { lines.Add("slope shot: no herd on a moderate slope"); yield break; }
            var wary = planRef.Value.Wary[body];
            float2 toHome = math.normalizesafe(home - at, new float2(0, 1));
            float2 stand = at + toHome * math.min(0.8f * CreaturePlanSystem.SpawnReach, 1.5f * math.max(wary.Calm, wary.Alert));
            PlaceServerPlayers(server, new Vector2(stand.x, stand.y));
            cam.fieldOfView = HerdFov;
            int awake = 0;
            for (int k = 0; k < 3600 && awake < 240; k++)
            {
                PlanHerd(planRef, herd, server.Time.ElapsedTime, out var p, out _, out _);
                cam.transform.SetPositionAndRotation(new Vector3(p.x + 20f, GroundHeight(p.x + 20f, p.y) + 8f, p.y), Quaternion.LookRotation(new Vector3(-20f, -8f, 0)));
                yield return null;
                var w = WakeOf(view, herd);
                awake = w.Count == members && w.Min >= 1 && !Settling() ? awake + 1 : 0;
            }
            FreezeCreatures(true);
            for (int k = 0; k < 10; k++) yield return null;
            var em = view.EntityManager;
            em.CompleteAllTrackedJobs();
            float3 pos = 0;
            float heading = 0, steep = -1, stand3 = 1, length = 1;
            using (var q = em.CreateEntityQuery(typeof(CreatureRoot)))
            using (var roots = q.ToComponentDataArray<CreatureRoot>(Allocator.Temp))
                foreach (var c in roots)
                {
                    if (!c.Visible || !em.HasComponent<CreatureState>(c.Ghost) || CreaturePlan.HerdOf(em.GetComponentData<CreatureState>(c.Ghost).Id) != herd) continue;
                    float g = GradeAt(c.Position.xz, 1.5f, out _);
                    if (g > 0.6f || g <= steep) continue;
                    steep = g;
                    pos = c.Position;
                    heading = c.Heading;
                    stand3 = c.Rig.Value.Stand;
                    length = c.Rig.Value.Length;
                }
            if (steep < 0) { lines.Add($"slope shot: herd {herd} woke but no visible member"); FreezeCreatures(false); yield break; }
            float gapWorst = 0, gapCentre = 0;
            int gapCount = 0;
            using (var q = em.CreateEntityQuery(typeof(CreatureRoot)))
            using (var roots = q.ToComponentDataArray<CreatureRoot>(Allocator.Temp))
                foreach (var c in roots)
                {
                    if (!c.Visible || !em.HasComponent<CreatureState>(c.Ghost) || CreaturePlan.HerdOf(em.GetComponentData<CreatureState>(c.Ghost).Id) != herd) continue;
                    gapWorst = math.max(gapWorst, FootGap(c, out float centreGap));
                    gapCentre = math.max(gapCentre, centreGap);
                    gapCount++;
                }
            lines.Add($"slope feet: {gapCount} visible members, worst hoof left off or in the ground after leg IK {100 * gapWorst:F1} cm (single centre sample with no pitch would need {100 * gapCentre:F1} cm of leg correction)");
            GradeAt(pos.xz, 1.5f, out float2 down);
            float2 side = math.normalizesafe(new float2(down.y, -down.x), new float2(math.cos(heading), -math.sin(heading)));
            float2 eyeXZ = pos.xz + side * (3f * length);
            float gy = GroundHeight(pos.x, pos.z);
            var target = new Vector3(pos.x, gy + 0.4f * stand3, pos.z);
            var eye = new Vector3(eyeXZ.x, math.max(GroundHeight(eyeXZ.x, eyeXZ.y), gy) + 0.3f * stand3, eyeXZ.y);
            for (int k = 0; k < 60 || Settling(); k++)
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                yield return null;
            }
            Shot(cam, null, "warmup");
            Shot(cam, dir, "creature-slope");
            lines.Add($"slope shot: herd {herd} ({members} members) {math.distance(at, home):F0} m from spawn, plan ground grade {math.degrees(math.atan(planGrade)):F0} deg; framed member on {math.degrees(math.atan(steep)):F0} deg local slope, camera {math.distance(eyeXZ, pos.xz):F1} m along the contour at hoof height");
            FreezeCreatures(false);
        }

        static bool Settling() => Loading();

        static System.Collections.IEnumerator FarTour(Camera cam, string dir)
        {
            var lines = new List<string>();
            var server = ServerWorld();
            var view = ViewWorld();
            if (server == null || view == null) { GameStatus.Lines["~far"] = "far: no worlds"; yield break; }
            var sem = server.EntityManager;
            var planRef = PlanOf(sem);
            for (int k = 0; k < 1800 && !planRef.IsCreated; k++) { yield return null; planRef = PlanOf(sem); }
            if (!planRef.IsCreated || !FirstPlayer(sem, out float2 home)) { GameStatus.Lines["~far"] = "far: no plan or player"; yield break; }
            if (SlopeOnly)
            {
                GameStatus.HoldCamera = true;
                CreatureView.Current = default;
                FreezeCreatures(false);
                var only = SlopeShot(cam, dir, lines, planRef, home);
                while (only.MoveNext()) yield return only.Current;
                System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "creature-slope.txt"), lines);
                yield break;
            }
            var host = server.GetExistingSystem<CreatureHostSystem>();
            GameStatus.HoldCamera = true;
            CreatureView.Current = default;
            FreezeCreatures(false);
            float despawn = CreaturePlanSystem.DespawnReach, spawn = CreaturePlanSystem.SpawnReach;
            float routeReach = planRef.Value.RouteReach, siteMeters = planRef.Value.SiteMeters;
            int across = planRef.Value.Across;

            if (!PickFarHerd(planRef, home, server.Time.ElapsedTime, despawn, out int herd, out Vector3 eye, out int members))
            {
                GameStatus.Lines["~far"] = "far: no herd with a clear view";
                yield break;
            }
            PlanHerd(planRef, herd, server.Time.ElapsedTime, out var p0, out _, out int body);
            var wary = planRef.Value.Wary[body];
            var eyeFlat = new float2(eye.x, eye.z);
            float2 toEye = math.normalizesafe(eyeFlat - p0, new float2(0, 1));
            float standDist = math.min(0.8f * spawn, 1.5f * math.max(wary.Calm, wary.Alert));
            float2 stand = p0 + toEye * standDist, stage = p0 + toEye * (despawn + routeReach + 20f);
            PlaceServerPlayers(server, new Vector2(stage.x, stage.y));
            cam.fieldOfView = WakeFov;
            for (int k = 0; k < 120 || Settling(); k++)
            {
                PlanHerd(planRef, herd, server.Time.ElapsedTime, out var p, out _, out _);
                AimAt(cam, eye, p);
                yield return null;
            }
            HerdCentre(server, herd, out int liveBefore, out _);
            PlanHerd(planRef, herd, server.Time.ElapsedTime, out p0, out _, out _);
            lines.Add($"far herd {herd}: {members} members, plan centre {p0.x:F1},{p0.y:F1}, {math.distance(p0, home):F0} m from spawn; spawn reach {spawn:F0} m, despawn reach {despawn:F0} m, route reach {routeReach:F0} m; live while the player waited {math.distance(stage, p0):F0} m out: {liveBefore}");
            lines.Add($"camera {math.distance(p0, eyeFlat):F1} m from the herd; player walks in at {WalkSpeed:F0} m/s and stops {standDist:F0} m out (calm {wary.Calm:F0} m, alert {wary.Alert:F0} m)");

            using var hostRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerName(server, typeof(CreatureHostSystem)), 4);
            using var buildRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerName(view, typeof(CreatureBuildSystem)), 4);
            using var mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
            var fr = new FarFrames();
            for (int k = 0; k < 4; k++) yield return null;
            fr.Reset(sem, view.EntityManager);
            bool shot = false;
            float wakeErr = -1, eyeDist = math.distance(p0, eyeFlat);
            int moves = 0;
            float2 player = stage;
            int settled = 0;
            for (int f = 0; f < 1800 && settled < 120; f++)
            {
                float step = WalkSpeed * Time.deltaTime;
                float2 left = stand - player;
                player = math.length(left) <= step ? stand : player + math.normalize(left) * step;
                PlaceServerPlayers(server, new Vector2(player.x, player.y));
                yield return null;
                fr.Add(sem, view.EntityManager, hostRec, buildRec, mainRec);
                PlanHerd(planRef, herd, server.Time.ElapsedTime, out var p, out _, out _);
                if (f % 15 == 0 && Blockers(ViewField(), eye, new Vector3(p.x, GroundHeight(p.x, p.y) + 0.9f, p.y), WakeFov) > 0 && EyeFor(planRef, p, eyeDist, math.normalizesafe(new float2(eye.x, eye.z) - p, toEye), out var e2) == 0) { eye = e2; moves++; }
                AimAt(cam, eye, p);
                if (wakeErr < 0)
                {
                    var c = HerdCentre(server, herd, out int live, out _);
                    if (live == members) wakeErr = math.distance(new float2(c.x, c.y), p);
                }
                var wake = WakeOf(view, herd);
                if (!shot && wake.Count == members && wake.Mean >= 0.4f && wake.Mean <= 0.7f)
                {
                    int wakeBlock = Blockers(ViewField(), eye, new Vector3(p.x, GroundHeight(p.x, p.y) + 0.9f, p.y), WakeFov);
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, "creature-far-wake");
                    shot = true;
                    lines.Add($"wake shot: camera {math.distance(new float2(eye.x, eye.z), p):F0} m from the herd at {WakeFov:F0} degree FOV (moved {moves} times to keep a clear sightline, {wakeBlock} terrain or feature blockers on the shot), player {math.distance(player, p):F0} m from the herd, {wake.Count} members on the client, wake mean {wake.Mean:F2} (min {wake.Min:F2}, max {wake.Max:F2})");
                }
                if (player.Equals(stand) && wakeErr >= 0 && wake.Count == members && wake.Min >= 1) settled++;
            }
            if (!shot) lines.Add("wake shot missed: no frame caught the crossfade between 0.4 and 0.7");
            lines.Add($"real plan walk-in wake: {fr.Text}");
            lines.Add($"agreement on wake: server centroid {wakeErr:F3} m from the plan");

            double t0 = server.Time.ElapsedTime;
            float simMax = 0, simEnd = 0;
            var modes = new HashSet<string>();
            while (server.Time.ElapsedTime - t0 < FarSimSeconds)
            {
                yield return null;
                PlanHerd(planRef, herd, server.Time.ElapsedTime, out var p, out _, out _);
                AimAt(cam, eye, p);
                var c = HerdCentre(server, herd, out int live, out _);
                if (HerdMindOf(server, herd, out var m)) modes.Add(m.Mode.ToString());
                if (live != members) continue;
                simEnd = math.distance(new float2(c.x, c.y), p);
                simMax = math.max(simMax, simEnd);
            }
            PlanHerd(planRef, herd, t0, out var ps0, out _, out _);
            PlanHerd(planRef, herd, server.Time.ElapsedTime, out var ps1, out _, out _);
            lines.Add($"agreement while simulated: over {FarSimSeconds:F0} s the plan moved {math.distance(ps0, ps1):F2} m; server centroid within {simMax:F2} m of the plan (end {simEnd:F2} m; members wander inside the {CreatureHostSystem.Follow:F0} m leash), herd modes {string.Join("/", modes)}");

            cam.fieldOfView = HerdFov;
            for (int k = 0; k < 4; k++) yield return null;
            var wakeInfo = WakeOf(view, herd);
            float q = math.max(DetailView.Current.Quality, 0.1f);
            float px = 0.5f * (wakeInfo.LowPx + wakeInfo.FadePx) / q;
            float range = Detail.Reach(math.max(wakeInfo.Length, 0.5f), math.max(px, 1f), DetailView.Current.PixelsPerMetre);
            bool clear = EyeFor(planRef, ps1, range, toEye, out Vector3 rangeEye, HerdFov, RangeSteps, 3) == 0;
            for (int k = 0; k < 120 || Settling(); k++)
            {
                PlanHerd(planRef, herd, server.Time.ElapsedTime, out var p, out _, out _);
                AimAt(cam, rangeEye, p);
                yield return null;
            }
            Shot(cam, null, "warmup");
            Shot(cam, dir, "creature-far-range");
            HerdCentre(server, herd, out int liveAtRange, out _);
            lines.Add($"range shot: camera {range:F0} m from the herd ({px:F1} px target between low {wakeInfo.LowPx / q:F1} and fade {wakeInfo.FadePx / q:F1} px), clear view {clear} from {rangeEye.y - GroundHeight(ps1.x, ps1.y):F0} m above the herd, {liveAtRange} live");

            PlanHerd(planRef, herd, server.Time.ElapsedTime, out var pLeave, out _, out _);
            float2 away = pLeave + toEye * (3 * despawn);
            PlaceServerPlayers(server, new Vector2(away.x, away.y));
            int goneFrames = 0;
            for (; goneFrames < 900; goneFrames++)
            {
                yield return null;
                HerdCentre(server, herd, out int live, out _);
                if (live == 0 && !HerdMindOf(server, herd, out _)) break;
            }
            double tLeft = server.Time.ElapsedTime;
            PlanHerd(planRef, herd, tLeft, out var pGone, out _, out _);
            while (server.Time.ElapsedTime - tLeft < FarAwaySeconds) yield return null;
            float2 back = away;
            float backErr = -1;
            for (int f = 0; f < 9000 && backErr < 0; f++)
            {
                float step = WalkSpeed * Time.deltaTime;
                float2 left = stand - back;
                back = math.length(left) <= step ? stand : back + math.normalize(left) * step;
                PlaceServerPlayers(server, new Vector2(back.x, back.y));
                yield return null;
                var c = HerdCentre(server, herd, out int live, out _);
                if (live != members) continue;
                PlanHerd(planRef, herd, server.Time.ElapsedTime, out var p, out _, out _);
                backErr = math.distance(new float2(c.x, c.y), p);
                lines.Add($"agreement after walking away: herd and mind gone {goneFrames + 1} frames after the player left, plan moved {math.distance(pGone, p):F2} m during {server.Time.ElapsedTime - tLeft:F1} s away, woken centroid {backErr:F3} m from the plan as the player walked back within {math.distance(back, p):F0} m");
            }
            if (backErr < 0) lines.Add("agreement after walking away: herd did not wake again");

            PlaceServerPlayers(server, new Vector2(home.x, home.y));
            for (int k = 0; k < 120; k++) yield return null;
            var systems = new[] { host };
            int realLive = BrainCount(sem, home, despawn, out int realBeyond, out int realHerds);
            var realTime = TimeBoth(server, systems);
            lines.Add($"real plan ({CreaturePlanSystem.Planned} creatures): at spawn {realLive} live in {realHerds} herds, {realBeyond} beyond despawn reach, host {realTime.Par:F3} ms parallel / {realTime.Single:F3} ms one thread");

            float extent = across * siteMeters;
            float2 mid = new float2(extent * 0.5f);
            float2 out2 = math.normalizesafe(mid - home, new float2(1, 0));
            float2 dense = DenseSpot(planRef, home, out2, despawn, siteMeters, extent);
            out2 = math.normalizesafe(dense - home, out2);
            float2 denseStage = dense - out2 * (despawn + routeReach + (DenseSites / 2 + 1) * siteMeters);
            foreach (int n in FarHerdCounts)
            {
                var synth = SynthPlan(planRef, n, dense, out int made, out int rowCount, out int denseMade);
                ClearCreatures(server);
                SetPlan(server, synth);
                PlaceServerPlayers(server, new Vector2(home.x, home.y));
                int stable = 0, last = -1;
                for (int k = 0; k < 900 && stable < 60; k++)
                {
                    yield return null;
                    int c = BrainCount(sem, home, despawn, out _, out _);
                    stable = c == last ? stable + 1 : 0;
                    last = c;
                }
                int live = BrainCount(sem, home, despawn, out int beyond, out int liveHerds);
                var t = TimeBoth(server, systems);
                lines.Add($"{(FarDeerOnly ? "deer only" : "mixed species")}: {made} plan herds ({rowCount} creatures, {denseMade} packed into {DenseSites}x{DenseSites} sites around {dense.x:F0},{dense.y:F0}): at spawn {live} live in {liveHerds} herds, {beyond} beyond despawn reach, host {t.Par:F3} ms parallel / {t.Single:F3} ms one thread");

                PlaceServerPlayers(server, new Vector2(denseStage.x, denseStage.y));
                var look = Quaternion.LookRotation(new Vector3(out2.x, -0.05f, out2.y));
                var camAt = new Vector3(denseStage.x, GroundHeight(denseStage.x, denseStage.y) + 3f, denseStage.y);
                for (int k = 0; k < 120 || Settling(); k++)
                {
                    cam.transform.SetPositionAndRotation(camAt, look);
                    yield return null;
                }
                int before = BrainCount(sem, denseStage, despawn, out _, out _);
                fr.Reset(sem, view.EntityManager);
                player = denseStage;
                stable = 0;
                last = -1;
                for (int f = 0; f < 20000 && stable < 120; f++)
                {
                    float step = WalkSpeed * Time.deltaTime;
                    float2 left = dense - player;
                    player = math.length(left) <= step ? dense : player + math.normalize(left) * step;
                    PlaceServerPlayers(server, new Vector2(player.x, player.y));
                    camAt = new Vector3(player.x, GroundHeight(player.x, player.y) + 3f, player.y);
                    cam.transform.SetPositionAndRotation(camAt, look);
                    yield return null;
                    fr.Add(sem, view.EntityManager, hostRec, buildRec, mainRec);
                    int c = BrainCount(sem, player, despawn, out _, out _);
                    stable = player.Equals(dense) && c == last ? stable + 1 : 0;
                    last = c;
                }
                int woke = BrainCount(sem, player, despawn, out int denseBeyond, out int denseHerds);
                var td = TimeBoth(server, systems);
                lines.Add($"  walk-in mass wake over {math.distance(denseStage, dense):F0} m into the cluster (site {siteMeters:F0} m, {before} live before, player {math.distance(player, dense):F0} m short at the end): {woke} live in {denseHerds} herds, {denseBeyond} beyond despawn reach; {fr.Text}");
                lines.Add($"  host with the cluster awake: {td.Par:F3} ms parallel / {td.Single:F3} ms one thread");

                ClearCreatures(server);
                SetPlan(server, planRef);
                PlaceServerPlayers(server, new Vector2(home.x, home.y));
                yield return null;
                sem.CompleteAllTrackedJobs();
                synth.Dispose();
                for (int k = 0; k < 60; k++) yield return null;
            }

            var slope = SlopeShot(cam, dir, lines, planRef, home);
            while (slope.MoveNext()) yield return slope.Current;
            for (int i = 0; i < lines.Count; i++) GameStatus.Lines[$"~far {i:D2}"] = lines[i];
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "creature-far.txt"), lines);
        }
    }
}
