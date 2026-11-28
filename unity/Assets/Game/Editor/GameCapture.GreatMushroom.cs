using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float GreatMushroomHour = 10f, GreatMushroomMoon = 0.5f, GreatMushroomEyeUp = 1.7f, GreatMushroomNearM = 27f, GreatMushroomAimUp = 5.5f, GreatMushroomSide = 0.35f;

        static System.Collections.IEnumerator GreatMushroomTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~greatmushroom"] = GameStatus.Failure = "greatmushroom: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "great-mushroom");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~greatmushroom"] = GameStatus.Failure = "greatmushroom: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~greatmushroom"] = GameStatus.Failure = "greatmushroom: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            float baseY = ground.HeightAt(s.X, s.Z);
            var centre = new Vector3(s.X, baseY, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(GreatMushroomHour, GreatMushroomMoon); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            var sun = new Vector3(sky.SunX, 0, sky.SunZ);
            if (sun.sqrMagnitude < 1e-6f) sun = Vector3.forward;
            sun.Normalize();
            var side = new Vector3(sun.z, 0, -sun.x);
            var eye = centre + (sun + side * GreatMushroomSide).normalized * GreatMushroomNearM;
            eye.y = ground.HeightAt(eye.x, eye.z) + GreatMushroomEyeUp;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * GreatMushroomAimUp - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            sky = DayNightSystem.Shown;
            GameStatus.Lines["~greatmushroom"] = $"greatmushroom: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, ground {baseY:F2} fit {field.GreatMushroomFit(s.X, s.Z):F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "great-mushroom");
        }
    }
}
