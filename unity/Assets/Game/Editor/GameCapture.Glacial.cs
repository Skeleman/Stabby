using System;
using System.Globalization;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const string GlacialAtVariable = "STABBY_GLACIAL_AT";
        const float GlacialHour = 15f, GlacialMoon = 0.5f, ErraticMinR = 6f;

        static System.Collections.IEnumerator GlacialTour(Camera cam, string dir)
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
            if (!found) { GameStatus.Lines["~glacial"] = GameStatus.Failure = "glacial: no ground"; yield break; }
            var at = Environment.GetEnvironmentVariable(GlacialAtVariable);
            if (string.IsNullOrEmpty(at)) { GameStatus.Lines["~glacial"] = GameStatus.Failure = "glacial: set " + GlacialAtVariable + " to kettleX,kettleZ,crestX,crestZ,erraticX,erraticZ"; yield break; }
            var inv = CultureInfo.InvariantCulture;
            var p = at.Split(',');
            var kettle = new Vector3(float.Parse(p[0], inv), 0, float.Parse(p[1], inv));
            var crest = new Vector3(float.Parse(p[2], inv), 0, float.Parse(p[3], inv));
            var near = new Vector3(float.Parse(p[4], inv), 0, float.Parse(p[5], inv));
            kettle.y = ground.HeightAt(kettle.x, kettle.z);
            crest.y = ground.HeightAt(crest.x, crest.z);
            var fd = crest - kettle;
            fd.y = 0;
            float span = fd.magnitude;
            fd /= span;
            cam.fieldOfView = 55f;
            for (var w = ShowHour(GlacialHour, GlacialMoon); w.MoveNext();) yield return null;
            var eye = kettle - fd * (0.9f * span + 120f);
            eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z) + 1.7f, Mathf.Max(kettle.y, crest.y) + 25f);
            var aim = Vector3.Lerp(kettle, crest, 0.5f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~glacial"] = string.Format(inv, "glacial: kettle {0:F0},{1:F0},{2:F0} crest {3:F0},{4:F0},{5:F0} eye {6:F0},{7:F0},{8:F0}, GV {9}, pinned hour {10:F2} moon {11:F2}",
                kettle.x, kettle.y, kettle.z, crest.x, crest.y, crest.z, eye.x, eye.y, eye.z, WorldConfig.GeneratorVersion, sky.Hour, sky.Phase);
            Shot(cam, dir, "glacial-far");
            if (!NearestBoulder(ground, near, ErraticMinR, out var b)) { GameStatus.Lines["~erratic"] = GameStatus.Failure = "erratic: none near " + at; yield break; }
            var mid = new Vector3(0.5f * (b.Ax + b.Bx), 0.5f * (b.Ay + b.By), 0.5f * (b.Az + b.Bz));
            var side = new Vector3(-(b.Bz - b.Az), 0, b.Bx - b.Ax).normalized;
            var eye2 = mid + side * 3.5f * b.R;
            eye2.y = Mathf.Max(mid.y + 0.3f * b.R, ground.HeightAt(eye2.x, eye2.z) + 1.7f);
            cam.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(mid + Vector3.up * 0.1f * b.R - eye2));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            GameStatus.Lines["~erratic"] = string.Format(inv, "erratic: R {0:F1} at {1:F0},{2:F0},{3:F0}", b.R, mid.x, mid.y, mid.z);
            Shot(cam, dir, "glacial-erratic");
        }

        static bool NearestBoulder(Ground ground, Vector3 from, float minR, out FeatureEdit b)
        {
            ref var g = ref ground.Blob.Value;
            int at = -1;
            float best = float.MaxValue;
            for (int k = 0; k < g.Edits.Length; k++)
            {
                var e = g.Edits[k];
                if (e.Shape != FeatureEdit.Boulder || e.R < minR) continue;
                float dx = 0.5f * (e.Ax + e.Bx) - from.x, dz = 0.5f * (e.Az + e.Bz) - from.z, d = dx * dx + dz * dz;
                if (d < best) { best = d; at = k; }
            }
            b = at < 0 ? default : g.Edits[at];
            return at >= 0;
        }
    }
}
