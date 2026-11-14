namespace RecDiffy.Excel;

/// <summary>What to write and how to label it.</summary>
public sealed record ExcelReportOptions
{
    /// <summary>The reports to write. Null writes every report.</summary>
    public IReadOnlyList<ReportKind>? Reports { get; init; }

    /// <summary>The as-of date. Ageing and settlement status are formulas on it (the named cell AsOf on Run_Info).</summary>
    public required DateOnly AsOf { get; init; }

    /// <summary>A title shown on every report and on Run_Info.</summary>
    public string Title { get; init; } = "RecDiffy reconciliation";

    /// <summary>The order currencies are listed and ranked in. Currencies not listed follow in ordinal order.</summary>
    public IReadOnlyList<string> CurrencyOrder { get; init; } = ["USD", "EUR", "GBP", "CHF", "CAD", "JPY"];

    /// <summary>Canonical field names for each report role. Null uses <see cref="FieldMap.FromProfile"/> when <see cref="Profile"/> is set.</summary>
    public FieldMap? FieldMap { get; init; }

    /// <summary>Wording overrides: a report heading or column name, as written by default, mapped to the firm's own wording.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The compiled profile the result came from. IReconciliationResult does not carry the profile, so the profile id, tolerance values (MO6),
    /// pass order (MO2, MO3) and raw column names (BO3) are read from here when it is set.
    /// </summary>
    public CompiledProfile? Profile { get; init; }

    /// <summary>The profile id used in file names. Defaults to the profile's id, or the first 12 characters of the profile hash.</summary>
    public string? ProfileId { get; init; }

    /// <summary>Side values that pay cash (BO6 signs net cash by side: receive positive, pay negative).</summary>
    public IReadOnlyList<string> PayingSides { get; init; } = ["BUY", "PAY_FIXED", "PAY"];

    /// <summary>Raw column names per source side ("left", "right") for BO3. Null reads them from the profile's normalization rules.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? RawColumns { get; init; }

    /// <summary>Entry ids to show on BO3 and MO7. Null picks them: BO3 shows non-exact one-to-one matches, MO7 every entry that is not an exact match.</summary>
    public IReadOnlyList<string>? SelectedEntries { get; init; }

    /// <summary>The most cards BO3 and MO7 draw when <see cref="SelectedEntries"/> is null.</summary>
    public int MaxCards { get; init; } = 50;
}

/// <summary>What happened to one report: the file written, or why it was skipped.</summary>
public sealed record ReportOutcome(ReportKind Kind, string FileName, string? Path, string? SkipReason)
{
    public bool Written => SkipReason is null;
}
