using System.Globalization;

namespace RecDiffy.Engine;

internal enum MidpointMode { ToEven, AwayFromZero }

internal static class Rounding
{
    public static decimal FromDouble(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static decimal Round(decimal value, int scale, MidpointMode mode)
    {
        var midpoint = mode == MidpointMode.ToEven ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero;
        return decimal.Round(value, scale, midpoint);
    }
}

internal static class Formatting
{
    public static string Decimal(decimal value, int scale) =>
        value.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}

internal sealed class InternPool
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _symbols = new(StringComparer.Ordinal);
    private readonly List<string> _symbolValues = [];
    private readonly Dictionary<(string Scheme, string Scope, string Value), int> _ids = [];
    private readonly List<(string Scheme, string Scope, string Value)> _idValues = [];

    public int InternSymbol(string symbol)
    {
        lock (_gate)
        {
            if (_symbols.TryGetValue(symbol, out var id))
                return id;
            id = _symbolValues.Count + 1;
            _symbolValues.Add(symbol);
            _symbols[symbol] = id;
            return id;
        }
    }

    public int InternId(string scheme, string scope, string value)
    {
        var key = (scheme, scope, value);
        lock (_gate)
        {
            if (_ids.TryGetValue(key, out var id))
                return id;
            id = _idValues.Count + 1;
            _idValues.Add(key);
            _ids[key] = id;
            return id;
        }
    }

    public string Symbol(int id) => id <= 0 ? "" : _symbolValues[id - 1];

    public (string Scheme, string Scope, string Value) Id(int id) => id <= 0 ? ("", "", "") : _idValues[id - 1];

    public string Format(CanonicalValue value)
    {
        if (value.IsNull)
            return "";
        return value.Type switch
        {
            FieldType.Decimal => value.Number.ToString(CultureInfo.InvariantCulture),
            FieldType.Date => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FieldType.Symbol => Symbol(value.Interned),
            FieldType.Identifier => Id(value.Interned).Value,
            _ => "",
        };
    }
}

internal sealed class RawRow
{
    public required SourceRef Ref { get; set; }
    public Dictionary<string, RawValue> Cells { get; } = new(StringComparer.Ordinal);

    public void Set(string column, RawValue value) => Cells[column] = value;

    public bool TryGet(string column, out RawValue value) => Cells.TryGetValue(column, out value!);

    public string Fingerprint(IEnumerable<string> columns)
    {
        var parts = new List<string>();
        foreach (var column in columns.Order(StringComparer.Ordinal))
        {
            if (!Cells.TryGetValue(column, out var cell) || cell.IsNull)
            {
                parts.Add(column + "=\u0000");
                continue;
            }
            if (cell.Date is DateOnly date)
                parts.Add(column + "=d:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            else if (cell.Number is decimal number)
                parts.Add(column + "=n:" + number.ToString(CultureInfo.InvariantCulture));
            else
                parts.Add(column + "=t:" + cell.Text);
        }
        return string.Join("\u001f", parts);
    }
}

internal sealed class StoredDiagnostic
{
    public StoredDiagnostic(string code, string ruleId, string? field, string? detail, DiagnosticSeverity severity)
    {
        Code = code;
        RuleId = ruleId;
        Field = field;
        Detail = detail;
        Severity = severity;
    }

    public string Code { get; }
    public string RuleId { get; }
    public string? Field { get; }
    public string? Detail { get; }
    public DiagnosticSeverity Severity { get; }

    public Diagnostic ToPublic() => new(Code, RuleId, Field, Detail);
}

internal sealed class MutableRow
{
    public required Side Side { get; init; }
    public required SourceRef Primary { get; init; }
    public List<SourceRef> Members { get; } = [];
    public RawRow Raw { get; set; } = null!;
    public Dictionary<string, CanonicalValue> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string?> Texts { get; } = new(StringComparer.Ordinal);
    public List<TraceStep> Traces { get; } = [];
    public List<StoredDiagnostic> Diagnostics { get; } = [];
    public HashSet<string> Derived { get; } = new(StringComparer.Ordinal);
    public bool Quarantined { get; set; }
    public SuppressionReason? Suppression { get; set; }
    public bool ChainCollapsed { get; set; }
    public SourceRef? ChainSurvivor { get; set; }
    public string? PathId { get; set; }
    public int? AggregateOf { get; set; }

    public bool Folded { get; set; }
}

internal sealed class InstrumentMaster
{
    public Dictionary<(string Scheme, string Value), string> Map { get; } = [];
    public HashSet<(string Scheme, string Value)> Conflicts { get; } = [];

    public void Add(string scheme, string value, string canonical)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(canonical))
            return;
        var key = (scheme, value);
        if (Conflicts.Contains(key))
            return;
        if (Map.TryGetValue(key, out var existing) && !string.Equals(existing, canonical, StringComparison.Ordinal))
        {
            Map.Remove(key);
            Conflicts.Add(key);
            return;
        }
        Map[key] = canonical;
    }
}

internal sealed class RunState
{
    public required CompiledProfile Profile { get; init; }
    public required ReferenceDataSet Reference { get; init; }
    public required InternPool Pool { get; init; }
    public required InstrumentMaster Instruments { get; init; }
    public required MidpointMode Midpoint { get; init; }
    public Dictionary<(string Source, string RowKey), MutableRow> ByRef { get; } = [];
    public List<MutableRow> Left { get; } = [];
    public List<MutableRow> Right { get; } = [];

    public int ScaleOf(string field) =>
        Profile.Model.Fields.FirstOrDefault(f => f.Name == field)?.Scale ?? 0;

    public FieldType TypeOf(string field) =>
        Profile.Model.Fields.FirstOrDefault(f => f.Name == field)?.Type ?? FieldType.Text;
}

internal sealed class FieldModel
{
    public required string Name { get; init; }
    public required FieldType Type { get; init; }
    public int Scale { get; init; }
    public FieldRole Role { get; init; }
}

internal sealed class WhenClause
{
    public required string Field { get; init; }
    public string? EqualsValue { get; init; }
    public string? NotEquals { get; init; }
    public IReadOnlyList<string>? In { get; init; }
    public IReadOnlyList<string>? NotIn { get; init; }
}

internal sealed class RuleUse
{
    public required INormalizationRule Rule { get; init; }
    public WhenClause? When { get; init; }
}

internal sealed class Limits
{
    public int MaxGroupSize { get; init; } = 8;
    public int MaxBlockItems { get; init; } = 24;
    public int MaxTargetsPerComponent { get; init; } = 8;
    public int MaxNodesPerTarget { get; init; } = 250_000;
    public int StopAfterSolutions { get; init; } = 2;
}

internal sealed class PassModel
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public IReadOnlyList<string> Key { get; init; } = [];
    public IReadOnlyList<string> Block { get; init; } = [];
    public IReadOnlyList<string> Conserve { get; init; } = [];
    public string Many { get; init; } = "left";
    public string? Veto { get; init; }
    public Limits Limits { get; init; } = new();
}

internal sealed class DiffFieldModel
{
    public required string Field { get; init; }
    public decimal? Absolute { get; init; }
    public decimal? Relative { get; init; }
    public int? Days { get; init; }
    public string? Attributed { get; init; }
    public WhenClause? ToleranceWhen { get; init; }
    public WhenClause? ApplicableWhen { get; init; }
}

internal sealed class PathModel
{
    public required string Id { get; init; }
    public required string Route { get; init; }
    public string? RouteField { get; init; }
    public string? RouteEquals { get; init; }
    public IReadOnlyList<PassModel> Passes { get; init; } = [];
    public IReadOnlyList<DiffFieldModel> Diffs { get; init; } = [];
    public bool DefaultExact { get; init; } = true;
}

internal sealed class ProfileModel
{
    public required string ProfileId { get; init; }
    public required string Version { get; init; }
    public required MidpointMode Midpoint { get; init; }
    public required string LeftSource { get; init; }
    public required string RightSource { get; init; }
    public required string LeftKey { get; init; }
    public required string RightKey { get; init; }
    public IReadOnlyList<FieldModel> Fields { get; init; } = [];
    public IReadOnlyList<PathModel> Paths { get; init; } = [];
    public IReadOnlyList<string> MappedColumnsLeft { get; init; } = [];
    public IReadOnlyList<string> MappedColumnsRight { get; init; } = [];
    public string RawRetention { get; init; } = "OnDifference";
}

internal interface IRuleFactory
{
    string RuleId { get; }
    RulePhase Phase { get; }
    INormalizationRule Bind(RuleArguments args);
}

internal interface ILifecycleFactory
{
    string RuleId { get; }
    LifecycleBinding Bind(RuleArguments args);
}

internal interface IPackageFactory
{
    string RuleId { get; }
    PackageBinding Bind(RuleArguments args, RuleRegistry registry);
}

internal interface IAggregatorFactory
{
    string Name { get; }
    IAggregator Create(IReadOnlyList<string> args);
}

internal interface IAggregator
{
    void Apply(string field, IReadOnlyList<MutableRow> members, MutableRow target, RunState state, out string? failure);
}

public sealed class RuleArguments
{
    private readonly Dictionary<string, string> _values;
    private readonly Dictionary<string, IReadOnlyList<string>> _lists;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _maps;
    public RuleArguments(
        Dictionary<string, string> values,
        Dictionary<string, IReadOnlyList<string>> lists,
        Dictionary<string, IReadOnlyDictionary<string, string>> maps)
    {
        _values = values;
        _lists = lists;
        _maps = maps;
    }

    public string? Optional(string name) => _values.TryGetValue(name, out var value) ? value : null;
    public string Required(string name) => Optional(name) ?? throw new ProfileException("MissingParameter", $"Missing parameter '{name}'.");
    public IReadOnlyList<string> List(string name) => _lists.TryGetValue(name, out var list) ? list : [];
    public IReadOnlyDictionary<string, string> Map(string name) =>
        _maps.TryGetValue(name, out var map) ? map : new Dictionary<string, string>();
}

internal enum LifeFate { Survivor, Superseded, CancelEvent, FullyCancelled, Quarantine }

internal sealed record LifeEvent(SourceRef Source, string Scope, string Id, string Action, string? Target);

internal sealed record LifeDecision(SourceRef Source, LifeFate Fate, string? Code, bool Collapsed, SourceRef? LinkedTo = null);

internal sealed class LifecycleBinding
{
    public required string RuleId { get; init; }
    public string CorrPointsAt { get; init; } = "CANC";
    public string? SequenceField { get; init; }
    public Func<IReadOnlyList<LifeEvent>, string, List<LifeDecision>>? Apply { get; init; }
}

internal sealed class PackageBinding
{
    public required string RuleId { get; init; }
    public required string LinkField { get; init; }
    public required string RoleField { get; init; }
    public required IReadOnlyList<string> RequiredRoles { get; init; }
    public required IReadOnlyDictionary<string, IAggregator> Aggregates { get; init; }
}

internal static class WhenEvaluator
{
    public static bool Matches(WhenClause? when, MutableRow row, InternPool pool)
    {
        if (when is null)
            return true;
        var text = Read(row, when.Field, pool);
        if (when.EqualsValue is not null)
            return string.Equals(text, when.EqualsValue, StringComparison.Ordinal);
        if (when.NotEquals is not null)
            return !string.Equals(text, when.NotEquals, StringComparison.Ordinal);
        if (when.In is not null)
            return text is not null && when.In.Contains(text, StringComparer.Ordinal);
        if (when.NotIn is not null)
            return text is null || !when.NotIn.Contains(text, StringComparer.Ordinal);
        return true;
    }

    public static string? Read(MutableRow row, string field, InternPool pool)
    {
        if (row.Values.TryGetValue(field, out var canonical) && !canonical.IsNull)
        {
            return canonical.Type switch
            {
                FieldType.Symbol => pool.Symbol(canonical.Interned),
                FieldType.Identifier => pool.Id(canonical.Interned).Value,
                FieldType.Decimal => canonical.Number.ToString(CultureInfo.InvariantCulture),
                FieldType.Date => canonical.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => row.Texts.TryGetValue(field, out var text) ? text : null,
            };
        }
        if (!row.Raw.TryGet(field, out var raw) || raw.IsNull)
            return null;
        if (raw.Text is not null)
            return raw.Text;
        if (raw.Date is DateOnly date)
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (raw.Number is decimal number)
            return number.ToString(CultureInfo.InvariantCulture);
        return null;
    }
}
