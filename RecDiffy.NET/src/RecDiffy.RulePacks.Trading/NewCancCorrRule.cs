namespace RecDiffy.RulePacks.Trading;

internal sealed class NewCancCorrFactory : ILifecycleFactory
{
    public string RuleId => "lifecycle.newCancCorr";
    public LifecycleBinding Bind(RuleArguments args) => new()
    {
        RuleId = RuleId,
        CorrPointsAt = args.Optional("corrTarget") ?? "CANC",
        Apply = NewCancCorr.Resolve,
    };
}

internal static class NewCancCorr
{
    public static List<LifeDecision> Resolve(IReadOnlyList<LifeEvent> events, string corrPointsAt)
    {
        var decisions = new List<LifeDecision>();
        var byKey = new Dictionary<(string Scope, string Id), LifeEvent>();
        var duplicate = new HashSet<(string Scope, string Id)>();
        foreach (var evt in events)
        {
            if (!byKey.TryAdd((evt.Scope, evt.Id), evt))
                duplicate.Add((evt.Scope, evt.Id));
        }

        var links = new Dictionary<(string Scope, string Id), List<(string Scope, string Id)>>();
        void Touch((string Scope, string Id) key) => links.TryAdd(key, []);
        foreach (var evt in events)
        {
            var key = (evt.Scope, evt.Id);
            Touch(key);
            if (string.IsNullOrEmpty(evt.Target))
                continue;
            var target = (evt.Scope, evt.Target);
            Touch(target);
            links[key].Add(target);
            links[target].Add(key);
        }

        var seen = new HashSet<(string Scope, string Id)>();
        foreach (var start in links.Keys)
        {
            if (!seen.Add(start))
                continue;
            var component = new List<(string Scope, string Id)>();
            var stack = new Stack<(string Scope, string Id)>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                component.Add(current);
                foreach (var neighbor in links[current])
                {
                    if (seen.Add(neighbor))
                        stack.Push(neighbor);
                }
            }

            var members = component.Where(byKey.ContainsKey).Select(key => byKey[key]).ToList();
            if (members.Count == 0 || (members.Count == 1 && members[0].Action == "NEW" && string.IsNullOrEmpty(members[0].Target)))
                continue;
            if (members.Any(member => duplicate.Contains((member.Scope, member.Id))))
            {
                foreach (var member in members)
                    decisions.Add(new LifeDecision(member.Source, duplicate.Contains((member.Scope, member.Id)) ? LifeFate.Quarantine : LifeFate.Quarantine, duplicate.Contains((member.Scope, member.Id)) ? "DuplicateConflict" : "ChainMemberQuarantined", false));
                continue;
            }
            if (members.Any(member => !string.IsNullOrEmpty(member.Target) && !byKey.ContainsKey((member.Scope, member.Target))))
            {
                Quarantine(decisions, members, "MissingLifecycleTarget");
                continue;
            }
            if (HasCycle(members))
            {
                Quarantine(decisions, members, "LifecycleCycle");
                continue;
            }

            var superseded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var corr in members.Where(member => member.Action == "CORR" && !string.IsNullOrEmpty(member.Target)))
            {
                var predecessor = corr.Target!;
                if (byKey.TryGetValue((corr.Scope, predecessor), out var target) && target.Action == "CANC" && !string.IsNullOrEmpty(target.Target))
                    predecessor = target.Target;
                superseded.Add(predecessor);
                _ = corrPointsAt;
            }
            var cancelled = members.Where(member => member.Action == "CANC" && !string.IsNullOrEmpty(member.Target)).Select(member => member.Target!).ToHashSet(StringComparer.Ordinal);
            var survivors = members.Where(member => member.Action != "CANC" && !superseded.Contains(member.Id) && !cancelled.Contains(member.Id)).ToList();
            if (survivors.Count > 1)
            {
                Quarantine(decisions, members, "LifecycleConflict");
                continue;
            }
            if (survivors.Count == 0)
            {
                foreach (var member in members)
                    decisions.Add(new LifeDecision(member.Source, LifeFate.FullyCancelled, null, false));
                continue;
            }
            foreach (var member in members)
            {
                var fate = member.Id == survivors[0].Id ? LifeFate.Survivor : member.Action == "CANC" ? LifeFate.CancelEvent : LifeFate.Superseded;
                decisions.Add(new LifeDecision(member.Source, fate, null, true, survivors[0].Source));
            }
        }
        return decisions;
    }

    private static bool HasCycle(IReadOnlyList<LifeEvent> members)
    {
        var color = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Walk(string id, string scope)
        {
            color.TryGetValue(id, out var state);
            if (state == 1)
                return true;
            if (state == 2)
                return false;
            color[id] = 1;
            var evt = members.FirstOrDefault(member => member.Scope == scope && member.Id == id);
            if (evt?.Target is string target && members.Any(member => member.Id == target) && Walk(target, scope))
                return true;
            color[id] = 2;
            return false;
        }
        return members.Any(member => Walk(member.Id, member.Scope));
    }

    private static void Quarantine(List<LifeDecision> decisions, IReadOnlyList<LifeEvent> members, string code)
    {
        foreach (var member in members)
            decisions.Add(new LifeDecision(member.Source, LifeFate.Quarantine, code, false));
    }
}
