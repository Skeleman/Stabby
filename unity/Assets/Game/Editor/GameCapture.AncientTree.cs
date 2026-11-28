using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float AncientHour = 15.5f, AncientMoon = 0.5f, AncientEyeUp = 1.7f, AncientNearM = 30f, AncientAimUp = 7f;

        static System.Collections.IEnumerator AncientTreeTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~ancienttree"] = GameStatus.Failure = "ancient tree: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "ancient-tree");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~ancienttree"] = GameStatus.Failure = "ancient tree: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~ancienttree"] = GameStatus.Failure = "ancient tree: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            float baseY = ground.HeightAt(s.X, s.Z);
            var rot = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            var fwd = rot * Vector3.forward;
            fwd.y = 0;
            fwd.Normalize();
            var side = new Vector3(fwd.z, 0, -fwd.x);
            var centre = new Vector3(s.X, baseY, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(AncientHour, AncientMoon); w.MoveNext();) yield return null;
            var eye = centre + (fwd - side * 0.4f).normalized * AncientNearM;
            eye.y = ground.HeightAt(eye.x, eye.z) + AncientEyeUp;
            var sill = centre + Vector3.up * 1.5f;
            for (int i = 0; i < 40 && !Clear(ground, eye, sill); i++) eye.y += 0.25f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * AncientAimUp - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~ancienttree"] = $"ancient tree: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, ground {baseY:F2} woods {field.Woods(s.X, s.Z):F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "ancient-tree");
        }
    }
}
