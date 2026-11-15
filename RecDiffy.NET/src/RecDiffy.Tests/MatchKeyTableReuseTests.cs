using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #396. CascadeMatcher.Run composes every pass's keys once, in a table, and each pass's index reads them from it. An
// index asks the table by its own fields, and a table that was built for other fields answers by composing afresh: the
// keys are still right, so no result shows that the reuse was lost. CascadeMatcher lists the fields of each pass kind
// twice, once to build the pass and once to build the table, and this is the guard that the two lists agree.
//
// It runs the real matcher and counts what the calling thread allocates, so machine load does not move it. Every row
// has a key text of 20,000 characters. A pass listed twice builds its index twice and composes its keys once, so the
// second copy must cost far less than one key text a row. If the table held the wrong fields for that pass kind, each
// copy would compose its own keys, and the second copy would cost more than a whole key text a row.
public class MatchKeyTableReuseTests
{
    private const int Rows = 48;
    private const int KeyLength = 20_000;

    // A .NET string takes two bytes a character.
    private const long KeyBytes = 2L * KeyLength;

    private static readonly FieldDeclaration[] Fields =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Book", FieldType.Symbol, FieldRole.Key, 0),
        new("TradeRef", FieldType.Identifier, FieldRole.Identity, 0),
        new("Note", FieldType.Text, FieldRole.Text, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
    ];

    // One declaration for each way CascadeMatcher builds a pass. Every key holds the long field: TradeRef or Note.
    private static readonly Dictionary<string, PassDeclaration> Passes = new(StringComparer.Ordinal)
    {
        ["match.sharedRef with no key"] = new("P", "match.sharedRef", [], [], [], null, null),
        ["match.sharedRef with a declared key"] = new("P", "match.sharedRef", ["Book", "TradeRef"], [], [], null, null),
        ["match.naturalKey"] = new("P", "match.naturalKey", ["Counterparty", "Note"], [], [], null, null),
        ["match.reducedKey"] = new("P", "match.reducedKey", ["Book", "Note"], [], [], null, null),
        ["match.groupSum"] = new("P", "match.groupSum", [], ["Counterparty", "Note"], ["Quantity"], "left", null),
    };

    public static TheoryData<string> Kinds => [.. Passes.Keys.Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_second_pass_with_the_same_key_reads_the_keys_the_run_already_composed(string kind)
    {
        var pass = Passes[kind];
        PassDeclaration[] once = [pass];
        PassDeclaration[] twice = [pass, pass with { Id = "Q" }];
        _ = Allocated(once);
        _ = Allocated(twice);

        var one = Allocated(once);
        var two = Allocated(twice);
        var second = two - one;

        // The run does compose a key for every row: without this, a pass that keyed no row would pass the bound below.
        Assert.True(one >= Rows * KeyBytes, $"{kind}: one pass allocated {one:N0} bytes, less than one key text ({KeyBytes:N0} bytes) for each of {Rows} rows.");
        Assert.True(
            second < Rows * KeyBytes / 2,
            $"{kind}: a second pass with the same key allocated {second:N0} bytes more, {second / Rows:N0} a row. A key text is {KeyBytes:N0} bytes, so the second pass composed its keys again instead of reading the table.");
    }

    private static long Allocated(PassDeclaration[] passes)
    {
        var rows = Left();
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = CascadeMatcher.Run(rows, [], passes, Fields, SearchLimits.Default);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // Left rows only, each with its own reference and note: every row has a key under every pass, and nothing commits.
    private static ConsolidatedRow[] Left() =>
        [.. Enumerable.Range(0, Rows).Select(index => Row($"L{index.ToInvariant()}", new string('k', KeyLength) + index.ToInvariant()))];

    private static ConsolidatedRow Row(string id, string text)
    {
        var row = RowContext.Create(id, Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "Counterparty", "CP");
        row = Symbol(row, "Book", "BK");
        var (pool, reference) = row.Pool.InternIdentifier("shared", "", text);
        row = row.WithPool(pool).Write("TradeRef", CanonicalCell.FromInterned(FieldType.Identifier, reference), "test", null, text);
        row = row.Write("Note", CanonicalCell.FromText(text), "test", null, null);
        row = row.Write("Quantity", CanonicalCell.FromDecimal(1m), "test", null, null);
        return new ConsolidatedRow([new SourceRef("left", id, 0)], row, false, null, null);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, id) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), "test", null, value);
    }
}
