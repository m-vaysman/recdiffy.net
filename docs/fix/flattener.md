# Reconcile FIX messages: FixReader and FixFlattener

Part of the [FIX section](llms.txt). `RecDiffy.Fix` reads FIX tag=value text strictly and turns accepted messages into rows. The rows keep FIX's own tag numbers as column names and interpret nothing: the profile decides what each value means.

```shell
dotnet add package RecDiffy.Fix --prerelease
```

```csharp
using RecDiffy;
using RecDiffy.Fix;

var fix = new FixOptions(OurCompId: "OURFIRM", Delimiter: '|');   // the default delimiter is FixOptions.Soh

// One row per execution report that changes an order: no pending states, fills or status replies.
var orderStates = new FixFlattenOptions(Filter: new FixFilter(
    Include: new Dictionary<int, IReadOnlyList<string>> { [35] = ["8"] },
    Exclude: new Dictionary<int, IReadOnlyList<string>> { [150] = ["A", "6", "E", "F", "I"] }));

var ours = FixFlattener.Read(File.ReadAllText("oms.log"), fix, orderStates);         // what our OMS recorded
var theirs = FixFlattener.Read(File.ReadAllText("dropcopy.log"), fix, orderStates);  // the broker's drop copy

var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText("generic-orders.profile.json")), RuleRegistry.CreateCore());
var (view, _) = profile.Push(ours.Rows, theirs.Rows).Snapshot();

// Never drop these silently: report them beside the result.
foreach (var rejected in ours.Rejected.Concat(theirs.Rejected))
    Console.WriteLine($"rejected: {rejected.Reason}");
```

## FixReader: strict reading

`FixReader.Read(text, options)` splits the text into frames by BodyLength and returns one `FixReading` per frame: `FixAccepted(Message)`, or `FixRejected(Text, Reason)` for a frame that fails any check. `FixReader.ReadMessage(text, options)` reads one message. A frame is rejected when:

- the header doesn't start `8`, `9`, `35`, or CheckSum (`10`) isn't the last field, or appears earlier too;
- BodyLength (`9`) or CheckSum (`10`) is wrong; with `|` as the delimiter, the sum is taken as if it were SOH;
- a field is malformed, isn't closed by the delimiter, or has an empty value;
- a character is outside ISO 8859-1 (Latin-1);
- a tag repeats outside a repeating group, or a group has a different number of entries than its count field declares. The reader knows the groups of trading and post-trade messages, nested ones included, such as `78` (allocation accounts), `453` (parties), `552` (sides), `73` (orders) and `124` (executions);
- length-prefixed data fields, such as `95`/`96`, are read by their length, so they may hold the delimiter.

A `FixMessage` has `Fields`, `Text`, `BeginString`, `MsgType`, `Get(tag)` for the first value and `GetAll(tag)` for every value. `FixOptions(OurCompId, Delimiter, ThrowOnRejected)`: with `ThrowOnRejected: true`, the first rejection throws `FixRejectedException` instead of being returned.

## FixFlattener: messages to rows

`FixFlattener.Read(text, fixOptions, flattenOptions)`, or `FixFlattener.Flatten(readings, fixOptions, flattenOptions)`, returns a `FixFlat`:

| Member | What it holds |
|---|---|
| `Rows` | One `FrozenDictionary<string, string?>` per row: column to text. `profile.Push` and `RecordSources.FromObjects` take it as it is. |
| `Skipped` | `FixSkipped(Message, Reason)`: session messages, and messages the filter left out. |
| `Rejected` | `FixRejected(Text, Reason)`: frames the reader rejected, and messages where neither `49` nor `56` is `OurCompId`. |

**Columns.** Every field of the message is a column named by its tag number, such as `"55"` or `"38"`, with nothing renamed or interpreted. Four kinds of column are derived:

| Column | Value |
|---|---|
| `Counterparty` | Whichever of SenderCompID (`49`) and TargetCompID (`56`) isn't `OurCompId`. |
| `RootClOrdID` | On an execution report: the first ClOrdID of the order's chain, followed through OrigClOrdID (`41`). |
| `PrevExecID` | On an execution report: the ExecID (`17`) of the previous kept execution report on the same chain. It is the lifecycle `target` for cancels and replaces. |
| `{tag}:ns` | For a UTCTimestamp value, such as `60:ns`: decimal nanoseconds since the Unix epoch, never through a binary floating-point type. |

**`FixFlattenOptions`**

| Option | What it sets |
|---|---|
| `Grain` | The count tag of the repeating group whose entries become rows, such as `78` for one row per allocation account. Null, the default, makes one row per message. A row at a group grain holds the message's fields, its enclosing entries' fields and its own entry's fields. A nested group with exactly one entry is folded into the row; one with several entries is left to a finer grain. |
| `Filter` | A `FixFilter(Include, Exclude)` by tag value. A message is kept when every `Include` tag holds one of its values and no `Exclude` tag holds one of its values. Null keeps every application message. |
| `Keys` | Composite columns: each name gets its tags' values joined with `\|`, or null when any tag is missing. A row key can be one of them, such as `Key` from `[70, 79]` for an allocation account. |
| `Reference` | A message's ID tag and the tag that refers to an earlier message, such as `(70, 72)`. At a group grain, a message that refers to an earlier one but has no entries of its own stands for each entry of the earlier one, so a cancel without accounts cancels every account. |

**The profile decides what values mean.** It reads tag columns with ordinary rules: `symbol.map` from `54` with `map` `{"1": "BUY", "2": "SELL"}`, from `150` to a lifecycle action (`0` to `NEW`, `5` and `D` to `CORR`, `4` and `C` to `CANC`), `text.copy` from `PrevExecID` to the lifecycle target, and `decimal.parse` from `38` and `44`. Keep `Counterparty` in every pass key and make it the lifecycle scope, so nothing matches across counterparties. [FIX-RECIPE.md](../../RecDiffy.NET/docs/FIX-RECIPE.md) explains why each rule holds.

## Sample profiles and the options they need

Each one is in [test-data/fix-reconciliation](../../RecDiffy.NET/test-data/fix-reconciliation), and [FixScenarioTests.cs](../../RecDiffy.NET/src/RecDiffy.Fix.Tests/FixScenarioTests.cs) runs each one against FIX messages.

| Profile | Flatten options |
|---|---|
| [generic-orders.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-orders.profile.json) | One row per message; `Filter` includes `35=8` and excludes `150` = `A`, `6`, `E`, `F`, `I`. Row key `17`. |
| [generic-fills.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-fills.profile.json) | One row per message; `Filter` includes `35=8`. Corrections and busts follow ExecRefID (`19`). |
| [generic-equity-allocations.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-equity-allocations.profile.json) | Our instructions: `Grain: 78`, `Filter` `35=J`, `Keys` `Key` = `[70, 79]` and `TargetKey` = `[72, 79]`, `Reference: (70, 72)`. Their confirmations: `Filter` `35=AK`. |
| [generic-futures-allocations.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-futures-allocations.profile.json) | `Grain: 78`; `Keys` `Key` = `[70, 79]` on our side and `[755, 79, 366]` on theirs. |
| [generic-bond-allocations.profile.json](../../RecDiffy.NET/test-data/fix-reconciliation/generic-bond-allocations.profile.json) | `Grain: 78`; `Keys` `Key` = `[70, 79]` on our side and `[755, 79]` on theirs. |

The code is [RecDiffy.Fix](../../RecDiffy.NET/src/RecDiffy.Fix), and the tests are [FixReaderTests.cs](../../RecDiffy.NET/src/RecDiffy.Fix.Tests/FixReaderTests.cs), [FixFlattenerTests.cs](../../RecDiffy.NET/src/RecDiffy.Fix.Tests/FixFlattenerTests.cs) and [FixScenarioTests.cs](../../RecDiffy.NET/src/RecDiffy.Fix.Tests/FixScenarioTests.cs).
