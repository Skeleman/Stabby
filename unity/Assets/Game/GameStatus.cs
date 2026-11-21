using System.Collections.Generic;

namespace Stabby.Game
{
    public static class GameStatus
    {
        public static readonly Dictionary<string, string> Lines = new Dictionary<string, string>();
        public static readonly HashSet<string> Synced = new HashSet<string>();
        public static int Capsules, TerrainBehind, ToolFrame = -100, ToolFrames;
        public static bool TerrainBuilt, HoldCamera;
        public static string Failure;
    }
}
