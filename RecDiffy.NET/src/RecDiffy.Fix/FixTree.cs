namespace RecDiffy.Fix;

// A message's fields as a tree: the fields at one level, and the repeating groups that start there.
internal sealed record FixNode(IReadOnlyList<FixField> Fields, IReadOnlyList<FixGroup> Groups);

internal sealed record FixGroup(int CountTag, IReadOnlyList<FixNode> Entries);

internal static class FixTree
{
    public static FixNode Parse(IReadOnlyList<FixField> fields) => Level(fields, 0, null).Node;

    // A level runs until a field outside its group, or until its group's first field opens the next entry.
    // A loop keeps deep nesting off the call stack only as far as group depth, which FIX bounds.
    private static (FixNode Node, int Next) Level(IReadOnlyList<FixField> fields, int start, (IReadOnlySet<int> Members, int First)? group)
    {
        var direct = new List<FixField>();
        var groups = new List<FixGroup>();
        var position = start;
        while (position < fields.Count)
        {
            var tag = fields[position].Tag;
            if (group is { } open && (!open.Members.Contains(tag) || (position > start && tag == open.First)))
                break;
            direct.Add(fields[position]);
            position++;
            if (!FixReader.Groups.TryGetValue(tag, out var members))
                continue;
            var entries = new List<FixNode>();
            var memberSet = members.ToHashSet();
            while (position < fields.Count && fields[position].Tag == members[0])
            {
                var entry = Level(fields, position, (memberSet, members[0]));
                entries.Add(entry.Node);
                position = entry.Next;
            }

            groups.Add(new FixGroup(tag, entries));
        }

        return (new FixNode(direct, groups), position);
    }
}
