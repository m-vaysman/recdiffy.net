using System.Globalization;
using System.Text.RegularExpressions;

namespace RecDiffy.Rules;

// Rewrites a raw cell with one pattern before matching, so two spellings of one key reach the same symbol (#547).
// NonBacktracking runs in time linear in the cell, so no timeout is set: a timeout would make the result depend on the machine.
internal sealed class RegexTextRule(string from, string to, FieldType type, Regex pattern, string replacement, TextCase textCase, bool passthrough) : INormalizationRule
{
    public static readonly IReadOnlyList<string> Required = ["from", "to", "pattern", "replacement"];

    public string RuleId => "text.regex";

    public RulePhase Phase => RulePhase.Identify;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public static RegexTextRule Create(RuleCall call, FieldSchema schema)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(schema);
        ProfileError[] shapeErrors = [.. Required.Where(name => call.Strings.ContainsKey(name) is false).Select(name =>
            new ProfileError("InvalidParameter", $"text.regex {name} must be a string, not a list or an object."))];
        if (shapeErrors.Length > 0)
            throw new ProfileCompileException(shapeErrors);

        var to = call.Strings["to"];
        var type = schema.TryGet(to, out var field) ? field.Type : FieldType.Text;
        ProfileError[] errors =
        [
            .. TargetErrors(to, type),
            .. FlagErrors(call, "ignoreCase"),
            .. FlagErrors(call, "passthrough"),
            .. CaseErrors(call),
        ];
        if (errors.Length > 0)
            throw new ProfileCompileException(errors);

        var pattern = Compile(call.Strings["pattern"], Flag(call, "ignoreCase", false));
        var replacement = call.Strings["replacement"];
        ProfileError[] referenceErrors = [.. ReferenceErrors(pattern, replacement)];
        return referenceErrors.Length == 0
            ? new RegexTextRule(call.Strings["from"], to, type, pattern, replacement, ReadCase(call.Strings.GetValueOrDefault("case")), Flag(call, "passthrough", true))
            : throw new ProfileCompileException(referenceErrors);
    }

    // .NET writes a reference to a group the pattern does not have as literal text, so "$2" with one group would put
    // "$2" into every key. Compile rejects it instead. "$$" is a literal dollar, and $&, $`, $', $+ and $_ always exist.
    private static IEnumerable<ProfileError> ReferenceErrors(Regex pattern, string replacement) =>
        References(replacement)
            .Where(reference => Names(pattern, reference.Group) is false)
            .Select(reference => new ProfileError(
                "InvalidParameter",
                $"text.regex replacement \"{replacement}\" refers to {reference.Text}, a group the pattern \"{pattern}\" does not have."));

    // Each $n or ${name} in the replacement, as written and by the group it names. "$$" is skipped as a literal dollar.
    private static IEnumerable<(string Text, string Group)> References(string replacement)
    {
        var index = 0;
        while (index < replacement.Length - 1)
        {
            var (length, group) = Reference(replacement, index);
            if (group is not null)
                yield return (replacement.Substring(index, length), group);
            index += length;
        }
    }

    // How far the text at index runs as one token, and the group it names, if it names one.
    private static (int Length, string? Group) Reference(string replacement, int index) =>
        (replacement[index], replacement[index + 1]) switch
        {
            ('$', '$') => (2, null),
            ('$', '{') when replacement.IndexOf('}', index + 2) is > 0 and var close => (close - index + 1, replacement[(index + 2)..close]),
            ('$', var digit) when char.IsAsciiDigit(digit) => DigitRun(replacement, index),
            _ => (1, null),
        };

    private static (int Length, string? Group) DigitRun(string replacement, int index)
    {
        var digits = string.Concat(replacement.Skip(index + 1).TakeWhile(char.IsAsciiDigit));
        return (digits.Length + 1, digits);
    }

    private static bool Names(Regex pattern, string group) =>
        group switch
        {
            [_, ..] when group.All(char.IsAsciiDigit) =>
                int.TryParse(group, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && pattern.GetGroupNumbers().Contains(number),
            _ => pattern.GetGroupNames().Contains(group, StringComparer.Ordinal),
        };

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
