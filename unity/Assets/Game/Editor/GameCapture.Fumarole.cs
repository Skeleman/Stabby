using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float FumaroleHour = 10f, FumaroleMoon = 0.5f, FumaroleEyeM = 17f, FumaroleEyeUpM = 6f, FumaroleAimUpM = 1.5f;

        static System.Collections.IEnumerator FumaroleTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~fumarole"] = GameStatus.Failure = "fumarole: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "fumarole-field");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~fumarole"] = GameStatus.Failure = "fumarole: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~fumarole"] = GameStatus.Failure = "fumarole: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            var centre = new Vector3(s.X, s.Y, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(FumaroleHour, FumaroleMoon); w.MoveNext();) yield return null;
            var eye = centre + new Vector3(0.7f, 0, -0.7f) * FumaroleEyeM;
            eye.y = ground.HeightAt(eye.x, eye.z) + FumaroleEyeUpM;
            var aim = centre;
            aim.y = s.Y + FumaroleAimUpM;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (var w = Settle(600); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var vents = new System.Collections.Generic.List<float>();
            field.FumaroleVentsAt(s, vents);
            GameStatus.Lines["~fumarole"] = $"fumarole: {all.Length} placed, {vents.Count / Structures.FumaroleVentStride} vents, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, GV {WorldConfig.GeneratorVersion}";
            Shot(cam, dir, "fumarole-field");
        }
    }
}
