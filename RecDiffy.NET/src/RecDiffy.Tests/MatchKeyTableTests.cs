using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #396. The matcher composes every item's key once for a run and the indexes read it from a table. MatchKeys.Compose
// stays as the key composed afresh, and it is the oracle here: a key read from the table is the same key, or the same
// absence of one, for every pass shape, and an index built from the table is the index built without it.
public class MatchKeyTableTests
{
    private static readonly Dictionary<string, FieldDeclaration> Schema = new(StringComparer.Ordinal)
    {
        ["Party"] = new("Party", FieldType.Symbol, FieldRole.Key, 0),
        ["Ref"] = new("Ref", FieldType.Identifier, FieldRole.Identity, 0),
        ["Other"] = new("Other", FieldType.Identifier, FieldRole.Key, 0),
        ["Day"] = new("Day", FieldType.Date, FieldRole.Key, 0),
        ["Qty"] = new("Qty", FieldType.Decimal, FieldRole.Economic, 2),
        ["Price"] = new("Price", FieldType.Decimal, FieldRole.Economic, 6),
        ["Units"] = new("Units", FieldType.Decimal, FieldRole.Economic, 0),
        ["Note"] = new("Note", FieldType.Text, FieldRole.Text, 0),
    };

    // Every shape a pass can ask for: one field, many, none, a repeated field, each type, an identifier first, last and
    // twice, and each of them with and without the shared-only flag.
    private static readonly string[][] FieldLists =
    [
        [],
        ["Party"],
        ["Ref"],
        ["Day"],
        ["Qty"],
        ["Note"],
        ["Party", "Ref"],
        ["Ref", "Party"],
        ["Ref", "Other"],
        ["Party", "Day", "Qty"],
        ["Party", "Day", "Price"],
        ["Party", "Day"],
        ["Qty", "Qty"],
        ["Units", "Price", "Qty"],
        ["Party", "Ref", "Other", "Day", "Qty", "Price", "Units", "Note"],
    ];

    private static readonly MatchKeys.Spec[] Specs = [.. FieldLists.SelectMany(fields => new MatchKeys.Spec[] { new(fields, false), new(fields, true) })];

    private static readonly string[] Schemes = ["shared", "ISIN", "broker", "Broker", "cusip|1", ""];

    // Text that holds every character a key escapes, the separator between scheme and value, and a backslash.
    private static readonly string[] Tokens = ["A", "B", "", " ", "A\u001fB", "A\u001eB", "A|B", "A,B", "A=B", "A\\B", "A\\\u001fB", "\r\n", "ünï", "A\u001f", "\u001fA"];

    [Fact]
    public void A_key_read_from_the_table_is_the_key_composed_afresh_for_every_pass_shape()
    {
        for (var seed = 0; seed < 24; seed++)
        {
            var items = Items(seed, 60);
            var workspace = Workspace(items, MatchKeys.Table.Build(items, Schema, Specs));

            foreach (var spec in Specs)
            {
                var read = workspace.Keys.Reader(workspace, spec.Fields, spec.SharedOnly);
                Assert.All(items, item => Assert.Equal(MatchKeys.Compose(item, spec.Fields, Schema, spec.SharedOnly), read(item.Index)));
            }
        }
    }

    // The generated rows must reach both answers under every shape, or the test above compares nothing.
    [Fact]
    public void The_generated_rows_give_keys_and_missing_keys_under_every_shape_with_a_field()
    {
        var items = Enumerable.Range(0, 24).SelectMany(seed => Items(seed, 60)).ToArray();

        Assert.All(
            Specs.Where(spec => spec.Fields.Count > 0),
            spec => Assert.Equal(new[] { false, true }, items.Select(item => MatchKeys.Compose(item, spec.Fields, Schema, spec.SharedOnly) is null).Distinct().Order()));
    }

    // The table was built without these fields, or with the other flag: the index still gets the right keys.
    [Fact]
    public void Fields_the_table_was_not_built_for_are_composed_afresh()
    {
        var items = Items(7, 60);
        var workspace = Workspace(items, MatchKeys.Table.Build(items, Schema, [new(["Party", "Day"], false), new(["Ref"], true)]));

        foreach (var spec in Specs)
        {
            var read = workspace.Keys.Reader(workspace, spec.Fields, spec.SharedOnly);
            Assert.All(items, item => Assert.Equal(MatchKeys.Compose(item, spec.Fields, Schema, spec.SharedOnly), read(item.Index)));
        }
    }

    // A copy of the workspace with other items carries the first table along. It must not read another row's key.
    [Fact]
    public void A_workspace_with_other_items_or_another_schema_does_not_read_the_table()
    {
        var first = Items(1, 60);
        var others = Items(2, 60);
        var workspace = Workspace(first, MatchKeys.Table.Build(first, Schema, Specs));
        var copies = new[]
        {
            workspace with { Items = others },
            workspace with { Schema = new Dictionary<string, FieldDeclaration>(Schema, StringComparer.Ordinal) { ["Qty"] = new("Qty", FieldType.Decimal, FieldRole.Economic, 4) } },
        };

        foreach (var copy in copies)
        {
            foreach (var spec in Specs)
            {
                var read = copy.Keys.Reader(copy, spec.Fields, spec.SharedOnly);
                Assert.All(copy.Items, item => Assert.Equal(MatchKeys.Compose(item, spec.Fields, copy.Schema, spec.SharedOnly), read(item.Index)));
            }
        }
    }

    // Composing afresh asks the schema for a field only when there is a row. With no row, an unknown field is not an error.
    [Fact]
    public void With_no_item_the_table_does_not_ask_the_schema_for_a_field()
    {
        ActiveItem[] none = [];

        var workspace = Workspace(none, MatchKeys.Table.Build(none, Schema, [new(["NotDeclared"], false)]));

        Assert.Empty(PairIndex.Create("P", ["NotDeclared"], true, workspace, new MatchCounters()).Index.ReadyProposals);
    }

    [Fact]
    public void With_an_item_an_unknown_field_fails_as_composing_afresh_fails()
    {
        var items = Items(3, 4);

        Assert.Throws<KeyNotFoundException>(() => MatchKeys.Compose(items[0], ["NotDeclared"], Schema));
        Assert.Throws<KeyNotFoundException>(() => MatchKeys.Table.Build(items, Schema, [new(["NotDeclared"], false)]));
    }

    // The index a pair pass builds from the table is the index it builds from keys composed afresh.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_pair_index_is_the_same_with_and_without_the_table(bool veto, bool sharedOnly)
    {
        for (var seed = 0; seed < 12; seed++)
        {
            var items = Items(seed, 120);
            foreach (var fields in FieldLists)
            {
                var cached = PairIndex.Create("P", fields, veto, Workspace(items, MatchKeys.Table.Build(items, Schema, Specs)), new MatchCounters(), sharedOnly).Index;
                var fresh = PairIndex.Create("P", fields, veto, Workspace(items, MatchKeys.Table.Empty), new MatchCounters(), sharedOnly).Index;

                Assert.Equal(Proposals(fresh), Proposals(cached));
                Assert.Equal(Clusters(fresh), Clusters(cached));
            }
        }
    }

    [Fact]
    public void A_group_index_is_the_same_with_and_without_the_table()
    {
        for (var seed = 0; seed < 12; seed++)
        {
            var items = Items(seed, 120);
            foreach (var block in FieldLists)
            {
                var cached = GroupIndex.Create("G", block, ["Units"], Side.Left, 8, Workspace(items, MatchKeys.Table.Build(items, Schema, Specs)), new MatchCounters());
                var fresh = GroupIndex.Create("G", block, ["Units"], Side.Left, 8, Workspace(items, MatchKeys.Table.Empty), new MatchCounters());

                Assert.Equal(Proposals(fresh.Index), Proposals(cached.Index));
                Assert.Equal(Clusters(fresh.Index).Order(StringComparer.Ordinal), Clusters(cached.Index).Order(StringComparer.Ordinal));
                Assert.Equal(fresh.Counters, cached.Counters);
            }
        }
    }

    // #403 item 4. The format text of a decimal key comes from a table of "F0" to "F28"; it used to be built per key.
    [Fact]
    public void A_decimal_key_is_fixed_point_at_the_field_scale_for_every_scale()
    {
        decimal[] numbers = [0m, -0.00m, 1.5m, 1.50m, -2.345m, 0.0000000000000000000000000001m, 79228162514264337593543950335m, 123456.789012345678901234567m];

        foreach (var scale in Enumerable.Range(0, 31))
        {
            var field = new FieldDeclaration("N", FieldType.Decimal, FieldRole.Economic, scale);
            Assert.All(numbers, number => Assert.Equal(
                number.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                MatchKeys.ValueText(Row("row", Side.Left).Write("N", CanonicalCell.FromDecimal(number), "test", null, null), field)));
        }
    }

    private static string[] Proposals(PassIndex index) =>
        [.. index.ReadyProposals.Select(proposal => $"{proposal.Key}: {string.Join(' ', proposal.Left)} / {string.Join(' ', proposal.Right)}")];

    private static string[] Clusters(PassIndex index) =>
        [.. index.Residue().Select(cluster => $"{cluster.Alignment} {cluster.Code} {cluster.Key}: {string.Join(' ', cluster.Rows.Select(row => row.Context.RowKey))}")];

    private static MatchWorkspace Workspace(ActiveItem[] items, MatchKeys.Table keys) =>
        new("P", items, items.Select(item => item.Index).ToImmutableHashSet(), Schema, SearchLimits.Default, "Ref", SearchLimits.Default.MaxGroupSize, CancellationToken.None) { Keys = keys };

    // Few distinct values per field, so rows share keys and the indexes have pairs, contests and groups to find.
    private static ActiveItem[] Items(int seed, int count)
    {
        var random = new Random((seed * 7919) + 13);
        return [.. Enumerable.Range(0, count).Select(index => Item(index, random))];
    }

    private static ActiveItem Item(int index, Random random)
    {
        var side = random.Next(2) is 0 ? Side.Left : Side.Right;
        var id = $"{(side is Side.Left ? "L" : "R")}{index.ToInvariant()}";
        var row = Row(id, side);
        row = Maybe(random) ? Symbol(row, "Party", Tokens[random.Next(Tokens.Length)]) : row;
        row = Maybe(random) ? Identifier(row, "Ref", Schemes[random.Next(Schemes.Length)], Tokens[random.Next(4)]) : row;
        row = Maybe(random) ? Identifier(row, "Other", Schemes[random.Next(Schemes.Length)], Tokens[random.Next(Tokens.Length)]) : row;
        row = Maybe(random) ? row.Write("Day", CanonicalCell.FromDate(new DateOnly(2025, 6, 1 + random.Next(3))), "test", null, null) : row;
        row = Maybe(random) ? row.Write("Qty", CanonicalCell.FromDecimal(random.Next(2) is 0 ? -0.00m : random.Next(-3, 4) / 2m), "test", null, null) : row;
        row = Maybe(random) ? row.Write("Price", CanonicalCell.FromDecimal(random.Next(4) / 8m), "test", null, null) : row;
        row = Maybe(random) ? row.Write("Units", CanonicalCell.FromDecimal(1 + random.Next(3)), "test", null, null) : row;
        row = random.Next(8) switch
        {
            0 => row,
            1 => row.Write("Note", CanonicalCell.Null(FieldType.Text), "test", null, null),
            _ => row.Write("Note", CanonicalCell.FromText(Tokens[random.Next(Tokens.Length)]), "test", null, null),
        };
        return new ActiveItem(index, side, new ConsolidatedRow([new SourceRef(side is Side.Left ? "left" : "right", id, index)], row, false, null, null));
    }

    // One row in seven leaves a field out.
    private static bool Maybe(Random random) => random.Next(7) is not 0;

    private static RowContext Row(string id, Side side) =>
        RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, id) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), "test", null, value);
    }

    private static RowContext Identifier(RowContext row, string field, string scheme, string value)
    {
        var (pool, id) = row.Pool.InternIdentifier(scheme, "", value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Identifier, id), "test", null, value);
    }
}
