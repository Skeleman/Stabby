using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float TownShotHour = 16f, TownEyeBack = 22f, TownEyeUp = 14f;

        static System.Collections.IEnumerator TownsTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~towns"] = GameStatus.Failure = "towns: no object field"; yield break; }
            var plans = System.Threading.Tasks.Task.Run(() => field.TownPlans());
            while (!plans.IsCompleted) yield return null;
            if (plans.IsFaulted) { GameStatus.Lines["~towns"] = GameStatus.Failure = "towns: " + plans.Exception; yield break; }
            var town = plans.Result.OrderBy(p => new Vector2(p.X - home.x, p.Z - home.z).sqrMagnitude).FirstOrDefault();
            if (town == null) { GameStatus.Lines["~towns"] = GameStatus.Failure = "towns: no town in this world"; yield break; }
            int houses = town.Members.Count(m => field.Library.Objects[m.Def].Id == "house");
            cam.fieldOfView = 60f;
            for (var w = ShowHour(TownShotHour); w.MoveNext();) yield return null;
            var u = new Vector3(town.Ux, 0, town.Uz).normalized;
            var side = new Vector3(-u.z, 0, u.x);
            var centre = new Vector3(town.X, ground.HeightAt(town.X, town.Z), town.Z);
            var eye = centre - u * (town.LaneM + TownEyeBack) + side * 6f;
            eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), centre.y) + TownEyeUp;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * 2f - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~towns"] = $"towns: {plans.Result.Length} towns, GV {WorldConfig.GeneratorVersion}\ntown at {town.X:F0},{centre.y:F0},{town.Z:F0}, {town.Members.Count} sites, {houses} houses, radius {town.Radius:F0} m, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}, pinned hour {sky.Hour:F2}";
            Shot(cam, null, "warmup");
            Shot(cam, dir, "towns-lane");
        }
    }
}
