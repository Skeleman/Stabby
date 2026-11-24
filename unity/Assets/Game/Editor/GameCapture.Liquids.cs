using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly string[] LiquidShots = { "liquids-bog-close", "liquids-bog-mid" };

        static System.Collections.IEnumerator LiquidsTour(Camera cam, string dir)
        {
            for (int v = 0; v < LiquidShots.Length; v++)
            {
                if (!AimBog(cam, v)) { GameStatus.Lines["~" + LiquidShots[v]] = "no bog pool placed"; continue; }
                for (var s = Settle(180); s.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, LiquidShots[v]);
            }
            ClientMapSystem sys = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (s?.Map == null || q.CalculateEntityCount() != 1) continue;
                sys = s;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (sys == null) { GameStatus.Lines["~falls"] = "no map"; yield break; }
            for (var f = FallsTour(cam, dir, sys.Map, ground, 1); f.MoveNext();) yield return null;
        }
    }
}
