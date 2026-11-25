using System.Collections.Generic;

namespace Stabby.World
{
    public static class ListPool
    {
        static class Free<T>
        {
            public static readonly Stack<List<T>> Lists = new Stack<List<T>>();
        }

        public static List<T> Rent<T>()
        {
            var free = Free<T>.Lists;
            lock (free) return free.Count > 0 ? free.Pop() : new List<T>();
        }

        public static void Return<T>(List<T> list)
        {
            list.Clear();
            var free = Free<T>.Lists;
            lock (free) free.Push(list);
        }
    }
}
