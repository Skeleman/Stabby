using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float RoadShotHour = 15f, RoadEyeBack = 26f, RoadEyeUp = 9f, RoadLookM = 40f;

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
            var dirt = roads.Where(s => s.Kind != RoadSegment.Lane).ToArray();
            if (dirt.Length == 0) { GameStatus.Lines["~roads"] = GameStatus.Failure = $"roads: no road links in this world ({roads.Length} lanes)"; yield break; }
            var seg = dirt.OrderBy(s => new Vector2((s.X0 + s.X1) * 0.5f - home.x, (s.Z0 + s.Z1) * 0.5f - home.z).sqrMagnitude).First();
            float km = dirt.Sum(s => new Vector2(s.X1 - s.X0, s.Z1 - s.Z0).magnitude) / 1000f;
            int fords = dirt.Count(s => s.Kind == RoadSegment.Ford);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(RoadShotHour); w.MoveNext();) yield return null;
            var u = new Vector3(seg.X1 - seg.X0, 0, seg.Z1 - seg.Z0).normalized;
            var mid = new Vector3((seg.X0 + seg.X1) * 0.5f, 0, (seg.Z0 + seg.Z1) * 0.5f);
            var eye = mid - u * RoadEyeBack;
            eye.y = ground.HeightAt(eye.x, eye.z) + RoadEyeUp;
            var look = mid + u * RoadLookM;
            look.y = ground.HeightAt(look.x, look.z);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~roads"] = $"roads: {dirt.Length} segments, {km:F1} km, {fords} fords, {field.TownPlans().Length} towns, GV {WorldConfig.GeneratorVersion}\nsegment {seg.X0:F0},{seg.Z0:F0} to {seg.X1:F0},{seg.Z1:F0}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}, pinned hour {sky.Hour:F2}";
            Shot(cam, null, "warmup");
            Shot(cam, dir, "roads-link");
        }
    }
}
