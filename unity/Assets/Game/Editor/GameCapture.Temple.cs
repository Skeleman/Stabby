using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float TempleHour = 15.5f, TempleMoon = 0.5f, TempleEyeOut = 17f, TempleEyeSide = 7f, TempleEyeUp = 3.2f, TempleAimOut = 2f, TempleAimUp = 1.8f;

        static System.Collections.IEnumerator TempleTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~sunkentemple"] = GameStatus.Failure = "sunkentemple: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "sunken-temple");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~sunkentemple"] = GameStatus.Failure = "sunkentemple: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~sunkentemple"] = GameStatus.Failure = "sunkentemple: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            var rot = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            var across = rot * Vector3.forward;
            var along = rot * Vector3.right;
            var centre = new Vector3(s.X, s.Y, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(TempleHour, TempleMoon); w.MoveNext();) yield return null;
            var eye = centre + across * TempleEyeOut + along * TempleEyeSide + Vector3.up * TempleEyeUp;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + across * TempleAimOut + Vector3.up * TempleAimUp - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~sunkentemple"] = $"sunkentemple: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, floor {s.Y:F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}";
            Shot(cam, dir, "sunken-temple");
        }
    }
}
