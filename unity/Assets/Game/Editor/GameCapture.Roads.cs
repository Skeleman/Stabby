using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float RoadShotHour = 15.5f, RoadShotMoon = 0.5f, RoadSideM = 20f, RoadMinM = 20f, RoadEyeBack = 26f, RoadEyeUp = 9f, RoadLookM = 40f, PortalEyeBack = 34f, PortalEyeUp = 7f, PortalLookUp = 4f;

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
            var dirt = roads.Where(s => s.Kind != RoadSegment.Lane && s.Kind != RoadSegment.Tunnel).ToArray();
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

        static float CrossSlope(Ground ground, RoadSegment s)
        {
            float dx = s.X1 - s.X0, dz = s.Z1 - s.Z0, len = Mathf.Max(1e-3f, Mathf.Sqrt(dx * dx + dz * dz));
            float px = -dz / len * RoadSideM, pz = dx / len * RoadSideM, mx = (s.X0 + s.X1) * 0.5f, mz = (s.Z0 + s.Z1) * 0.5f;
            return Mathf.Abs(ground.HeightAt(mx + px, mz + pz) - ground.HeightAt(mx - px, mz - pz));
        }
    }
}
