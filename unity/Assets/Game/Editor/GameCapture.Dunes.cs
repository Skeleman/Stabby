using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float DunesHour = 17.25f, DunesMoon = 0.5f;

        static System.Collections.IEnumerator DunesTour(Camera cam, string dir)
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
            if (!found) { GameStatus.Lines["~dunes"] = GameStatus.Failure = "dunes: no ground"; yield break; }
            var cellsView = ground.Cells;
            int n = cellsView.N, specCount = ground.Blob.Value.DuneSpecs.Length;
            float cm = cellsView.CellMeters;
            if (ground.Blob.Value.Dune.Length == 0 || specCount == 0) { GameStatus.Lines["~dunes"] = GameStatus.Failure = "dunes: no dune field"; yield break; }
            var cells = new int[specCount];
            for (int i = 0; i < n * n; i++)
            {
                int p = cellsView.Dune(i);
                if ((p & 255) >= 128) cells[(p >> 8) - 1]++;
            }
            int region = 0;
            for (int r = 1; r < cells.Length; r++) if (cells[r] > cells[region]) region = r;
            if (cells[region] == 0) { GameStatus.Lines["~dunes"] = GameStatus.Failure = "dunes: empty field"; yield break; }
            var spec = cellsView.DuneRegion(region);
            double sx = 0, sz = 0;
            for (int i = 0; i < n * n; i++)
            {
                int p = cellsView.Dune(i);
                if ((p & 255) >= 128 && (p >> 8) - 1 == region) { sx += i % n; sz += i / n; }
            }
            var centre = new Vector3((float)(sx / cells[region]) * cm, 0f, (float)(sz / cells[region]) * cm);
            centre.y = ground.HeightAt(centre.x, centre.z);
            float span = Mathf.Sqrt(cells[region] / Mathf.PI) * cm;
            Vector3 crest = default, peak = default;
            float bestCrest = float.MinValue, bestPeak = 0f, maxD = 0f;
            int samples = 0;
            float step = Mathf.Max(2f, spec.Spacing / 24f);
            for (float z = centre.z - span; z <= centre.z + span; z += step)
                for (float x = centre.x - span; x <= centre.x + span; x += step)
                {
                    float d = Dunes.At(cellsView, x, z);
                    samples++;
                    maxD = Mathf.Max(maxD, d);
                    if (d < 0.3f * spec.Height) continue;
                    float r = Mathf.Sqrt((x - centre.x) * (x - centre.x) + (z - centre.z) * (z - centre.z)) / span;
                    if (r < 0.6f && d > bestPeak) { bestPeak = d; peak = new Vector3(x, 0f, z); }
                    float score = d - spec.Height * 2f * Mathf.Abs(r - 0.55f);
                    if (score > bestCrest) { bestCrest = score; crest = new Vector3(x, 0f, z); }
                }
            cam.fieldOfView = 55f;
            for (var w = ShowHour(DunesHour, DunesMoon); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            string info = $"dunes: {specCount} fields, field {region} {cells[region]} cells ({cells[region] * cm * cm / 1e6f:F2} km2) height {spec.Height:F1} spacing {spec.Spacing:F0} {(spec.Linear != 0 ? "linear" : "barchan")} centre {centre.x:F0},{centre.z:F0} span {span:F0} cm {cm:F2} n {n}, scan {samples} samples max {maxD:F1} m, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
            if (bestCrest > float.MinValue)
            {
                var eye = new Vector3(crest.x, ground.HeightAt(crest.x, crest.z) + 1.7f, crest.z);
                var dirFlat = new Vector3(centre.x - eye.x, 0f, centre.z - eye.z).normalized;
                var aim = eye + dirFlat * span - new Vector3(0f, 0.06f * span, 0f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                for (var w = Settle(240); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "dunes-crest");
                info += $", crest eye {eye.x:F0},{eye.y:F0},{eye.z:F0} dune {Dunes.At(cellsView, crest.x, crest.z):F1} m";
            }
            else info += ", no crest";
            if (bestPeak > 0f)
            {
                float reach = 1.5f * bestPeak / Dunes.SlipSlope, fall = 0f;
                Vector3 slip = Vector3.forward;
                for (int k = 0; k < 32; k++)
                {
                    float a = k * Mathf.PI / 16f;
                    var s = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float drop = bestPeak - Dunes.At(cellsView, peak.x + s.x * 0.5f * reach, peak.z + s.z * 0.5f * reach);
                    if (drop > fall) { fall = drop; slip = s; }
                }
                var side = new Vector3(-slip.z, 0f, slip.x);
                var foot = peak + slip * (0.9f * reach);
                var e = foot + side * (5f * bestPeak) + slip * (0.6f * bestPeak);
                e.y = ground.HeightAt(e.x, e.z) + 1.7f;
                var mid = peak + slip * (0.45f * reach);
                var look = new Vector3(mid.x, ground.HeightAt(mid.x, mid.z), mid.z);
                cam.transform.SetPositionAndRotation(e, Quaternion.LookRotation(look - e));
                for (var w = Settle(240); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "dunes-slip");
                info += $", slip eye {e.x:F0},{e.y:F0},{e.z:F0} peak {bestPeak:F1} m drop {fall:F1} m";
            }
            else info += ", no slip face";
            GameStatus.Lines["~dunes"] = info;
        }
    }
}
