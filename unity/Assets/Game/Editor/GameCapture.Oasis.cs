using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float OasisHour = 10.5f, OasisMoon = 0.5f;
        static readonly string[] OasisShots = { "oasis", "oasis-wide" };

        static System.Collections.IEnumerator OasisTour(Camera cam, string dir)
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
            if (field == null) { GameStatus.Lines["~oasis"] = GameStatus.Failure = "oasis: no object field"; yield break; }
            var b = field.OasisPools();
            int pick = -1;
            float best = float.MaxValue;
            for (int k = 0; k < b.Length; k += ObjectField.OasisStride)
            {
                float score = new Vector2(b[k] - home.x, b[k + 1] - home.z).magnitude;
                if (score < best) { best = score; pick = k; }
            }
            if (pick < 0) { GameStatus.Lines["~oasis"] = GameStatus.Failure = $"oasis: none placed in seed {GameSettings.ActiveSeed}"; yield break; }
            float r = b[pick + 2], level = b[pick + 3];
            var c = new Vector3(b[pick], level, b[pick + 1]);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(OasisHour, OasisMoon); w.MoveNext();) yield return null;
            var side = new Vector3(0.8f, 0, 0.6f);
            var lines = new System.Collections.Generic.List<string>();
            for (int v = 0; v < OasisShots.Length; v++)
            {
                float back = v == 0 ? r + ObjectField.OasisRingM + 6f : 3.5f * (r + ObjectField.OasisRingM);
                var eye = c + side * back;
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z) + (v == 0 ? 1.7f : 0.35f * back), level + 1.7f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c + Vector3.up * (v == 0 ? 2f : 0f) - eye));
                for (var w = Settle(180); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, OasisShots[v]);
                lines.Add($"{OasisShots[v]} eye {(eye - c).magnitude:F0} m");
            }
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~oasis"] = $"oasis: seed {GameSettings.ActiveSeed}, {b.Length / ObjectField.OasisStride} oases, pool r {r:F1} m depth {level - b[pick + 4]:F2} m at {c.x:F0},{c.y:F1},{c.z:F0}, {string.Join(", ", lines)}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
        }
    }
}
