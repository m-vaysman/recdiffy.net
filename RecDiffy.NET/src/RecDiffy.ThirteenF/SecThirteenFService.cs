using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;

namespace RecDiffy.ThirteenF;

public sealed class SecThirteenFService : IThirteenFService
{
    private const string SecArchivesBase = "https://www.sec.gov/Archives/edgar/";
    private const string SecSubmissionsBase = "https://data.sec.gov/submissions/";
    private const int TargetIndexedQuarters = 4;

    private readonly SecTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, ParsedSubmissionsRoot> _submissionsRoots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ImmutableArray<SubmissionFiling>> _archivePages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Cik, string Accession), AmendmentKind> _amendmentKinds = new();
    private readonly ConcurrentDictionary<(string Cik, string Accession, DateOnly Filed), ImmutableArray<RawHoldingEntry>> _filingHoldings = new();
    private readonly ConcurrentDictionary<ComparisonCacheKey, FullComparison> _comparisons = new();

    private ImmutableArray<ManagerHit>? _managerDirectory;

    public SecThirteenFService(HttpClient httpClient, string userAgentContact, TimeProvider? timeProvider = null)
        : this(
            CreateTransport(httpClient, userAgentContact, timeProvider),
            timeProvider ?? TimeProvider.System)
    {
    }

    internal SecThirteenFService(SecTransport transport, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _transport = transport;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ManagerHit>> SearchManagersAsync(string prefix, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ct.ThrowIfCancellationRequested();

        var trimmed = prefix.Trim();
        if (trimmed.Length == 0)
            return [];

        var directory = await EnsureManagerDirectoryAsync(ct).ConfigureAwait(false);
        return
        [
            .. directory
                .Where(manager => manager.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                .Take(take),
        ];
    }

    public async Task<IReadOnlyList<FilingRef>> GetFilingsAsync(string cik, int year, CancellationToken ct)
    {
        var normalizedCik = SecText.NormalizeCik(cik);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        ct.ThrowIfCancellationRequested();

        var (_, allFilings) = await LoadSubmissionsAsync(normalizedCik, year, ct).ConfigureAwait(false);
        var yearFilings = allFilings.Where(filing => filing.PeriodOfReport.Year == year).ToArray();
        if (yearFilings.Length == 0)
            return [];

        var enriched = await PopulateAmendmentKindsAsync(normalizedCik, yearFilings, ct).ConfigureAwait(false);
        return
        [
            .. enriched
                .GroupBy(filing => filing.PeriodOfReport)
                .Select(group => SecParsers.ApplyAmendmentRules(group))
                .Where(chain => !chain.IsEmpty)
                .Select(chain => chain[^1].ToFilingRef(normalizedCik))
                .OrderByDescending(filing => filing.PeriodOfReport)
                .ThenByDescending(filing => filing.Filed)
                .ThenByDescending(filing => filing.AccessionNumber, StringComparer.Ordinal),
        ];
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
        var normalizedCik = SecText.NormalizeCik(cik);
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ct.ThrowIfCancellationRequested();

        var cacheKey = new ComparisonCacheKey(
            normalizedCik,
            prior.PeriodOfReport,
            prior.AccessionNumber,
            prior.Filed,
            current.PeriodOfReport,
            current.AccessionNumber,
            current.Filed);

        if (_comparisons.TryGetValue(cacheKey, out var cached))
            return cached.Slice(prior, current, filter, page, pageSize);

        var earliestYear = Math.Min(prior.PeriodOfReport.Year, current.PeriodOfReport.Year);
        var (manager, allFilings) = await LoadSubmissionsAsync(normalizedCik, earliestYear, ct).ConfigureAwait(false);

        var priorChain = await ResolveEffectiveFilingChainAsync(normalizedCik, allFilings, prior, ct).ConfigureAwait(false);
        var currentChain = await ResolveEffectiveFilingChainAsync(normalizedCik, allFilings, current, ct).ConfigureAwait(false);

        var priorHoldings = await LoadAndAggregateChainAsync(normalizedCik, priorChain, ct).ConfigureAwait(false);
        var currentHoldings = await LoadAndAggregateChainAsync(normalizedCik, currentChain, ct).ConfigureAwait(false);

        var full = ThirteenFReconciler.Reconcile(manager, priorHoldings, currentHoldings, ct);
        _comparisons.TryAdd(cacheKey, full);
        return full.Slice(prior, current, filter, page, pageSize);
    }

    private static SecTransport CreateTransport(HttpClient httpClient, string userAgentContact, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        var userAgent = SecTransport.FormatUserAgent(userAgentContact);
        var useProcessDefaults = ReferenceEquals(httpClient, SecSharedHttp.Client) && timeProvider is null;
        var throttle = timeProvider is null
            ? SecThrottle.ProcessDefault
            : new SecThrottle(timeProvider, TimeSpan.FromMilliseconds(125));
        var cache = useProcessDefaults
            ? SecResponseCache.ProcessDefault
            : new SecResponseCache();
        return new SecTransport(httpClient, userAgent, throttle, cache);
    }

    private async ValueTask<ImmutableArray<ManagerHit>> EnsureManagerDirectoryAsync(CancellationToken ct)
    {
        if (_managerDirectory is { } existing)
            return existing;

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var candidates = SecParsers.RecentQuarters(today, TargetIndexedQuarters + 1);
        var loadedEntries = new List<MasterIndexEntry>();
        var loadedQuarters = 0;
        HttpRequestException? lastNotFound = null;

        for (var index = 0; index < candidates.Length && loadedQuarters < TargetIndexedQuarters; index++)
        {
            var (year, quarter) = candidates[index];
            var uri = new Uri($"{SecArchivesBase}full-index/{year.ToInvariant()}/QTR{quarter.ToInvariant()}/master.gz");
            try
            {
                var content = await _transport.GetStringAsync(uri, ct).ConfigureAwait(false);
                loadedEntries.AddRange(SecParsers.ParseMasterIndex(content));
                loadedQuarters++;
            }
            catch (HttpRequestException ex) when (index == 0 && ex.StatusCode is HttpStatusCode.NotFound)
            {
                lastNotFound = ex;
            }
        }

        if (loadedQuarters == 0 && lastNotFound is not null)
            throw lastNotFound;

        var built = SecParsers.BuildManagerDirectory(loadedEntries);
        _managerDirectory = built;
        return built;
    }

    private async Task<(ManagerHit Manager, ImmutableArray<SubmissionFiling> Filings)> LoadSubmissionsAsync(
        string normalizedCik,
        int minPeriodYear,
        CancellationToken ct)
    {
        if (!_submissionsRoots.TryGetValue(normalizedCik, out var root))
        {
            var rootUri = new Uri($"{SecSubmissionsBase}CIK{normalizedCik}.json");
            var rootJson = await _transport.GetStringAsync(rootUri, ct).ConfigureAwait(false);
            root = SecParsers.ParseSubmissionsRoot(rootJson, normalizedCik);
            _submissionsRoots.TryAdd(normalizedCik, root);
        }

        var manager = new ManagerHit(root.Cik, root.ManagerName);
        var minNeededFiledDate = new DateOnly(minPeriodYear, 1, 1);
        var needsArchives = !root.ArchiveFiles.IsEmpty
            && (root.RecentFilings.IsEmpty || root.RecentFilings.Min(filing => filing.Filed) >= minNeededFiledDate);

        if (!needsArchives)
            return (manager, root.RecentFilings);

        var relevantArchives = root.ArchiveFiles
            .Where(file => file.FilingTo is null || file.FilingTo.Value >= minNeededFiledDate)
            .ToArray();

        var combined = new List<SubmissionFiling>(root.RecentFilings);
        foreach (var archive in relevantArchives)
        {
            if (!_archivePages.TryGetValue(archive.Name, out var pageFilings))
            {
                var pageUri = new Uri($"{SecSubmissionsBase}{archive.Name}");
                var pageJson = await _transport.GetStringAsync(pageUri, ct).ConfigureAwait(false);
                pageFilings = SecParsers.ParseSubmissionsArchivePage(pageJson);
                _archivePages.TryAdd(archive.Name, pageFilings);
            }

            combined.AddRange(pageFilings);
        }

        var deduplicated = combined
            .GroupBy(filing => filing.AccessionNumber, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToImmutableArray();

        return (manager, deduplicated);
    }

    private async Task<ImmutableArray<SubmissionFiling>> PopulateAmendmentKindsAsync(
        string normalizedCik,
        IReadOnlyList<SubmissionFiling> filings,
        CancellationToken ct)
    {
        var updated = new List<SubmissionFiling>(filings.Count);
        foreach (var filing in filings)
        {
            if (!string.Equals(filing.FormType, "13F-HR/A", StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(filing);
                continue;
            }

            var kind = await LoadAmendmentKindAsync(normalizedCik, filing, ct).ConfigureAwait(false);
            updated.Add(filing with { AmendmentKind = kind });
        }

        return [.. updated];
    }

    private async Task<AmendmentKind> LoadAmendmentKindAsync(
        string normalizedCik,
        SubmissionFiling filing,
        CancellationToken ct)
    {
        var key = (normalizedCik, filing.AccessionNumber);
        if (_amendmentKinds.TryGetValue(key, out var cached))
            return cached;

        var cikDir = SecText.ArchiveCikSegment(normalizedCik);
        var accDir = SecText.AccessionSegment(filing.AccessionNumber);
        var coverDoc = filing.PrimaryDocumentFileName ?? "primary_doc.xml";
        if (!coverDoc.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            _amendmentKinds.TryAdd(key, AmendmentKind.Restatement);
            return AmendmentKind.Restatement;
        }

        var coverUri = new Uri($"{SecArchivesBase}data/{cikDir}/{accDir}/{Uri.EscapeDataString(coverDoc)}");

        var xml = await _transport.GetStringAsync(coverUri, ct).ConfigureAwait(false);
        var kind = SecParsers.ParseCoverPageAmendmentKind(xml);
        _amendmentKinds.TryAdd(key, kind);
        return kind;
    }

    private async Task<ImmutableArray<SubmissionFiling>> ResolveEffectiveFilingChainAsync(
        string normalizedCik,
        ImmutableArray<SubmissionFiling> allFilings,
        FilingRef target,
        CancellationToken ct)
    {
        var samePeriod = SecParsers.OrderChronologically(
            allFilings.Where(filing => filing.PeriodOfReport == target.PeriodOfReport));

        var targetIndex = FindAccessionIndex(samePeriod, target.AccessionNumber);
        if (targetIndex < 0)
            return [SubmissionFiling.FromFilingRef(target)];

        var slice = samePeriod[..(targetIndex + 1)];
        var enriched = await PopulateAmendmentKindsAsync(normalizedCik, slice, ct).ConfigureAwait(false);
        var effective = SecParsers.ApplyAmendmentRules(enriched);
        return effective.IsEmpty ? [SubmissionFiling.FromFilingRef(target)] : effective;
    }

    private static int FindAccessionIndex(ImmutableArray<SubmissionFiling> filings, string accessionNumber)
    {
        for (var index = 0; index < filings.Length; index++)
        {
            if (string.Equals(filings[index].AccessionNumber, accessionNumber, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private async Task<ImmutableArray<AggregatedHolding>> LoadAndAggregateChainAsync(
        string normalizedCik,
        ImmutableArray<SubmissionFiling> chain,
        CancellationToken ct)
    {
        var rawEntries = new List<RawHoldingEntry>();
        var sequenceOffset = 0;

        foreach (var filing in chain)
        {
            var entries = await LoadFilingHoldingsAsync(normalizedCik, filing, sequenceOffset, ct).ConfigureAwait(false);
            rawEntries.AddRange(entries);
            sequenceOffset += entries.Length;
        }

        return SecParsers.AggregateHoldings(rawEntries);
    }

    private async Task<ImmutableArray<RawHoldingEntry>> LoadFilingHoldingsAsync(
        string normalizedCik,
        SubmissionFiling filing,
        int sequenceOffset,
        CancellationToken ct)
    {
        var cacheKey = (normalizedCik, filing.AccessionNumber, filing.Filed);
        if (_filingHoldings.TryGetValue(cacheKey, out var cached))
        {
            return sequenceOffset == 0
                ? cached
                : [.. cached.Select((item, idx) => item with { Sequence = sequenceOffset + idx })];
        }

        var cikDir = SecText.ArchiveCikSegment(normalizedCik);
        var accDir = SecText.AccessionSegment(filing.AccessionNumber);
        var indexUri = new Uri($"{SecArchivesBase}data/{cikDir}/{accDir}/index.json");
        var indexJson = await _transport.GetStringAsync(indexUri, ct).ConfigureAwait(false);
        var candidates = SecParsers.ParseFilingXmlCandidates(indexJson, filing.PrimaryDocumentFileName);

        foreach (var fileName in candidates)
        {
            var xmlUri = new Uri($"{SecArchivesBase}data/{cikDir}/{accDir}/{Uri.EscapeDataString(fileName)}");
            var xml = await _transport.GetStringAsync(xmlUri, ct).ConfigureAwait(false);
            if (SecParsers.TryParseInformationTableXml(xml, filing.Filed, 0, out var parsed))
            {
                _filingHoldings.TryAdd(cacheKey, parsed);
                return sequenceOffset == 0
                    ? parsed
                    : [.. parsed.Select((item, idx) => item with { Sequence = sequenceOffset + idx })];
            }
        }

        _filingHoldings.TryAdd(cacheKey, []);
        return [];
    }

    private readonly record struct ComparisonCacheKey(
        string Cik,
        DateOnly PriorPeriod,
        string PriorAccession,
        DateOnly PriorFiled,
        DateOnly CurrentPeriod,
        string CurrentAccession,
        DateOnly CurrentFiled);
}
