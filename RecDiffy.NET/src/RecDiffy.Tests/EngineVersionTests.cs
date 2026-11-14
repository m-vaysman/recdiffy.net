using System.Globalization;
using System.Reflection;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// #295: Run.EngineVersion is the package version, without build metadata. The assembly version drops the prerelease
// label, so every 1.2.0 preview reports 1.2.0.0 there; the informational version carries the full package version, and
// the PackageSmoke consumer check pins EngineVersion to the version it packed.
public class EngineVersionTests
{
    [Fact]
    public void Engine_version_is_the_informational_version_without_build_metadata()
    {
        var assembly = typeof(Reconciler).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrEmpty(informational));

        var engineVersion = Run().Run.EngineVersion;

        Assert.Equal(informational!.Split('+', 2)[0], engineVersion);
        Assert.False(engineVersion.Contains('+', StringComparison.Ordinal), $"'{engineVersion}' carries build metadata.");
    }

    [Fact]
    public void Engine_version_is_not_the_assembly_version_when_the_package_has_a_prerelease_label()
    {
        var assembly = typeof(Reconciler).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        if (!informational.Contains('-', StringComparison.Ordinal))
            return;

        // The assembly version is what every 1.2.0-preview.N package reported as 1.2.0.0 before #295.
        var assemblyVersion = assembly.GetName().Version!.ToString();
        var engineVersion = Run().Run.EngineVersion;
        Assert.False(string.Equals(assemblyVersion, engineVersion, StringComparison.Ordinal), $"EngineVersion is still the assembly version '{assemblyVersion}'.");
    }

    [Fact]
    public void Engine_version_reads_the_same_in_every_run_of_one_build()
    {
        Assert.Equal(Run().Run.EngineVersion, Run().Run.EngineVersion);
    }

    private static IReconciliationResult Run()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        return Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", Bins, Map()),
            RecordSources.FromObjects("book", Book, Map()),
            ReferenceDataSet.Empty);
    }

    private static SourceMap<BinRow> Map() =>
        new SourceMap<BinRow>()
            .Text("Id", row => row.Id)
            .Text("Sku", row => row.Sku)
            .Text("Quantity", row => row.Quantity);

    private static readonly BinRow[] Bins = [new("A", "SKU-1", "4"), new("B", "SKU-1", "6")];

    private static readonly BinRow[] Book = [new("BOOK", "SKU-1", "10")];

    private sealed record BinRow(string Id, string Sku, string Quantity);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "engine-version",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] }
          ] } ]
        }
        """;
}
