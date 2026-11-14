using BenchmarkDotNet.Attributes;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Benchmarks;

public enum PipelineStage
{
    Normalize,
    Consolidate,
    Packages,
    Match,
    Diff,
    Build,
}

// #377 P2: one class, one dataset shape, one input per stage, so the per-stage costs are comparable at the same size.
// Every stage runs its real internal entry point; only the work before it happens in setup, outside the measurement.
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchScale.LaunchCount, warmupCount: BenchScale.WarmupCount, iterationCount: BenchScale.IterationCount)]
public class PipelineStageBench
{
    [Params(BenchScale.Small, BenchScale.Large)]
    public int Rows { get; set; }

    [Params(
        PipelineStage.Normalize,
        PipelineStage.Consolidate,
        PipelineStage.Packages,
        PipelineStage.Match,
        PipelineStage.Diff,
        PipelineStage.Build)]
    public PipelineStage Stage { get; set; }

    private CompiledProfile _profile = null!;
    private RawRecord[] _rawLeft = null!;
    private RawRecord[] _rawRight = null!;
    private ConsolidatedRow[] _left = null!;
    private ConsolidatedRow[] _right = null!;
    private (PathDeclaration Path, MatchReport Match)[] _routed = null!;
    private DiffPlan _diffPlan = null!;
    private (RowContext Left, RowContext Right)[] _pairs = null!;
    private IntakeRow[] _lifecycleRows = null!;
    private LifecyclePlan _lifecyclePlan = null!;
    private ConsolidatedRow[] _packageRows = null!;
    private PackagePlan _packagePlan = null!;

    [GlobalSetup]
    public void Setup()
    {
        _profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        (_rawLeft, _rawRight) = RawTrades(Rows);

        var normalized = Intake.Normalize(_profile, _rawLeft, _rawRight, ReferenceDataSet.Empty);
        _left = [.. Consolidation.Run(normalized.Left, null, null)];
        _right = [.. Consolidation.Run(normalized.Right, null, null)];

        var path = _profile.Document.Paths[0];
        var match = CascadeMatcher.Run(_left, _right, path.Passes, _profile.Document.Fields, _profile.Document.Limits);
        _routed = [(path, match)];
        _diffPlan = DiffPlan.From(path, _profile.Document.Fields, _profile.Document.Midpoint);
        _pairs = [.. match.Committed.Select(group => (Left: group.Left[0].Context, Right: group.Right[0].Context))];

        _lifecyclePlan = new LifecyclePlan("EventAction", "EventTarget", "Counterparty", "CANC");
        _lifecycleRows = LifecycleEvents(Rows);

        _packagePlan = new PackagePlan(
            "LinkId",
            "LegRole",
            ["PAY", "REC"],
            [new FieldAggregate("Quantity", AggregateKind.Sum, null, null), new FieldAggregate("NetCash", AggregateKind.Sum, null, null)]);
        _packageRows = PackageLegs(Rows);
        Verify(match);
    }

    // The setup checks every stage's input once, so a dataset that drifts fails the run instead of being timed as
    // something else.
    private void Verify(MatchReport match)
    {
        var units = Math.Max(1, Rows / 2);
        var survivors = Consolidation.Run(_lifecycleRows, _lifecyclePlan, null).Count(row => row.Suppression is null);
        var packages = Packages.Pack(_packageRows, _packagePlan).Count;
        if (_left.Length != Rows || _right.Length != Rows || match.Committed.Count != Rows || _pairs.Length != Rows
            || survivors != units || packages != units)
        {
            throw new InvalidOperationException(
                $"PipelineStageBench at {Rows.ToInvariant()} rows: left {_left.Length.ToInvariant()}, right {_right.Length.ToInvariant()}, "
                + $"committed {match.Committed.Count.ToInvariant()}, pairs {_pairs.Length.ToInvariant()}, surviving lifecycle chains {survivors.ToInvariant()}, "
                + $"packages {packages.ToInvariant()}; expected {Rows.ToInvariant()}, {Rows.ToInvariant()}, {Rows.ToInvariant()}, "
                + $"{Rows.ToInvariant()}, {units.ToInvariant()}, {units.ToInvariant()}.");
        }
    }

    [Benchmark]
    public int Run() => Stage switch
    {
        PipelineStage.Normalize => Intake.Normalize(_profile, _rawLeft, _rawRight, ReferenceDataSet.Empty).Right.Count,
        PipelineStage.Consolidate => Consolidation.Run(_lifecycleRows, _lifecyclePlan, null).Count,
        PipelineStage.Packages => Packages.Pack(_packageRows, _packagePlan).Count,
        PipelineStage.Match => CascadeMatcher.Run(_left, _right, _profile.Document.Paths[0].Passes, _profile.Document.Fields, _profile.Document.Limits).Committed.Count,
        PipelineStage.Diff => Differences(),
        PipelineStage.Build => ResultBuilder.Build(_profile, ReferenceDataSet.Empty, _routed, _left, _right, new RunOptions(), CancellationToken.None).Entries.Length,
        _ => throw new InvalidOperationException("Unknown pipeline stage."),
    };

    private int Differences()
    {
        var total = 0;
        foreach (var (left, right) in _pairs)
            total += FieldDiffer.Compare(left, right, _diffPlan).Length;
        return total;
    }

    // Every tenth pair differs by one cent on the right side, so the differ has both scan-only and materializing work.
    private static (RawRecord[] Left, RawRecord[] Right) RawTrades(int rows)
    {
        var left = new RawRecord[rows];
        var right = new RawRecord[rows];
        for (var index = 0; index < rows; index++)
        {
            var quantity = 100m + (index % 97);
            var price = 12.5m + ((index % 7) * 0.01m);
            var cash = Math.Round(quantity * price, 2, MidpointRounding.ToEven);
            var breaking = index % 10 == 0;
            var rightQuantity = breaking ? quantity + 0.01m : quantity;
            var rightCash = breaking ? Math.Round(rightQuantity * price, 2, MidpointRounding.ToEven) : cash;
            left[index] = Raw(Side.Left, "L" + index.ToInvariant(), index, quantity, price, cash);
            right[index] = Raw(Side.Right, "R" + index.ToInvariant(), index, rightQuantity, price, rightCash);
        }

        return (left, right);
    }

    private static RawRecord Raw(Side side, string id, int ordinal, decimal quantity, decimal price, decimal cash) =>
        new(
            new SourceRef(side is Side.Left ? "left" : "right", id, ordinal),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Counterparty"] = "CP" + ((ordinal % 4) + 1).ToInvariant(),
                ["Instrument"] = "I" + ordinal.ToInvariant(),
                ["TradeDate"] = "2025-06-02",
                ["Side"] = "BUY",
                ["Quantity"] = quantity.ToInvariant("0.00"),
                ["Price"] = price.ToInvariant("0.000000"),
                ["NetCash"] = cash.ToInvariant("0.00"),
            });

    // One NEW event superseded by one CORR event, so each two-row chain collapses to a single survivor.
    private static IntakeRow[] LifecycleEvents(int rows)
    {
        var units = Math.Max(1, rows / 2);
        var events = new List<IntakeRow>(units * 2);
        for (var unit = 0; unit < units; unit++)
        {
            var id = "L" + unit.ToInvariant();
            var party = "CP" + ((unit % 4) + 1).ToInvariant();
            events.Add(Event(id, unit, party, "NEW", null));
            events.Add(Event(id + "-C", units + unit, party, "CORR", id));
        }

        return [.. events];
    }

    private static IntakeRow Event(string id, int ordinal, string counterparty, string action, string? target)
    {
        var row = RowContext.Create(id, Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "Counterparty", counterparty);
        row = Symbol(row, "EventAction", action);
        if (target is not null)
            row = Symbol(row, "EventTarget", target);
        return new IntakeRow([new SourceRef("left", id, ordinal)], row, false);
    }

    // One package per link, made of a PAY leg and a REC leg, so the sum aggregates have real work.
    private static ConsolidatedRow[] PackageLegs(int rows)
    {
        var units = Math.Max(1, rows / 2);
        var legs = new List<ConsolidatedRow>(units * 2);
        for (var unit = 0; unit < units; unit++)
        {
            var link = "LINK" + unit.ToInvariant();
            legs.Add(Leg("L" + unit.ToInvariant(), unit, link, "PAY", 60m, 750m));
            legs.Add(Leg("L" + unit.ToInvariant() + "-R", units + unit, link, "REC", 40m, 500m));
        }

        return [.. legs];
    }

    private static ConsolidatedRow Leg(string id, int ordinal, string link, string role, decimal quantity, decimal cash)
    {
        var row = RowContext.Create(id, Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "LinkId", link);
        row = Symbol(row, "LegRole", role);
        row = row.Write("Quantity", CanonicalCell.FromDecimal(quantity), "bench", null, null);
        row = row.Write("NetCash", CanonicalCell.FromDecimal(cash), "bench", null, null);
        return new ConsolidatedRow([new SourceRef("left", id, ordinal)], row, false, null, null);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, id) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), "bench", null, value);
    }

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "pipeline-stages",
          "version": "1.0.0",
          "culture": "",
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 2 },
            { "name": "Price", "type": "Decimal", "role": "Economic", "scale": 6 },
            { "name": "NetCash", "type": "Decimal", "role": "Economic", "scale": 2 }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "symbol.map", "from": "Side", "to": "Side" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": [ "yyyy-MM-dd" ] },
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "symbol.map", "from": "Side", "to": "Side" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": [ "yyyy-MM-dd" ] },
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
              ]
            }
          },
          "paths": [
            {
              "id": "all",
              "route": { "rule": "route.all" },
              "passes": [
                { "id": "P2", "pass": "match.naturalKey", "key": [ "Counterparty", "Instrument", "TradeDate", "Side", "Quantity" ] },
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": [ "Counterparty", "Instrument", "TradeDate", "Side", "Price" ], "conserve": [ "Quantity", "NetCash" ] },
                { "id": "P4", "pass": "match.reducedKey", "key": [ "Counterparty", "Instrument", "TradeDate", "Side" ] }
              ]
            }
          ]
        }
        """;
}
