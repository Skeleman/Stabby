using Unity.NetCode;
using Unity.Networking.Transport;
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
        public const int PiecesPerTick = 24;
        public const float WalkSpeed = 6f;
        public static readonly string[] Clients = { "Client1", "Client2" };
        public const string FullStreamClient = "Client2";
        public const string ViewClient = "Client1";
    }
}
