using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float BrineHour = 10f, BrineMoon = 0.5f, BrineEyeM = 14f, BrineEyeUpM = 3f, BrineAimUpM = 0f;

        static System.Collections.IEnumerator BrinePoolsTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~brine"] = GameStatus.Failure = "brine-pools: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "brine-pools");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~brine"] = GameStatus.Failure = "brine-pools: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~brine"] = GameStatus.Failure = "brine-pools: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            var centre = new Vector3(s.X, s.Y, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(BrineHour, BrineMoon); w.MoveNext();) yield return null;
            var pools = new System.Collections.Generic.List<float>();
            field.SpringPoolsAt(s, pools);
            var side = centre + new Vector3(0.7f, 0, -0.7f) * 20f;
            int near = 0;
            for (int k = 0; k < pools.Count; k += ObjectField.SpringStride)
                if (new Vector2(pools[k] - side.x, pools[k + 1] - side.z).sqrMagnitude < new Vector2(pools[near] - side.x, pools[near + 1] - side.z).sqrMagnitude) near = k;
            var aim = new Vector3(pools[near], pools[near + 4] + BrineAimUpM, pools[near + 1]);
            var away = new Vector3(side.x - aim.x, 0, side.z - aim.z).normalized;
            var eye = aim + away * BrineEyeM;
            eye.y = ground.HeightAt(eye.x, eye.z) + BrineEyeUpM;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (var w = Settle(600); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            GameStatus.Lines["~brine"] = $"brine-pools: {all.Length} placed, {pools.Count / ObjectField.SpringStride} pools, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, GV {WorldConfig.GeneratorVersion}";
            Shot(cam, dir, "brine-pools");
        }
    }
}
