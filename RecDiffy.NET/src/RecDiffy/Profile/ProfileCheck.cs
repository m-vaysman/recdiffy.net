using System.Globalization;

namespace RecDiffy;

/// <summary>Pre-flight checks of a profile against the files it will read, before any row is read.</summary>
public static class ProfileCheck
{
    /// <summary>
    /// Lists every source column the profile reads that a side's headers lack, once per side and column, in the order the
    /// profile first reads it, each with the closest header as a suggestion when one is close.
    /// </summary>
    /// <remarks>
    /// A rule's <c>from</c> parameter is a column for every rule. Other parameters hold columns only for some rules, so
    /// <paramref name="columnParameters"/> names them by rule ID; a rule pack supplies its own. A <c>when</c> field that is
    /// not a declared field is read as a column. Headers compare ordinally, as the engine reads them.
    /// </remarks>
    public static IReadOnlyList<MissingColumn> AgainstHeaders(
        ProfileDocument document,
        IReadOnlyCollection<string> leftHeaders,
        IReadOnlyCollection<string> rightHeaders,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? columnParameters = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(leftHeaders);
        ArgumentNullException.ThrowIfNull(rightHeaders);
        var parameters = columnParameters ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var declared = document.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. Missing(Side.Left, Reads(document, "left", declared, parameters), leftHeaders),
            .. Missing(Side.Right, Reads(document, "right", declared, parameters), rightHeaders),
        ];
    }

    private static IEnumerable<(string Column, string Location)> Reads(
        ProfileDocument document,
        string side,
        IReadOnlySet<string> declared,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        var rowKey = document.SourceKeys.TryGetValue(side, out var key) && key.Length > 0
            ? [(key, $"sources.{side}.rowKey")]
            : Array.Empty<(string, string)>();
        var rules = document.Normalization.TryGetValue(side, out var normalization)
            ? ProfileValidation.Phases(normalization)
                .SelectMany(phase => phase.Calls.SelectMany((call, index) => RuleReads($"normalization.{side}.{phase.Phase}[{index.ToString(CultureInfo.InvariantCulture)}]", call, declared, parameters)))
            : [];
        return [.. rowKey, .. rules];
    }

    private static IEnumerable<(string Column, string Location)> RuleReads(
        string location,
        RuleCall call,
        IReadOnlySet<string> declared,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        string[] names = ["from", .. parameters.GetValueOrDefault(call.Rule) ?? []];
        var columns = names
            .Distinct(StringComparer.Ordinal)
            .Where(call.Strings.ContainsKey)
            .Select(name => (call.Strings[name], $"{location}.{name}"));
        var when = call.When is { } condition && !declared.Contains(condition.Field)
            ? [(condition.Field, $"{location}.when.field")]
            : Array.Empty<(string, string)>();
        return [.. columns, .. when];
    }

    // The same missing column is reported once, at the first place the profile reads it.
    private static IEnumerable<MissingColumn> Missing(Side side, IEnumerable<(string Column, string Location)> reads, IReadOnlyCollection<string> headers)
    {
        var present = headers.ToHashSet(StringComparer.Ordinal);
        return reads
            .Where(read => !present.Contains(read.Column))
            .DistinctBy(read => read.Column, StringComparer.Ordinal)
            .Select(read => new MissingColumn(side, read.Column, read.Location, HeaderSimilarity.Closest(read.Column, headers)));
    }
}

/// <summary>A column a profile reads that a file does not have.</summary>
public sealed record MissingColumn(Side Side, string Column, string Location, string? Suggestion)
{
    public string Message => Suggestion is null
        ? $"'{Column}' not found in {Name}."
        : $"'{Column}' not found in {Name}; did you mean '{Suggestion}'?";

    private string Name => Side is Side.Left ? "left" : "right";
}

// Ranks headers by how likely they are the column the profile meant. Deterministic: ties go to the ordinally smaller header.
internal static class HeaderSimilarity
{
    public static string? Closest(string wanted, IEnumerable<string> headers) =>
        headers
            .Select(header => (Header: header, Score: Score(wanted, header)))
            .Where(candidate => candidate.Score is not null)
            .OrderBy(candidate => candidate.Score!.Value.Rank)
            .ThenBy(candidate => candidate.Score!.Value.Distance)
            .ThenBy(candidate => candidate.Header, StringComparer.Ordinal)
            .Select(candidate => candidate.Header)
            .FirstOrDefault();

    // Rank 0: the same once case, spaces and punctuation are ignored (Trade_Date and trade date).
    // Rank 1: an abbreviation (Qty for Quantity): same first letter, every letter in order.
    // Rank 2: a small edit (Quantiy for Quantity), at most a third of the name's length.
    private static (int Rank, int Distance)? Score(string wanted, string header)
    {
        var left = Letters(wanted);
        var right = Letters(header);
        if (left.Length == 0 || right.Length == 0)
            return null;
        var distance = Distance(left, right);
        return (left, right) switch
        {
            _ when left == right => (0, distance),
            _ when left[0] == right[0] && (Subsequence(left, right) || Subsequence(right, left)) => (1, distance),
            _ when distance <= Math.Max(1, Math.Max(left.Length, right.Length) / 3) => (2, distance),
            _ => null,
        };
    }

    private static string Letters(string text) =>
        new([.. text.Where(char.IsLetterOrDigit).Select(character => char.ToUpperInvariant(character))]);

    // The candidate must be strictly shorter. An equal letter sequence is handled as rank 0.
    private static bool Subsequence(string shorter, string longer)
    {
        var position = 0;
        foreach (var character in longer)
        {
            if (position < shorter.Length && shorter[position] == character)
                position++;
        }

        return position == shorter.Length && shorter.Length < longer.Length;
    }

    private static int Distance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var row = 1; row <= left.Length; row++)
        {
            var current = new int[right.Length + 1];
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(Math.Min(previous[column] + 1, current[column - 1] + 1), substitution);
            }

            previous = current;
        }

        return previous[right.Length];
    }
}
