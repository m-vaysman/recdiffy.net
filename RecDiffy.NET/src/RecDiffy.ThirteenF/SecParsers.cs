using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace RecDiffy.ThirteenF;

internal enum AmendmentKind
{
    None,
    Restatement,
    NewHoldings,
}

internal sealed record MasterIndexEntry(string Cik, string Name, DateOnly Filed);

internal sealed record SubmissionsArchiveFile(string Name, DateOnly? FilingFrom, DateOnly? FilingTo);

internal sealed record SubmissionFiling(
    string AccessionNumber,
    DateOnly PeriodOfReport,
    DateOnly Filed,
    string AcceptanceKey,
    string FormType,
    string? PrimaryDocumentFileName,
    AmendmentKind AmendmentKind = AmendmentKind.None)
{
    public FilingRef ToFilingRef(string normalizedCik) =>
        new(normalizedCik, PeriodOfReport, Filed, AccessionNumber, FormType);

    public static SubmissionFiling FromFilingRef(FilingRef filing) =>
        new(filing.AccessionNumber, filing.PeriodOfReport, filing.Filed, "", filing.FormType, null);
}

internal sealed record ParsedSubmissionsRoot(
    string Cik,
    string ManagerName,
    ImmutableArray<SubmissionFiling> RecentFilings,
    ImmutableArray<SubmissionsArchiveFile> ArchiveFiles);

internal sealed record RawHoldingEntry(
    string Cusip,
    string Issuer,
    string TitleOfClass,
    string? PutCall,
    string AmountType,
    decimal Shares,
    decimal ValueUsd,
    int Sequence);

internal sealed record AggregatedHolding(
    string Cusip,
    string PutCallKey,
    string AmountType,
    string Issuer,
    string TitleOfClass,
    string? PutCall,
    decimal Shares,
    decimal ValueUsd);

internal static class SecParsers
{
    public static readonly DateOnly ValueInThousandsCutoff = new(2023, 1, 3);

    internal const string NoPutCallKey = "NONE";

    private static readonly XmlReaderSettings SafeXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    public static ImmutableArray<(int Year, int Quarter)> RecentQuarters(DateOnly today, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var startQuarterIndex = (today.Year * 4) + ((today.Month - 1) / 3);
        return
        [
            .. Enumerable.Range(0, count)
                .Select(offset => startQuarterIndex - offset)
                .Select(index => (Year: index / 4, Quarter: (index % 4) + 1)),
        ];
    }

    public static ImmutableArray<MasterIndexEntry> ParseMasterIndex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return
        [
            .. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TryParseMasterLine)
                .OfType<MasterIndexEntry>(),
        ];
    }

    public static ImmutableArray<ManagerHit> BuildManagerDirectory(IEnumerable<MasterIndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return
        [
            .. entries
                .GroupBy(entry => entry.Cik, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(item => item.Filed).First())
                .Select(winner => new ManagerHit(winner.Cik, winner.Name))
                .OrderBy(manager => manager.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(manager => manager.Cik, StringComparer.Ordinal),
        ];
    }

    public static ParsedSubmissionsRoot ParseSubmissionsRoot(string json, string fallbackCik)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var rawCik = root.TryGetProperty("cik", out var cikProp) ? ReadScalarText(cikProp) : null;
        var cik = !string.IsNullOrWhiteSpace(rawCik) ? SecText.NormalizeCik(rawCik) : SecText.NormalizeCik(fallbackCik);
        var name = root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind is JsonValueKind.String
            ? SecText.CollapseWhitespace(nameProp.GetString())
            : $"CIK {cik}";

        if (!root.TryGetProperty("filings", out var filingsElement) || filingsElement.ValueKind is not JsonValueKind.Object)
            return new ParsedSubmissionsRoot(cik, name, [], []);

        var recent = filingsElement.TryGetProperty("recent", out var recentElement) && recentElement.ValueKind is JsonValueKind.Object
            ? ParseColumnarFilings(recentElement)
            : [];

        var archives = filingsElement.TryGetProperty("files", out var filesElement) && filesElement.ValueKind is JsonValueKind.Array
            ? ParseArchiveFiles(filesElement)
            : [];

        return new ParsedSubmissionsRoot(cik, name, recent, archives);
    }

    public static ImmutableArray<SubmissionFiling> ParseSubmissionsArchivePage(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return root is { ValueKind: JsonValueKind.Object } && root.TryGetProperty("filings", out var filings)
            && filings is { ValueKind: JsonValueKind.Object } && filings.TryGetProperty("recent", out var recent)
            && recent is { ValueKind: JsonValueKind.Object }
                ? ParseColumnarFilings(recent)
                : ParseColumnarFilings(root);
    }

    public static AmendmentKind ParseCoverPageAmendmentKind(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var document = LoadSafeXml(xml);
        var amendmentType = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "amendmentType", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return ClassifyAmendmentType(amendmentType);
    }

    public static AmendmentKind ClassifyAmendmentType(string? amendmentType) =>
        SecText.CollapseWhitespace(amendmentType).ToUpperInvariant() switch
        {
            "NEW HOLDINGS" => AmendmentKind.NewHoldings,
            _ => AmendmentKind.Restatement,
        };

    public static ImmutableArray<SubmissionFiling> OrderChronologically(IEnumerable<SubmissionFiling> filings) =>
    [
        .. filings
            .OrderBy(filing => filing.Filed)
            .ThenBy(filing => filing.AcceptanceKey, StringComparer.Ordinal)
            .ThenBy(filing => filing.AccessionNumber, StringComparer.Ordinal),
    ];

    public static ImmutableArray<SubmissionFiling> ApplyAmendmentRules(IEnumerable<SubmissionFiling> periodFilings) =>
        OrderChronologically(periodFilings).Aggregate(
            ImmutableArray<SubmissionFiling>.Empty,
            static (active, filing) => (filing.FormType.ToUpperInvariant(), filing.AmendmentKind) switch
            {
                ("13F-HR", _) => [filing],
                ("13F-HR/A", AmendmentKind.NewHoldings) => active.IsEmpty ? [filing] : active.Add(filing),
                ("13F-HR/A", _) => [filing],
                _ => active,
            });

    public static ImmutableArray<string> ParseFilingXmlCandidates(string indexJson, string? primaryDocumentFileName)
    {
        ArgumentNullException.ThrowIfNull(indexJson);
        using var document = JsonDocument.Parse(indexJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("directory", out var dir) || dir.ValueKind is not JsonValueKind.Object)
            return [];
        if (!dir.TryGetProperty("item", out var items) || items.ValueKind is not JsonValueKind.Array)
            return [];

        var xmlFiles = Enumerable.Range(0, items.GetArrayLength())
            .Select(index => items[index])
            .Select(item => item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind is JsonValueKind.String ? nameProp.GetString() : null)
            .OfType<string>()
            .Select(name => name.Trim())
            .Where(name => name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. xmlFiles
                .Where(name => !IsKnownCoverDocument(name, primaryDocumentFileName))
                .OrderByDescending(LooksLikeInfoTableName)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public static bool TryParseInformationTableXml(string xml, DateOnly filedDate, int sequenceOffset, out ImmutableArray<RawHoldingEntry> holdings)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var document = LoadSafeXml(xml);
        if (document.Root is null)
        {
            holdings = [];
            return false;
        }

        var rootName = document.Root.Name.LocalName;
        if (string.Equals(rootName, "edgarSubmission", StringComparison.OrdinalIgnoreCase))
        {
            holdings = [];
            return false;
        }

        var infoTables = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "infoTable", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!string.Equals(rootName, "informationTable", StringComparison.OrdinalIgnoreCase) && infoTables.Length == 0)
        {
            holdings = [];
            return false;
        }

        var multiplier = filedDate < ValueInThousandsCutoff ? 1_000m : 1m;
        holdings =
        [
            .. infoTables.Select((element, index) => ParseInfoTableEntry(element, multiplier, sequenceOffset + index)),
        ];
        return true;
    }

    public static ImmutableArray<RawHoldingEntry> ParseInformationTableXml(string xml, DateOnly filedDate, int sequenceOffset = 0) =>
        TryParseInformationTableXml(xml, filedDate, sequenceOffset, out var holdings)
            ? holdings
            : throw new InvalidDataException("XML document is not a valid SEC Form 13F informationTable.");

    public static ImmutableArray<AggregatedHolding> AggregateHoldings(IEnumerable<RawHoldingEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return
        [
            .. entries
                .GroupBy(
                    entry => (entry.Cusip, PutCallKey: entry.PutCall ?? NoPutCallKey, entry.AmountType),
                    entry => entry)
                .Select(group =>
                {
                    var representative = group
                        .OrderByDescending(item => item.ValueUsd)
                        .ThenByDescending(item => item.Sequence)
                        .First();
                    var totalShares = group.Aggregate(0m, static (sum, item) => checked(sum + item.Shares));
                    var totalValueUsd = group.Aggregate(0m, static (sum, item) => checked(sum + item.ValueUsd));
                    return new AggregatedHolding(
                        group.Key.Cusip,
                        group.Key.PutCallKey,
                        group.Key.AmountType,
                        representative.Issuer,
                        representative.TitleOfClass,
                        representative.PutCall,
                        totalShares,
                        totalValueUsd);
                })
                .OrderBy(holding => holding.Cusip, StringComparer.Ordinal)
                .ThenBy(holding => holding.PutCallKey, StringComparer.Ordinal)
                .ThenBy(holding => holding.AmountType, StringComparer.Ordinal),
        ];
    }

    private static RawHoldingEntry ParseInfoTableEntry(XElement element, decimal valueMultiplier, int sequence)
    {
        var cusip = DirectChildValue(element, "cusip").ToUpperInvariant();
        if (cusip.Length == 0)
            throw new InvalidDataException("Form 13F infoTable entry is missing a CUSIP.");

        var issuer = DirectChildValue(element, "nameOfIssuer");
        var titleOfClass = DirectChildValue(element, "titleOfClass");
        var rawValueText = DirectChildValue(element, "value");
        if (!decimal.TryParse(rawValueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var rawValue))
            throw new InvalidDataException($"Form 13F infoTable entry for CUSIP '{cusip}' has invalid value '{rawValueText}'.");

        var shrsElement = element
            .Elements()
            .FirstOrDefault(child => string.Equals(child.Name.LocalName, "shrsOrPrnAmt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Form 13F infoTable entry for CUSIP '{cusip}' is missing shrsOrPrnAmt.");

        var rawSharesText = DirectChildValue(shrsElement, "sshPrnamt");
        if (!decimal.TryParse(rawSharesText, NumberStyles.Number, CultureInfo.InvariantCulture, out var shares))
            throw new InvalidDataException($"Form 13F infoTable entry for CUSIP '{cusip}' has invalid sshPrnamt '{rawSharesText}'.");

        var amountType = DirectChildValue(shrsElement, "sshPrnamtType").ToUpperInvariant() switch
        {
            "" => "SH",
            var kind => kind,
        };

        var putCall = NormalizePutCall(DirectChildValue(element, "putCall"));
        var normalizedUsd = checked(rawValue * valueMultiplier);

        return new RawHoldingEntry(
            cusip,
            issuer,
            titleOfClass,
            putCall,
            amountType,
            shares,
            normalizedUsd,
            sequence);
    }

    internal static string? NormalizePutCall(string? rawPutCall) =>
        SecText.CollapseWhitespace(rawPutCall) switch
        {
            "" => null,
            var token when string.Equals(token, "PUT", StringComparison.OrdinalIgnoreCase) => "Put",
            var token when string.Equals(token, "CALL", StringComparison.OrdinalIgnoreCase) => "Call",
            var token => token,
        };

    private static string DirectChildValue(XElement parent, string localName) =>
        SecText.CollapseWhitespace(
            parent.Elements()
                .FirstOrDefault(child => string.Equals(child.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                ?.Value);

    private static bool IsKnownCoverDocument(string fileName, string? primaryDocumentFileName) =>
        string.Equals(fileName, "primary_doc.xml", StringComparison.OrdinalIgnoreCase)
        || (primaryDocumentFileName is { Length: > 0 } && string.Equals(fileName, primaryDocumentFileName, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeInfoTableName(string fileName) =>
        fileName.Contains("info", StringComparison.OrdinalIgnoreCase)
        || fileName.Contains("table", StringComparison.OrdinalIgnoreCase)
        || fileName.Contains("13f", StringComparison.OrdinalIgnoreCase);

    private static XDocument LoadSafeXml(string xml)
    {
        try
        {
            using var stringReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(stringReader, SafeXmlSettings);
            return XDocument.Load(xmlReader);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Invalid SEC Form 13F XML document.", ex);
        }
    }

    private static MasterIndexEntry? TryParseMasterLine(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 5)
            return null;

        var form = parts[2].Trim();
        if (!IsThirteenFForm(form))
            return null;

        var rawCik = parts[0].Trim();
        if (rawCik.Length is 0 or > 10 || !rawCik.All(char.IsAsciiDigit))
            return null;

        if (!TryParseSecDate(parts[3], out var filed))
            return null;

        var cik = rawCik.PadLeft(10, '0');
        var name = SecText.CollapseWhitespace(parts[1]);
        return cik == "0000000000" || name.Length == 0
            ? null
            : new MasterIndexEntry(cik, name, filed);
    }

    private static ImmutableArray<SubmissionFiling> ParseColumnarFilings(JsonElement container)
    {
        if (!container.TryGetProperty("form", out var forms) || forms.ValueKind is not JsonValueKind.Array)
            return [];

        var count = forms.GetArrayLength();
        var accessions = GetArrayOrDefault(container, "accessionNumber");
        var filingDates = GetArrayOrDefault(container, "filingDate");
        var reportDates = GetArrayOrDefault(container, "reportDate");
        var acceptances = GetArrayOrDefault(container, "acceptanceDateTime");
        var primaryDocs = GetArrayOrDefault(container, "primaryDocument");

        return
        [
            .. Enumerable.Range(0, count)
                .Select(index => TryReadSubmissionFiling(
                    ReadArrayString(forms, index),
                    ReadArrayString(accessions, index),
                    ReadArrayString(filingDates, index),
                    ReadArrayString(reportDates, index),
                    ReadArrayString(acceptances, index),
                    ReadArrayString(primaryDocs, index)))
                .OfType<SubmissionFiling>(),
        ];
    }

    private static SubmissionFiling? TryReadSubmissionFiling(
        string? rawForm,
        string? rawAccession,
        string? rawFilingDate,
        string? rawReportDate,
        string? rawAcceptance,
        string? rawPrimaryDoc)
    {
        if (string.IsNullOrWhiteSpace(rawForm) || !IsThirteenFForm(rawForm.Trim()))
            return null;
        if (string.IsNullOrWhiteSpace(rawAccession))
            return null;
        if (!TryParseSecDate(rawFilingDate, out var filed) || !TryParseSecDate(rawReportDate, out var period))
            return null;

        return new SubmissionFiling(
            rawAccession.Trim(),
            period,
            filed,
            rawAcceptance?.Trim() ?? "",
            rawForm.Trim().ToUpperInvariant(),
            ExtractFileName(rawPrimaryDoc));
    }

    private static ImmutableArray<SubmissionsArchiveFile> ParseArchiveFiles(JsonElement filesArray) =>
    [
        .. Enumerable.Range(0, filesArray.GetArrayLength())
            .Select(index => filesArray[index])
            .Where(item => item.ValueKind is JsonValueKind.Object)
            .Select(item =>
            {
                var name = item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind is JsonValueKind.String
                    ? nameProp.GetString()?.Trim()
                    : null;
                var from = item.TryGetProperty("filingFrom", out var fromProp) && TryParseSecDate(fromProp.GetString(), out var parsedFrom)
                    ? parsedFrom
                    : (DateOnly?)null;
                var to = item.TryGetProperty("filingTo", out var toProp) && TryParseSecDate(toProp.GetString(), out var parsedTo)
                    ? parsedTo
                    : (DateOnly?)null;
                return string.IsNullOrWhiteSpace(name) ? null : new SubmissionsArchiveFile(name, from, to);
            })
            .OfType<SubmissionsArchiveFile>(),
    ];

    private static JsonElement? GetArrayOrDefault(JsonElement container, string propertyName) =>
        container.TryGetProperty(propertyName, out var element) && element.ValueKind is JsonValueKind.Array
            ? element
            : null;

    private static string? ReadArrayString(JsonElement? array, int index) =>
        array is { } element && index < element.GetArrayLength()
            ? ReadScalarText(element[index])
            : null;

    private static string? ReadScalarText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        _ => null,
    };

    private static string? ExtractFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var trimmed = path.Trim();
        var slash = trimmed.LastIndexOf('/');
        var fileName = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return fileName.Length > 0 ? fileName : null;
    }

    private static bool IsThirteenFForm(string form) =>
        string.Equals(form, "13F-HR", StringComparison.OrdinalIgnoreCase)
        || string.Equals(form, "13F-HR/A", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseSecDate(string? text, out DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            date = default;
            return false;
        }

        var trimmed = text.Trim();
        return DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParseExact(trimmed, "MM-dd-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
