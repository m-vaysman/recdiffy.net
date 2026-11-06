namespace RecDiffy.Excel;

/// <summary>The reports RecDiffy.Excel can write. Each is written to its own .xlsx file.</summary>
public enum ReportKind
{
    /// <summary>BO1: one line per open item, ranked by settlement urgency and cash at stake.</summary>
    ExceptionBlotter,

    /// <summary>BO2: ours and theirs next to each other for every compared field of each non-exact matched pair.</summary>
    SideBySideCompare,

    /// <summary>BO3: each side's original columns next to the canonical values they normalized to.</summary>
    RawVsNormalized,

    /// <summary>BO4: lifecycle chains (NEW, CANC, CORR) with the surviving row and what it matched.</summary>
    LifecycleChains,

    /// <summary>BO5: block and allocations, with a check that the allocations add up to the block.</summary>
    Allocations,

    /// <summary>BO6: expected cash by settle date and currency, with the difference split into its causes.</summary>
    CashLadder,

    /// <summary>MO1: control totals by counterparty, asset and currency, tied back to the files.</summary>
    ControlTotals,

    /// <summary>MO2: match-quality KPIs and rates by counterparty.</summary>
    Scorecard,

    /// <summary>MO3: how the matching cascade consumed the population, with a bar chart.</summary>
    CascadeFunnel,

    /// <summary>MO4: open items by counterparty and break category.</summary>
    BreakHeatmap,

    /// <summary>MO5: open items aged in business days, with settlement status at the as-of date.</summary>
    Ageing,

    /// <summary>MO6: every within-tolerance difference and the share of its tolerance it used.</summary>
    ToleranceUsage,

    /// <summary>MO7: the engine's explanation trail for each selected entry.</summary>
    AuditTrail,
}

/// <summary>A report role: the meaning a report gives to a canonical field. <see cref="FieldMap"/> binds roles to field names.</summary>
public enum ReportRole
{
    Counterparty,
    Instrument,
    Asset,
    Side,
    TradeDate,
    SettleDate,
    Quantity,
    Price,
    Currency,
    NetCash,
    Accrued,
    Fees,

    /// <summary>Optional: the book an allocation is booked to (BO5).</summary>
    Book,

    /// <summary>Optional: the lifecycle action field the profile's lifecycle consolidation reads (BO4).</summary>
    LifecycleAction,

    /// <summary>Optional: the lifecycle target field the profile's lifecycle consolidation reads (BO4).</summary>
    LifecycleTarget,
}
