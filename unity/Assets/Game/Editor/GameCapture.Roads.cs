using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float RoadShotHour = 15.5f, RoadShotMoon = 0.5f, RoadSideM = 20f, RoadMinM = 20f, RoadEyeBack = 26f, RoadEyeUp = 9f, RoadLookM = 40f, PortalEyeBack = 34f, PortalEyeUp = 7f, PortalLookUp = 4f, BridgeEyeSide = 24f, BridgeEyeUp = 3f;

        static System.Collections.IEnumerator RoadsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectField field = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var f = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                var q = f == null ? default : w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (f == null || q.CalculateEntityCount() != 1) continue;
                field = f;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (field == null) { GameStatus.Lines["~roads"] = GameStatus.Failure = "roads: no object field"; yield break; }
            var job = System.Threading.Tasks.Task.Run(() => field.Roads());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~roads"] = GameStatus.Failure = "roads: " + job.Exception; yield break; }
            var roads = job.Result;
            var dirt = roads.Where(s => s.Kind != RoadSegment.Lane && s.Kind != RoadSegment.Tunnel && s.Kind != RoadSegment.Bridge).ToArray();
            if (dirt.Length == 0) { GameStatus.Lines["~roads"] = GameStatus.Failure = $"roads: no road links in this world ({roads.Length} lanes)"; yield break; }
            var seg = dirt.Where(s => new Vector2(s.X1 - s.X0, s.Z1 - s.Z0).magnitude >= RoadMinM).DefaultIfEmpty(dirt[0]).OrderByDescending(s => CrossSlope(ground, s)).First();
            float km = dirt.Sum(s => new Vector2(s.X1 - s.X0, s.Z1 - s.Z0).magnitude) / 1000f;
            int fords = dirt.Count(s => s.Kind == RoadSegment.Ford);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(RoadShotHour, RoadShotMoon); w.MoveNext();) yield return null;
            var u = new Vector3(seg.X1 - seg.X0, 0, seg.Z1 - seg.Z0).normalized;
            var mid = new Vector3((seg.X0 + seg.X1) * 0.5f, 0, (seg.Z0 + seg.Z1) * 0.5f);
            var eye = mid - u * RoadEyeBack;
            eye.y = ground.HeightAt(eye.x, eye.z) + RoadEyeUp;
            var look = mid + u * RoadLookM;
            look.y = ground.HeightAt(look.x, look.z);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~roads"] = $"roads: {dirt.Length} segments, {km:F1} km, {fords} fords, {field.TownPlans().Length} towns, cross rise {CrossSlope(ground, seg):F1} m, GV {WorldConfig.GeneratorVersion}\nsegment {seg.X0:F0},{seg.Z0:F0} to {seg.X1:F0},{seg.Z1:F0}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}, pinned hour {sky.Hour:F2}";
            Shot(cam, null, "warmup");
            Shot(cam, dir, "roads-link");
            var spans = roads.Where(s => s.Kind == RoadSegment.Bridge).ToArray();
            if (spans.Length == 0) GameStatus.Lines["~bridges"] = "bridges: none in this world";
            else
            {
                var b = spans.OrderBy(s => new Vector2(s.X0 - home.x, s.Z0 - home.z).sqrMagnitude).First();
                var bu = new Vector3(b.X1 - b.X0, 0, b.Z1 - b.Z0).normalized;
                var deck = new Vector3((b.X0 + b.X1) * 0.5f, 0, (b.Z0 + b.Z1) * 0.5f);
                deck.y = Mathf.Max(ground.HeightAt(b.X0, b.Z0), ground.HeightAt(b.X1, b.Z1));
                var side = deck + new Vector3(-bu.z, 0, bu.x) * BridgeEyeSide;
                side.y = Mathf.Max(ground.HeightAt(side.x, side.z), deck.y) + BridgeEyeUp;
                cam.transform.SetPositionAndRotation(side, Quaternion.LookRotation(deck - side));
                for (var w = Settle(180); w.MoveNext();) yield return null;
                float bm = spans.Sum(s => new Vector2(s.X1 - s.X0, s.Z1 - s.Z0).magnitude);
                GameStatus.Lines["~bridges"] = $"bridges: {spans.Length} spans, {bm:F0} m, nearest {b.X0:F0},{b.Z0:F0} to {b.X1:F0},{b.Z1:F0}, eye {side.x:F0},{side.y:F1},{side.z:F0}";
                Shot(cam, dir, "roads-bridge");
            }
            var bores = roads.Where(s => s.Kind == RoadSegment.Tunnel).ToArray();
            if (bores.Length == 0) { GameStatus.Lines["~tunnels"] = "tunnels: none in this world"; yield break; }
            var t = bores.OrderBy(s => new Vector2(s.X0 - home.x, s.Z0 - home.z).sqrMagnitude).First();
            var tu = new Vector3(t.X1 - t.X0, 0, t.Z1 - t.Z0).normalized;
            var portal = new Vector3(t.X0, 0, t.Z0);
            portal.y = ground.HeightAt(portal.x, portal.z) + PortalLookUp;
            var from = portal - tu * PortalEyeBack;
            from.y = ground.HeightAt(from.x, from.z) + PortalEyeUp;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(portal - from));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            float tkm = bores.Sum(s => new Vector2(s.X1 - s.X0, s.Z1 - s.Z0).magnitude) / 1000f;
            GameStatus.Lines["~tunnels"] = $"tunnels: {bores.Length} bores, {tkm:F2} km, portal {t.X0:F0},{t.Z0:F0}, eye {from.x:F0},{from.y:F1},{from.z:F0}";
            Shot(cam, dir, "roads-tunnel");
        }

        const float TrailChainM = 300f, TrailEyeMin = 90f, TrailEyeUp = 25f, TrailJoinM = 0.5f;

        static System.Collections.IEnumerator TrailsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectField field = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var f = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                var q = f == null ? default : w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (f == null || q.CalculateEntityCount() != 1) continue;
                field = f;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (field == null) { GameStatus.Lines["~trails"] = GameStatus.Failure = "trails: no object field"; yield break; }
            var job = System.Threading.Tasks.Task.Run(() => (field.Roads(), field.Discoveries(), field.MineMouths().ToArray()));
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~trails"] = GameStatus.Failure = "trails: " + job.Exception; yield break; }
            var (roads, found, mines) = job.Result;
            var trails = roads.Where(ObjectField.IsTrail).ToArray();
            if (trails.Length == 0) { GameStatus.Lines["~trails"] = GameStatus.Failure = "trails: none in this world"; yield break; }
            var ends = new System.Collections.Generic.List<(Vector3 at, string what, float reach)>();
            foreach (var m in mines) ends.Add((new Vector3(m.x, 0, m.z), "mine", ObjectField.TrailMouthM + 0.05f));
            foreach (var d in found)
                if (ObjectField.TrailLandmark(field.Library.Objects[d.Def].Placement.Discovery)) ends.Add((new Vector3(d.X, 0, d.Z), field.Library.Objects[d.Def].Id, field.TrailHeadM(d) + 0.05f));
            float best = float.MinValue;
            Vector3 high = default, low = default;
            string target = null;
            int mineLinks = 0, landmarkLinks = 0;
            foreach (var (at, what, reach) in ends)
            {
                bool hit = false;
                foreach (var s in trails)
                {
                    var a = new Vector3(s.X0, 0, s.Z0);
                    var b = new Vector3(s.X1, 0, s.Z1);
                    bool atA = (a - at).sqrMagnitude <= reach * reach, atB = (b - at).sqrMagnitude <= reach * reach;
                    if (!atA && !atB) continue;
                    hit = true;
                    var head = atA ? a : b;
                    var bottom = Chain(trails, head, ground);
                    float rise = ground.HeightAt(head.x, head.z) - ground.HeightAt(bottom.x, bottom.z);
                    if (rise <= best) continue;
                    best = rise;
                    high = head;
                    low = bottom;
                    target = what;
                }
                if (hit && what == "mine") mineLinks++;
                else if (hit) landmarkLinks++;
            }
            if (target == null) { GameStatus.Lines["~trails"] = GameStatus.Failure = $"trails: {trails.Length} segments but none reach a mine or landmark"; yield break; }
            high.y = ground.HeightAt(high.x, high.z);
            low.y = ground.HeightAt(low.x, low.z);
            var look = (high + low) * 0.5f;
            var run = new Vector3(high.x - low.x, 0, high.z - low.z);
            float span = Mathf.Max(TrailEyeMin, run.magnitude);
            var side = Vector3.Cross(Vector3.up, run.normalized);
            var e0 = look + side * span - run.normalized * span * 0.3f;
            var e1 = look - side * span - run.normalized * span * 0.3f;
            var eye = ground.HeightAt(e0.x, e0.z) <= ground.HeightAt(e1.x, e1.z) ? e0 : e1;
            eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), look.y) + TrailEyeUp;
            float km = trails.Sum(s => new Vector2(s.X1 - s.X0, s.Z1 - s.Z0).magnitude) / 1000f;
            cam.fieldOfView = 60f;
            for (var w = ShowHour(RoadShotHour, RoadShotMoon); w.MoveNext();) yield return null;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            GameStatus.Lines["~trails"] = $"trails: {trails.Length} segments, {km:F1} km, mines linked {mineLinks}/{mines.Length}, landmarks linked {landmarkLinks}/{ends.Count - mines.Length}, GV {WorldConfig.GeneratorVersion}; shot climbs {best:F1} m over {run.magnitude:F0} m to {target} at {high.x:F0},{high.z:F0}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}";
            Shot(cam, null, "warmup");
            Shot(cam, dir, "trails-climb");
        }

        static Vector3 Chain(RoadSegment[] trails, Vector3 head, Ground ground)
        {
            var used = new bool[trails.Length];
            Vector3 at = head, low = head;
            float walked = 0, lowY = ground.HeightAt(head.x, head.z);
            while (walked < TrailChainM)
            {
                int next = -1;
                bool fromA = false;
                for (int k = 0; k < trails.Length && next < 0; k++)
                {
                    if (used[k]) continue;
                    var s = trails[k];
                    if ((new Vector3(s.X0, 0, s.Z0) - at).sqrMagnitude <= TrailJoinM * TrailJoinM) { next = k; fromA = true; }
                    else if ((new Vector3(s.X1, 0, s.Z1) - at).sqrMagnitude <= TrailJoinM * TrailJoinM) next = k;
                }
                if (next < 0) break;
                used[next] = true;
                var t = trails[next];
                var to = fromA ? new Vector3(t.X1, 0, t.Z1) : new Vector3(t.X0, 0, t.Z0);
                walked += (to - at).magnitude;
                at = to;
                float y = ground.HeightAt(at.x, at.z);
                if (y < lowY) { lowY = y; low = at; }
            }
            return low;
        }

        static float CrossSlope(Ground ground, RoadSegment s)
        {
            float dx = s.X1 - s.X0, dz = s.Z1 - s.Z0, len = Mathf.Max(1e-3f, Mathf.Sqrt(dx * dx + dz * dz));
            float px = -dz / len * RoadSideM, pz = dx / len * RoadSideM, mx = (s.X0 + s.X1) * 0.5f, mz = (s.Z0 + s.Z1) * 0.5f;
            return Mathf.Abs(ground.HeightAt(mx + px, mz + pz) - ground.HeightAt(mx - px, mz - pz));
        }
    }
}
