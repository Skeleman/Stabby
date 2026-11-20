using Unity.Entities;
using Unity.NetCode;

namespace Stabby.Game
{
    public static class Rpc
    {
        public static void Send<T>(EntityCommandBuffer ecb, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, rpc);
            ecb.AddComponent(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }
}
