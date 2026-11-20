namespace RecDiffy.ThirteenF;

internal static class SecText
{
    public static string NormalizeCik(string cik)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cik);
        var trimmed = cik.Trim();
        var digits = trimmed.StartsWith("CIK", StringComparison.OrdinalIgnoreCase)
            ? trimmed[3..].Trim()
            : trimmed;

        if (digits.Length is 0 or > 10 || !digits.All(char.IsAsciiDigit))
            throw new ArgumentException($"Invalid SEC CIK '{cik}'. Expected 1 to 10 ASCII digits.", nameof(cik));

        var padded = digits.PadLeft(10, '0');
        return padded == "0000000000"
            ? throw new ArgumentException($"Invalid SEC CIK '{cik}'. CIK cannot be all zeros.", nameof(cik))
            : padded;
    }

    public static string ArchiveCikSegment(string normalizedCik) =>
        normalizedCik.TrimStart('0') is { Length: > 0 } stripped ? stripped : "0";

    public static string AccessionSegment(string accessionNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessionNumber);
        var stripped = string.Concat(accessionNumber.Trim().Where(ch => ch is not '-'));
        return stripped.Length > 0 && stripped.All(char.IsAsciiLetterOrDigit)
            ? stripped
            : throw new ArgumentException($"Invalid SEC accession number '{accessionNumber}'.", nameof(accessionNumber));
    }

    public static string CollapseWhitespace(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
