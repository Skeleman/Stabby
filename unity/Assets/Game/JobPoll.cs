using Unity.Jobs;

namespace Stabby.Game
{
    public static class JobPoll
    {
        public static bool Done(JobHandle handle)
        {
            if (!handle.IsCompleted) return false;
            handle.Complete();
            return true;
        }

        public static void Teardown(JobHandle handle) => handle.Complete();
    }
}
