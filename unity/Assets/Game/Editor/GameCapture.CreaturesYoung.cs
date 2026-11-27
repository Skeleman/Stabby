using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float YoungHour = 15.5f, YoungMoon = 0.5f, YoungAwayM = 70f, YoungEyeM = 17f, YoungCalmSeconds = 20f, YoungFleeSeconds = 8f, YoungFlatM = 12f, YoungFlatRise = 1.2f, YoungEyeRise = 4.5f;
        static readonly float[] YoungTurns = { 0f, -55f, 55f };

        static float Rise(float2 at, float r)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    float h = GroundHeight(at.x + i * r, at.y + j * r);
                    lo = math.min(lo, h);
                    hi = math.max(hi, h);
                }
            return hi - lo;
        }

        static bool PlanYoung(BlobAssetReference<CreaturePlanData> planRef, int body, float2 from, double t, out int young, out int herd)
        {
            ref var plan = ref planRef.Value;
            young = herd = int.MinValue;
            float best = float.MaxValue;
            for (int i = 0; i < plan.Spawns.Length; i++)
            {
                ref var row = ref plan.Spawns[i];
                if (row.Lead < 0 || row.Body != body) continue;
                int h = CreaturePlan.HerdOf(row.Id);
                if (!PlanHerd(planRef, h, t, out float2 centre, out _, out _)) continue;
                float d = math.distancesq(centre, from);
                if (d >= best || Rise(centre, YoungFlatM) > YoungFlatRise) continue;
                best = d; young = row.Id; herd = h;
            }
            return young != int.MinValue;
        }

        static bool LivePair(EntityManager em, int young, out CreatureState ys, out CreatureBrain yb, out CreatureState ls)
        {
            ys = default; yb = default; ls = default;
            em.CompleteAllTrackedJobs();
            bool gotYoung = false, gotLead = false;
            using var q = em.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain));
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var brains = q.ToComponentDataArray<CreatureBrain>(Allocator.Temp);
            for (int i = 0; i < states.Length; i++)
                if (states[i].Id == young) { ys = states[i]; yb = brains[i]; gotYoung = true; }
            if (!gotYoung) return false;
            for (int i = 0; i < states.Length; i++)
                if (states[i].Id == yb.Lead) { ls = states[i]; gotLead = true; }
            return gotLead;
        }

        static bool ViewPair(Unity.Entities.World view, int young, int lead, out float2 y, out float2 l, out float age)
        {
            y = l = 0;
            age = 0;
            bool a = false, b = false;
            using var q = view.EntityManager.CreateEntityQuery(typeof(CreatureState));
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            foreach (var s in states)
            {
                if (s.Id == young) { y = s.Position; age = s.Age; a = true; }
                if (s.Id == lead) { l = s.Position; b = true; }
            }
            return a && b;
        }

        static void AimPair(Camera cam, float2 y, float2 l, float2 player, float turn = 0)
        {
            float2 mid = (y + l) * 0.5f, along = math.normalizesafe(l - y, new float2(1, 0));
            float2 side = new float2(-along.y, along.x);
            var sun = RenderSettings.sun != null ? RenderSettings.sun : System.Array.Find(Object.FindObjectsByType<Light>(), x => x.type == LightType.Directional);
            float2 toward = sun != null ? -new float2(sun.transform.forward.x, sun.transform.forward.z) : player - mid;
            if (math.dot(side, toward) < 0) side = -side;
            side = math.normalizesafe(side + 0.6f * math.normalizesafe(toward));
            var flat = Quaternion.Euler(0, turn, 0) * new Vector3(side.x, 0, side.y);
            var target = new Vector3(mid.x, GroundHeight(mid.x, mid.y) + 0.6f, mid.y);
            var eye = target + flat * YoungEyeM;
            eye.y = Mathf.Max(GroundHeight(eye.x, eye.z) + 1.7f, target.y + YoungEyeRise);
            cam.fieldOfView = HerdFov;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        }

        static System.Collections.IEnumerator CreaturesYoung(Camera cam, string dir)
        {
            var lines = new List<string>();
            var server = ServerWorld();
            var view = ViewWorld();
            if (server == null || view == null) { GameStatus.Lines["~young"] = "young: no worlds"; yield break; }
            GameStatus.HoldCamera = true;
            for (var w = ShowHour(YoungHour, YoungMoon); w.MoveNext();) yield return null;
            var sem = server.EntityManager;
            var lib = Stabby.Viewer.ContentFiles.Creatures(Stabby.Viewer.ContentFiles.Biomes());
            int deer = lib.Bodies.FindIndex(b => b.Id == "deer");
            for (int k = 0; k < 3600 && CreatureBuildSystem.Count == 0; k++) yield return null;
            var planRef = PlanOf(sem);
            if (!planRef.IsCreated || !FirstPlayer(sem, out float2 from) || !PlanYoung(planRef, deer, from, server.Time.ElapsedTime, out int young, out int herd))
            {
                GameStatus.Lines["~young"] = $"young: no planned deer young (deer body {deer})";
                yield break;
            }
            PlanHerd(planRef, herd, server.Time.ElapsedTime, out float2 centre, out int planned, out _);
            float2 dirAway = math.normalizesafe(from - centre, new float2(-1, 0));
            float2 player = centre + dirAway * YoungAwayM;
            lines.Add($"young: deer young {young} in herd {herd} of {planned}, plan centre {centre.x:F0},{centre.y:F0}, {math.distance(from, centre):F0} m from start");
            CreatureState ys = default, ls = default;
            CreatureBrain yb = default;
            double t0 = server.Time.ElapsedTime;
            bool live = false;
            while (!live && server.Time.ElapsedTime - t0 < 90)
            {
                if (PlanHerd(planRef, herd, server.Time.ElapsedTime, out centre, out _, out _)) player = centre + dirAway * YoungAwayM;
                PlaceServerPlayers(server, new Vector2(player.x, player.y));
                AimHerd(cam, new Vector2(centre.x, centre.y), new Vector2(dirAway.x, dirAway.y));
                yield return null;
                live = LivePair(sem, young, out ys, out yb, out ls);
            }
            if (!live) { lines.Add($"young: pair never went live in 90 s"); GameStatus.Lines["~young"] = string.Join(" | ", lines); yield break; }
            int lead = yb.Lead;
            float maxGap = 0, sumGap = 0;
            int samples = 0;
            t0 = server.Time.ElapsedTime;
            float2 vy = ys.Position, vl = ls.Position;
            float shownAge = 0;
            while (server.Time.ElapsedTime - t0 < YoungCalmSeconds)
            {
                if (LivePair(sem, young, out ys, out yb, out ls))
                {
                    float gap = math.distance(ys.Position, ls.Position);
                    maxGap = math.max(maxGap, gap);
                    sumGap += gap;
                    samples++;
                    player = (ys.Position + ls.Position) * 0.5f + dirAway * YoungAwayM;
                }
                PlaceServerPlayers(server, new Vector2(player.x, player.y));
                if (ViewPair(view, young, lead, out float2 a, out float2 b, out shownAge)) { vy = math.lerp(vy, a, 0.1f); vl = math.lerp(vl, b, 0.1f); }
                AimPair(cam, vy, vl, player);
                yield return null;
            }
            HerdMindOf(server, herd, out var calmMind);
            var settle = Settle(30);
            while (settle.MoveNext()) yield return null;
            if (ViewPair(view, young, lead, out float2 sy, out float2 sl, out shownAge)) { vy = sy; vl = sl; }
            for (int k = 0; k < YoungTurns.Length; k++)
            {
                AimPair(cam, vy, vl, player, YoungTurns[k]);
                Shot(cam, null, "warmup");
                Shot(cam, dir, $"creatures-young-{(char)('a' + k)}");
            }
            int herdYoung = 0, herdAll = 0;
            sem.CompleteAllTrackedJobs();
            using (var q = sem.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain)))
            using (var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp))
            using (var brains = q.ToComponentDataArray<CreatureBrain>(Allocator.Temp))
                for (int i = 0; i < states.Length; i++)
                {
                    if (brains[i].Herd != herd) continue;
                    herdAll++;
                    if (brains[i].Lead >= 0) herdYoung++;
                }
            lines.Add($"young: live herd {herdAll} with {herdYoung} young; young age {ys.Age:F2} (client {shownAge:F2}), lead {lead}; calm {YoungCalmSeconds:F0} s gap mean {(samples > 0 ? sumGap / samples : 0):F2} m, max {maxGap:F2} m, mode {calmMind.Mode}; shot from {YoungEyeM:F0} m at hour {YoungHour}, moon {YoungMoon}");

            float2 scare = (ys.Position + ls.Position) * 0.5f + dirAway * 6f;
            float fleeMax = 0, fleeRun = 0, leadRun = 0;
            int fleeSamples = 0;
            HerdMode mode = HerdMode.Calm;
            t0 = server.Time.ElapsedTime;
            while (server.Time.ElapsedTime - t0 < YoungFleeSeconds)
            {
                PlaceServerPlayers(server, new Vector2(scare.x, scare.y));
                if (HerdMindOf(server, herd, out var m)) mode = m.Mode;
                if (LivePair(sem, young, out ys, out yb, out ls) && mode == HerdMode.Flee)
                {
                    fleeMax = math.max(fleeMax, math.distance(ys.Position, ls.Position));
                    fleeRun = math.max(fleeRun, math.distance(ys.Position, scare));
                    leadRun = math.max(leadRun, math.distance(ls.Position, scare));
                    fleeSamples++;
                }
                yield return null;
            }
            lines.Add($"young: flee {fleeSamples} samples over {YoungFleeSeconds:F0} s, gap to lead max {fleeMax:F2} m, young reached {fleeRun:F0} m and lead {leadRun:F0} m from the scare, end mode {mode}");
            for (int i = 0; i < lines.Count; i++) GameStatus.Lines[$"~young {i:D2}"] = lines[i];
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "creatures-young.txt"), lines);
        }
    }
}
