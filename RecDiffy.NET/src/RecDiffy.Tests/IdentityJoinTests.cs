using System.Text.Json;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class IdentityJoinTests
{
    private const string Nasty = "A\u001f\\B\r\nC|D,E";

    [Fact]
    public void Match_keys_keep_adversarial_parts_apart()
    {
        var crossed = Compose(Nasty, "Z");
        var other = Compose("A", "Z" + Nasty);

        Assert.NotEqual(crossed, other);
        Assert.Equal(crossed, Compose(Nasty, "Z"));
    }

    [Fact]
    public void Lifecycle_links_ids_that_contain_adversarial_characters()
    {
        var plan = new LifecyclePlan("EventAction", "EventTarget", "Counterparty", "CANC");
        var rows = new[]
        {
            Event("CORR" + Nasty, "CORR", "CANC" + Nasty, Nasty),
            Event("NEW" + Nasty, "NEW", null, Nasty),
            Event("CANC" + Nasty, "CANC", "NEW" + Nasty, Nasty),
        };
        var result = Consolidation.Run(rows, plan, null).ToDictionary(row => row.Context.RowKey, StringComparer.Ordinal);

        Assert.Null(result["CORR" + Nasty].Suppression);
        Assert.Equal(SuppressionReason.Superseded, result["NEW" + Nasty].Suppression);
        Assert.Equal(SuppressionReason.CancelEvent, result["CANC" + Nasty].Suppression);
    }

    [Fact]
    public void Fingerprints_keep_an_adversarial_cell_apart_from_two_cells()
    {
        var one = new RawRecord(new SourceRef("left", "1", 0), new Dictionary<string, object?>(StringComparer.Ordinal) { ["A"] = Nasty });
        var two = new RawRecord(new SourceRef("left", "2", 0), new Dictionary<string, object?>(StringComparer.Ordinal) { ["A"] = "A", ["B"] = Nasty });

        Assert.NotEqual(one.Fingerprint(), two.Fingerprint(), StringComparer.Ordinal);
        Assert.Equal(one.Fingerprint(), new RawRecord(new SourceRef("left", "3", 0), new Dictionary<string, object?>(StringComparer.Ordinal) { ["A"] = Nasty }).Fingerprint());
    }

    [Fact]
    public void Group_ids_keep_adversarial_source_and_row_keys_apart()
    {
        var crossed = ResultBuilder.Identify([new SourceRef(Nasty, "Z", 0)]);
        var other = ResultBuilder.Identify([new SourceRef("A", Nasty, 0)]);

        Assert.NotEqual(crossed, other);
        Assert.Equal(crossed, ResultBuilder.Identify([new SourceRef(Nasty, "Z", 0)]));
    }

    [Fact]
    public void Profile_hash_keeps_a_comma_inside_one_list_value()
    {
        var glued = Compile(JsonSerializer.Serialize(new[] { "Bond,Swap" }));
        var split = Compile(JsonSerializer.Serialize(new[] { "Bond", "Swap" }));
        var nasty = Compile(JsonSerializer.Serialize(new[] { Nasty }));

        Assert.NotEqual(glued.Hash, split.Hash, StringComparer.Ordinal);
        Assert.NotEqual(nasty.Hash, split.Hash, StringComparer.Ordinal);
        Assert.Equal(nasty.Hash, Compile(JsonSerializer.Serialize(new[] { Nasty })).Hash);
    }

    [Fact]
    public void Side_by_side_keeps_pipe_parts_in_different_cells()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Grid), RuleRegistry.CreateCore());
        Dictionary<string, string> Stock(string id, string desk, string book, string quantity) => new(StringComparer.Ordinal)
        {
            ["Id"] = id,
            ["Desk"] = desk,
            ["Book"] = book,
            ["Quantity"] = quantity,
        };
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", new[] { Stock("1", "A|B", "C", "1"), Stock("2", "A", "B|C", "10") }, GridMap()),
            RecordSources.FromObjects("book", Array.Empty<Dictionary<string, string>>(), GridMap()),
            ReferenceDataSet.Empty);
        var view = result.BuildSideBySide(new SideBySideSpec(["Desk", "Book"], [], [new MeasureSpec("Quantity")], null));

        Assert.Equal(2, view.Lines.Length);
        Assert.Equal(["1", "10"], view.Lines.Select(line => line.Cells["Quantity"].Left.ToString(System.Globalization.CultureInfo.InvariantCulture)).OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public void Identity_joins_live_in_Delimited_or_the_allowlist()
    {
        var root = FindLibrary();
        var hits = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "Delimited.cs")
            .SelectMany(path => File.ReadAllLines(path).Select((text, index) => (File: Relative(root, path), Line: index + 1, Text: text)))
            .Where(hit => IsIdentityJoin(hit.Text))
            .Select(hit => hit.File + ":" + hit.Line + ":" + hit.Text.Trim())
            .ToArray();
        var unexpected = hits.Where(hit => Allowed.Any(item => hit.Contains(item, StringComparison.Ordinal)) is false).ToArray();

        Assert.True(unexpected.Length == 0, string.Join('\n', unexpected));
    }

    private static bool IsIdentityJoin(string line) =>
        line.Contains("string.Join", StringComparison.Ordinal)
        || line.Contains("StringBuilder", StringComparison.Ordinal)
        || (line.Contains("\\u001f", StringComparison.Ordinal) && line.Contains("Delimited.Join", StringComparison.Ordinal) is false);

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string FindLibrary()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "src", "RecDiffy")) is false)
            dir = dir.Parent;
        return dir is null ? throw new DirectoryNotFoundException("library source") : Path.Combine(dir.FullName, "RecDiffy.NET", "src", "RecDiffy");
    }

    private static CompiledProfile Compile(string key) =>
        Reconciler.Compile(ProfileDocument.Parse(Profile(key)), RuleRegistry.CreateCore());

    private static MatchKey? Compose(string first, string second)
    {
        var schema = new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal)
        {
            ["A"] = new("A", FieldType.Symbol, FieldRole.Key, 0),
            ["B"] = new("B", FieldType.Symbol, FieldRole.Key, 0),
        };
        var row = RowContext.Create("row", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "A", first);
        row = Symbol(row, "B", second);
        return MatchKeys.Compose(new ActiveItem(0, Side.Left, new ConsolidatedRow([new SourceRef("left", "row", 0)], row, false, null, null)), ["A", "B"], schema);
    }

    private static IntakeRow Event(string id, string action, string? target, string scope)
    {
        var row = RowContext.Create(id, Side.Right, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "EventAction", action);
        row = Symbol(row, "Counterparty", scope);
        if (target is not null)
            row = row.Write("EventTarget", CanonicalCell.FromText(target), "test", target, target);
        return new IntakeRow([new SourceRef("right", id, 0)], row, false);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, id) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), "test", null, value);
    }

    private static SourceMap<Dictionary<string, string>> GridMap() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row["Id"])
            .Text("Desk", row => row["Desk"])
            .Text("Book", row => row["Book"])
            .Text("Quantity", row => row["Quantity"]);

    private static string Profile(string key) => $$"""
        {
          "formatVersion": 1,
          "profileId": "sample",
          "version": "1.0.0",
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku", "when": { "field": "Sku", "in": {{key}} } } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Sku"] }
          ] } ]
        }
        """;

    private const string Grid = """
        {
          "formatVersion": 1,
          "profileId": "grid",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Desk", "type": "Symbol", "role": "Key" },
            { "name": "Book", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Desk", "to": "Desk" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Desk", "to": "Desk" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "grid", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Desk"] }
          ] } ]
        }
        """;

    // Every composite key, class key and fingerprint goes through Delimited or MatchKeys. The allowlist is for joins that
    // are not an identity: a side-by-side display label (pipe-joined), and a trail key's value form, which is display only
    // (#338), so a value that holds the separator is shown as written and never has to round-trip.
    private static readonly string[] Allowed =
    [
        "string.Join('\\u001f', pair.Values!",
        "string.Join('\\u001f', cls.Values.Select",
        "string.Join(\"; \", errors.Select",
        "string.Join(\"|\", fields.Select",
        "string.Join(ValueSeparator, parts)",
        "var text = string.Join(",
    ];
}
