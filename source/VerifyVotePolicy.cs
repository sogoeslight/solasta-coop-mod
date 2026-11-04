using System;
using System.Collections.Generic;
using System.Linq;
using SolastaCoop;

public static class VerifyVotePolicy
{
    private static int checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    public static int Main()
    {
        var players = new HashSet<int> { 7, 19 };
        bool everyone;
        var split = new Dictionary<int, List<int>> { { 4, new List<int> { 7 } }, { 9, new List<int> { 19 } } };
        Check(VotePolicy.Candidates(split, players, 7, DialogueMode.RandomTie, out everyone).SequenceEqual(new[] { 4, 9 }) && everyone,
            "Two different votes yield two equally eligible candidates, without host weight");
        Check(VotePolicy.Candidates(split, players, 19, DialogueMode.RandomTie, out everyone).SequenceEqual(new[] { 4, 9 }),
            "Changing the host does not change random-tie eligibility");
        var reverse = new Dictionary<int, List<int>> { { 9, new List<int> { 19 } }, { 4, new List<int> { 7 } } };
        Check(VotePolicy.Candidates(reverse, players, 7, DialogueMode.RandomTie, out everyone).SequenceEqual(new[] { 4, 9 }),
            "Candidate order is independent of vote arrival and dictionary order");
        var consensus = new Dictionary<int, List<int>> { { 9, new List<int> { 19, 7 } } };
        Check(VotePolicy.Candidates(consensus, players, 7, DialogueMode.RandomTie, out everyone).SequenceEqual(new[] { 9 }) && everyone,
            "Agreement always selects the agreed answer");
        var three = new HashSet<int> { 7, 19, 23 };
        split[9].Add(23);
        Check(VotePolicy.Candidates(split, three, 7, DialogueMode.RandomTie, out everyone).SequenceEqual(new[] { 9 }) && everyone,
            "A genuine two-to-one majority wins, even against the host");
        Check(VotePolicy.Candidates(split, three, 7, DialogueMode.FollowHost, out everyone).SequenceEqual(new[] { 4 }) && everyone,
            "Follow-host accepts the host's answer regardless of guest votes");
        var hostFirst = new Dictionary<int, List<int>> { { 4, new List<int> { 7 } } };
        Check(VotePolicy.Candidates(hostFirst, players, 7, DialogueMode.FollowHost, out everyone).SequenceEqual(new[] { 4 }) && everyone,
            "Follow-host does not require guest clicks");
        var guestFirst = new Dictionary<int, List<int>> { { 9, new List<int> { 19 } } };
        Check(VotePolicy.Candidates(guestFirst, players, 7, DialogueMode.FollowHost, out everyone).Length == 0 && !everyone,
            "Guest vote cannot resolve follow-host before the host chooses");
        Check(VotePolicy.Candidates(guestFirst, players, 7, DialogueMode.RandomTie, out everyone).SequenceEqual(new[] { 9 }) && !everyone,
            "Partial voting can provide a timeout candidate without pretending everyone voted");
        var malformed = new Dictionary<int, List<int>> { { -1, new List<int> { 7 } }, { 1, new List<int> { 7, 7, 99 } }, { 2, null } };
        Check(VotePolicy.Candidates(malformed, players, 7, DialogueMode.RandomTie, out everyone).SequenceEqual(new[] { 1 }) && !everyone,
            "Duplicate votes, disconnected actors, negative answers and null lists do not inflate totals");
        var empty = new Dictionary<int, List<int>>();
        Check(VotePolicy.Candidates(empty, players, 7, DialogueMode.RandomTie, out everyone).Length == 0 && !everyone,
            "No votes has no winner");
        Check(VotePolicy.Candidates(empty, new HashSet<int>(), 7, DialogueMode.RandomTie, out everyone).Length == 0 && !everyone,
            "An empty room cannot complete a vote");
        Console.WriteLine("Vote policy checks: " + checks + " PASS");
        return 0;
    }
}
