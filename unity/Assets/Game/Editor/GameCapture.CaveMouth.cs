using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly Vector3 MouthEye = new Vector3(7618f, 363f, 7050f), MouthLook = new Vector3(7612f, 360f, 7058f), CliffLift = new Vector3(0f, 1.6f, 0f);

        static System.Collections.IEnumerator CaveMouthTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            if (GameSettings.ActiveSeed != 6) { GameStatus.Lines["~cavemouth"] = GameStatus.Failure = "cavemouth: needs STABBY_SEED=6"; yield break; }
            cam.fieldOfView = 60f;
            var poses = new[] { ("mouth", MouthEye, MouthLook), ("cliff", MouthLook + CliffLift, MouthLook + CliffLift + (MouthLook - MouthEye)) };
            foreach (var (name, eye, at) in poses)
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
                for (var w = Settle(30); w.MoveNext();) yield return null;
                Shot(cam, dir, "cavemouth-" + name);
                GeometryShot(cam, dir, "cavemouth-" + name + "-unlit");
            }
            GameStatus.Lines["~cavemouth"] = $"cavemouth: seed 6, hour {PinnedMidday}, full moon, eye {MouthEye} to {MouthLook}";
        }
    }
}
