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
        const float DesireAwayM = 90f, DesireSeconds = 60f;

        static System.Collections.IEnumerator DesireShots(Camera cam, string dir)
        {
            var lines = new List<string>();
            var server = ServerWorld();
            var view = ViewWorld();
            if (server == null || view == null) { GameStatus.Lines["~desire"] = "desire: no worlds"; yield break; }
            GameStatus.HoldCamera = true;
            var sem = server.EntityManager;
            for (int k = 0; k < 3600 && CreatureBuildSystem.Count == 0; k++) yield return null;
            var used = new HashSet<int>();
            foreach (byte act in new[] { Drives.Drink, Drives.Graze })
            {
                var run = DesireShot(cam, dir, server, view, act, used, lines);
                while (run.MoveNext()) yield return run.Current;
            }
            GameStatus.Lines["~desire"] = string.Join(" | ", lines);
        }

        static int PickDesireHerd(Unity.Entities.World server, byte act, HashSet<int> used, out CreatureHerd chosen)
        {
            var sem = server.EntityManager;
            sem.CompleteAllTrackedJobs();
            var host = server.GetExistingSystem<CreatureHostSystem>();
            ref var sys = ref server.Unmanaged.GetUnsafeSystemRef<CreatureHostSystem>(host);
            var sizes = new Dictionary<int, int>();
            using (var q = sem.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain)))
            using (var brains = q.ToComponentDataArray<CreatureBrain>(Allocator.Temp))
                foreach (var b in brains) sizes[b.Herd] = sizes.TryGetValue(b.Herd, out int c) ? c + 1 : 1;
            int herd = int.MinValue, best = 0;
            chosen = default;
            foreach (var kv in sizes)
            {
                if (kv.Value < 3 || used.Contains(kv.Key) || !sys.Herds.TryGetValue(kv.Key, out var h) || !h.Needs.Can(act)) continue;
                if (kv.Value > best) { best = kv.Value; herd = kv.Key; chosen = h; }
            }
            return herd;
        }

        static string PlanCan(BlobAssetReference<CreaturePlanData> planRef, byte act)
        {
            if (!planRef.IsCreated) return "no plan";
            ref var plan = ref planRef.Value;
            var herds = new Dictionary<int, (int Members, bool Can, int Body)>();
            for (int i = 0; i < plan.Spawns.Length; i++)
            {
                ref var row = ref plan.Spawns[i];
                int h = CreaturePlan.HerdOf(row.Id);
                herds.TryGetValue(h, out var v);
                herds[h] = (v.Members + 1, v.Can || row.Needs.Can(act), row.Body);
            }
            var byBody = new Dictionary<int, (int All, int Can, int CanBig)>();
            foreach (var v in herds.Values)
            {
                byBody.TryGetValue(v.Body, out var b);
                byBody[v.Body] = (b.All + 1, b.Can + (v.Can ? 1 : 0), b.CanBig + (v.Can && v.Members >= 3 ? 1 : 0));
            }
            var parts = new List<string>();
            foreach (var kv in byBody) parts.Add($"body {kv.Key}: {kv.Value.Can}/{kv.Value.All} herds can, {kv.Value.CanBig} of 3+");
            return string.Join(", ", parts);
        }

        static bool NearestPlanHerd(BlobAssetReference<CreaturePlanData> planRef, byte act, float2 from, HashSet<int> used, out int herd)
        {
            ref var plan = ref planRef.Value;
            herd = int.MinValue;
            float best = float.MaxValue;
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < plan.Spawns.Length; i++)
            {
                int h = CreaturePlan.HerdOf(plan.Spawns[i].Id);
                counts[h] = counts.TryGetValue(h, out int c) ? c + 1 : 1;
            }
            for (int i = 0; i < plan.Spawns.Length; i++)
            {
                ref var row = ref plan.Spawns[i];
                int h = CreaturePlan.HerdOf(row.Id);
                if (!row.Needs.Can(act) || used.Contains(h) || counts[h] < 3) continue;
                float d = math.distancesq(row.At, from);
                if (d < best) { best = d; herd = h; }
            }
            return herd != int.MinValue;
        }

        static System.Collections.IEnumerator DesireShot(Camera cam, string dir, Unity.Entities.World server, Unity.Entities.World view, byte act, HashSet<int> used, List<string> lines)
        {
            string name = act == Drives.Drink ? "creature-drink" : "creature-graze";
            var sem = server.EntityManager;
            int herd = PickDesireHerd(server, act, used, out var chosen);
            var planRef = PlanOf(sem);
            if (herd == int.MinValue && planRef.IsCreated && FirstPlayer(sem, out float2 from) && NearestPlanHerd(planRef, act, from, used, out int wanted))
            {
                if (PlanHerd(planRef, wanted, server.Time.ElapsedTime, out float2 c0, out int m0, out _)) lines.Add($"{name}: plan herd {wanted} of {m0} at {math.distance(c0, from):F0} m");
                double tw = server.Time.ElapsedTime;
                while (herd == int.MinValue && server.Time.ElapsedTime - tw < 90)
                {
                    if (PlanHerd(planRef, wanted, server.Time.ElapsedTime, out float2 centre, out _, out _))
                    {
                        PlaceServerPlayers(server, new Vector2(centre.x - 60f, centre.y));
                        AimHerd(cam, new Vector2(centre.x, centre.y), new Vector2(-1, 0));
                    }
                    yield return null;
                    herd = PickDesireHerd(server, act, used, out chosen);
                }
            }
            if (herd == int.MinValue) { lines.Add($"{name}: no live herd can {act}, plan {(planRef.IsCreated ? planRef.Value.Spawns.Length : -1)} rows, {PlanCan(planRef, act)}"); yield break; }
            used.Add(herd);
            chosen.Needs.Target(act, out float tx, out float tz);
            float2 target = new float2(tx, tz);
            float2 water = act == Drives.Drink ? math.normalizesafe(new float2(chosen.Needs.WaterX, chosen.Needs.WaterZ), new float2(0, 1)) : new float2(0, 1);
            float2 tangent = new float2(-water.y, water.x);
            float2 player = target - water * DesireAwayM;
            PlaceServerPlayers(server, new Vector2(player.x, player.y));
            using (var q = sem.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain)))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    var b = sem.GetComponentData<CreatureBrain>(e);
                    if (b.Herd != herd) continue;
                    var s = sem.GetComponentData<CreatureState>(e);
                    s.Position = target - water * 6f + math.clamp(b.Offset, -4f, 4f);
                    s.Act = Drives.None;
                    s.Move = 0;
                    b.Home = s.Position;
                    b.Levels = new DriveLevels { Hunger = act == Drives.Graze ? 1 : 0, Thirst = act == Drives.Drink ? 1 : 0 };
                    b.Want = Drives.None;
                    b.Placed = false;
                    sem.SetComponentData(e, s);
                    sem.SetComponentData(e, b);
                }
            Vector2 side = new Vector2(-water.x * 0.6f + tangent.x * 0.8f, -water.y * 0.6f + tangent.y * 0.8f).normalized;
            Vector2 aim = new Vector2(target.x, target.y);
            double t0 = server.Time.ElapsedTime;
            int members = 0, acting = 0;
            float far = 0, alert = 0;
            HerdMode mode = HerdMode.Calm;
            while (server.Time.ElapsedTime - t0 < DesireSeconds)
            {
                PlaceServerPlayers(server, new Vector2(player.x, player.y));
                members = acting = 0;
                far = alert = 0;
                sem.CompleteAllTrackedJobs();
                using (var q = sem.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain)))
                using (var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp))
                using (var brains = q.ToComponentDataArray<CreatureBrain>(Allocator.Temp))
                    for (int i = 0; i < states.Length; i++)
                    {
                        if (brains[i].Herd != herd) continue;
                        members++;
                        alert = math.max(alert, states[i].Alert);
                        if (states[i].Act != act) continue;
                        acting++;
                        far = math.max(far, math.distance(states[i].Position, target));
                    }
                if (HerdMindOf(server, herd, out var mind)) mode = mind.Mode;
                Vector2 seen = HerdCentre(view, herd, out int count, out _);
                if (count > 0) aim = Vector2.Lerp(aim, seen, 0.1f);
                AimHerd(cam, aim, side);
                if (members > 0 && acting * 10 >= members * 7 && server.Time.ElapsedTime - t0 > 8) break;
                yield return null;
            }
            var settle = Settle(30);
            while (settle.MoveNext()) yield return null;
            AimHerd(cam, aim, side);
            Shot(cam, null, "warmup");
            Shot(cam, dir, name);
            lines.Add($"{name}: herd {herd} {acting}/{members} acting after {server.Time.ElapsedTime - t0:F1} s, mode {mode}, max alert {alert:F2}, furthest actor {far:F1} m from target, water dir {water.x:F2},{water.y:F2}");
        }
    }
}
