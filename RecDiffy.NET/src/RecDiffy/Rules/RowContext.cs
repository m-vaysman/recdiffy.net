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

    // Names here are canonical fields already written by an earlier rule.
    // The raw source of a parse is not one of them. Listing that name is FieldReadBeforeWrite while it is still unwritten.
    IReadOnlyList<FieldId> Reads { get; }

    IReadOnlyList<FieldId> Writes { get; }

    // The next row comes back through the ref parameter. The previous instance stays unchanged.
    void Apply(ref RowContext row);
}

public sealed record RowContext
{
    public required string RowKey { get; init; }

    public required Side Side { get; init; }

    public required IReadOnlyDictionary<string, object?> Raw { get; init; }

    // Each row starts empty. An id from one row is not an id in another row's pool.
    internal InternPool Pool { get; init; } = InternPool.Empty;

    public required ImmutableDictionary<string, CanonicalCell> Values { get; init; }

    public required ImmutableList<Diagnostic> Diagnostics { get; init; }

    public required ImmutableList<TraceStep> Traces { get; init; }

    public static RowContext Create(string rowKey, Side side, IReadOnlyDictionary<string, object?> raw)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        ArgumentNullException.ThrowIfNull(raw);
        return new()
    {
        RowKey = rowKey,
        Side = side,
        Raw = raw,
        Pool = InternPool.Empty,
        Values = ImmutableDictionary.Create<string, CanonicalCell>(StringComparer.Ordinal),
        Diagnostics = [],
        Traces = [],
        };
    }

    // A missing field and a written null are both null. Compile's write set is what separates them.
    public bool IsNull(string field) => !Values.TryGetValue(field, out var cell) || cell.IsNull;

    public RowContext Write(string field, CanonicalCell cell, string ruleId, string? raw, string? canonical) =>
        this with
        {
            Values = Values.SetItem(field, cell),
            Traces = Traces.Add(new TraceStep(ruleId, field, raw, canonical)),
        };

    // Write keeps the pool already on the row. Display looks up ids in that pool.
    internal RowContext WithPool(InternPool pool) => this with { Pool = pool };

    public RowContext Diagnose(string code, DiagnosticSeverity severity, string ruleId, string? field, string? raw) =>
        this with { Diagnostics = Diagnostics.Add(new Diagnostic(code, severity, ruleId, field, raw)) };

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
