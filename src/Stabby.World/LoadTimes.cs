using System.Collections.Generic;
using System.Diagnostics;

namespace Stabby.World
{
    public sealed class LoadTimes
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly List<string> parts = new List<string>();
        long last;

        public void Mark(string phase)
        {
            long now = sw.ElapsedMilliseconds;
            lock (parts) { parts.Add($"{phase} {now - last}"); last = now; }
        }

        public void Add(string phase, LoadTimes inner)
        {
            if (inner == null) return;
            long now = sw.ElapsedMilliseconds;
            lock (parts) { parts.Add($"{phase} wait {now - last} ({inner})"); last = now; }
        }

        public override string ToString()
        {
            lock (parts) return string.Join(", ", parts);
        }
    }
}
