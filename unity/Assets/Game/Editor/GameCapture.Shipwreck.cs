using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float ShipwreckHour = 15.5f, ShipwreckMoon = 0.5f, ShipwreckEyeUp = 1.7f, ShipwreckNearM = 16f, ShipwreckAimUp = 1.2f;

        static System.Collections.IEnumerator ShipwreckTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~shipwreck"] = GameStatus.Failure = "shipwreck: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "shipwreck");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~shipwreck"] = GameStatus.Failure = "shipwreck: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~shipwreck"] = GameStatus.Failure = "shipwreck: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            float baseY = ground.HeightAt(s.X, s.Z);
            var rot = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            var fwd = rot * Vector3.forward;
            fwd.y = 0;
            fwd.Normalize();
            var side = new Vector3(fwd.z, 0, -fwd.x);
            var centre = new Vector3(s.X, baseY, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(ShipwreckHour, ShipwreckMoon); w.MoveNext();) yield return null;
            var eye = centre + (side * 0.75f - fwd * 0.65f).normalized * ShipwreckNearM;
            eye.y = ground.HeightAt(eye.x, eye.z) + ShipwreckEyeUp;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * ShipwreckAimUp - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~shipwreck"] = $"shipwreck: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, ground {baseY:F2} fit {field.ShipwreckFit(s.X, s.Z):F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "shipwreck");
        }
    }
}
