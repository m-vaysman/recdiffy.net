namespace RecDiffy.Results;

/// <summary>Where a side's culture came from: its own declaration, the run's declaration, or the server's culture when neither was declared.</summary>
public enum CultureOrigin
{
    Side,
    Run,
    Server,
}

/// <summary>How one side's text was read: the culture's name (empty for invariant), its separators, where the culture came from, and whether date formats were strict.</summary>
public sealed record SideReading(string Culture, string DecimalSeparator, string GroupSeparator, CultureOrigin Origin, bool StrictDateFormats);
