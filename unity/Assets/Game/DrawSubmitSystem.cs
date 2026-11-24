using Unity.Entities;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    public partial class DrawSubmitSystem : SystemBase
    {
        CreatureDrawSystem creatures;
        LightPresenterSystem lights;

        protected override void OnUpdate()
        {
            creatures ??= World.GetExistingSystemManaged<CreatureDrawSystem>();
            lights ??= World.GetExistingSystemManaged<LightPresenterSystem>();
            if (creatures != null && creatures.Enabled) creatures.Submit();
            lights?.Submit();
        }
    }
}
