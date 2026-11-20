# RecDiffy.NET for AI agents

This page is for an AI agent that writes code **with** RecDiffy.NET: it installs the packages, builds a profile, runs a reconciliation and reports the result. An agent that changes the RecDiffy.NET repository itself follows [AGENTS.md](../AGENTS.md) instead.

Every name on this page is in the public API baseline, [RecDiffy.txt](../RecDiffy.NET/src/RecDiffy.Tests/ApiBaseline/RecDiffy.txt). When this page and that file disagree, the file is right.

## Load the docs

| File | What it holds |
|---|---|
| [llms.txt](https://m-vaysman.github.io/recdiffy.net/llms.txt) | The index: every doc as a raw Markdown link, with one line on what it answers. |
| [llms-full.txt](https://m-vaysman.github.io/recdiffy.net/llms-full.txt) | This page, the README and the reference docs in one file. |
| [RecDiffy.txt](https://raw.githubusercontent.com/m-vaysman/recdiffy.net/main/RecDiffy.NET/src/RecDiffy.Tests/ApiBaseline/RecDiffy.txt) | Every public type and member of the `RecDiffy` package, one per line. Look a name up here before you use it. |
| [RecDiffy.RulePacks.Trading.txt](https://raw.githubusercontent.com/m-vaysman/recdiffy.net/main/RecDiffy.NET/src/RecDiffy.Tests/ApiBaseline/RecDiffy.RulePacks.Trading.txt) | The same for the trading rule pack. |

### Start with one prompt

A person can paste this into a coding agent, with their own data named in it:

```text
Read https://m-vaysman.github.io/recdiffy.net/llms.txt, then
https://raw.githubusercontent.com/m-vaysman/recdiffy.net/main/docs/agents.md.
Use RecDiffy.NET to reconcile <our data> against <their data>. Install the packages with --prerelease.
Use only types and members that the public API baseline lists. Declare a culture, keep amounts in decimal,
and report matched, broken, one-sided, ambiguous and quarantined rows separately.
```

## Install

Every release is a prerelease, so `dotnet add package` needs `--prerelease`. The packages target .NET 8 and .NET 10.

```shell
dotnet add package RecDiffy --prerelease
```

| Package | Add it when you need |
|---|---|
| `RecDiffy` | The engine: profiles, matching, comparison, results, `RecBuilder`, push and snapshot. |
| `RecDiffy.IO` | To read CSV and Excel files as record sources. |
| `RecDiffy.Excel` | To write formatted Excel reports from a result. |
| `RecDiffy.RulePacks.Trading` | Trading rules: LEI and alias lookup, side resolution, absolute quantity, 32nds prices, and the net cash and pro rata attributions. |
| `RecDiffy.Fix` | To read FIX tag=value messages strictly and flatten them into rows. |
| `RecDiffy.Extensions` | `DataTable` and HTML grids from a `RecResult`. |

`RecDiffy`, `RecDiffy.IO`, `RecDiffy.Excel`, `RecDiffy.RulePacks.Trading` and `RecDiffy.Fix` ship together at one version, so use the same version of each. `RecDiffy.Extensions` has its own version numbers.

## Pick the API

| You have | Use |
|---|---|
| Two lists of your own objects, matched on equal members, with a few members to compare | `RecBuilder.Between(left, right)`. See [RecBuilder](#recbuilder-typed-lambdas). |
| A reconciliation that needs row keys, a culture, lifecycle chains, packages, routes, a conditional tolerance, date formats or trading rules | `ProfileBuilder`, or a JSON profile. See [ProfileBuilder](#profilebuilder-sidebuilder-pathbuilder). |
| A profile that people edit, review or keep as configuration | JSON, read with `ProfileDocument.Parse`. See [PROFILES.md](../RecDiffy.NET/docs/PROFILES.md). |
| CSV or Excel files | `RecDiffy.IO`: `FileSources.FromCsv`, `FileSources.FromExcel`. |
| Rows that arrive in batches | `profile.Push(left, right)`, then `Snapshot()`. See [Push rows in batches](#push-rows-in-batches). |
| FIX logs or drop copies | `RecDiffy.Fix`: `FixRows.Read`, then a profile. See [Part 3](#part-3-fix-messages-with-recdiffyfix). |
| Excel reports for operations staff | `RecDiffy.Excel`: `result.WriteAll(folder, options)`. See the README's [Write Excel reports](../README.md#write-excel-reports). |
| Tables or HTML from a `RecResult` | `RecDiffy.Extensions`: `ToMatchTable`, `ToBreakTable`, `ToControlTotalsTable`, `ToSummaryTable`, `ToDataGridHtml`. |

## How a reconciliation runs

**Normalize → Consolidate → Match → Compare → Explain.** The two inputs are always called `left` and `right`.

1. **Normalize.** Each side's rules turn raw cells into typed, canonical fields. A value that can't be read becomes null with an `Invalid` diagnostic, and its row is quarantined.
2. **Consolidate** (optional, per side). `lifecycle` collapses each cancel and correct chain into the one row that survives. `packages` merges the linked legs of one trade into one item.
3. **Match.** Each row goes to the first path whose route accepts it. The path's passes run in order, and the earliest pass that can commit a match takes the rows.
4. **Compare.** On every matched entry, each `Key` and `Economic` field is compared, after rounding to its scale, with the tolerance declared for it.
5. **Explain.** Every entry records the pass that decided it, and every input row gets exactly one disposition.

## Part 1: the engine

### Compile once, run many times

```csharp
using RecDiffy;
using RecDiffy.IO;        // FileSources, CsvOptions
using RecDiffy.Results;   // Alignment, FieldOutcome, Disposition

var registry = RuleRegistry.CreateCore();   // TradingRulePack.AddTo(RuleRegistry.CreateCore()) for trading rules
var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText("warehouse.profile.json")), registry);

var csv = new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma };
var result = Reconciler.Run(
    profile,
    FileSources.FromCsv("bins", "bins.csv", csv),
    FileSources.FromCsv("book", "book.csv", csv),
    ReferenceDataSet.Empty);
```

- **`Reconciler.Compile(document, registry)`** checks the whole profile and throws one `ProfileCompileException` whose `Errors` list every `ProfileError(Code, Message)`. Keep the `CompiledProfile` and reuse it for every run.
- **`Reconciler.Run`** and **`RunAsync`** take the profile, a left and a right `RecordSource`, a `ReferenceDataSet` (`ReferenceDataSet.Empty` unless a rule looks parties or date formats up), optional `RunOptions` and a `CancellationToken`. A cancelled run throws `OperationCanceledException` and returns nothing.
- **A bad value doesn't stop a run.** Its row is quarantined and the run goes on.

The warehouse profile and its two CSV files are in the README's [Reconcile two CSV files with a profile](../README.md#reconcile-two-csv-files-with-a-profile). A JSON profile has these top-level keys; only `formatVersion` (always `1`), `profileId`, `version`, `fields` and `paths` are required.

| Key | What it declares |
|---|---|
| `culture`, `strictDateFormats`, `rounding` | How text is read and how values round. |
| `sources` | Each side's `rowKey` column, and a side's own culture. |
| `fields` | The canonical fields: `name`, `type` (`Decimal`, `Date`, `Symbol`, `Identifier`, `Text`), `role` (`Key`, `Economic`, `Identity`, `Lifecycle`, `Package`, `Text`) and `scale`. |
| `normalization` | Each side's rules, in four phases: `identify`, `parse`, `derive`, `validate`. |
| `consolidation` | Each side's `lifecycle` and `packages`. |
| `paths` | A `route`, the `passes` in order, and `diff.fields` tolerances. |
| `limits` | Bounds on the group search. |

Names and keys are case-sensitive, and an unknown key is silently ignored. Every key, rule, pass, error code and diagnostic code is in [PROFILES.md](../RecDiffy.NET/docs/PROFILES.md).

### Feed it rows

| Rows come from | Call |
|---|---|
| Objects in memory | `RecordSources.FromObjects(name, rows, new SourceMap<T>().Text(column, row => ...).Decimal(column, row => ...).Date(column, row => ...))`. Also takes an `IAsyncEnumerable<T>`. |
| An `IDataReader` | `RecordSources.FromDataReader(name, () => reader, tabularOptions)` |
| CSV or Excel files | `FileSources.FromCsv(name, path, csvOptions)`, `FileSources.FromExcel(name, path, excelOptions)`, in `RecDiffy.IO`. |
| Dictionaries of text | `profile.Push(left, right)`. See [Push rows in batches](#push-rows-in-batches). |

The `name` you give a source is only a label. In the result, `SourceRef.Source` is always `left` or `right`. Before reading a file, `ProfileCheck.AgainstHeaders(document, leftHeaders, rightHeaders)` lists the columns the profile reads that a file doesn't have.

### Read the result

A result answers four separate questions. None implies another.

| Question | Property | Values |
|---|---|---|
| Did rows correspond across the sides? | `ResultEntry.Alignment` | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly` |
| How were rows grouped? | `ResultEntry.Composition` (flags) | `OneToOne`, `ManyToOne`, `OneToMany`, plus `Package` and `LifecycleCollapsed`; `None` when not matched |
| Do the compared values agree? | `ResultEntry.Outcome`, `FieldDiff.Outcome` | `Exact`, `WithinTolerance`, `Break`; `NotCompared` when nothing was compared |
| What happened to this input row? | `RowResult.Disposition` | `Matched`, `Ambiguous`, `SearchIncomplete`, `LeftOnly`, `RightOnly`, `Quarantined`, `Suppressed` |

```csharp
var summary = result.Summary;   // entry counts, except Quarantined and Suppressed, which count rows

var breaks = result.Entries.Where(entry => entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Break });
foreach (var entry in breaks)
    foreach (var diff in entry.Diffs)   // only the fields that differ
        Console.WriteLine($"{diff.Field}: {result.Format(diff.Left)} vs {result.Format(diff.Right)}");

foreach (var row in result.Rows.Where(row => row.Disposition == Disposition.Quarantined))
    Console.WriteLine($"{row.Row.Source} {row.Row.RowKey}: {string.Join(", ", row.Diagnostics.Select(d => d.Code))}");
```

| Member | What it gives |
|---|---|
| `result.Entries` | One `ResultEntry` per match, ambiguous cluster, incomplete search or unmatched row: `Id`, `PathId`, `Left` and `Right` (`SourceRef`s), `Diffs`, `Explanation`. |
| `result.Rows` | One `RowResult` per input row: `Disposition`, `Suppression`, `Validity`, `Diagnostics`, `EntryId`. |
| `result.Raw(row, column)` | A source cell as text. |
| `result.Canonical(row)` | The row's values after normalization. |
| `result.Trace(row)` | Each rule that wrote a field, with the raw and canonical text. |
| `result.Projection(entry.Id, side)` | The combined row a `ManyToOne` or `OneToMany` entry compared. |
| `entry.Explanation` | `CommittedBy`, the pass that decided the entry, and `Trail`, each pass's verdict, key and rows. Check `Alignment` before you read `CommittedBy` as a match. |
| `result.Run` | `ProfileHash`, `ReferenceId`, `ReferenceHash`, `EngineVersion`, and `Reading["left"]` and `Reading["right"]`: each side's culture and separators. |

The result types are read-only, and the concrete types are internal, so don't cast them. [RESULTS.md](../RecDiffy.NET/docs/RESULTS.md) explains every part, and [RESULT-EXAMPLES.md](../RecDiffy.NET/docs/RESULT-EXAMPLES.md) shows real output.

### Push rows in batches

The `RecPush` extensions keep every row pushed so far and reconcile them when you ask. Each call returns a new value and changes nothing.

```csharp
// A row is its source cells by column, as text: any IReadOnlyDictionary<string, string?>.
var batch = profile.Push(left: ourRows, right: theirRows);

var (view, current) = batch.Snapshot();   // runs Reconciler.Run over every row pushed so far
var breaks = view.Breaks;                 // the entries whose outcome is Break
var result = view.Result;                 // the full IReconciliationResult

batch = current.Push(moreOurRows, moreTheirRows);   // adds rows; the next snapshot runs again

var tomorrow = profile.Push(view.Result);   // a new batch holding the rows this result left open
```

- **`Snapshot()` runs every pushed row again.** It isn't incremental. Keep the batch it returns: snapshotting that batch again, with no push in between, returns the same view without running. `batch.IsCurrent` says whether it would.
- **`Push(previous)` carries what a result left open:** the rows of every entry that isn't `Matched`, the rows their lifecycle chains replaced, and quarantined rows. Each is rebuilt from `Raw` with the row-key column and the columns the profile's rules read, so other columns are dropped.
- **`RecBatch.Version`** counts the pushes, and `RecView.Version` the pushes a view covers.

## Part 2: profiles in code

### RecBuilder: typed lambdas

`RecBuilder` reconciles two lists of your own objects. Each pair of `Expression<Func<...>>` lambdas selects a member on each side, and the builder writes the profile for you.

```csharp
var trades = RecBuilder.Between(ours, confirms)
    .MatchOn(t => t.Isin, c => c.Isin)                      // pass P1: a natural key
        .And(t => t.Side, c => c.BuySell)                  // adds Side to P1's key
            .Value("BUY",  accepts: ["B", "BOT", "BUY"])   // spellings that mean BUY, on either side
            .Value("SELL", accepts: ["S", "SLD", "SELL"])
    .Compare(t => t.Qty, c => c.Qty)                        // compared exactly
    .Compare(t => t.Price, c => c.Price, tolerance: 0.0001m)
    .Run();

foreach (var match in trades.Breaks)
    foreach (var diff in match.Diffs)
        Console.WriteLine($"{match.Left[0].Ref} vs {match.Right[0].Ref}: {diff.Field} {trades.Result.Format(diff.Left)} vs {trades.Result.Format(diff.Right)}");

record Trade(string Ref, string Isin, string Side, int Qty, decimal Price);
record Confirm(string Ref, string Isin, string BuySell, int Qty, decimal Price);
```

| Method | What it does |
|---|---|
| `MatchOn(left, right)` | Adds a natural-key pass on one member pair. Passes are named `P1`, `P2` and so on. |
| `.And(left, right)` | Adds a member pair to the latest pass's key. |
| `ThenMatchOn(left, right)` | Adds the next pass, tried on what earlier passes left unmatched. |
| `Compare(left, right, tolerance:)` | Compares a member pair on matched items. A decimal may differ by up to `tolerance`, inclusive. |
| `Sum(left, right)` | Compares a decimal pair and adds two group-sum passes after the natural-key passes: several left rows against one right row, then several right rows against one left row. Each blocks on the latest `MatchOn` or `ThenMatchOn` key and matches only when every summed pair adds up exactly. |
| `Value(value, accepts)`, `Value(value, ours, theirs)`, `Value(spelling => ...)` | Names a value of the string member just added to a key, and the spellings that mean it. A spelling nobody listed is quarantined with `UnknownSymbol`. Call it right after `MatchOn`, `ThenMatchOn` or `And`. |
| `Opposite("BUY", "SELL")`, `Opposite(("BUY", "S"), ("SELL", "B"))`, `Opposite((ours, theirs) => ...)` | Says a left value matches the opposite right value, as for a cross trade. Two items that agree don't match. |
| `Named(profileId, version)` | Names the profile. The default is `typed`, version `1.0.0`. |
| `Profile` | The `ProfileDocument` it builds. `.ToJson()` writes it as JSON, a good start for a JSON profile. |
| `Run(cancellationToken)` | Runs with the core rules and `ReferenceDataSet.Empty`, and returns a `RecResult<TLeft, TRight>`: `Matched`, `Breaks`, `Ambiguous`, `LeftOnly`, `RightOnly`, all as your own objects, and `Result`, the full `IReconciliationResult`. |

- **Field types come from member types.** `decimal`, `int` and `long` are `Decimal` fields; `DateOnly` and `DateTime` are `Date` fields, and a `DateTime` loses its time of day; a `string` is an `Identifier` in a key and `Text` otherwise, or a `Symbol` once `Value` names its values. Any other type throws `ArgumentException`. Nullable types are fine.
- **A field is named after the left member,** and a nested member such as `row => row.Header.Ref` is named `Header.Ref`.
- **`RecBuilder` can't express** row keys, a culture, explicit roles or scales, lifecycle chains, packages, routes, a `when` on a tolerance, a group sum in one direction only, trading rules or reference data. For any of these, build the profile with `ProfileBuilder` and pass your objects through `RecordSources.FromObjects` with a `SourceMap<T>`.

[docs/typed-reconciliation](typed-reconciliation/README.md) walks through `RecBuilder` step by step, and the README's ten examples all use it.

### ProfileBuilder, SideBuilder, PathBuilder

`ProfileBuilder` builds the same `ProfileDocument` that JSON does, so it can say everything a JSON profile can, except a `days` tolerance on a date: `PathBuilder` has no method for one, so write that profile in JSON. Every builder is immutable, and every call returns a new builder.

```csharp
using System.Globalization;
using RecDiffy;

var registry = RuleRegistry.CreateCore();
var common = SideBuilder.Empty
    .MapSymbol("Counterparty", "Counterparty")
    .MapSymbol("Instrument", "Instrument")
    .MapSymbol("Side", "Side")
    .ParseDate("TradeDate", "TradeDate", "yyyy-MM-dd")
    .ParseDecimal("Quantity", "Quantity")
    .ParseDecimal("Price", "Price");

var built = ProfileBuilder.Create("trades", "1.0.0")
    .Culture(CultureInfo.GetCultureInfo("en-US"))
    .RowKeys(left: "TradeId", right: "Ref")
    .Key("Counterparty", FieldType.Symbol)
    .Key("Instrument", FieldType.Symbol)
    .Key("Side", FieldType.Symbol)
    .Key("TradeDate", FieldType.Date)
    .Economic("Quantity", FieldType.Decimal, scale: 0)
    .Economic("Price", FieldType.Decimal, scale: 3)
    .Lifecycle("EventAction", FieldType.Symbol)              // a field with the role Lifecycle
    .Lifecycle("EventTarget", FieldType.Text)
    .Left(common)
    .Right(common.MapSymbol("EventAction", "EventAction").CopyText("EventTarget", "EventTarget"))
    .Lifecycle(Side.Right)                                   // collapse the right side's cancel and correct chains
    .Path("trades", path => path
        .RouteAll()
        .NaturalKey("key", "Counterparty", "Instrument", "TradeDate", "Side")
        .Tolerance("Price", absolute: 0.001m))
    .Build(registry);

if (built is not { Succeeded: true, Document: { } document })
{
    foreach (var problem in built.Problems)
        Console.WriteLine($"{problem.Code} at {problem.Location}: {problem.Message}");   // e.g. paths[trades].passes[key].key[0]
    return;
}

var profile = Reconciler.Compile(document, registry);
Console.WriteLine(document.ToJson());   // the same profile as JSON
```

**`ProfileBuilder`**

| Method | JSON it writes |
|---|---|
| `Create(profileId, version)`, `From(document)` | `profileId`, `version`; `From` starts from an existing document. |
| `Key`, `Economic`, `Identity`, `Lifecycle`, `Package` `(name, type)`, `Field(name, type, role, scale)` | `fields`. `Key`, `Economic` and `Field` take a `scale`. |
| `RowKeys(left, right)`, `RowKey(side, column)` | `sources.<side>.rowKey` |
| `Culture(culture)`, `Culture(side, culture)` | `culture`, `sources.<side>.culture` |
| `StrictDateFormats(strict)`, `StrictDateFormats(side, strict)` | `strictDateFormats` |
| `Midpoint(mode)`, `Limits(searchLimits)` | `rounding.midpoint`, `limits` |
| `Left(rules)`, `Right(rules)`, `Normalize(side, rules)` | `normalization.<side>`. `Left` and `Right` also take a `Func<SideBuilder, SideBuilder>`. |
| `Lifecycle(side, action, target, scope, corrPointsAt)` | `consolidation.<side>.lifecycle`, with the defaults `EventAction`, `EventTarget`, `Counterparty` and `CANC`. |
| `Packages(side, PackageBuilder...)` | `consolidation.<side>.packages` |
| `Path(id, path => ...)`, `Path(pathBuilder)` | `paths` |
| `Build()`, `Build(registry)` | A `ProfileBuildResult`: `Succeeded`, `Document`, and `Problems`, each a `ProfileProblem(Code, Message, Location)`. `Build()` runs the structural checks; `Build(registry)` also compiles, and reports compile errors with the location `compile`. |

**`SideBuilder`**: one method per core rule. Each call lands in its rule's phase, whatever order you call them in.

| Method | Rule |
|---|---|
| `MapSymbol(from, to, map, passthrough, when)` | `symbol.map` |
| `MapIdentifier(from, to, scheme, scope, when)` | `identifier.map` |
| `CopyText(from, to, when)` | `text.copy` |
| `ParseDecimal(from, to, when)` | `decimal.parse` |
| `ParseDate(from, to, format or formats, when)` | `date.parse` with `formats` |
| `ParseDateBy(from, to, formatBy, when)` | `date.parse` with `formatBy` |
| `Required(fields...)` | `required.present` |
| `Rule(phase, name, ...)` | Any registered rule, by name. |

`using RecDiffy.RulePacks.Trading;` adds `ResolveLei`, `ResolveAlias`, `ResolveSide`, `AbsoluteQuantity` and `From32nds`. A profile that uses them compiles only with `TradingRulePack.AddTo(RuleRegistry.CreateCore())`.

**`PathBuilder`**

| Method | JSON it writes |
|---|---|
| `RouteAll()`, `RouteWhen(field, value)` | `route.all`, `route.byField` |
| `SharedRef(id, key...)`, `NaturalKey(id, key...)`, `ReducedKey(id, key...)` | One-to-one passes: `match.sharedRef`, `match.naturalKey`, `match.reducedKey` |
| `GroupSum(id, many, block, conserve, maxGroupSize, key)` | `match.groupSum`: several rows on the `many` side against one on the other. |
| `Tolerance(field, absolute, relative, when, applicableWhen)` | `diff.fields.<field>.tolerance` |
| `AttributedTolerance(field, formula, when, applicableWhen)`, `Attribution(field, formula)` | An attribution by name, such as `trading.netCash`. |
| `ApplicableWhen(field, when)` | Compares the field only when the condition holds on at least one side. |

Conditions are `Condition.Equal(field, value)`, `Condition.NotEqual(field, value)` and `Condition.In(field, values...)`. **`PackageBuilder.Create(link, role)`** declares a package, with `RequireRoles`, `Same`, `Sum`, `FromRole` and `DifferenceByRole`.

[BuiltProfile.cs](../RecDiffy.NET/src/RecDiffy.Samples/BuiltProfile.cs) is a runnable sample, and [ProfileBuilderTests.cs](../RecDiffy.NET/src/RecDiffy.Tests/ProfileBuilderTests.cs) covers every method.

## Part 3: FIX messages with RecDiffy.Fix

`RecDiffy.Fix` reads FIX tag=value text strictly and flattens it into the rows that [FIX-RECIPE.md](../RecDiffy.NET/docs/FIX-RECIPE.md) describes. A profile then reconciles those rows like any others.

```csharp
using RecDiffy;
using RecDiffy.Fix;

var options = new FixOptions(OurCompId: "OURFIRM", Delimiter: '|');   // the default delimiter is FixOptions.Soh
var ours = FixRows.Read(File.ReadAllText("oms.log"), options);         // what our OMS recorded from the broker
var theirs = FixRows.Read(File.ReadAllText("dropcopy.log"), options);  // the broker's drop copy

var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText("orders.profile.json")), RuleRegistry.CreateCore());
var (view, _) = profile.Push(ours.Orders, theirs.Orders).Snapshot();

// Never drop these silently: report them beside the result.
foreach (var rejected in ours.Rejected.Concat(theirs.Rejected))
    Console.WriteLine($"rejected: {rejected.Reason}");
```

**`FixReader`** splits the text into frames by BodyLength and checks each one. A frame that fails a check becomes a `FixRejected` with its reason, never a message:

- the header starts `8`, `9`, `35`, and CheckSum (`10`) is the last field and appears once;
- BodyLength (`9`) and CheckSum (`10`) are right; with `|` as the delimiter, the sum is taken as if it were SOH;
- no tag repeats outside the repeating groups `78`, `453` and `552`, and each group has as many entries as it declares;
- the text is ASCII, and length-prefixed data fields, such as `95`/`96`, are read by their length.

`FixReader.Read(text, options)` returns one `FixReading` per frame: `FixAccepted(Message)` or `FixRejected(Text, Reason)`. `FixReader.ReadMessage` reads one message. A `FixMessage` has `BeginString`, `MsgType`, `Get(tag)` and `GetAll(tag)`. With `ThrowOnRejected: true`, the first rejection throws `FixRejectedException` instead.

**`FixRows.Read(text, options)`**, or `FixRows.From(readings, options)`, flattens the accepted messages in order and returns six lists. Each row is a `FrozenDictionary<string, string?>` of column to text, which `Push` takes as it is.

| List | From | Columns |
|---|---|---|
| `Orders` | Execution reports (`35=8`) that change an order: new (`150=0`), replace (`150=5`), restatement (`150=D`), cancel (`150=4`), expiry (`150=C`) | `ExecID`, `ClOrdID`, `OrigClOrdID`, `Root`, `ExecType`, `Action`, `Target`, `Counterparty`, `Account`, `Symbol`, `Side`, `OrderQty`, `Price`, `OrdStatus`, `TransactNanos` |
| `Fills` | Fills (`150=F`, or `150=1`/`2`), corrections (`150=G`) and busts (`150=H`); FIX 4.2 and earlier use `20` instead | `ExecID`, `ClOrdID`, `Root`, `Action`, `Target`, `Counterparty`, `Account`, `Symbol`, `Side`, `LastQty`, `LastPx`, `TransactNanos` |
| `Allocations` | `35=J` and `35=AS`, one row per account | `AllocRow`, `AllocID`, `AllocRoot`, `Action`, `Target`, `Counterparty`, `AllocAccount`, `AllocQty`, `AvgPx` |
| `Trades` | Trade capture reports (`35=AE`) | `TradeReportID`, `TradeRoot`, `Action`, `Target`, `Counterparty`, `Account`, `Symbol`, `Side`, `LastQty`, `LastPx` |
| `Skipped` | Messages that make no row, each with its reason: session messages, requests (`D`, `F`, `G`), rejected requests (`9`), pending states, done for day, rejected, stopped or suspended orders, status replies, don't-know-trade (`Q`), acks (`P`, `AR`), declined trade reports, and message types the recipe doesn't reconcile | `FixSkipped(Message, Reason)` |
| `Rejected` | Frames that failed a check, and messages that can't be flattened | `FixRejected(Text, Reason)` |

- **`Action` and `Target` feed lifecycle consolidation.** `Action` is `NEW`, `CORR` or `CANC`, and `Target` is the row the event acts on: for an order, the `ExecID` of its previous accepted state. `Root` is the order's first `ClOrdID`, so fills under any `ClOrdID` of one order group together.
- **`Counterparty` is the other party.** When `49` is `OurCompId` it is `56`, and when `56` is `OurCompId` it is `49`. A message where neither is ours is rejected.
- **`Side`** maps `1` to `BUY`, `2` to `SELL`, `5` to `SELL_SHORT` and `6` to `SELL_SHORT_EXEMPT`, and keeps any other code.
- **`TransactNanos`** is TransactTime (`60`) as decimal nanoseconds since the Unix epoch. A malformed timestamp rejects the message.
- **Read each log from the start, in order.** A replace, cancel, correction or bust whose order, allocation or trade report wasn't read earlier is rejected, and so is a correction or bust without `19`, or an allocation whose `79` and `80` counts differ.

The sample profiles that read these rows are in [test-data/fix-reconciliation](../RecDiffy.NET/test-data/fix-reconciliation): `orders.profile.json` for `Orders`, `fills.profile.json` for `Fills`, `allocations.profile.json` for `Allocations`, and `trades.profile.json` for `Trades`. `executions.profile.json` matches your own average-price bookings against the broker's `Fills`. FIX-RECIPE.md explains each one, and the scope rules it depends on: the lifecycle scope is the counterparty, and `Counterparty` is in every pass key.

## Rules that keep a result correct

1. **Declare a culture.** A profile with none reads text with the machine's culture, so a run can differ between machines. `"culture": ""` is the invariant culture.
2. **Keep amounts in `decimal` or text.** A `float` or `double` is never converted; its row is quarantined with `FloatValue`.
3. **Matched doesn't mean balanced.** Read `Outcome` and `Diffs` on every matched entry.
4. **Missing isn't zero.** Null against a value is a `Break`. Check `FieldDiff.Left.IsNull` and `FieldDiff.Right.IsNull` before you read `Delta`, and never total deltas across such rows.
5. **Count entries and rows separately.** One entry can hold many rows. To count rows, group `result.Rows` by `Disposition`.
6. **`Ambiguous` and `SearchIncomplete` aren't unmatched.** They mean "not decided". Report them apart from `LeftOnly` and `RightOnly`.
7. **Show quarantined rows with their diagnostics.** They were never matched or compared.
8. **Only `Key` and `Economic` fields are compared.** A tolerance on any other field is accepted and ignored.
9. **Trading rules and attributions need the trading pack** in the registry: `TradingRulePack.AddTo(RuleRegistry.CreateCore())`. Without it they are `UnknownRule`.
10. **Record `result.Run`** next to any report, so the run can be reproduced. The same rows in any order, with the same profile, reference data, cultures and engine version, give the same result.

## When something fails

| Symptom | First thing to check |
|---|---|
| `ProfileCompileException` | Its `Errors`. Each `Code` is explained in [PROFILES.md](../RecDiffy.NET/docs/PROFILES.md#compile-errors). |
| `InvalidOperationException` from `ProfileDocument.Parse` | A JSON value of the wrong type, such as `"scale": "2"`. |
| A setting has no effect | The key's spelling and case. Unknown keys are ignored. |
| Every row on one side is unmatched | That side's `normalization`. A side with no rules writes no fields. |
| A row is quarantined | `RowResult.Diagnostics`. Every code is in [PROFILES.md](../RecDiffy.NET/docs/PROFILES.md#run-diagnostics). |
| A number that looks right is `DecimalParse` | The side's culture, in `result.Run.Reading`. |
| Many rows are `Ambiguous` | Whether the key tells the rows apart. Add a key field, or an earlier pass with a stricter key. |
| `dotnet add package` finds no version | Add `--prerelease`. |

## Where the code is

| What | Where |
|---|---|
| Compile and run | [Reconciler.cs](../RecDiffy.NET/src/RecDiffy/Reconciler.cs) |
| Rule names and the classes they build | [RuleRegistry.cs](../RecDiffy.NET/src/RecDiffy/RuleRegistry.cs), [TradingRulePack.cs](../RecDiffy.NET/src/RecDiffy.RulePacks.Trading/TradingRulePack.cs) |
| Builders | [RecDiffy/Profile](../RecDiffy.NET/src/RecDiffy/Profile), [RecBuilder.cs](../RecDiffy.NET/src/RecDiffy/Typed/RecBuilder.cs) |
| Push and snapshot | [RecDiffy/Push](../RecDiffy.NET/src/RecDiffy/Push) |
| Result types | [IReconciliationResult.cs](../RecDiffy.NET/src/RecDiffy/Results/IReconciliationResult.cs), [ResultModel.cs](../RecDiffy.NET/src/RecDiffy/Results/ResultModel.cs) |
| FIX | [RecDiffy.Fix](../RecDiffy.NET/src/RecDiffy.Fix) |
| Runnable samples | [RecDiffy.Samples](../RecDiffy.NET/src/RecDiffy.Samples) |
