using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float SaltHour = 15.5f, SaltMoon = 0.5f, SaltEyeUp = 1.7f, SaltNearM = 20f, SaltFarM = 48f, SaltFarUp = 9f;

        static System.Collections.IEnumerator SaltTowerTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~salt"] = GameStatus.Failure = "salt: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "salt-tower");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~salt"] = GameStatus.Failure = "salt: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~salt"] = GameStatus.Failure = "salt: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            float baseY = ground.HeightAt(s.X, s.Z);
            var rot = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            var fwd = rot * Vector3.forward;
            fwd.y = 0;
            fwd.Normalize();
            var side = new Vector3(fwd.z, 0, -fwd.x);
            var centre = new Vector3(s.X, baseY, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(SaltHour, SaltMoon); w.MoveNext();) yield return null;
            var eye = centre + (fwd * 0.8f + side * 0.6f).normalized * SaltNearM;
            eye.y = ground.HeightAt(eye.x, eye.z) + SaltEyeUp;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * 3.5f - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "salt-tower");
            var far = centre - (side * 0.8f + fwd * 0.6f).normalized * SaltFarM;
            far.y = ground.HeightAt(far.x, far.z) + SaltFarUp;
            cam.transform.SetPositionAndRotation(far, Quaternion.LookRotation(centre + Vector3.up * 2f - far));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~salt"] = $"salt: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, ground {baseY:F2} fit {field.SaltFit(s.X, s.Z):F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "salt-basin");
        }
    }
}
