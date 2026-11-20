using Unity.Entities;
using Unity.NetCode;

namespace Stabby.Game
{
    public static class Rpc
    {
        // A null connection means the server on a client, every connection on the server.
        public static void Send<T>(EntityCommandBuffer ecb, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, rpc);
            ecb.AddComponent(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }
}
