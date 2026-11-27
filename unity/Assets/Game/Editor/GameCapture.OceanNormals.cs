using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float NormalsBeachHour = 15f, NormalsGlintPitch = 9f;

        static System.Collections.IEnumerator OceanNormalsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            for (var w = ShowHour(NormalsBeachHour); w.MoveNext();) yield return null;
            if (!AimBeach(cam, 0, true)) { GameStatus.Lines["~oceannormals"] = GameStatus.Failure = "oceannormals: no beach"; yield break; }
            for (var s = Settle(240); s.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "oceannormals-beach");
            var beach = cam.transform.position;
            for (var w = ShowHour(OceanGlitter); w.MoveNext();) yield return null;
            var eye = new Vector3(OceanGlitterPose.x, OceanEyeM, OceanGlitterPose.y);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(OceanGlitterPose.z, 0, OceanGlitterPose.w)) * Quaternion.Euler(NormalsGlintPitch, 0, 0));
            for (var s = Settle(240); s.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "oceannormals-glint");
            GameStatus.Lines["~oceannormals"] = $"oceannormals: beach eye {beach.x:F0},{beach.y:F0},{beach.z:F0} hour {NormalsBeachHour}; glint eye {eye.x:F0},{eye.y:F0},{eye.z:F0} hour {OceanGlitter} pitch {NormalsGlintPitch}; moon full";
        }
    }
}
