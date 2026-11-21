using System.Text.RegularExpressions;

namespace RecDiffy.Rules;

// Rewrites a raw cell with one pattern before matching, so two spellings of one key reach the same symbol (#547).
// NonBacktracking runs in time linear in the cell, so no timeout is set: a timeout would make the result depend on the machine.
internal sealed class RegexTextRule(string from, string to, FieldType type, Regex pattern, string replacement, TextCase textCase, bool passthrough) : INormalizationRule
{
    public string RuleId => "text.regex";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public static RegexTextRule Create(RuleCall call, FieldSchema schema)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(schema);
        var to = call.Strings["to"];
        var type = schema.TryGet(to, out var field) ? field.Type : FieldType.Text;
        ProfileError[] errors =
        [
            .. TargetErrors(to, type),
            .. FlagErrors(call, "ignoreCase"),
            .. FlagErrors(call, "passthrough"),
            .. CaseErrors(call),
        ];
        return errors.Length == 0
            ? new RegexTextRule(
                call.Strings["from"],
                to,
                type,
                Compile(call.Strings["pattern"], Flag(call, "ignoreCase", false)),
                call.Strings["replacement"],
                ReadCase(call.Strings.GetValueOrDefault("case")),
                Flag(call, "passthrough", true))
            : throw new ProfileCompileException(errors);
    }

    public void Apply(ref RowContext row) => row = Rewrite(row);

    private RowContext Rewrite(RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? InvariantText.Of(value)?.Trim() : null;
        return raw switch
        {
            null or "" => row.Write(to, CanonicalCell.Null(type), RuleId, raw, null),
            var text when pattern.IsMatch(text) => WriteValue(row, Cased(pattern.Replace(text, replacement).Trim()), text),
            var text when passthrough => WriteValue(row, Cased(text), text),
            var text => row.Write(to, CanonicalCell.Null(type), RuleId, text, null)
                .Diagnose("PatternNoMatch", DiagnosticSeverity.Invalid, RuleId, to, text),
        };
    }

    private string Cased(string text) => textCase switch
    {
        TextCase.Upper => text.ToUpperInvariant(),
        TextCase.Lower => text.ToLowerInvariant(),
        _ => text,
    };

    // A rewrite that leaves nothing is a blank cell, as a blank raw cell is.
    private RowContext WriteValue(RowContext row, string value, string raw) =>
        (value, type) switch
        {
            ("", _) => row.Write(to, CanonicalCell.Null(type), RuleId, raw, null),
            (_, FieldType.Symbol) => Intern(row, value, raw),
            _ => row.Write(to, CanonicalCell.FromText(value), RuleId, raw, value),
        };

    private RowContext Intern(RowContext row, string symbol, string raw)
    {
        var (pool, id) = row.Pool.InternSymbol(symbol);
        return row.WithPool(pool).Write(to, CanonicalCell.FromInterned(FieldType.Symbol, id), RuleId, raw, symbol);
    }

    private static IEnumerable<ProfileError> TargetErrors(string to, FieldType type) =>
        type is FieldType.Symbol or FieldType.Text
            ? []
            : [new ProfileError("InvalidParameter", $"text.regex writes '{to}', a {type} field. It writes only Symbol or Text fields.")];

    private static IEnumerable<ProfileError> FlagErrors(RuleCall call, string name) =>
        call.Strings.GetValueOrDefault(name) is null or "true" or "false"
            ? []
            : [new ProfileError("InvalidParameter", $"text.regex {name} must be true or false, not \"{call.Strings[name]}\".")];

    private static IEnumerable<ProfileError> CaseErrors(RuleCall call) =>
        call.Strings.GetValueOrDefault("case") is null or "upper" or "lower"
            ? []
            : [new ProfileError("InvalidParameter", $"text.regex case must be \"upper\" or \"lower\", not \"{call.Strings["case"]}\".")];

    private static bool Flag(RuleCall call, string name, bool fallback) =>
        call.Strings.GetValueOrDefault(name) switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };

    private static TextCase ReadCase(string? text) => text switch
    {
        "upper" => TextCase.Upper,
        "lower" => TextCase.Lower,
        _ => TextCase.AsWritten,
    };

    // The compile boundary: .NET reports a bad pattern as an exception, and the profile reports it as an error.
    private static Regex Compile(string pattern, bool ignoreCase)
    {
        var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        try
        {
            return new Regex(pattern, options, Regex.InfiniteMatchTimeout);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ProfileCompileException([new ProfileError("InvalidParameter", $"text.regex pattern \"{pattern}\" is not a supported pattern: {exception.Message}")]);
        }
    }
}

internal enum TextCase
{
    AsWritten,
    Upper,
    Lower,
}
