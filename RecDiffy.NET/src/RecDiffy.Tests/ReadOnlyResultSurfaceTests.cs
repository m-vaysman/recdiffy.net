using System.Collections;
using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.Tests;

public class ReadOnlyResultSurfaceTests
{
    [Fact]
    public void No_public_type_implements_the_result_interfaces()
    {
        var exported = typeof(Reconciler).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exported, Implements<IReconciliationResult>);
        Assert.DoesNotContain(exported, Implements<ISideBySideView>);
    }

    [Fact]
    public void Collection_properties_on_the_host_surface_are_immutable()
    {
        AssertProperty<IReconciliationResult, ImmutableArray<ResultEntry>>(nameof(IReconciliationResult.Entries));
        AssertProperty<IReconciliationResult, ImmutableArray<RowResult>>(nameof(IReconciliationResult.Rows));
        AssertProperty<ISideBySideView, ImmutableArray<SideBySideLine>>(nameof(ISideBySideView.Lines));
        AssertProperty<ResultEntry, ImmutableArray<SourceRef>>(nameof(ResultEntry.Left));
        AssertProperty<ResultEntry, ImmutableArray<SourceRef>>(nameof(ResultEntry.Right));
        AssertProperty<ResultEntry, ImmutableArray<FieldDiff>>(nameof(ResultEntry.Diffs));
        AssertProperty<PassAttempt, ImmutableArray<SourceRef>>(nameof(PassAttempt.Candidates));
        AssertProperty<SideBySideLine, ImmutableDictionary<string, SideBySideCell>>(nameof(SideBySideLine.Cells));
        AssertProperty<ProfileDocument, ImmutableDictionary<string, SourceReading>>(nameof(ProfileDocument.SourceReadings));
    }

    [Fact]
    public void A_run_does_not_hand_the_host_a_public_or_mutable_result()
    {
        var result = Run();
        var entry = Assert.Single(result.Entries, item => item.Alignment == Alignment.Matched);
        var view = result.BuildSideBySide(new SideBySideSpec([], [], [new MeasureSpec("Quantity")], null));
        var line = Assert.Single(view.Lines);

        Assert.False(result.GetType().IsPublic);
        Assert.False(view.GetType().IsPublic);
        Assert.Equal(typeof(ImmutableArray<ResultEntry>), result.Entries.GetType());
        Assert.Equal(typeof(ImmutableArray<RowResult>), result.Rows.GetType());
        Assert.Equal(typeof(ImmutableArray<SourceRef>), entry.Left.GetType());
        Assert.Equal(typeof(ImmutableArray<FieldDiff>), entry.Diffs.GetType());
        Assert.Equal(typeof(ImmutableArray<SourceRef>), Assert.Single(entry.Explanation.Trail).Candidates.GetType());
        Assert.Equal(typeof(ImmutableDictionary<string, SideBySideCell>), line.Cells.GetType());
        Assert.Throws<NotSupportedException>(() => ((IList<ResultEntry>)result.Entries).Add(entry));
        Assert.Throws<NotSupportedException>(() => ((IList<SourceRef>)entry.Left).Add(entry.Left[0]));
        Assert.Throws<NotSupportedException>(() => ((IList<FieldDiff>)entry.Diffs).Add(default));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, SideBySideCell>)line.Cells)["Quantity"] = line.Cells["Quantity"]);
    }

    [Fact]
    public void Parse_assigns_an_ordinal_ImmutableDictionary_of_source_readings()
    {
        var parsed = ProfileDocument.Parse(Readings);

        Assert.IsType<ImmutableDictionary<string, SourceReading>>(parsed.SourceReadings);
        Assert.Same(StringComparer.Ordinal, parsed.SourceReadings.KeyComparer);
        Assert.Equal(new SourceReading("de-DE", null), parsed.SourceReadings["left"]);
        Assert.Equal(new SourceReading("en-US", true), parsed.SourceReadings["Left"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, SourceReading>)parsed.SourceReadings)["left"] = new SourceReading("fr-FR", null));
        Assert.Equal("de-DE", parsed.SourceReadings["left"].Culture);
    }

    [Fact]
    public void A_document_built_in_code_defaults_source_readings_to_an_empty_ordinal_dictionary()
    {
        var document = new ProfileDocument
        {
            FormatVersion = 1,
            ProfileId = "built",
            Version = "1",
            Midpoint = MidpointMode.ToEven,
            Fields = [],
            Normalization = ImmutableDictionary<string, NormalizationSide>.Empty,
            Paths = [],
            SourceKeys = ImmutableDictionary<string, string>.Empty,
            Limits = SearchLimits.Default,
        };

        Assert.IsType<ImmutableDictionary<string, SourceReading>>(document.SourceReadings);
        Assert.Same(StringComparer.Ordinal, document.SourceReadings.KeyComparer);
        Assert.Empty(document.SourceReadings);
    }

    private static void AssertProperty<TOwner, TProperty>(string name) =>
        Assert.Equal(typeof(TProperty), typeof(TOwner).GetProperty(name)!.PropertyType);

    private static bool Implements<TInterface>(Type type) =>
        type != typeof(TInterface) && typeof(TInterface).IsAssignableFrom(type);

    private static IReconciliationResult Run()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        return Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [new Item("A", "4")], Map),
            RecordSources.FromObjects("right", [new Item("A", "4")], Map),
            ReferenceDataSet.Empty);
    }

    private static readonly SourceMap<Item> Map = new SourceMap<Item>()
        .Text("Id", row => row.Id)
        .Text("Quantity", row => row.Quantity);

    private sealed record Item(string Id, string Quantity);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "readonly-surface",
          "version": "1",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Id" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Id" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [
            { "id": "by-id", "pass": "match.naturalKey", "key": ["Id"] }
          ] } ]
        }
        """;

    private const string Readings = """
        {
          "formatVersion": 1,
          "profileId": "readings",
          "version": "1",
          "fields": [ { "name": "Id", "type": "Identifier", "role": "Key" } ],
          "paths": [ { "id": "all", "route": { "rule": "route.all" } } ],
          "sources": {
            "left": { "culture": "de-DE" },
            "Left": { "culture": "en-US", "strictDateFormats": true }
          }
        }
        """;
}
