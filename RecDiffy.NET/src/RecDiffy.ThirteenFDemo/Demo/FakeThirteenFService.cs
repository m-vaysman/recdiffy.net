using System.Collections.Frozen;
using RecDiffy.ThirteenF;

namespace RecDiffy.ThirteenFDemo.Demo;

public sealed class FakeThirteenFService : IThirteenFService
{
    private sealed record ManagerSeed(ManagerHit Hit, int BaseMultiplier, int RotationOffset);

    private sealed record SecuritySeed(
        string Cusip,
        string Issuer,
        string TitleOfClass,
        string? PutCall,
        decimal BaseShares,
        decimal BasePriceUsd);

    private static readonly IReadOnlyList<ManagerSeed> Managers =
    [
        new(new ManagerHit("0001067983", "BERKSHIRE HATHAWAY INC"), 18, 0),
        new(new ManagerHit("0001350694", "BRIDGEWATER ASSOCIATES, LP"), 6, 5),
        new(new ManagerHit("0001037389", "RENAISSANCE TECHNOLOGIES LLC"), 4, 11),
        new(new ManagerHit("0000102909", "VANGUARD GROUP INC"), 24, 3),
        new(new ManagerHit("0001423053", "CITADEL ADVISORS LLC"), 9, 8),
        new(new ManagerHit("0001061768", "BAUPOST GROUP LLC/MA"), 5, 14),
        new(new ManagerHit("0001336528", "PERSHING SQUARE CAPITAL MANAGEMENT, L.P."), 7, 19),
        new(new ManagerHit("0001167483", "TIGER GLOBAL MANAGEMENT LLC"), 8, 23),
    ];

    private static readonly FrozenDictionary<string, ManagerSeed> ManagersByCik =
        Managers.ToFrozenDictionary(seed => seed.Hit.Cik, StringComparer.Ordinal);

    private static readonly IReadOnlyList<SecuritySeed> Securities =
    [
        new("037833100", "APPLE INC", "COM", null, 2_400_000m, 228.50m),
        new("025816109", "AMERICAN EXPRESS CO", "COM", null, 1_516_000m, 271.20m),
        new("060505104", "BANK OF AMERICA CORP", "COM", null, 3_850_000m, 41.80m),
        new("191216100", "COCA COLA CO", "COM", null, 4_000_000m, 69.40m),
        new("166764100", "CHEVRON CORP NEW", "COM", null, 1_186_000m, 152.30m),
        new("674599105", "OCCIDENTAL PETE CORP", "COM", null, 2_550_000m, 51.60m),
        new("615369105", "MOODYS CORP", "COM", null, 246_700m, 474.10m),
        new("500754106", "KRAFT HEINZ CO", "COM", null, 3_256_000m, 34.25m),
        new("H1467J104", "CHUBB LIMITED", "COM", null, 270_300m, 288.40m),
        new("23918K108", "DAVITA INC", "COM", null, 360_900m, 156.80m),
        new("594918104", "MICROSOFT CORP", "COM", null, 980_000m, 432.15m),
        new("67066G104", "NVIDIA CORP", "COM", null, 1_750_000m, 138.60m),
        new("023135106", "AMAZON COM INC", "COM", null, 1_420_000m, 204.75m),
        new("02079K305", "ALPHABET INC", "CAP STK CL A", null, 1_110_000m, 176.90m),
        new("02079K107", "ALPHABET INC", "CAP STK CL C", null, 640_000m, 178.30m),
        new("30303M102", "META PLATFORMS INC", "CL A", null, 495_000m, 582.40m),
        new("11135F101", "BROADCOM INC", "COM", null, 820_000m, 174.20m),
        new("532457108", "ELI LILLY & CO", "COM", null, 215_000m, 812.00m),
        new("46625H100", "JPMORGAN CHASE & CO", "COM", null, 760_000m, 236.50m),
        new("92826C839", "VISA INC", "COM CL A", null, 530_000m, 294.80m),
        new("57636Q104", "MASTERCARD INCORPORATED", "CL A", null, 310_000m, 508.60m),
        new("30231G102", "EXXON MOBIL CORP", "COM", null, 1_090_000m, 118.40m),
        new("91324P102", "UNITEDHEALTH GROUP INC", "COM", null, 240_000m, 564.20m),
        new("22160K105", "COSTCO WHSL CORP NEW", "COM", null, 185_000m, 914.50m),
        new("437076102", "HOME DEPOT INC", "COM", null, 320_000m, 402.10m),
        new("742718109", "PROCTER AND GAMBLE CO", "COM", null, 610_000m, 171.35m),
        new("478160104", "JOHNSON & JOHNSON", "COM", null, 680_000m, 158.20m),
        new("931142103", "WALMART INC", "COM", null, 1_340_000m, 84.90m),
        new("68389X105", "ORACLE CORP", "COM", null, 590_000m, 182.60m),
        new("79466L302", "SALESFORCE INC", "COM", null, 275_000m, 318.40m),
        new("00724F101", "ADOBE INC", "COM", null, 165_000m, 504.80m),
        new("007903107", "ADVANCED MICRO DEVICES INC", "COM", null, 620_000m, 146.70m),
        new("64110L106", "NETFLIX INC", "COM", null, 148_000m, 824.30m),
        new("747525103", "QUALCOMM INC", "COM", null, 410_000m, 166.90m),
        new("882508104", "TEXAS INSTRS INC", "COM", null, 330_000m, 203.40m),
        new("907818108", "UNION PAC CORP", "COM", null, 260_000m, 242.10m),
        new("149123101", "CATERPILLAR INC", "COM", null, 192_000m, 386.50m),
        new("438516106", "HONEYWELL INTL INC", "COM", null, 280_000m, 224.80m),
        new("244199105", "DEERE & CO", "COM", null, 142_000m, 418.20m),
        new("539830109", "LOCKHEED MARTIN CORP", "COM", null, 118_000m, 542.60m),
        new("38141G104", "GOLDMAN SACHS GROUP INC", "COM", null, 174_000m, 568.90m),
        new("617446448", "MORGAN STANLEY", "COM NEW", null, 490_000m, 126.40m),
        new("09290D101", "BLACKROCK INC", "COM", null, 96_000m, 1_012.00m),
        new("78409V104", "S&P GLOBAL INC", "COM", null, 158_000m, 502.30m),
        new("45866F104", "INTERCONTINENTAL EXCHANGE IN", "COM", null, 310_000m, 159.70m),
        new("12572Q105", "CME GROUP INC", "COM", null, 225_000m, 228.40m),
        new("808513105", "SCHWAB CHARLES CORP", "COM", null, 710_000m, 76.80m),
        new("743315103", "PROGRESSIVE CORP", "COM", null, 238_000m, 254.60m),
        new("571748102", "MARSH & MCLENNAN COS INC", "COM", null, 210_000m, 226.10m),
        new("09857L108", "BOOKING HOLDINGS INC", "COM NEW", null, 24_500m, 4_890.00m),
        new("81762P102", "SERVICENOW INC", "COM", null, 84_000m, 998.50m),
        new("461202103", "INTUIT", "COM", null, 112_000m, 646.20m),
        new("038222105", "APPLIED MATLS INC", "COM", null, 345_000m, 182.90m),
        new("512807306", "LAM RESEARCH CORP", "COM NEW", null, 510_000m, 76.40m),
        new("482480100", "KLA CORP", "COM NEW", null, 79_000m, 678.30m),
        new("032654105", "ANALOG DEVICES INC", "COM", null, 205_000m, 221.70m),
        new("595112103", "MICRON TECHNOLOGY INC", "COM", null, 460_000m, 104.80m),
        new("697435105", "PALO ALTO NETWORKS INC", "COM", null, 190_000m, 388.20m),
        new("22788C105", "CROWDSTRIKE HLDGS INC", "CL A", null, 128_000m, 346.90m),
        new("040413106", "ARISTA NETWORKS INC", "COM", null, 164_000m, 404.50m),
        new("G29183103", "EATON CORP PLC", "SHS", null, 188_000m, 358.40m),
        new("701094104", "PARKER HANNIFIN CORP", "COM", null, 74_000m, 668.10m),
        new("G8994E103", "TRANE TECHNOLOGIES PLC", "SHS", null, 136_000m, 396.20m),
        new("824348106", "SHERWIN WILLIAMS CO", "COM", null, 115_000m, 378.90m),
        new("58155Q103", "MCKESSON CORP", "COM", null, 82_000m, 604.50m),
        new("92532F100", "VERTEX PHARMACEUTICALS INC", "COM", null, 108_000m, 476.30m),
        new("75886F107", "REGENERON PHARMACEUTICALS", "COM", null, 54_000m, 812.40m),
        new("46120E602", "INTUITIVE SURGICAL INC", "COM NEW", null, 132_000m, 526.80m),
        new("863667101", "STRYKER CORPORATION", "COM", null, 146_000m, 374.10m),
        new("101137107", "BOSTON SCIENTIFIC CORP", "COM", null, 540_000m, 88.70m),
        new("872590104", "T-MOBILE US INC", "COM", null, 290_000m, 232.60m),
        new("65339F101", "NEXTERA ENERGY INC", "COM", null, 640_000m, 78.40m),
        new("92343E102", "VERISIGN INC", "COM", null, 128_200m, 191.50m),
        new("874039100", "TAIWAN SEMICONDUCTOR MFG LTD", "SPONSORED ADS", null, 520_000m, 192.30m),
        new("78462F103", "SPDR S&P 500 ETF TR", "TR UNIT", "Call", 350_000m, 586.00m),
        new("78462F103", "SPDR S&P 500 ETF TR", "TR UNIT", "Put", 420_000m, 586.00m),
        new("46090E103", "INVESCO QQQ TR", "UNIT SER 1", "Call", 280_000m, 503.00m),
        new("46090E103", "INVESCO QQQ TR", "UNIT SER 1", "Put", 310_000m, 503.00m),
        new("464287655", "ISHARES TR", "RUSSELL 2000 ETF", "Put", 500_000m, 226.00m),
    ];

    public async Task<IReadOnlyList<ManagerHit>> SearchManagersAsync(string prefix, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ct.ThrowIfCancellationRequested();

        await Task.Delay(TimeSpan.FromMilliseconds(60L), ct).ConfigureAwait(false);

        var trimmed = prefix.Trim();
        if (trimmed.Length < 2 || take <= 0)
        {
            return [];
        }

        var normalizedDigits = trimmed.TrimStart('0');

        return
        [
            .. Managers
                .Select(seed => seed.Hit)
                .Where(hit =>
                    hit.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                    || hit.Cik.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                    || (normalizedDigits.Length > 0 && hit.Cik.TrimStart('0').StartsWith(normalizedDigits, StringComparison.Ordinal)))
                .OrderBy(hit => hit.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(hit => hit.Name, StringComparer.Ordinal)
                .Take(take),
        ];
    }

    public async Task<IReadOnlyList<FilingRef>> GetFilingsAsync(string cik, int year, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cik);
        ct.ThrowIfCancellationRequested();

        await Task.Delay(TimeSpan.FromMilliseconds(50L), ct).ConfigureAwait(false);

        if (!ManagersByCik.TryGetValue(cik, out var manager))
        {
            return [];
        }

        var serial = 100 + manager.RotationOffset;
        return year switch
        {
            2026 =>
            [
                BuildFiling(cik, new DateOnly(2026, 9, 30), new DateOnly(2026, 11, 13), 26, serial + 3, "13F-HR"),
                BuildFiling(cik, new DateOnly(2026, 6, 30), new DateOnly(2026, 8, 14), 26, serial + 2, "13F-HR"),
                BuildFiling(cik, new DateOnly(2026, 3, 31), new DateOnly(2026, 5, 15), 26, serial + 1, "13F-HR"),
            ],
            2025 =>
            [
                BuildFiling(cik, new DateOnly(2025, 12, 31), new DateOnly(2026, 2, 14), 26, serial, "13F-HR"),
                BuildFiling(cik, new DateOnly(2025, 9, 30), new DateOnly(2025, 11, 14), 25, serial + 4, "13F-HR"),
                BuildFiling(cik, new DateOnly(2025, 6, 30), new DateOnly(2025, 8, 22), 25, serial + 3, "13F-HR/A"),
                BuildFiling(cik, new DateOnly(2025, 6, 30), new DateOnly(2025, 8, 14), 25, serial + 2, "13F-HR"),
                BuildFiling(cik, new DateOnly(2025, 3, 31), new DateOnly(2025, 5, 15), 25, serial + 1, "13F-HR"),
            ],
            2024 =>
            [
                BuildFiling(cik, new DateOnly(2024, 12, 31), new DateOnly(2025, 2, 14), 25, serial - 1, "13F-HR"),
                BuildFiling(cik, new DateOnly(2024, 9, 30), new DateOnly(2024, 11, 14), 24, serial + 3, "13F-HR"),
                BuildFiling(cik, new DateOnly(2024, 6, 30), new DateOnly(2024, 8, 14), 24, serial + 2, "13F-HR"),
                BuildFiling(cik, new DateOnly(2024, 3, 31), new DateOnly(2024, 5, 15), 24, serial + 1, "13F-HR"),
            ],
            _ => [],
        };
    }

    public async Task<QuarterComparison> CompareAsync(
        string cik,
        FilingRef prior,
        FilingRef current,
        HoldingStatus? filter,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cik);
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(current);
        ct.ThrowIfCancellationRequested();

        await Task.Delay(TimeSpan.FromMilliseconds(80L), ct).ConfigureAwait(false);

        var seed = ManagersByCik.GetValueOrDefault(cik)
            ?? new ManagerSeed(new ManagerHit(cik, $"FILER {cik}"), 5, 0);

        var allRows = BuildRows(seed, prior, current);

        var unchangedCount = allRows.Count(row => row.Status is HoldingStatus.Unchanged);
        var changedCount = allRows.Count(row => row.Status is HoldingStatus.Changed);
        var newCount = allRows.Count(row => row.Status is HoldingStatus.New);
        var exitedCount = allRows.Count(row => row.Status is HoldingStatus.Exited);

        var priorTotalUsd = allRows.Sum(row => row.PriorValueUsd ?? 0m);
        var currentTotalUsd = allRows.Sum(row => row.CurrentValueUsd ?? 0m);

        var filtered = filter is { } selectedStatus
            ? [.. allRows.Where(row => row.Status == selectedStatus)]
            : allRows;

        var safePageSize = Math.Max(1, pageSize);
        var safePage = Math.Max(1, page);
        var skip = (safePage - 1) * safePageSize;
        IReadOnlyList<HoldingRow> pageRows = skip >= filtered.Count
            ? []
            : [.. filtered.Skip(skip).Take(safePageSize)];

        return new QuarterComparison(
            seed.Hit,
            prior,
            current,
            unchangedCount,
            changedCount,
            newCount,
            exitedCount,
            priorTotalUsd,
            currentTotalUsd,
            pageRows,
            filtered.Count,
            safePage,
            safePageSize);
    }

    private static FilingRef BuildFiling(
        string cik,
        DateOnly periodOfReport,
        DateOnly filed,
        int twoDigitYear,
        int sequence,
        string formType) =>
        new(
            cik,
            periodOfReport,
            filed,
            $"{cik}-{twoDigitYear.ToInvariant("D2")}-{sequence.ToInvariant("D6")}",
            formType);

    private static IReadOnlyList<HoldingRow> BuildRows(ManagerSeed manager, FilingRef prior, FilingRef current)
    {
        var priorQuarterIndex = QuarterOrdinal(prior.PeriodOfReport) + (prior.FormType.EndsWith("/A", StringComparison.Ordinal) ? 1 : 0);
        var currentQuarterIndex = QuarterOrdinal(current.PeriodOfReport) + (current.FormType.EndsWith("/A", StringComparison.Ordinal) ? 1 : 0);

        return
        [
            .. Securities
                .Select((security, index) => BuildRow(security, index, manager, priorQuarterIndex, currentQuarterIndex))
                .OfType<HoldingRow>()
                .OrderBy(row => StatusRank(row.Status))
                .ThenByDescending(row => row.CurrentValueUsd ?? row.PriorValueUsd ?? 0m)
                .ThenBy(row => row.Issuer, StringComparer.Ordinal),
        ];
    }

    private static HoldingRow? BuildRow(
        SecuritySeed security,
        int index,
        ManagerSeed manager,
        int priorQuarter,
        int currentQuarter)
    {
        var priorSnap = SnapshotAt(security, index, manager, priorQuarter);
        var currentSnap = SnapshotAt(security, index, manager, currentQuarter);

        return (priorSnap, currentSnap) switch
        {
            (null, null) => null,
            (null, { } cur) => new HoldingRow(
                security.Cusip,
                security.Issuer,
                security.TitleOfClass,
                security.PutCall,
                HoldingStatus.New,
                null,
                cur.Shares,
                null,
                null,
                cur.ValueUsd,
                null),
            ({ } pri, null) => new HoldingRow(
                security.Cusip,
                security.Issuer,
                security.TitleOfClass,
                security.PutCall,
                HoldingStatus.Exited,
                pri.Shares,
                null,
                null,
                pri.ValueUsd,
                null,
                null),
            ({ } pri, { } cur) => BuildMatchedRow(security, pri, cur),
        };
    }

    private static HoldingRow BuildMatchedRow(
        SecuritySeed security,
        (decimal Shares, decimal ValueUsd) prior,
        (decimal Shares, decimal ValueUsd) current)
    {
        var sharesPct = PercentChange(prior.Shares, current.Shares);
        var valuePct = PercentChange(prior.ValueUsd, current.ValueUsd);
        var status = prior.Shares == current.Shares
            ? HoldingStatus.Unchanged
            : HoldingStatus.Changed;

        return new HoldingRow(
            security.Cusip,
            security.Issuer,
            security.TitleOfClass,
            security.PutCall,
            status,
            prior.Shares,
            current.Shares,
            sharesPct,
            prior.ValueUsd,
            current.ValueUsd,
            valuePct);
    }

    private static (decimal Shares, decimal ValueUsd)? SnapshotAt(
        SecuritySeed security,
        int index,
        ManagerSeed manager,
        int quarterOrdinal)
    {
        var slot = (index + manager.RotationOffset + (quarterOrdinal * 3)) % 12;
        if (slot is 10 or 11)
        {
            return null;
        }

        var shareFactor = ((index + manager.RotationOffset) % 5) switch
        {
            0 => 1.00m,
            1 => 1.00m + ((quarterOrdinal % 4) * 0.08m),
            2 => 1.24m - ((quarterOrdinal % 4) * 0.07m),
            3 => 1.00m,
            _ => 0.85m + ((quarterOrdinal % 3) * 0.11m),
        };

        var priceStep = (((index * 7) + (quarterOrdinal * 5)) % 19) - 8;
        var priceFactor = 1.00m + (priceStep * 0.015m);

        var shares = decimal.Round(security.BaseShares * manager.BaseMultiplier * 0.1m * shareFactor, 0, MidpointRounding.AwayFromZero);
        var unitPrice = decimal.Round(security.BasePriceUsd * priceFactor, 2, MidpointRounding.AwayFromZero);
        var valueUsd = decimal.Round(shares * unitPrice, 0, MidpointRounding.AwayFromZero);

        return (shares, valueUsd);
    }

    private static decimal? PercentChange(decimal prior, decimal current) =>
        prior == 0m
            ? null
            : decimal.Round((current - prior) / prior * 100m, 2, MidpointRounding.AwayFromZero);

    private static int QuarterOrdinal(DateOnly period) =>
        (period.Year * 4) + ((period.Month - 1) / 3);

    private static int StatusRank(HoldingStatus status) => status switch
    {
        HoldingStatus.Changed => 0,
        HoldingStatus.New => 1,
        HoldingStatus.Exited => 2,
        HoldingStatus.Unchanged => 3,
        _ => 4,
    };
}
