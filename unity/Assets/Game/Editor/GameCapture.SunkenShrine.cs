using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float SunkenHour = 15.5f, SunkenMoon = 0.5f, SunkenEyeUp = 0.9f, SunkenNearM = 17f, SunkenSideM = 26f, SunkenSideUp = 6f;

        static System.Collections.IEnumerator SunkenShrineTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~sunken"] = GameStatus.Failure = "sunken: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "sunken-shrine");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~sunken"] = GameStatus.Failure = "sunken: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~sunken"] = GameStatus.Failure = "sunken: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            float level = field.SunkenWaterLevel(s.X, s.Z);
            var rot = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            var fwd = rot * Vector3.forward;
            fwd.y = 0;
            fwd.Normalize();
            var side = new Vector3(fwd.z, 0, -fwd.x);
            var centre = new Vector3(s.X, level, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(SunkenHour, SunkenMoon); w.MoveNext();) yield return null;
            var eye = centre + (fwd * 0.8f + side * 0.6f).normalized * SunkenNearM;
            eye.y = Mathf.Max(level, ground.HeightAt(eye.x, eye.z)) + SunkenEyeUp;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * 1.6f - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "sunken-waterline");
            var far = centre - (side * 0.8f + fwd * 0.6f).normalized * SunkenSideM;
            far.y = Mathf.Max(level, ground.HeightAt(far.x, far.z)) + SunkenSideUp;
            cam.transform.SetPositionAndRotation(far, Quaternion.LookRotation(centre + Vector3.up * 1f - far));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~sunken"] = $"sunken: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, water {level:F2} ground {ground.HeightAt(s.X, s.Z):F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "sunken-whole");
        }
    }
}
