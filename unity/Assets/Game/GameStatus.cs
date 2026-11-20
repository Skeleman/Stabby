using System.Collections.Generic;

namespace Stabby.Game
{
    // Read by the batch capture; one line per world.
    public static class GameStatus
    {
        public static readonly Dictionary<string, string> Lines = new Dictionary<string, string>();
        public static readonly HashSet<string> Synced = new HashSet<string>();
        public static int Capsules;
        public static bool TerrainBuilt;
        public static string Failure;
    }
}
