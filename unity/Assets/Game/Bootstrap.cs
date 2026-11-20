using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine.SceneManagement;

namespace Stabby.Game
{
    // Only the Game scene runs netcode; the viewer keeps a plain local world.
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
            // Loopback only, so the spike never asks for a firewall exception.
            DefaultListenAddress = NetworkEndpoint.LoopbackIpv4;
            AutoConnectPort = GameSettings.Port;
            CreateServerWorld("Server");
            foreach (var name in GameSettings.Clients) CreateClientWorld(name);
            return true;
        }
    }

    public static class GameSettings
    {
        public const string Scene = "Game";
        public const ushort Port = 7979;
        public const ulong Seed = 3;
        public const int Size = 512;
        public const int PieceBytes = 1000;
        // The reliable pipeline holds about 32 packets in flight per connection.
        public const int PiecesPerTick = 24;
        public const float WalkSpeed = 6f;
        public static readonly string[] Clients = { "Client1", "Client2" };
        // Client2 generates the wrong seed on purpose so it must stream the whole map.
        public const string FullStreamClient = "Client2";
        public const string ViewClient = "Client1";
    }
}
