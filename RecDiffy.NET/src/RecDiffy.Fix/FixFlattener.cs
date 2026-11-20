using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace RecDiffy.Fix;

/// <summary>Which messages to keep, by tag value. A message is kept when every Include tag holds one of its values and no Exclude tag holds one of its values.</summary>
public sealed record FixFilter(
    IReadOnlyDictionary<int, IReadOnlyList<string>>? Include = null,
    IReadOnlyDictionary<int, IReadOnlyList<string>>? Exclude = null)
{
    public bool Keeps(FixMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return (Include ?? FrozenDictionary<int, IReadOnlyList<string>>.Empty).All(rule => message.Get(rule.Key) is { } value && rule.Value.Contains(value, StringComparer.Ordinal))
            && !(Exclude ?? FrozenDictionary<int, IReadOnlyList<string>>.Empty).Any(rule => message.Get(rule.Key) is { } value && rule.Value.Contains(value, StringComparer.Ordinal));
    }
}

/// <summary>How messages become rows.</summary>
/// <param name="Grain">The count tag of the repeating group whose entries become rows, such as 78 for accounts; null for one row per message.</param>
/// <param name="Filter">Which messages to keep; null keeps every application message.</param>
public sealed record FixFlattenOptions(int? Grain = null, FixFilter? Filter = null);

/// <summary>Flattened rows, and the messages that made none.</summary>
public sealed record FixFlat(
    IReadOnlyList<FrozenDictionary<string, string?>> Rows,
    IReadOnlyList<FixSkipped> Skipped,
    IReadOnlyList<FixRejected> Rejected);

/// <summary>
/// Turns accepted FIX messages into rows a profile reads. A row's columns are its tags, named by number ("55"), with nothing
/// renamed or interpreted. A row holds its message's fields and, at a group grain, its entry's fields and its enclosing entries'.
/// A nested group with exactly one entry is folded into the row; one with several entries is left to a finer grain.
/// Four columns are derived, because they need more than one message or a datatype rule:
/// <list type="bullet">
/// <item><c>Counterparty</c>: whichever of SenderCompID (49) and TargetCompID (56) isn't yours.</item>
/// <item><c>RootClOrdID</c> and <c>PrevExecID</c>, on execution reports: the first ClOrdID of the order's chain (followed through 41), and the ExecID of the previous kept execution report on that chain.</item>
/// <item><c>{tag}:ns</c>: a UTCTimestamp value as decimal nanoseconds since the Unix epoch.</item>
/// </list>
/// Session-level messages never become rows.
/// </summary>
public static class FixFlattener
{
    private static readonly FrozenSet<string> SessionTypes = FrozenSet.ToFrozenSet(["0", "1", "2", "3", "4", "5", "A"], StringComparer.Ordinal);

    public static FixFlat Read(string text, FixOptions options, FixFlattenOptions? flatten = null) =>
        Flatten(FixReader.Read(text, options), options, flatten);

    public static FixFlat Flatten(IEnumerable<FixReading> readings, FixOptions options, FixFlattenOptions? flatten = null)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(options);
        var settings = flatten ?? new FixFlattenOptions();
        var done = readings.Aggregate(State.Empty, (state, reading) => reading switch
        {
            FixAccepted accepted => Message(state, accepted.Message, options, settings),
            FixRejected rejected => state with { Rejected = state.Rejected.Add(rejected) },
            _ => state,
        });
        return new FixFlat(done.Rows, done.Skipped, done.Rejected);
    }

    private static State Message(State state, FixMessage message, FixOptions options, FixFlattenOptions settings)
    {
        if (SessionTypes.Contains(message.MsgType))
            return state with { Skipped = state.Skipped.Add(new FixSkipped(message, "session message")) };
        if (settings.Filter is { } filter && !filter.Keeps(message))
            return state with { Skipped = state.Skipped.Add(new FixSkipped(message, "filtered out")) };
        if (Counterparty(message, options.OurCompId) is not { } counterparty)
            return state with { Rejected = state.Rejected.Add(FixReader.Reject(new FixRejected(message.Text, "neither SenderCompID (49) nor TargetCompID (56) is ours"), options)) };
        var (chain, chains) = Chain(state.Chains, message, counterparty);
        var derived = (IReadOnlyList<(string Column, string? Value)>)[("Counterparty", counterparty), .. chain];
        var rows = Rows(FixTree.Parse(message.Fields), settings.Grain).Select(fields => Row(fields, derived));
        return state with { Rows = state.Rows.AddRange(rows), Chains = chains };
    }

    // An execution report continues the chain of its OrigClOrdID (41) when it has one, otherwise of its own ClOrdID (11).
    private static (IReadOnlyList<(string Column, string? Value)> Columns, ImmutableDictionary<(string, string), Link> Chains) Chain(
        ImmutableDictionary<(string, string), Link> chains, FixMessage message, string counterparty)
    {
        if (message.MsgType is not "8" || message.Get(11) is not { } clOrdId)
            return ([], chains);
        var prior = chains.GetValueOrDefault((counterparty, message.Get(41) ?? clOrdId));
        var root = prior?.Root ?? message.Get(41) ?? clOrdId;
        var next = message.Get(17) is { } execId ? chains.SetItem((counterparty, clOrdId), new Link(root, execId)) : chains;
        return ([("RootClOrdID", root), ("PrevExecID", prior?.LastExecId)], next);
    }

    private static IReadOnlyList<IReadOnlyList<FixField>> Rows(FixNode root, int? grain) =>
        grain is { } tag ? Entries(root, tag, []) : [Fold(root)];

    // Each entry of the grain group becomes a row: the enclosing levels' fields, then the entry's own.
    private static IReadOnlyList<IReadOnlyList<FixField>> Entries(FixNode node, int grain, IReadOnlyList<FixField> context) =>
        [
            .. node.Groups.SelectMany(group =>
            {
                var level = (IReadOnlyList<FixField>)[.. context, .. Fold(node, group.CountTag)];
                return group.CountTag == grain
                    ? group.Entries.Select(entry => (IReadOnlyList<FixField>)[.. level, .. Fold(entry)])
                    : group.Entries.SelectMany(entry => Entries(entry, grain, level));
            }),
        ];

    // A level's own fields, plus every nested group with exactly one entry.
    private static IReadOnlyList<FixField> Fold(FixNode node, int? except = null) =>
        [.. node.Fields, .. node.Groups.Where(group => group.CountTag != except && group.Entries.Count == 1).SelectMany(group => Fold(group.Entries[0]))];

    private static FrozenDictionary<string, string?> Row(IReadOnlyList<FixField> fields, IReadOnlyList<(string Column, string? Value)> derived)
    {
        var tagged = fields.Select(field => (Column: field.Tag.ToInvariant(), Value: (string?)field.Value));
        var timestamps = fields
            .Select(field => (field.Tag, Nanos: Nanos(field.Value)))
            .Where(field => field.Nanos is not null)
            .Select(field => (Column: $"{field.Tag.ToInvariant()}:ns", Value: field.Nanos));
        return tagged.Concat(timestamps).Concat(derived)
            .GroupBy(cell => cell.Column, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
    }

    private static string? Counterparty(FixMessage message, string ourCompId) =>
        (message.Get(49), message.Get(56)) switch
        {
            (var sender, var target) when string.Equals(sender, ourCompId, StringComparison.Ordinal) => target,
            (var sender, var target) when string.Equals(target, ourCompId, StringComparison.Ordinal) => sender,
            _ => null,
        };

    // UTCTimestamp: yyyyMMdd-HH:mm:ss, optionally .sss, .ssssss or .sssssssss. Anything else is not a timestamp and gets no column.
    // Decimal throughout: a double can't hold every nanosecond of a current timestamp.
    internal static string? Nanos(string value)
    {
        var fraction = value.Length > 18 && value[17] == '.' ? value[18..] : "";
        var shaped = (value.Length == 17 || fraction.Length is 3 or 6 or 9) && value.Length >= 17 && value[8] == '-' && fraction.All(char.IsAsciiDigit);
        if (!shaped || !DateTime.TryParseExact(value[..17], "yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var moment))
            return null;
        var seconds = (moment.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerSecond;
        var nanos = (seconds * 1_000_000_000m) + (fraction.Length == 0 ? 0m : decimal.Parse(fraction.PadRight(9, '0'), NumberStyles.None, CultureInfo.InvariantCulture));
        return nanos.ToInvariant();
    }

    private sealed record Link(string Root, string LastExecId);

    private sealed record State(
        ImmutableList<FrozenDictionary<string, string?>> Rows,
        ImmutableList<FixSkipped> Skipped,
        ImmutableList<FixRejected> Rejected,
        ImmutableDictionary<(string, string), Link> Chains)
    {
        public static State Empty { get; } = new([], [], [], ImmutableDictionary<(string, string), Link>.Empty);
    }
}
