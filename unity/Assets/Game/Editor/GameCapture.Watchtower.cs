using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float WatchtowerHour = 15.5f, WatchtowerMoon = 0.5f, WatchtowerEyeUp = 1.7f, WatchtowerNearM = 15f, WatchtowerAimUp = 2.5f, WatchtowerFarM = 20f, WatchtowerHighEyeUp = 3f, WatchtowerHighAimUp = 5f;

        static System.Collections.IEnumerator WatchtowerTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~watchtower"] = GameStatus.Failure = "watchtower: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "watchtower");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~watchtower"] = GameStatus.Failure = "watchtower: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~watchtower"] = GameStatus.Failure = "watchtower: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            float baseY = ground.HeightAt(s.X, s.Z);
            var rot = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            var fwd = rot * Vector3.forward;
            fwd.y = 0;
            fwd.Normalize();
            var side = new Vector3(fwd.z, 0, -fwd.x);
            var centre = new Vector3(s.X, baseY, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(WatchtowerHour, WatchtowerMoon); w.MoveNext();) yield return null;
            var eye = centre + (fwd - side * 0.6f).normalized * WatchtowerNearM;
            eye.y = ground.HeightAt(eye.x, eye.z) + WatchtowerEyeUp;
            var sill = centre + fwd * (Structures.WatchR * Structures.WatchtowerU * s.Size) + Vector3.up * 0.3f;
            for (int i = 0; i < 40 && !Clear(ground, eye, sill); i++) eye.y += 0.25f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * WatchtowerAimUp - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~watchtower"] = $"watchtower: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, ground {baseY:F2} fit {field.WatchFit(s.X, s.Z):F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "watchtower");
            var back = centre + (-fwd - side * 0.8f).normalized * WatchtowerFarM;
            back.y = ground.HeightAt(back.x, back.z) + WatchtowerHighEyeUp;
            var rubble = centre - fwd * (Structures.WatchR * Structures.WatchtowerU * s.Size) + Vector3.up * 0.3f;
            for (int i = 0; i < 40 && !Clear(ground, back, rubble); i++) back.y += 0.25f;
            cam.transform.SetPositionAndRotation(back, Quaternion.LookRotation(centre + Vector3.up * WatchtowerHighAimUp - back));
            for (var w = Settle(60); w.MoveNext();) yield return null;
            Shot(cam, dir, "watchtower-collapse");
        }
    }
}
