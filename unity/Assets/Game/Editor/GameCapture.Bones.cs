using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float BonesHour = 15.5f, BonesMoon = 0.5f, BonesEyeUp = 1.7f, BonesSideM = 35f, BonesSideUp = 10f;

        static System.Collections.IEnumerator BonesTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~bones"] = GameStatus.Failure = "bones: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "bones");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~bones"] = GameStatus.Failure = "bones: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~bones"] = GameStatus.Failure = "bones: none placed in this world"; yield break; }
            var b = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            var rot = new Quaternion(b.Qx, b.Qy, b.Qz, b.Qw);
            var fwd = rot * Vector3.forward;
            fwd.y = 0;
            fwd.Normalize();
            var side = new Vector3(fwd.z, 0, -fwd.x);
            var centre = new Vector3(b.X, ground.HeightAt(b.X, b.Z), b.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(BonesHour, BonesMoon); w.MoveNext();) yield return null;
            var eye = centre - fwd * 2f;
            eye.y = ground.HeightAt(eye.x, eye.z) + BonesEyeUp;
            var look = centre + fwd * 12f + Vector3.up * 5f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "bones-under");
            var far = centre + side * BonesSideM;
            far.y = ground.HeightAt(far.x, far.z) + BonesSideUp;
            cam.transform.SetPositionAndRotation(far, Quaternion.LookRotation(centre + Vector3.up * 3f - far));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~bones"] = $"bones: {all.Length} placed, shown {b.X:F0},{b.Z:F0} size {b.Size:F1} variant {b.Variant}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "bones-whole");
        }
    }
}
