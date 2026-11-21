using RecDiffy;
using RecDiffy.IO;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

// args: referenceProfile builderProfile left right
var csv = new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma };
IReconciliationResult RunOne(string profile) =>
    Reconciler.Run(
        Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText(profile)), TradingRulePack.AddTo(RuleRegistry.CreateCore())),
        FileSources.FromCsv("left", args[2], csv),
        FileSources.FromCsv("right", args[3], csv),
        ReferenceDataSet.Empty);
static string Side(IEnumerable<SourceRef> rows) => string.Join(",", rows.Select(r => r.Ordinal).Order());
static HashSet<string> Pairings(IReconciliationResult r) =>
    r.Entries.Where(e => e.Alignment == Alignment.Matched).Select(e => $"L:{Side(e.Left)}|R:{Side(e.Right)}").ToHashSet(StringComparer.Ordinal);
static string Sum(RunSummary s) => $"matched={s.Matched} exact={s.Exact} tol={s.WithinTolerance} break={s.Break} lo={s.LeftOnly} ro={s.RightOnly} amb={s.Ambiguous} si={s.SearchIncomplete} q={s.Quarantined} sup={s.Suppressed}";
var reference = RunOne(args[0]);
var builder = RunOne(args[1]);
var rp = Pairings(reference);
var bp = Pairings(builder);
var common = rp.Intersect(bp).Count();
var s = builder.Summary;
var clean = s.Break == 0 && s.LeftOnly == 0 && s.RightOnly == 0 && s.Ambiguous == 0 && s.SearchIncomplete == 0 && s.Quarantined == 0;
var samePairs = common == rp.Count && rp.Count == bp.Count;
var supp = reference.Rows.Where(x => x.Disposition == Disposition.Suppressed).Select(x => x.Row.Ordinal + x.Row.Source).ToHashSet();
var bsupp = builder.Rows.Where(x => x.Disposition == Disposition.Suppressed).Select(x => x.Row.Ordinal + x.Row.Source).ToHashSet();
Console.WriteLine($"builder  {Sum(s)}");
Console.WriteLine($"reference {Sum(reference.Summary)}");
Console.WriteLine($"pairings reference={rp.Count} builder={bp.Count} identical={common} sameSuppressed={supp.SetEquals(bsupp)}");
Console.WriteLine($"VERDICT clean100={clean} samePairingsAsKey={samePairs}");
