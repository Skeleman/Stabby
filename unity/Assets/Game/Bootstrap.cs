using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine.SceneManagement;

namespace Stabby.Game
{
    [UnityEngine.Scripting.Preserve]
    public sealed class GameBootstrap : ClientServerBootstrap
    {
        public override bool Initialize(string defaultWorldName)
        {
            if (SceneManager.GetActiveScene().name != GameSettings.Scene)
            {
                CreateLocalWorld(defaultWorldName);
                return true;
            }
            UnityEngine.Application.runInBackground = true;
            DefaultListenAddress = NetworkEndpoint.LoopbackIpv4;
            AutoConnectPort = GameSettings.ActivePort;
            NetworkStreamReceiveSystem.DriverConstructor = new WideWindowDriver();
            CreateServerWorld("Server");
            foreach (var name in GameSettings.Clients) CreateClientWorld(name);
            return true;
        }
    }

    public sealed class WideWindowDriver : INetworkStreamDriverConstructor
    {
        public void CreateClientDriver(Unity.Entities.World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
        {
            var s = DefaultDriverBuilder.GetNetworkClientSettings();
            s.WithReliableStageParameters(windowSize: GameSettings.ReliableWindow);
            DefaultDriverBuilder.RegisterClientDriver(world, ref driverStore, netDebug, s);
        }

        public void CreateServerDriver(Unity.Entities.World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
        {
            var s = DefaultDriverBuilder.GetNetworkServerSettings();
            s.WithReliableStageParameters(windowSize: GameSettings.ReliableWindow);
            DefaultDriverBuilder.RegisterServerDriver(world, ref driverStore, netDebug, s);
        }
    }

    public static class GameSettings
    {
        public const string Scene = "Game";
        public const ushort Port = 7979;
        public const string PortVariable = "STABBY_PORT";
        public static ushort ActivePort => ushort.TryParse(System.Environment.GetEnvironmentVariable(PortVariable), out var p) && p != 0 ? p : Port;
        public const ulong Seed = 3;
        public const string SeedVariable = "STABBY_SEED";
        public static ulong ActiveSeed => ulong.TryParse(System.Environment.GetEnvironmentVariable(SeedVariable), out var s) && s != 0 ? s : Seed;
        public const int Size = 2048;
        public const int PieceBytes = 1024;
        public const int PiecesPerTick = 256, BacklogBytes = 96 * 1024;
        public const int ReliableWindow = 128;
        public const float WalkSpeed = 6f, SprintSpeed = WalkSpeed * 1.8f, JumpSpeed = 7f, Gravity = 20f;
        public static float GraphicsQuality = 1f, GraphicsScale = 1f;
        static Stabby.World.QualityLevel graphicsLevel;
        public static string GraphicsLevel = "";
        public const string QualityVariable = "STABBY_QUALITY";

        public static void UseQuality(Stabby.World.QualityDef def)
        {
            var level = def.Pick(System.Environment.GetEnvironmentVariable(QualityVariable));
            graphicsLevel = level;
            GraphicsLevel = level.Id;
            GraphicsQuality = GraphicsScale = level.Scale;
        }

        public static void UseLines(float lines)
        {
            if (graphicsLevel != null) GraphicsQuality = graphicsLevel.ScaleAt(lines);
        }
        public static readonly string[] Clients = { "Client1", "Client2" };
        public const string FullStreamClient = "Client2";
        public const string ViewClient = "Client1";
    }
}
