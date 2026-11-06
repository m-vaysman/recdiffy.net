using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy.Results;

public enum Disposition
{
    Matched,
    Ambiguous,
    SearchIncomplete,
    LeftOnly,
    RightOnly,
    Quarantined,
    Suppressed,
}

public enum InputValidity
{
    Valid,
    ValidWithWarnings,
    Invalid,
}

public enum FieldOutcome
{
    NotCompared,
    Exact,
    WithinTolerance,
    Break,
}

public enum PassVerdict
{
    Committed,
    NoKey,
    NoCandidate,
    Vetoed,
    Contest,
    SearchIncomplete,
}

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

public enum DeltaKind
{
    None,
    Number,
    Days,
}

public readonly record struct ToleranceId(string Value);

public readonly record struct AttributionId(string Value);

public readonly record struct Delta(DeltaKind Kind, decimal Number, int Days);

public readonly record struct CanonicalValue(FieldType Type, bool IsNull, decimal Number, DateOnly Date, int Interned, string? Text = null);

public readonly record struct FieldDiff(
    FieldId Field,
    FieldOutcome Outcome,
    CanonicalValue Left,
    CanonicalValue Right,
    Delta Delta,
    ToleranceId Tolerance,
    AttributionId Attribution,
    ImmutableArray<string> AttributedTo);

public readonly record struct GroupId(string Value)
{
    public override string ToString() => Value;
}

public readonly struct CanonicalRow
{
    private readonly RowContext _row;

    internal CanonicalRow(RowContext row) => _row = row;

    public bool IsNull(string field) => _row.IsNull(field);

    public CanonicalValue this[string field] => _row.IsNull(field)
        ? new CanonicalValue(FieldType.Text, true, 0m, default, 0)
        : new CanonicalValue(_row.Values[field].Type, false, _row.Values[field].Number, _row.Values[field].Date, _row.Values[field].Interned, _row.Display(field));

    public string Text(string field) => _row.Display(field) ?? "";
}

public sealed record Explanation(string? CommittedBy, IReadOnlyList<PassAttempt> Trail, IReadOnlyList<Hint> Hints);

public sealed record PassAttempt(string PassId, PassVerdict Verdict, string? Key, IReadOnlyList<SourceRef> Candidates, string? Detail);

public sealed record Hint(string Code, string Detail);

public sealed record ResultEntry(
    GroupId Id,
    string PathId,
    Alignment Alignment,
    Composition Composition,
    FieldOutcome Outcome,
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

public sealed record RunInfo(string ProfileHash, string ReferenceId, string ReferenceHash, string EngineVersion);

public sealed record RunSummary(
    int Matched,
    int Exact,
    int WithinTolerance,
    int Break,
    int LeftOnly,
    int RightOnly,
    int Ambiguous,
    int SearchIncomplete,
    int Quarantined,
    int Suppressed);

public interface IFieldComparer<T>
    where T : unmanaged
{
    FieldOutcome Compare(in T left, in T right, out Delta delta);
}

public readonly struct DecimalComparer : IFieldComparer<decimal>
{
    public FieldOutcome Compare(in decimal left, in decimal right, out Delta delta)
    {
        var number = Difference(right, left);
        delta = new Delta(number == 0m ? DeltaKind.None : DeltaKind.Number, number, 0);
        return number == 0m ? FieldOutcome.Exact : FieldOutcome.Break;
    }

    // right - left overflows when the operands have opposite signs and the magnitude exceeds decimal.MaxValue.
    // The stand-in is one away from MinValue so a later absolute value does not overflow on negation.
    private static decimal Difference(decimal right, decimal left) =>
        left == right ? 0m
        : left >= 0m
            ? right < decimal.MinValue + left ? decimal.MinValue + 1m : right - left
            : right > decimal.MaxValue + left ? decimal.MaxValue : right - left;
}

public readonly struct DateComparer : IFieldComparer<DateOnly>
{
    public FieldOutcome Compare(in DateOnly left, in DateOnly right, out Delta delta)
    {
        var days = right.DayNumber - left.DayNumber;
        delta = new Delta(days == 0 ? DeltaKind.None : DeltaKind.Days, 0m, days);
        return days == 0 ? FieldOutcome.Exact : FieldOutcome.Break;
    }
}
