using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float BoulderHour = 15.5f, BoulderMoon = 0.5f;

        static System.Collections.IEnumerator BoulderTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            Ground ground = default;
            bool found = false;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                ground = q.GetSingleton<Ground>();
                found = true;
                break;
            }
            if (!found) { GameStatus.Lines["~boulder"] = GameStatus.Failure = "boulder: no ground"; yield break; }
            int at = LargestBoulder(ground, out var b);
            if (at < 0) { GameStatus.Lines["~boulder"] = GameStatus.Failure = "boulder: none placed"; yield break; }
            var mid = new Vector3(0.5f * (b.Ax + b.Bx), 0.5f * (b.Ay + b.By), 0.5f * (b.Az + b.Bz));
            var side = new Vector3(-(b.Bz - b.Az), 0, b.Bx - b.Ax).normalized;
            cam.fieldOfView = 60f;
            for (var w = ShowHour(BoulderHour, BoulderMoon); w.MoveNext();) yield return null;
            var eye = mid + side * 3.2f * b.R;
            eye.y = Mathf.Max(mid.y + 0.6f * b.R, ground.HeightAt(eye.x, eye.z) + 1.7f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(mid + Vector3.up * 0.2f * b.R - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~boulder"] = $"boulder: edit {at} R {b.R:F2} at {mid.x:F0},{mid.y:F0},{mid.z:F0}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
            Shot(cam, dir, "boulder-near");
        }

        static int LargestBoulder(Ground ground, out FeatureEdit b)
        {
            ref var g = ref ground.Blob.Value;
            int at = -1;
            for (int k = 0; k < g.Edits.Length; k++)
            {
                var e = g.Edits[k];
                if (e.Shape == FeatureEdit.Boulder && e.S == 0f && (e.Ax != e.Bx || e.Az != e.Bz) && (at < 0 || e.R > g.Edits[at].R)) at = k;
            }
            b = at < 0 ? default : g.Edits[at];
            return at;
        }
    }
}
