using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace RecDiffy.Fix;

/// <summary>FIX messages flattened into the rows of docs/FIX-RECIPE.md, one list per reconciliation, plus what was skipped or rejected.</summary>
public sealed record FixRows(
    IReadOnlyList<FrozenDictionary<string, string?>> Orders,
    IReadOnlyList<FrozenDictionary<string, string?>> Fills,
    IReadOnlyList<FrozenDictionary<string, string?>> Allocations,
    IReadOnlyList<FrozenDictionary<string, string?>> Trades,
    IReadOnlyList<FixSkipped> Skipped,
    IReadOnlyList<FixRejected> Rejected)
{
    /// <summary>Reads FIX text strictly and flattens every accepted message.</summary>
    public static FixRows Read(string text, FixOptions options) => From(FixReader.Read(text, options), options);

    /// <summary>Flattens readings in order. Order, allocation and trade chains are followed across messages.</summary>
    public static FixRows From(IEnumerable<FixReading> readings, FixOptions options)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(options);
        var done = readings.Aggregate(Flat.Empty, (flat, reading) => reading switch
        {
            FixAccepted accepted => Message(flat, accepted.Message, options),
            FixRejected rejected => flat with { Rejected = flat.Rejected.Add(rejected) },
            _ => flat,
        });
        return new FixRows(done.Orders, done.Fills, done.Allocations, done.Trades, done.Skipped, done.Rejected);
    }

    private static Flat Message(Flat flat, FixMessage message, FixOptions options) =>
        Counterparty(message, options.OurCompId) switch
        {
            null => Reject(flat, message, "neither SenderCompID (49) nor TargetCompID (56) is ours", options),
            var counterparty => message.MsgType switch
            {
                "0" or "1" or "2" or "3" or "4" or "5" or "A" => Skip(flat, message, "session message"),
                "j" => Skip(flat, message, "business message reject"),
                "D" or "F" or "G" => Skip(flat, message, "a request; only acknowledgements become rows"),
                "9" => Skip(flat, message, "cancel or replace rejected; the order stands as it was"),
                "Q" => Skip(flat, message, "don't know trade; the fill stays one-sided until it is busted"),
                "P" => Skip(flat, message, "allocation ack; the broker's side is read from its allocation report (AS)"),
                "AR" => Skip(flat, message, "trade capture report ack"),
                "8" => Execution(flat, message, counterparty, options),
                "J" or "AS" => Allocation(flat, message, counterparty, options),
                "AE" => Trade(flat, message, counterparty, options),
                _ => Skip(flat, message, "not a message type the recipe reconciles"),
            },
        };

    private static Flat Execution(Flat flat, FixMessage message, string counterparty, FixOptions options)
    {
        var legacy = string.CompareOrdinal(message.BeginString, "FIX.4.3") < 0 && message.BeginString.StartsWith("FIX.4", StringComparison.Ordinal);
        var kind = (legacy, ExecType: message.Get(150), TransType: message.Get(20)) switch
        {
            (true, _, "3") or (false, "I", _) => Kind.Skip("order status reply"),
            (true, _, "1") or (false, "H", _) => Kind.Fill("CANC"),
            (true, _, "2") or (false, "G", _) => Kind.Fill("CORR"),
            (_, "1" or "2", "0" or null) or (false, "F", _) => Kind.Fill("NEW"),
            (_, "0", _) => Kind.Order("NEW"),
            (_, "5" or "D", _) => Kind.Order("CORR"),
            (_, "4" or "C", _) => Kind.Order("CANC"),
            (_, "A" or "6" or "E", _) => Kind.Skip("pending; the change isn't accepted yet"),
            (_, "3", _) => Kind.Skip("done for day"),
            (_, "8", _) => Kind.Skip("order rejected; it never stood"),
            (_, "7" or "9", _) => Kind.Skip("stopped or suspended"),
            _ => Kind.Skip("an execution report the recipe doesn't reconcile"),
        };
        return kind switch
        {
            { Skipped: { } reason } => Skip(flat, message, reason),
            { IsFill: true } => Fill(flat, message, counterparty, kind.Action, options),
            _ => OrderEvent(flat, message, counterparty, kind.Action, options),
        };
    }

    private static Flat OrderEvent(Flat flat, FixMessage message, string counterparty, string action, FixOptions options)
    {
        var clOrdId = message.Get(11) ?? "";
        var execId = message.Get(17) ?? "";
        var priorKey = (counterparty, message.Get(150) is "5" || (message.Get(150) is "4" && message.Get(41) is not null) ? message.Get(41) ?? "" : clOrdId);
        var prior = flat.OrderStates.GetValueOrDefault(priorKey);
        if (action is not "NEW" && prior is null)
            return Reject(flat, message, $"{action} for an order not seen before: {priorKey.Item2}", options);
        if (Nanos(message.Get(60)) is { Ok: false })
            return Reject(flat, message, "TransactTime (60) is not a UTCTimestamp", options);
        var root = prior?.Root ?? clOrdId;
        var row = Row(
            ("ExecID", execId), ("ClOrdID", clOrdId), ("OrigClOrdID", message.Get(41)), ("Root", root), ("ExecType", message.Get(150)),
            ("Action", action), ("Target", prior?.LastExecId), ("Counterparty", counterparty), ("Account", message.Get(1)),
            ("Symbol", message.Get(55)), ("Side", Side(message.Get(54))), ("OrderQty", message.Get(38)), ("Price", message.Get(44)),
            ("OrdStatus", message.Get(39)), ("TransactNanos", Nanos(message.Get(60)).Value));
        return flat with
        {
            Orders = flat.Orders.Add(row),
            OrderStates = flat.OrderStates.SetItem((counterparty, clOrdId), new OrderState(root, execId)),
        };
    }

    private static Flat Fill(Flat flat, FixMessage message, string counterparty, string action, FixOptions options)
    {
        if (action is not "NEW" && message.Get(19) is null)
            return Reject(flat, message, "a trade correction or bust without ExecRefID (19)", options);
        if (Nanos(message.Get(60)) is { Ok: false })
            return Reject(flat, message, "TransactTime (60) is not a UTCTimestamp", options);
        var clOrdId = message.Get(11) ?? "";
        var row = Row(
            ("ExecID", message.Get(17)), ("ClOrdID", clOrdId), ("Root", flat.OrderStates.GetValueOrDefault((counterparty, clOrdId))?.Root ?? clOrdId),
            ("Action", action), ("Target", action is "NEW" ? null : message.Get(19)), ("Counterparty", counterparty), ("Account", message.Get(1)),
            ("Symbol", message.Get(55)), ("Side", Side(message.Get(54))), ("LastQty", message.Get(32)), ("LastPx", message.Get(31)),
            ("TransactNanos", Nanos(message.Get(60)).Value));
        return flat with { Fills = flat.Fills.Add(row) };
    }

    private static Flat Allocation(Flat flat, FixMessage message, string counterparty, FixOptions options)
    {
        var allocId = message.Get(70) ?? "";
        var action = message.Get(71) switch { "1" => "CORR", "2" => "CANC", _ => "NEW" };
        var prior = action is "NEW" ? null : flat.AllocationStates.GetValueOrDefault((counterparty, message.Get(72) ?? ""));
        if (action is not "NEW" && prior is null)
            return Reject(flat, message, $"{action} for an allocation not seen before: {message.Get(72)}", options);
        var accounts = message.GetAll(79);
        var quantities = message.GetAll(80);
        if (accounts.Count != quantities.Count)
            return Reject(flat, message, "each allocation account (79) needs a quantity (80)", options);
        var entries = accounts.Count > 0 || prior is null
            ? accounts.Zip(quantities, (account, quantity) => (Account: account, Quantity: (string?)quantity)).ToArray()
            : [.. prior.Accounts.Select(account => (Account: account, Quantity: (string?)null))];
        var root = prior?.Root ?? allocId;
        var rows = entries.Select(entry => Row(
            ("AllocRow", $"{allocId}/{entry.Account}"), ("AllocID", allocId), ("AllocRoot", root), ("Action", action),
            ("Target", prior is null ? null : $"{message.Get(72)}/{entry.Account}"), ("Counterparty", counterparty),
            ("AllocAccount", entry.Account), ("AllocQty", entry.Quantity), ("AvgPx", message.Get(6))));
        return flat with
        {
            Allocations = flat.Allocations.AddRange(rows),
            AllocationStates = flat.AllocationStates.SetItem((counterparty, allocId), new AllocationState(root, [.. entries.Select(entry => entry.Account)])),
        };
    }

    private static Flat Trade(Flat flat, FixMessage message, string counterparty, FixOptions options)
    {
        if (message.Get(856) is "3")
            return Skip(flat, message, "trade report declined");
        var reportId = message.Get(571) ?? "";
        var action = message.Get(487) switch { "2" => "CORR", "1" or "4" or "5" => "CANC", _ => "NEW" };
        var prior = action is "NEW" ? null : flat.TradeRoots.GetValueOrDefault((counterparty, message.Get(572) ?? ""));
        if (action is not "NEW" && prior is null)
            return Reject(flat, message, $"{action} for a trade report not seen before: {message.Get(572)}", options);
        var root = prior ?? reportId;
        var row = Row(
            ("TradeReportID", reportId), ("TradeRoot", root), ("Action", action), ("Target", action is "NEW" ? null : message.Get(572)),
            ("Counterparty", counterparty), ("Account", message.Get(1)), ("Symbol", message.Get(55)), ("Side", Side(message.Get(54))),
            ("LastQty", message.Get(32)), ("LastPx", message.Get(31)));
        return flat with { Trades = flat.Trades.Add(row), TradeRoots = flat.TradeRoots.SetItem((counterparty, reportId), root) };
    }

    private static string? Counterparty(FixMessage message, string ourCompId) =>
        (message.Get(49), message.Get(56)) switch
        {
            (var sender, var target) when string.Equals(sender, ourCompId, StringComparison.Ordinal) => target,
            (var sender, var target) when string.Equals(target, ourCompId, StringComparison.Ordinal) => sender,
            _ => null,
        };

    private static string? Side(string? side) =>
        side switch
        {
            "1" => "BUY",
            "2" => "SELL",
            "5" => "SELL_SHORT",
            "6" => "SELL_SHORT_EXEMPT",
            _ => side,
        };

    // UTCTimestamp, yyyyMMdd-HH:mm:ss with an optional .sss, .ssssss or .sssssssss, as nanoseconds since the Unix epoch.
    // Decimal throughout: a double can't hold every nanosecond of a current timestamp.
    internal static (bool Ok, string? Value) Nanos(string? timestamp)
    {
        if (timestamp is null)
            return (true, null);
        var fraction = timestamp.Length > 17 && timestamp[17] == '.' ? timestamp[18..] : "";
        var wellFormed = (timestamp.Length == 17 || fraction.Length is 3 or 6 or 9) && fraction.All(char.IsAsciiDigit);
        if (!wellFormed || !DateTime.TryParseExact(timestamp[..Math.Min(17, timestamp.Length)], "yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var moment))
            return (false, null);
        var seconds = (moment.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerSecond;
        var nanos = (seconds * 1_000_000_000m) + (fraction.Length == 0 ? 0m : decimal.Parse(fraction.PadRight(9, '0'), NumberStyles.None, CultureInfo.InvariantCulture));
        return (true, nanos.ToInvariant());
    }

    private static FrozenDictionary<string, string?> Row(params (string Column, string? Value)[] cells) =>
        cells.ToFrozenDictionary(cell => cell.Column, cell => cell.Value, StringComparer.Ordinal);

    private static Flat Skip(Flat flat, FixMessage message, string reason) => flat with { Skipped = flat.Skipped.Add(new FixSkipped(message, reason)) };

    private static Flat Reject(Flat flat, FixMessage message, string reason, FixOptions options) =>
        flat with { Rejected = flat.Rejected.Add(FixReader.Reject(new FixRejected(message.Text, reason), options)) };

    private sealed record OrderState(string Root, string LastExecId);

    private sealed record AllocationState(string Root, ImmutableArray<string> Accounts);

    private sealed record Kind(string Action, bool IsFill, string? Skipped)
    {
        public static Kind Fill(string action) => new(action, true, null);

        public static Kind Order(string action) => new(action, false, null);

        public static Kind Skip(string reason) => new("", false, reason);
    }

    private sealed record Flat(
        ImmutableList<FrozenDictionary<string, string?>> Orders,
        ImmutableList<FrozenDictionary<string, string?>> Fills,
        ImmutableList<FrozenDictionary<string, string?>> Allocations,
        ImmutableList<FrozenDictionary<string, string?>> Trades,
        ImmutableList<FixSkipped> Skipped,
        ImmutableList<FixRejected> Rejected,
        ImmutableDictionary<(string Counterparty, string ClOrdId), OrderState> OrderStates,
        ImmutableDictionary<(string Counterparty, string AllocId), AllocationState> AllocationStates,
        ImmutableDictionary<(string Counterparty, string ReportId), string> TradeRoots)
    {
        public static Flat Empty { get; } = new([], [], [], [], [], [], ImmutableDictionary<(string, string), OrderState>.Empty, ImmutableDictionary<(string, string), AllocationState>.Empty, ImmutableDictionary<(string, string), string>.Empty);
    }
}
