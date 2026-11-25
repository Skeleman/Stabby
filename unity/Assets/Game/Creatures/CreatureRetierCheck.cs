#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Stabby.Game
{
    public partial class CreatureBuildSystem
    {
        static readonly bool VerifyRetier = Environment.GetEnvironmentVariable("STABBY_RETIER_VERIFY") == "1";
        readonly List<RetierCall> retierExpect = new List<RetierCall>();
        long retierPasses, retierChecks, retierMismatches;
        string retierFirst = "";

        partial void ExpectRetier()
        {
            if (!VerifyRetier) return;
            retierExpect.Clear();
            var budget = new FrameBudget(FrameBudget.CreatureEntities);
            var asks = asking.ToEntityArray(Allocator.Temp);
            foreach (var r in asks)
            {
                var c = EntityManager.GetComponentData<CreatureRoot>(r);
                var links = EntityManager.GetBuffer<CreatureLink>(r, true);
                int half = links.Length / 2;
                bool hasHigh = links[0].Value != Entity.Null, hasLow = links[half].Value != Entity.Null, waiting = false;
                var donor = hasHigh ? links[0].Value : links[half].Value;
                bool buildHigh = c.WantHigh && !hasHigh, buildLow = c.WantLow && !hasLow;
                var call = new RetierCall { Root = r, Want = (c.WantHigh ? 1 : 0) | (c.WantLow ? 2 : 0), Size = c.Scale.Length };
                if (donor != Entity.Null && (buildHigh || buildLow))
                {
                    var b = EntityManager.GetComponentData<CreatureFurBase>(donor).Value;
                    var a = EntityManager.GetComponentData<CreatureFurAccent>(donor).Value;
                    var d = EntityManager.GetComponentData<CreatureFurDark>(donor).Value;
                    if (buildHigh && budget.Take(half)) { call.Build |= 1; hasHigh = true; }
                    else waiting |= buildHigh;
                    if (buildLow && budget.Take(half)) { call.Build |= 2; hasLow = true; }
                    else waiting |= buildLow;
                    if (call.Build != 0) { call.Base = b; call.Accent = a; call.Dark = d; }
                }
                for (int tier = 0; tier < 2; tier++)
                {
                    int at = tier * half;
                    bool want = tier == 0 ? c.WantHigh : c.WantLow, other = tier == 0 ? hasLow : hasHigh;
                    if (want || links[at].Value == Entity.Null) continue;
                    if (!other) { waiting = true; continue; }
                    call.Drop |= 1 << tier;
                }
                call.Settled = waiting ? 0 : 1;
                retierExpect.Add(call);
            }
            asks.Dispose();
        }

        partial void CheckRetier()
        {
            if (!VerifyRetier) return;
            int bad = 0;
            if (retierCalls.Length != retierExpect.Count)
            {
                bad++;
                if (retierFirst.Length == 0) retierFirst = $" first: pass {retierPasses} calls job {retierCalls.Length} old {retierExpect.Count}";
            }
            int n = Math.Min(retierCalls.Length, retierExpect.Count);
            for (int i = 0; i < n; i++)
            {
                var j = retierCalls[i];
                var o = retierExpect[i];
                if (j.Root == o.Root && j.Want == o.Want && j.Build == o.Build && j.Drop == o.Drop && j.Settled == o.Settled && j.Size == o.Size
                    && j.Base.Equals(o.Base) && j.Accent.Equals(o.Accent) && j.Dark.Equals(o.Dark)) continue;
                bad++;
                if (retierFirst.Length == 0) retierFirst = $" first: pass {retierPasses} root {o.Root.Index} job {j.Build}/{j.Drop}/{j.Settled} old {o.Build}/{o.Drop}/{o.Settled}";
            }
            retierPasses++;
            retierChecks += retierExpect.Count;
            retierMismatches += bad;
            GameStatus.Lines["creature~retier"] = $"creature retier job verify: {retierPasses} passes, {retierChecks} root checks, {retierMismatches} mismatches{retierFirst}";
        }
    }
}
#endif
