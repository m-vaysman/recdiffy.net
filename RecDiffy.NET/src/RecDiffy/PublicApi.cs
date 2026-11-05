using System.Data;
using System.Diagnostics;

namespace RecDiffy;

public static class EngineVersion
{
    public const string Value = "1.0.0";
}

public enum Side { Left, Right }

public enum Alignment { Matched, Ambiguous, SearchIncomplete, LeftOnly, RightOnly }

public enum Disposition { Matched, Ambiguous, SearchIncomplete, LeftOnly, RightOnly, Quarantined, Suppressed }

public enum SuppressionReason { Superseded, CancelEvent, FullyCancelled }

public enum InputValidity { Valid, ValidWithWarnings, Invalid }

public enum FieldOutcome { NotCompared, Exact, WithinTolerance, Break }

public enum PassVerdict { Committed, NoKey, NoCandidate, Vetoed, Contest, SearchIncomplete }

[Flags]
public enum Composition
{
    None = 0,
    OneToOne = 1,
    ManyToOne = 2,
    OneToMany = 4,
    Package = 8,
    LifecycleCollapsed = 16,
}

public enum FieldType { Decimal, Date, Symbol, Identifier, Text }

public enum FieldRole { Key, Economic, Identity, Lifecycle, Package, Descriptive }

public enum RulePhase { Identify = 1, Parse = 2, Derive = 3, Validate = 4 }

public enum DiagnosticSeverity { Warning, Invalid, Fatal }

public enum DeltaKind { None, Number, Days }

public readonly record struct SourceRef(string Source, string RowKey, int Ordinal);

public readonly record struct GroupId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct FieldId(string Name)
{
    public override string ToString() => Name;
}

public readonly record struct CanonicalValue(FieldType Type, bool IsNull, decimal Number, DateOnly Date, int Interned);

public readonly record struct Delta(DeltaKind Kind, decimal Number, int Days);

public readonly record struct ToleranceId(int Index);

public readonly record struct AttributionId(int Index);

public readonly record struct FieldDiff(
    FieldId Field,
    FieldOutcome Outcome,
    CanonicalValue Left,
    CanonicalValue Right,
    Delta Delta,
    ToleranceId Tolerance,
    AttributionId Attribution);

public readonly record struct RawValue(bool IsNull, string? Text, decimal? Number, DateOnly? Date)
{
    public static RawValue Null() => new(true, null, null, null);
}

public sealed record Diagnostic(string Code, string RuleId, string? Field, string? Detail);

public sealed record TraceStep(string RuleId, string Field, string? Raw, string? Canonical);

public sealed record Hint(string Kind, string Detail);

public sealed record PassAttempt(
    string PassId,
    PassVerdict Verdict,
    string? Key,
    IReadOnlyList<SourceRef> Candidates,
    string? Detail);

public sealed record Explanation(string? CommittedBy, IReadOnlyList<PassAttempt> Trail, IReadOnlyList<Hint> Hints);

public sealed record RunInfo(
    string ProfileId,
    string ProfileHash,
    string ProfileVersion,
    string ReferenceSnapshotId,
    string ReferenceHash,
    string EngineVersion);

public sealed record RunSummary(
    int LeftRows,
    int RightRows,
    int MatchedEntries,
    int Exact,
    int WithinTolerance,
    int Breaks,
    int LeftOnly,
    int RightOnly,
    int Ambiguous,
    int SearchIncomplete,
    int Quarantined,
    int Suppressed,
    IReadOnlyDictionary<string, int> CommitsByPass,
    IReadOnlyDictionary<string, int> BreaksByRoot);

public sealed record RunOptions(int? MaxDegreeOfParallelism = null, bool TelemetryIncludesRowKeys = false);

public sealed record ReferenceDataSet(
    string Id,
    DateOnly AsOf,
    string ContentHash,
    IReadOnlyDictionary<string, string> LeiToCode,
    IReadOnlyDictionary<string, string> AliasToCode,
    IReadOnlyDictionary<string, string> DateFormatByCode);

public sealed record ResultEntry(
    GroupId Id,
    string PathId,
    Alignment Alignment,
    Composition Composition,
    FieldOutcome Outcome,
    string? RootField,
    IReadOnlyList<SourceRef> Left,
    IReadOnlyList<SourceRef> Right,
    IReadOnlyList<FieldDiff> Diffs,
    Explanation Explanation);

public sealed record RowResult(
    SourceRef Row,
    Disposition Disposition,
    SuppressionReason? Suppression,
    InputValidity Validity,
    IReadOnlyList<Diagnostic> Diagnostics,
    GroupId? EntryId);

public sealed record SideBySideSpec(
    IReadOnlyList<string> Rows,
    IReadOnlyList<string> Columns,
    IReadOnlyList<MeasureSpec> Measures,
    FilterSpec? Filter);

public sealed record MeasureSpec(string Field, string Aggregate = "Sum");

public sealed record FilterSpec(IReadOnlyList<FilterTerm> Terms);

public sealed record FilterTerm(string Field, string Value);

public sealed record SideBySideMeasure(
    string Field,
    decimal Left,
    decimal Right,
    decimal Delta,
    decimal LeftOnly,
    decimal RightOnly,
    decimal MatchedDelta);

public sealed record SideBySideRow(
    IReadOnlyList<string> RowKeys,
    IReadOnlyList<string> ColumnKeys,
    int LeftCount,
    int RightCount,
    IReadOnlyList<SideBySideMeasure> Measures);

public sealed class SideBySideView
{
    internal SideBySideView(IReadOnlyList<SideBySideRow> rows) => Rows = rows;
    public IReadOnlyList<SideBySideRow> Rows { get; }
}

public readonly struct CanonicalRow
{
    private readonly IReadOnlyDictionary<string, CanonicalValue>? _values;
    private readonly Func<CanonicalValue, string>? _format;
    internal CanonicalRow(IReadOnlyDictionary<string, CanonicalValue> values, Func<CanonicalValue, string> format)
    {
        _values = values;
        _format = format;
    }

    public bool IsNull(string field) => !_values!.TryGetValue(field, out var value) || value.IsNull;

    public CanonicalValue this[string field] =>
        _values!.TryGetValue(field, out var value) ? value : new CanonicalValue(FieldType.Text, true, 0m, default, 0);

    public string Text(string field) => _format!(this[field]);
}

public interface IFieldComparer<T> where T : struct
{
    FieldOutcome Compare(in T left, in T right, out Delta delta);
}

public interface INormalizationRule
{
    string RuleId { get; }
    RulePhase Phase { get; }
    IReadOnlyList<FieldId> Reads { get; }
    IReadOnlyList<FieldId> Writes { get; }
    void Apply(ref RowContext row);
}

public interface IMatchPass
{
    string PassId { get; }
    void Propose(in PassContext context, ProposalSink sink);
}

public readonly struct PassContext
{
    private readonly Engine.MatchSession? _session;
    internal PassContext(Engine.MatchSession session, string passId)
    {
        _session = session;
        PassId = passId;
    }

    public string PassId { get; }
    public int ItemCount => _session!.ActiveIds.Count;
    public Side GetSide(int itemId) => _session!.Item(itemId).Side;
    public bool TryGet(int itemId, string field, out CanonicalValue value) => _session!.TryValue(itemId, field, out value);
    public IReadOnlyList<int> Unconsumed() => _session!.ActiveIds;
}

public abstract class ProposalSink
{
    public abstract void Propose(IReadOnlyList<int> leftIds, IReadOnlyList<int> rightIds, string key);
}

public sealed class ProfileException : Exception
{
    public ProfileException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

public sealed class RowContext
{
    internal Engine.RunState State { get; }
    internal Engine.MutableRow Row { get; }
    internal string RuleId { get; set; } = "";
    internal RowContext(Engine.RunState state, Engine.MutableRow row)
    {
        State = state;
        Row = row;
    }

    public string RowKey => Row.Primary.RowKey;
    public Side Side => Row.Side;
    public ReferenceDataSet Reference => State.Reference;

    public bool TryRaw(string column, out RawValue value) => Row.Raw.TryGet(column, out value);

    public bool TryCanonical(string field, out CanonicalValue value)
    {
        if (Row.Values.TryGetValue(field, out var slot) && !slot.IsNull)
        {
            value = slot;
            return true;
        }
        value = default;
        return false;
    }

    public void SetDecimal(string field, decimal value, string? raw = null)
    {
        var scale = State.ScaleOf(field);
        var rounded = Engine.Rounding.Round(value, scale, State.Midpoint);
        Row.Values[field] = new CanonicalValue(FieldType.Decimal, false, rounded, default, 0);
        Remember(field, raw, Engine.Formatting.Decimal(rounded, scale));
    }

    public void SetDate(string field, DateOnly value, string? raw = null)
    {
        Row.Values[field] = new CanonicalValue(FieldType.Date, false, 0m, value, 0);
        Remember(field, raw, value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    public void SetSymbol(string field, string symbol, string? raw = null)
    {
        var id = State.Pool.InternSymbol(symbol);
        Row.Values[field] = new CanonicalValue(FieldType.Symbol, false, 0m, default, id);
        Remember(field, raw, symbol);
    }

    public void SetIdentifier(string field, string scheme, string scope, string value, string? raw = null)
    {
        var id = State.Pool.InternId(scheme, scope, value);
        Row.Values[field] = new CanonicalValue(FieldType.Identifier, false, 0m, default, id);
        Remember(field, raw, value);
    }

    public void SetText(string field, string? text, string? raw = null)
    {
        Row.Values[field] = new CanonicalValue(FieldType.Text, text is null, 0m, default, 0);
        Row.Texts[field] = text;
        Remember(field, raw, text);
    }

    public void SetNull(string field)
    {
        var type = State.TypeOf(field);
        Row.Values[field] = new CanonicalValue(type, true, 0m, default, 0);
        Remember(field, null, null);
    }

    public void MarkDerived(string field) => Row.Derived.Add(field);

    public void Diagnose(string code, DiagnosticSeverity severity, string? field = null, string? detail = null) =>
        Row.Diagnostics.Add(new StoredDiagnostic(code, RuleId, field, detail, severity));

    public bool TryLookupInstrument(string scheme, string value, out string canonical)
    {
        if (State.Instruments.Conflicts.Contains((scheme, value)))
        {
            canonical = "";
            return false;
        }
        return State.Instruments.Map.TryGetValue((scheme, value), out canonical!);
    }

    private void Remember(string field, string? raw, string? canonical) =>
        Row.Traces.Add(new TraceStep(RuleId, field, raw, canonical));
}

public sealed record TabularOptions(int HeaderRowIndex = 0, string? RowKeyColumn = null);

public sealed class SourceMap<T>
{
    internal List<Action<T, Engine.RawRow>> Binders { get; } = [];

    public SourceMap<T> Decimal(string column, Func<T, decimal?> read)
    {
        Binders.Add((row, raw) =>
        {
            var value = read(row);
            raw.Set(column, value is null
                ? RawValue.Null()
                : new RawValue(false, null, value, null));
        });
        return this;
    }

    public SourceMap<T> Number(string column, Func<T, double?> read)
    {
        Binders.Add((row, raw) =>
        {
            var value = read(row);
            raw.Set(column, value is null
                ? RawValue.Null()
                : new RawValue(false, null, Engine.Rounding.FromDouble(value.Value), null));
        });
        return this;
    }

    public SourceMap<T> Text(string column, Func<T, string?> read)
    {
        Binders.Add((row, raw) =>
        {
            var value = read(row);
            raw.Set(column, string.IsNullOrWhiteSpace(value)
                ? RawValue.Null()
                : new RawValue(false, value.Trim(), null, null));
        });
        return this;
    }

    public SourceMap<T> Date(string column, Func<T, DateOnly?> read)
    {
        Binders.Add((row, raw) =>
        {
            var value = read(row);
            raw.Set(column, value is null
                ? RawValue.Null()
                : new RawValue(false, null, null, value));
        });
        return this;
    }
}

public abstract class RecordSource
{
    internal abstract IReadOnlyList<Engine.RawRow> Materialize(string sideName, string rowKey, CancellationToken cancellation);
    internal abstract Task<IReadOnlyList<Engine.RawRow>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation);
}

public static class RecordSources
{
    public static RecordSource FromObjects<T>(string name, IEnumerable<T> rows, SourceMap<T> map) =>
        new Engine.ObjectSource<T>(name, rows, map);

    public static RecordSource FromObjects<T>(string name, IAsyncEnumerable<T> rows, SourceMap<T> map) =>
        new Engine.AsyncObjectSource<T>(name, rows, map);

    public static RecordSource FromDataReader(string name, Func<IDataReader> open, TabularOptions options) =>
        new Engine.DataReaderSource(name, open, options);
}

public sealed class CompiledProfile
{
    internal CompiledProfile(
        string id,
        string version,
        string hash,
        Engine.ProfileModel model,
        IReadOnlyList<Engine.RuleUse> left,
        IReadOnlyList<Engine.RuleUse> right,
        Engine.LifecycleBinding? leftLife,
        Engine.LifecycleBinding? rightLife,
        IReadOnlyList<Engine.PackageBinding> leftPackages,
        IReadOnlyList<Engine.PackageBinding> rightPackages)
    {
        Id = id;
        Version = version;
        Hash = hash;
        Model = model;
        LeftRules = left;
        RightRules = right;
        LeftLife = leftLife;
        RightLife = rightLife;
        LeftPackages = leftPackages;
        RightPackages = rightPackages;
    }

    public string Id { get; }
    public string Version { get; }
    public string Hash { get; }
    internal Engine.ProfileModel Model { get; }
    internal IReadOnlyList<Engine.RuleUse> LeftRules { get; }
    internal IReadOnlyList<Engine.RuleUse> RightRules { get; }
    internal Engine.LifecycleBinding? LeftLife { get; }
    internal Engine.LifecycleBinding? RightLife { get; }
    internal IReadOnlyList<Engine.PackageBinding> LeftPackages { get; }
    internal IReadOnlyList<Engine.PackageBinding> RightPackages { get; }
}

public sealed class ProfileDocument
{
    internal ProfileDocument(string json) => Json = json;
    internal string Json { get; }
    public static ProfileDocument Parse(string json) => new(json);
}

public sealed class RuleRegistry
{
    internal Dictionary<string, Engine.IRuleFactory> Normalization { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Engine.ILifecycleFactory> Lifecycles { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Engine.IPackageFactory> Packages { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Engine.IAggregatorFactory> Aggregators { get; } = new(StringComparer.Ordinal);

    public static RuleRegistry CreateDefault()
    {
        var registry = new RuleRegistry();
        registry.Add(new Rules.DecimalParseFactory());
        registry.Add(new Rules.DateParseFactory());
        registry.Add(new Rules.SymbolMapFactory());
        registry.Add(new Rules.IdentifierMapFactory());
        registry.Add(new Rules.RequiredPresentFactory());
        registry.Add(new Rules.SideDeterminedFactory());
        return registry;
    }

    internal RuleRegistry Add(Engine.IRuleFactory factory)
    {
        Normalization[factory.RuleId] = factory;
        return this;
    }

    internal RuleRegistry AddLifecycle(Engine.ILifecycleFactory factory)
    {
        Lifecycles[factory.RuleId] = factory;
        return this;
    }

    internal RuleRegistry AddPackage(Engine.IPackageFactory factory)
    {
        Packages[factory.RuleId] = factory;
        return this;
    }

    internal RuleRegistry AddAggregator(Engine.IAggregatorFactory factory)
    {
        Aggregators[factory.Name] = factory;
        return this;
    }
}

public sealed class ReconciliationResult
{
    private readonly Engine.RunState _state;
    private readonly Dictionary<string, List<string>> _attributions;
    internal ReconciliationResult(
        RunInfo run,
        IReadOnlyList<ResultEntry> entries,
        IReadOnlyList<RowResult> rows,
        RunSummary summary,
        Engine.RunState state,
        Dictionary<string, List<string>> attributions,
        Dictionary<GroupId, (Dictionary<string, CanonicalValue> Left, Dictionary<string, CanonicalValue> Right)> projections)
    {
        Run = run;
        Entries = entries;
        Rows = rows;
        Summary = summary;
        _state = state;
        _attributions = attributions;
        Projections = projections;
    }

    public RunInfo Run { get; }
    public IReadOnlyList<ResultEntry> Entries { get; }
    public IReadOnlyList<RowResult> Rows { get; }
    public RunSummary Summary { get; }
    internal Dictionary<GroupId, (Dictionary<string, CanonicalValue> Left, Dictionary<string, CanonicalValue> Right)> Projections { get; }

    public SideBySideView BuildSideBySide(SideBySideSpec spec) => Engine.SideBySideBuilder.Build(this, _state, spec);

    public CanonicalRow Projection(GroupId entry, Side side)
    {
        var found = Projections[entry];
        var values = side == Side.Left ? found.Left : found.Right;
        return new CanonicalRow(values, value => Format(value));
    }

    public CanonicalRow Canonical(SourceRef row)
    {
        if (!_state.ByRef.TryGetValue((row.Source, row.RowKey), out var mutable) || mutable.Quarantined)
            return new CanonicalRow(new Dictionary<string, CanonicalValue>(), value => Format(value));
        return new CanonicalRow(mutable.Values, value => Format(value));
    }

    public IReadOnlyList<TraceStep> Trace(SourceRef row) =>
        _state.ByRef.TryGetValue((row.Source, row.RowKey), out var mutable)
            ? mutable.Traces
            : [];

    public string? Raw(SourceRef row, string column)
    {
        if (!_state.ByRef.TryGetValue((row.Source, row.RowKey), out var mutable))
            return null;
        return mutable.Raw.TryGet(column, out var value) ? value.Text ?? value.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    public string Format(in CanonicalValue value) => _state.Pool.Format(value);

    public IReadOnlyList<string> Attribution(AttributionId id) =>
        id.Index <= 0 || !_attributions.TryGetValue(id.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var names)
            ? []
            : names;

    internal string FormatValue(string field, CanonicalValue value)
    {
        if (value.IsNull)
            return "";
        return value.Type switch
        {
            FieldType.Decimal => Engine.Formatting.Decimal(value.Number, _state.ScaleOf(field)),
            FieldType.Date => value.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            _ => Format(value),
        };
    }
}

public static class Reconciler
{
    public static CompiledProfile Compile(ProfileDocument document, RuleRegistry registry) =>
        Engine.ProfileCompiler.Compile(document, registry);

    public static ReconciliationResult Run(
        CompiledProfile profile,
        RecordSource left,
        RecordSource right,
        ReferenceDataSet referenceData,
        RunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Engine.Pipeline.Run(profile, left, right, referenceData, options ?? new RunOptions(), cancellationToken);

    public static async Task<ReconciliationResult> RunAsync(
        CompiledProfile profile,
        RecordSource left,
        RecordSource right,
        ReferenceDataSet referenceData,
        RunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RunOptions();
        cancellationToken.ThrowIfCancellationRequested();
        var leftRows = await left.MaterializeAsync(profile.Model.LeftSource, profile.Model.LeftKey, cancellationToken).ConfigureAwait(false);
        var rightRows = await right.MaterializeAsync(profile.Model.RightSource, profile.Model.RightKey, cancellationToken).ConfigureAwait(false);
        return Engine.Pipeline.RunMaterialized(profile, leftRows, rightRows, referenceData, options, cancellationToken);
    }
}

internal static class DiagnosticsNames
{
    public const string Source = "RecDiffy";
}

internal sealed class RecDiffyTelemetry
{
    internal static readonly ActivitySource Activity = new(DiagnosticsNames.Source);
    internal static readonly System.Diagnostics.Metrics.Meter Meter = new(DiagnosticsNames.Source);
    internal static readonly System.Diagnostics.Metrics.Counter<long> Rows = Meter.CreateCounter<long>("recdiffy.rows");
    internal static readonly System.Diagnostics.Metrics.Counter<long> Commits = Meter.CreateCounter<long>("recdiffy.commits");
    internal static readonly System.Diagnostics.Metrics.Counter<long> Outcomes = Meter.CreateCounter<long>("recdiffy.field.outcomes");
}
