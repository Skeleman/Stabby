using Unity.Entities;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    public partial class DrawSubmitSystem : SystemBase
    {
        CreatureDrawSystem creatures;

        protected override void OnUpdate()
        {
            creatures ??= World.GetExistingSystemManaged<CreatureDrawSystem>();
            if (creatures != null && creatures.Enabled) creatures.Submit();
        }
    }
}
