using System;
using System.Collections.Generic;
using System.Linq;

namespace SolastaCoop
{
    internal enum DialogueMode { Vanilla, FollowHost, RandomTie }

    // Pure policy: one vote per connected player, equal weights, stable candidate order.
    internal static class VotePolicy
    {
        internal static int[] Candidates(IDictionary<int, List<int>> votes, ISet<int> players,
            int host, DialogueMode mode, out bool everyoneVoted)
        {
            var seen = new HashSet<int>();
            var counts = new SortedDictionary<int, int>();
            int hostChoice = -1;
            foreach (var choice in votes.OrderBy(v => v.Key))
            {
                if (choice.Key < 0 || choice.Value == null) continue;
                foreach (int actor in choice.Value)
                {
                    if (!players.Contains(actor) || !seen.Add(actor)) continue;
                    if (actor == host) hostChoice = choice.Key;
                    if (!counts.ContainsKey(choice.Key)) counts[choice.Key] = 0;
                    counts[choice.Key]++;
                }
            }
            everyoneVoted = players.Count > 0 && players.All(seen.Contains);
            if (mode == DialogueMode.FollowHost)
            {
                everyoneVoted = hostChoice >= 0;
                return hostChoice < 0 ? new int[0] : new[] { hostChoice };
            }
            if (counts.Count == 0) return new int[0];
            int maximum = counts.Values.Max();
            return counts.Where(v => v.Value == maximum).Select(v => v.Key).ToArray();
        }
    }
}
