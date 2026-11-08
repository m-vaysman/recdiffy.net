using System.Collections.Immutable;
using System.Globalization;

namespace RecDiffy.Rules;

public enum RulePhase
{
    Identify = 1,
    Parse = 2,
    Derive = 3,
    Validate = 4,
}

public interface INormalizationRule
{
    string RuleId { get; }

    RulePhase Phase { get; }

    IReadOnlyList<FieldId> Reads { get; }

    IReadOnlyList<FieldId> Writes { get; }

    void Apply(ref RowContext row);
}

public sealed record RowContext
{
    public required string RowKey { get; init; }

    public required Side Side { get; init; }

    public required IReadOnlyDictionary<string, object?> Raw { get; init; }

    internal InternPool Pool { get; init; } = InternPool.Empty;

    public ReferenceDataSet Reference { get; init; } = ReferenceDataSet.Empty;

    internal InstrumentMaster Instruments { get; init; } = InstrumentMaster.Empty;

    public required ImmutableDictionary<string, CanonicalCell> Values { get; init; }

    public required ImmutableList<Diagnostic> Diagnostics { get; init; }

    public required ImmutableList<TraceStep> Traces { get; init; }

    // The raw cells are copied under ordinal comparison. The caller's comparer does not survive.
    public static RowContext Create(string rowKey, Side side, IReadOnlyDictionary<string, object?> raw)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        ArgumentNullException.ThrowIfNull(raw);
        return new()
        {
            RowKey = rowKey,
            Side = side,
            Raw = raw.ToImmutableDictionary(StringComparer.Ordinal),
            Pool = InternPool.Empty,
            Reference = ReferenceDataSet.Empty,
            Instruments = InstrumentMaster.Empty,
            Values = ImmutableDictionary.Create<string, CanonicalCell>(StringComparer.Ordinal),
            Diagnostics = [],
            Traces = [],
        };
    }

    public bool IsNull(string field) => !Values.TryGetValue(field, out var cell) || cell.IsNull;

    public RowContext Write(string field, CanonicalCell cell, string ruleId, string? raw, string? canonical) =>
        this with
        {
            Values = Values.SetItem(field, cell),
            Traces = Traces.Add(new TraceStep(ruleId, field, raw, canonical)),
        };

    internal RowContext WithPool(InternPool pool) => this with { Pool = pool };

    public RowContext Diagnose(string code, DiagnosticSeverity severity, string ruleId, string? field, string? raw) =>
        this with { Diagnostics = Diagnostics.Add(new Diagnostic(code, severity, ruleId, field, raw)) };

    // An unknown cell type displays as null even when the cell is present.
    public string? Display(string field) =>
        Values.TryGetValue(field, out var cell) && !cell.IsNull
            ? cell.Type switch
            {
                FieldType.Decimal => cell.Number.ToString(CultureInfo.InvariantCulture),
                FieldType.Date => cell.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FieldType.Symbol => Pool.Symbol(cell.Interned),
                FieldType.Identifier => Pool.Identifier(cell.Interned).Value,
                FieldType.Text => cell.Text,
                _ => null,
            }
            : null;
}
