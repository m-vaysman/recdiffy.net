namespace RecDiffy;

public enum Side
{
    Left,
    Right,
}

public enum FieldType
{
    Decimal,
    Date,
    Symbol,
    Identifier,
    Text,
}

public enum FieldRole
{
    Key,
    Economic,
    Identity,
    Lifecycle,
    Package,
    Text,
}

public enum MidpointMode
{
    ToEven,
    AwayFromZero,
}

public enum DiagnosticSeverity
{
    Warning,
    Invalid,
    Fatal,
}

public readonly record struct FieldId(string Name)
{
    public override string ToString() => Name;
}

public readonly record struct SourceRef(string Source, string RowKey, int Ordinal);

// Every field type shares this shape. Inactive slots are zero, default, or null.
public readonly record struct CanonicalCell(FieldType Type, bool IsNull, decimal Number, DateOnly Date, int Interned, string? Text)
{
    public static CanonicalCell Null(FieldType type) => new(type, true, 0m, default, 0, null);

    public static CanonicalCell FromDecimal(decimal value) => new(FieldType.Decimal, false, value, default, 0, null);

    public static CanonicalCell FromDate(DateOnly value) => new(FieldType.Date, false, 0m, value, 0, null);

    // Stores the id. The text stays in the pool that minted the id.
    public static CanonicalCell FromInterned(FieldType type, int id) => new(type, false, 0m, default, id, null);

    // Null and empty share one cell. Blank text and omitted text are the same value.
    public static CanonicalCell FromText(string? text) =>
        string.IsNullOrEmpty(text) ? Null(FieldType.Text) : new(FieldType.Text, false, 0m, default, 0, text);
}

public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, string RuleId, string? Field, string? RawText);

public sealed record TraceStep(string RuleId, string Field, string? Raw, string? Canonical);

public sealed record ProfileError(string Code, string Message);

public enum SuppressionReason
{
    Superseded,
    CancelEvent,
    FullyCancelled,
}

public sealed class ProfileCompileException(IReadOnlyList<ProfileError> errors) : Exception(Format(errors))
{
    public IReadOnlyList<ProfileError> Errors { get; } = errors;

    private static string Format(IReadOnlyList<ProfileError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return string.Join("; ", errors.Select(error => $"{error.Code}: {error.Message}"));
    }
}
