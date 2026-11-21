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
        public const int Size = 2048;
        public const int PieceBytes = 1024;
        public const int PiecesPerTick = 256, BacklogBytes = 96 * 1024;
        public const int ReliableWindow = 128;
        public const float WalkSpeed = 6f;
        public static float GraphicsQuality = 1f;
        public static readonly string[] Clients = { "Client1", "Client2" };
        public const string FullStreamClient = "Client2";
        public const string ViewClient = "Client1";
    }
}
