using System.Text.RegularExpressions;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// #338: PassAttempt.Key is person-facing, so it carries the value form. These gates fail if the matcher's key form, with
// its scheme and its record and unit separators, can reach it again.
public class TrailKeyFormGateTests
{
    private static readonly Regex Separator = new(
        @"[\u001e\u001f]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Ref", row => row.Key)
        .Text("Counterparty", row => row.Key)
        .Text("Book", row => row.Key)
        .Text("TradeRef", row => row.Value)
        .Text("Amount", row => row.Value)
        .Text("Quantity", row => row.Value);

    [Fact]
    public void No_trail_key_carries_a_match_key_separator_for_a_pair_a_shared_ref_or_a_group_pass()
    {
        var results = new[] { Pair(), SharedRef(), Group(committed: true), Group(committed: false) };
        var keys = results
            .SelectMany(result => result.Entries)
            .SelectMany(entry => entry.Explanation.Trail)
            .Select(attempt => attempt.Key)
            .OfType<string>()
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => Separator.IsMatch(key));
    }

    // A pair key reads as the value of its key field. A committed group reads as its target row's row key, which is a
    // value already and so stands as it is.
    [Fact]
    public void A_pair_key_reads_as_its_value_and_a_group_commit_reads_as_its_row_key()
    {
        var paired = Pair().Entries.SelectMany(entry => entry.Explanation.Trail).ToArray();
        var grouped = Group(committed: true).Entries.SelectMany(entry => entry.Explanation.Trail).ToArray();

        Assert.Equal("T-1", Assert.Single(paired).Key);
        Assert.Equal("R1", Assert.Single(grouped).Key);
    }

    // The engine builds a trail attempt in ResultBuilder only, and the key it puts there comes from MatchKeys.
    [Fact]
    public void PassAttempt_keys_are_built_only_in_ResultBuilder_and_only_through_MatchKeys()
    {
        const string builder = "RecDiffy/Results/ResultBuilder.cs";
        var sites = EngineSources()
            .SelectMany(file => file.Content.Split('\n').Select(line => (file.RelativePath, Text: line.Trim())))
            .Where(site => site.Text.Contains("new PassAttempt(", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(sites);
        Assert.All(sites, site => Assert.Equal(builder, site.RelativePath));
        Assert.All(sites, site => Assert.Contains("TrailKey(", site.Text, StringComparison.Ordinal));
    }

    private static ReconciliationResult Pair() =>
        Run(
            """
            {
              "formatVersion": 1,
              "profileId": "trail-pair",
              "version": "1.0.0",
              "culture": "",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Ref", "type": "Identifier", "role": "Key" },
                { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] },
                "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
            }
            """,
            [new("L1", "T-1", "100.00")],
            [new("R1", "T-1", "100.00")]);

    private static ReconciliationResult SharedRef() =>
        Run(
            """
            {
              "formatVersion": 1,
              "profileId": "trail-shared",
              "version": "1.0.0",
              "culture": "",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "TradeRef", "type": "Identifier", "role": "Identity" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" }, { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" } ] },
                "right": { "identify": [ { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" }, { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" } ] }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.sharedRef" } ] } ]
            }
            """,
            [new("L1", "ACME", "T-1")],
            [new("R1", "ACME", "T-1")]);

    // A groupSum commit pairs many left rows with one right row, so the key is the right row's row key. A contest has no
    // commit, so the key is the block's value form.
    private static ReconciliationResult Group(bool committed) =>
        Run(
            """
            {
              "formatVersion": 1,
              "profileId": "trail-group",
              "version": "1.0.0",
              "culture": "",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Book", "type": "Identifier", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
                "right": { "identify": [ { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.groupSum", "block": ["Book"], "conserve": ["Quantity"], "many": "left" } ] } ]
            }
            """,
            committed
                ? [new("L1", "RATES", "50.00"), new("L2", "RATES", "50.00")]
                : [new("L1", "RATES", "50.00"), new("L2", "RATES", "50.00"), new("L3", "RATES", "50.00")],
            [new("R1", "RATES", "100.00")]);

    private static ReconciliationResult Run(string profile, Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private static IReadOnlyList<(string RelativePath, string Content)> EngineSources()
    {
        var root = Path.Combine(ProfileCorpus.Source, "RecDiffy");
        return
        [
            .. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(file => file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) is false
                    && file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) is false)
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(file => (RelativePath: Path.GetRelativePath(ProfileCorpus.Source, file).Replace('\\', '/'), Content: File.ReadAllText(file))),
        ];
    }

    private sealed record Row(string Id, string Key, string Value);
}
