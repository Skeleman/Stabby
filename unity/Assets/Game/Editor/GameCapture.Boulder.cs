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
            if (!SoilRockSlope(ground, mid, out var hill, out var up)) { GameStatus.Lines["~hillside"] = "hillside: no slope found"; yield break; }
            var eye2 = hill - up * 40f;
            eye2.y = Mathf.Max(ground.HeightAt(eye2.x, eye2.z) + 1.7f, hill.y + 6f);
            var aim = hill + up * 8f;
            aim.y = ground.HeightAt(aim.x, aim.z);
            cam.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(aim - eye2));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            GameStatus.Lines["~hillside"] = $"hillside: at {hill.x:F0},{hill.y:F0},{hill.z:F0} grade {Grade(ground, hill.x, hill.z, out _, out _):F2}";
            Shot(cam, dir, "hillside-slope");
        }

        static bool SoilRockSlope(Ground ground, Vector3 from, out Vector3 at, out Vector3 up)
        {
            const float Step = 8f, Span = 800f, Want = 1.15f;
            at = up = default;
            float best = float.MaxValue;
            for (float dz = -Span; dz <= Span; dz += Step)
                for (float dx = -Span; dx <= Span; dx += Step)
                {
                    if (dx * dx + dz * dz < 900f) continue;
                    float x = from.x + dx, z = from.z + dz, h = ground.HeightAt(x, z);
                    if (h < 3f) continue;
                    float g = Grade(ground, x, z, out float gx, out float gz), score = Mathf.Abs(g - Want) + 0.0005f * Mathf.Sqrt(dx * dx + dz * dz);
                    if (score >= best) continue;
                    float lx = x - 15f * gx / g, lz = z - 15f * gz / g;
                    if (ground.HeightAt(lx, lz) < 3f || Grade(ground, lx, lz, out _, out _) > 0.5f) continue;
                    best = score;
                    at = new Vector3(x, h, z);
                    up = new Vector3(gx, 0, gz) / g;
                }
            return best < float.MaxValue;
        }

        static float Grade(Ground ground, float x, float z, out float gx, out float gz)
        {
            gx = (ground.HeightAt(x + 3f, z) - ground.HeightAt(x - 3f, z)) / 6f;
            gz = (ground.HeightAt(x, z + 3f) - ground.HeightAt(x, z - 3f)) / 6f;
            return Mathf.Sqrt(gx * gx + gz * gz);
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
