using System.Collections.Generic;

namespace Stabby.Game
{
    public static class GameStatus
    {
        public static readonly Dictionary<string, string> Lines = new Dictionary<string, string>();
        public static readonly HashSet<string> Synced = new HashSet<string>();
        public static int Capsules, TerrainBehind, ToolFrame = -100, ToolFrames, TerrainVertices;
        public static float TerrainMs;
        public static bool TerrainBuilt, HoldCamera;
        public static string Failure, TerrainStage;
        public static int Frames, SlowFrames;
        public static float WorstMs, WorstPlayMs;
        public static string WorstWhy;

        public static void PutFrames()
        {
            if (Frames > 0) Lines["~frames"] = $"frames {Frames}, worst {WorstMs:F0} ms ({WorstWhy}), {SlowFrames} over 33 ms, worst after terrain built {WorstPlayMs:F0} ms, {ToolFrames} capture frames left out";
        }
    }
}
