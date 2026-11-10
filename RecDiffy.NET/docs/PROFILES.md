# Profile reference

A profile tells RecDiffy how to read two sources, which rows belong together, and how to compare them. This reference lists every section and option, its default, and every error and diagnostic code, with what to do about each.

It describes the code on `dev`. The parser is [ProfileParser.cs](../src/RecDiffy/Profile/ProfileParser.cs), the checks are in [ProfileCompiler.cs](../src/RecDiffy/Profile/ProfileCompiler.cs) and [ProfileValidation.cs](../src/RecDiffy/Profile/ProfileValidation.cs), and the rules are registered in [RuleRegistry.cs](../src/RecDiffy/RuleRegistry.cs) and [TradingRulePack.cs](../src/RecDiffy.RulePacks.Trading/TradingRulePack.cs). For what a result means, see [RESULTS.md](RESULTS.md).

## Contents

- [Write, check and compile a profile](#write-check-and-compile-a-profile)
- [The shape of a profile](#the-shape-of-a-profile)
- [Top-level settings](#top-level-settings)
- [`sources`: row keys and reading](#sources-row-keys-and-reading)
- [`fields`: the canonical schema](#fields-the-canonical-schema)
- [`normalization`: from raw cells to fields](#normalization-from-raw-cells-to-fields)
- [Conditions](#conditions)
- [`consolidation`: lifecycles and packages](#consolidation-lifecycles-and-packages)
- [`paths`: routes, passes and comparison](#paths-routes-passes-and-comparison)
- [Attributions](#attributions)
- [`limits`: search limits](#limits-search-limits)
- [The profile hash](#the-profile-hash)
- [Compile errors](#compile-errors)
- [Run diagnostics](#run-diagnostics)
- [Troubleshooting](#troubleshooting)
- [Known gaps](#known-gaps)

## Write, check and compile a profile

You can write a profile as JSON or build it in code. Either way, compile it once and reuse the compiled profile for every run.

```csharp
var registry = TradingRulePack.AddTo(RuleRegistry.CreateCore()); // or RuleRegistry.CreateCore() without the trading pack
var profile = Reconciler.Compile(ProfileDocument.Parse(json), registry);
```

- **`ProfileDocument.Parse(json)`** reads the JSON. It throws `ProfileCompileException` at the first problem it can't read past, such as an unknown type name, or listing every missing required key.
- **`Reconciler.Compile(document, registry)`** checks the whole profile against the rules in the registry. It collects every problem it finds and throws one `ProfileCompileException`. Its `Errors` list holds a `ProfileError(Code, Message)` for each. When two fields share a name, it stops early and reports only `DuplicateField`, with any `UnsupportedFormat` or scale errors.
- **`ProfileBuilder`** builds the same document in code. `Build()` runs the structural checks only. `Build(registry)` also compiles, when the structural checks pass. Each problem comes back as a `ProfileProblem` with a `Location` such as `paths[trades].passes[P2].key[0]`. See [BuiltProfile.cs](../src/RecDiffy.Samples/BuiltProfile.cs).
- **`ProfileCheck.AgainstHeaders(document, leftHeaders, rightHeaders)`** lists the source columns the profile reads that a file doesn't have, with the closest header as a suggestion. Pass `TradingColumns.Parameters` as the last argument when the profile uses the trading pack. It checks a file's headers before any row is read.
- **`document.ToJson()`** writes a document back to JSON.

Four things fail outside these rules:

- **A missing nested key** throws `KeyNotFoundException`: a field without `name`, `type` or `role`, a path without `id`, `route` or `route.rule`, a pass without `id` or `pass`, a rule call without `rule`, or a condition without `field`.
- **A JSON value of the wrong type** throws `InvalidOperationException` from System.Text.Json, not `ProfileCompileException`. For example, `"scale": "2"` fails this way, because `scale` must be a number.
- **An unknown key is ignored.** Keys are case-sensitive, so a misspelled key such as `"tolerence"` is silently dropped and its default applies.
- **A `date.parse` format without a year** stops compile at once, with only that `InvalidParameter` error. It is thrown even from `ProfileBuilder.Build(registry)`, which otherwise returns its problems.

Complete, runnable profiles are in [FirstReconciliation.cs](../src/RecDiffy.Samples/FirstReconciliation.cs) (warehouse, core rules only), [TradingReference.cs](../src/RecDiffy.Samples/TradingReference.cs) (trading pack and reference data) and [ResultReading.cs](../src/RecDiffy.Samples/ResultReading.cs). The sample program checks each result and returns 0 only when all of them hold.

## The shape of a profile

Every section and key, with placeholder values. Only `formatVersion`, `profileId`, `version`, `fields` and `paths` are required.

```json
{
  "formatVersion": 1,
  "profileId": "trades",
  "version": "1.0.0",
  "rounding": { "midpoint": "ToEven" },
  "culture": "en-US",
  "strictDateFormats": true,
  "sources": {
    "left":  { "rowKey": "TradeId", "culture": "de-DE", "strictDateFormats": true },
    "right": { "rowKey": "Ref" }
  },
  "fields": [
    { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 0 }
  ],
  "normalization": {
    "left":  { "identify": [], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ], "derive": [], "validate": [] },
    "right": { "identify": [], "parse": [], "derive": [], "validate": [] }
  },
  "consolidation": {
    "left": {
      "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" },
      "packages": [ { "link": "Parent_Link_ID", "role": "Leg_Structure", "requiredRoles": [], "aggregate": {} } ]
    }
  },
  "paths": [
    {
      "id": "all",
      "route": { "rule": "route.all" },
      "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Quantity"] } ],
      "diff": { "fields": { "Quantity": { "tolerance": { "absolute": 1 } } } }
    }
  ],
  "limits": { "maxGroupSize": 8, "maxBlockItems": 24, "maxTargetsPerComponent": 8, "maxNodesPerTarget": 250000 }
}
```

The sides are always named `left` and `right`. A run reads only those two names.

## Top-level settings

| Key | Required | Default | What it does |
|---|---|---|---|
| `formatVersion` | Yes | | Must be `1`. Anything else is `UnsupportedFormat`. |
| `profileId` | Yes | | Your name for the profile. |
| `version` | Yes | | Your version of the profile. |
| `rounding.midpoint` | No | `ToEven` | How a value exactly halfway is rounded: `ToEven` or `AwayFromZero`. It applies to every rounding to a field's scale. |
| `culture` | No | The server's culture, read once at compile | The culture both sides' text is read with, unless a side declares its own. `""` is the invariant culture. |
| `strictDateFormats` | No | `true` | When true, `/` and `:` in a date format mean exactly those characters. When false, they also match the culture's separators, as .NET does. |
| `sources` | No | Row ordinals as keys | Each side's row key and reading. See [`sources`](#sources-row-keys-and-reading). |
| `fields` | Yes | | The canonical fields. See [`fields`](#fields-the-canonical-schema). |
| `normalization` | No | No rules | The rules for each side. See [`normalization`](#normalization-from-raw-cells-to-fields). |
| `consolidation` | No | None | Lifecycle collapse and packages, per side. See [`consolidation`](#consolidation-lifecycles-and-packages). |
| `paths` | Yes | | At least one path. See [`paths`](#paths-routes-passes-and-comparison). |
| `limits` | No | See [`limits`](#limits-search-limits) | Bounds on the group search. |

The [README](../../README.md#declare-how-each-file-is-read) explains culture resolution with examples. The tests are in [DeclaredCultureTests.cs](../src/RecDiffy.Tests/DeclaredCultureTests.cs) and [StrictDateFormatTests.cs](../src/RecDiffy.Tests/StrictDateFormatTests.cs).

## `sources`: row keys and reading

| Key | Default | What it does |
|---|---|---|
| `rowKey` | None | The source column that identifies a row. Without it, the row's position in the source is its key. |
| `culture` | The run's `culture` | This side's culture. It overrides the run's. |
| `strictDateFormats` | The run's setting | This side's date strictness. It overrides the run's. |

- **A blank row key** quarantines the row with `MissingKey`. The key is trimmed, and a number in it is written as invariant text.
- **Two rows with one key** on the same side are collapsed when they are identical, with a `DuplicateRowCollapsed` warning. When they differ, both are quarantined with `DuplicateConflict`. Decimal cells count as identical by value, so `100m` and `100.00m` are the same.
- **`result.Run.Reading`** records each side's culture, its separators, where it came from, and whether dates were strict.

## `fields`: the canonical schema

Each field has a `name`, a `type`, a `role` and an optional `scale`. Type and role names are case-sensitive: `Decimal` works, `decimal` is `InvalidParameter`.

| Key | Default | What it does |
|---|---|---|
| `name` | | The field's name. Names must be unique (`DuplicateField`). |
| `type` | | `Decimal`, `Date`, `Symbol`, `Identifier` or `Text`. |
| `role` | | `Key`, `Economic`, `Identity`, `Lifecycle`, `Package` or `Text`. |
| `scale` | `0` | Decimal places, from 0 to 28. Parsing, matching and comparison all round to it. |

**Types:**

| Type | Holds | As a match key | Compared by |
|---|---|---|---|
| `Decimal` | A decimal number | Fixed-point text at the field's scale, so `1.5` and `1.50` share a key at scale 2 | Value after rounding to scale, with tolerances |
| `Date` | A calendar date | `yyyy-MM-dd` | Days, with a `days` tolerance |
| `Symbol` | A short code, such as a SKU or a currency | The code | Ordinal text equality |
| `Identifier` | A value with a scheme and a scope, such as an ISIN | The scheme and the value, so two schemes never share a key | Ordinal equality of the value |
| `Text` | Free text | Not allowed (`TextKey`) | Ordinal text equality |

**Roles:**

| Role | Compared after matching | Can be a match key |
|---|---|---|
| `Key` | Yes | Yes |
| `Economic` | Yes | Yes |
| `Identity` | No | Yes |
| `Lifecycle` | No | Yes |
| `Package` | No | Yes |
| `Text` | No | No (`TextKey`) |

Only `Key` and `Economic` fields are compared. A tolerance on any other field is accepted and ignored.

## `normalization`: from raw cells to fields

Each side has four phases, which run in this order: `identify`, `parse`, `derive`, `validate`. Within a phase, rules run in the order listed. A side with no rules writes no fields.

Each entry is a rule call:

```json
{ "rule": "decimal.parse", "from": "Qty", "to": "Quantity", "when": { "field": "AssetClass", "equals": "Equity" } }
```

- **`rule`** is the registered name.
- **`when`** is optional. The rule runs only on rows where the condition holds. See [Conditions](#conditions).
- **Every other key is a parameter.** A string, number or boolean is read as text: `1.10` stays `1.10`, and `false` becomes `"false"`. An array is a list of strings. An object is a map of strings. A `null` is dropped.
- **`from`** names a source column. Column names compare ordinally.

Compile checks every rule call:

- **The rule is registered** (`UnknownRule`) and listed under its own phase (`WrongPhase`).
- **Its required parameters are present** (`MissingParameter`).
- **Every field it writes or reads is declared** (`UnknownField`).
- **A field it reads was written by an earlier rule** (`FieldReadBeforeWrite`).
- **No other rule already writes the same field** (`AmbiguousWrite`). Two writers are allowed when the second also reads the field, or when their `when` clauses exclude each other: the same field with different `equals` values, or `equals` and `notEquals` of the same value.

A call that fails a check is left out, and later rules can still write its field.

### What reaches a rule

Values arrive from a record source ([Intake/RecordSource](../src/RecDiffy/Intake/RecordSource)):

- **From a data reader:** whitespace becomes null, text is trimmed, a `DateTime` keeps only its date, integers become decimals, and `float` and `double` pass through unchanged so `decimal.parse` can reject them. Anything else becomes invariant text.
- **From objects:** each `SourceMap` binder stores what your lambda returns. `Text` binders trim.

### Core rules

`RuleRegistry.CreateCore()` registers these.

| Rule | Phase | Required | Optional | Writes |
|---|---|---|---|---|
| `decimal.parse` | `parse` | `from`, `to` | | `to`, a decimal rounded to its scale |
| `date.parse` | `parse` | `from`, `to` | `formats` (list), `formatBy` (field) | `to`, a date |
| `symbol.map` | `identify` | `from`, `to` | `map` (object), `passthrough` (default `true`) | `to`, a symbol |
| `identifier.map` | `identify` | `from`, `to`, `scheme` | `scope` (default `""`) | `to`, an identifier |
| `text.copy` | `identify` | `from`, `to` | | `to`, text |
| `required.present` | `validate` | `fields` (list) | | Nothing. It reads each listed field. |

- **`decimal.parse`** reads text in the side's culture. A thousands separator must sit where the culture puts it, so `1,5` on an en-US side is `DecimalParse`, not 15. A blank cell is null with no diagnostic. A `float` or `double` is `FloatValue`. Tests: [CultureDecimalRuleTests.cs](../src/RecDiffy.Tests/CultureDecimalRuleTests.cs), [DecimalParseGroupingTests.cs](../src/RecDiffy.Tests/DecimalParseGroupingTests.cs).
- **`date.parse`** reads text only with a format that includes a year. Give the formats in `formats`, or name a field in `formatBy` whose value is a counterparty code, and the format comes from `ReferenceDataSet.DateFormatByCode`. With `formatBy`, `formats` is ignored, and when the row's party is missing or has no format, every value is `DateParse`, blanks and typed dates included. A `DateOnly` or `DateTime` value is read directly, and a `double` is read as an Excel date serial. With no format, any text is `DateParse`. A declared format with no year fails compile. Tests: [DeclaredDateRuleTests.cs](../src/RecDiffy.Tests/DeclaredDateRuleTests.cs), [DateParseClockTests.cs](../src/RecDiffy.Tests/DateParseClockTests.cs).
- **`symbol.map`** trims the value and looks it up in `map`. A value not in the map is kept as it is, unless `passthrough` is `false`, which makes it `UnknownSymbol`. A blank cell is null.
- **`identifier.map`** trims the value and stores it with `scheme` and `scope`. A blank cell is null.
- **`text.copy`** trims the value. A blank cell is null.
- **`required.present`** raises `RequiredMissing` for each listed field that is null, which quarantines the row.

### Trading rules

`TradingRulePack.AddTo(registry)` adds these, and the two [attributions](#attributions). Compile a trading profile with `TradingRulePack.AddTo(RuleRegistry.CreateCore())`. Without the pack, each of these is `UnknownRule`.

| Rule | Phase | Required | Optional | Writes |
|---|---|---|---|---|
| `party.resolveLei` | `identify` | `from`, `to` | | `to`, an identifier in scheme `PARTY` |
| `party.resolveAlias` | `identify` | `from`, `to` | | `to`, an identifier in scheme `PARTY` |
| `side.resolve` | `derive` | `code`, `quantity` | `invertWhen`, `perspective` (default `Reporting_Perspective`), `asset` (default `AssetClass`) | `Side`, a symbol |
| `quantity.absolute` | `derive` | `from`, `to` | | `to`, the absolute value rounded to its scale |
| `price.from32nds` | `parse` | `from`, `to` | | `to`, a decimal rounded to its scale |

- **`party.resolveLei`** looks the LEI up in `ReferenceDataSet.LeiToCode`. A blank value writes nothing. An unknown LEI is `UnknownParty`.
- **`party.resolveAlias`** looks the name up in `ReferenceDataSet.AliasToCode`, and writes only when `to` is still empty. An unknown name is `UnknownParty`. Compile still counts it as a second writer of `to`, so listing it after `party.resolveLei` on the same field is `AmbiguousWrite` unless their `when` clauses exclude each other. See [Known gaps](#known-gaps).
- **`side.resolve`** reads the `code` column. `B`, `BOT` and `BUY` are `BUY`; `S`, `SLD` and `SELL` are `SELL`; `P`, `PAY`, `PAYFIXED` and `PAY_FIXED` are `PAY_FIXED`; `R`, `REC`, `RECEIVEFIXED` and `REC_FIXED` are `REC_FIXED`. With no code, the sign of `quantity` decides, and a `Swap` asset gives `PAY_FIXED` or `REC_FIXED`. `BUY` or `REC_FIXED` with a negative quantity is `SideSignConflict`. Anything else is `UndeterminedSide`. When the `perspective` column equals `invertWhen`, the side is flipped. It reads the `asset` field, so declare that field and write it earlier. It always writes a field named `Side`, so declare that too.
- **`quantity.absolute`** reads the number as `decimal.parse` does, in the side's culture. When the cell is blank or unreadable and an earlier rule already wrote a decimal to `from`, it uses that.
- **`price.from32nds`** reads `99-16` as 99 + 16/32. A trailing `+` adds 1/64. Anything else is `PriceParse`.

For `ProfileBuilder`, the [TradingSideRules](../src/RecDiffy.RulePacks.Trading/TradingSideRules.cs) extensions add the same rules: `ResolveLei`, `ResolveAlias`, `ResolveSide`, `AbsoluteQuantity` and `From32nds`.

## Conditions

A condition is used by a rule's `when`, a field's `applicableWhen`, and a tolerance's `when`.

```json
{ "field": "AssetClass", "equals": "Bond" }
{ "field": "AssetClass", "notEquals": "Bond" }
{ "field": "AssetClass", "in": ["Bond", "Bill"] }
```

- **Give exactly one operand.** A condition with none is `MissingParameter`, and so is an empty `in` list. If you give more than one, `equals` wins, then `notEquals`, then `in`.
- **`"equals": ""`** holds when the value is an empty string. A data reader turns a blank cell into null, and null never equals `""`, so on CSV and Excel sources this check never holds. See [Known gaps](#known-gaps).
- **The value read** is the field's canonical value when a rule has written it: a decimal as invariant text, a date as `yyyy-MM-dd`, an identifier as its value. Otherwise it is the raw column, trimmed. A null cell reads as null.
- **A missing value** fails `equals` and `in`, and passes `notEquals`.
- **Comparison is ordinal.**

## `consolidation`: lifecycles and packages

Consolidation runs on each side after normalization and before matching. Every field it names must be declared (`UnknownField`), and it reads canonical values, so a rule must write each of those fields.

### `lifecycle`

Collapses cancels and corrections into the row that survives.

| Key | Default | What it names |
|---|---|---|
| `action` | `EventAction` | The field holding the event's action. |
| `target` | `EventTarget` | The field holding the row key of the event it acts on. |
| `scope` | `Counterparty` | The field that groups row keys. A target is looked up in the same scope. |
| `corrTarget` | `CANC` | How a correction that names a cancel is read. With `CANC`, it replaces the row that cancel cancelled. `corrPointsAt` is an older name for the same key. `corrTarget` wins when both are present. |

- **`CANC`** cancels its target. **`CORR`** replaces its target. Any other non-blank action, such as `NEW`, is an ordinary event. A row with a blank action isn't an event and passes through, so no event can target it.
- **A chain** is a set of events linked by targets. It keeps exactly one survivor. The others are suppressed as `Superseded` or `CancelEvent`. If every event is cancelled, they are all `FullyCancelled`.
- **A chain that can't be resolved** is quarantined: `MissingLifecycleTarget`, `LifecycleCycle`, `LifecycleConflict` or `ChainMemberQuarantined`.

Tests: [LifecycleTests.cs](../src/RecDiffy.Tests/LifecycleTests.cs), [AdversarialLifecycleConflictTests.cs](../src/RecDiffy.Tests/AdversarialLifecycleConflictTests.cs).

### `packages`

Merges linked rows, such as the legs of a swap, into one item before matching. `packages` is a list, applied in order.

| Key | Default | What it does |
|---|---|---|
| `link` | `Parent_Link_ID` | The field whose value links the rows of one package. Rows with a blank link stay apart. |
| `role` | `Leg_Structure` | The field holding each row's role in the package. |
| `requiredRoles` | `[]` | Roles every package must have. A package missing one is quarantined with `IncompletePackage`. |
| `aggregate` | `{}` | How each field of the merged item is computed, keyed by field name. |

Aggregators:

| Aggregator | Result |
|---|---|
| `"Same"` | The legs' common value. Legs that disagree make the package `PackageInconsistent`. |
| `"Sum"` | The sum of the legs. The field must be `Decimal`. One unknown leg makes the total unknown. |
| `"FromRole(PAY)"` | The value of the leg with that role. No such leg is `IncompletePackage`. Several that disagree make it unknown, with `MembersDisagree`. |
| `"DifferenceByRole(PAY,REC)"` | The first role's value minus the second's. The field must be `Decimal` and both roles are required. Don't put a space after the comma: the role names aren't trimmed. |

- **A field with no aggregator** keeps its value when every leg agrees. Otherwise it is unknown, with `MembersDisagree` if the run uses the field.
- **Every role an aggregator names** must be in `requiredRoles` (`InvalidParameter`).
- **An unknown aggregator name** is `InvalidParameter` when the profile is parsed.

In code, use `PackageBuilder`. Tests: [PackageFieldRuleTests.cs](../src/RecDiffy.Tests/PackageFieldRuleTests.cs), [PackageRowKeyTests.cs](../src/RecDiffy.Tests/PackageRowKeyTests.cs), [AdversarialPackageNullTests.cs](../src/RecDiffy.Tests/AdversarialPackageNullTests.cs).

## `paths`: routes, passes and comparison

A path takes the rows its route accepts, matches them with its passes, and compares the matches with its `diff` settings.

```json
{ "id": "equities", "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Equity" }, "passes": [], "diff": { "fields": {} } }
```

### `route`

| Rule | Takes |
|---|---|
| `route.all` | Every row. |
| `route.byField` | Rows whose `field` has the canonical value `equals`. Both keys are required. |

- **Each row goes to the first path that accepts it.**
- **Routes must not overlap** (`OverlappingRoute`). `route.all` overlaps every other route, so a profile with `route.all` has one path. Two `route.byField` routes overlap when they name the same field and value.
- **Routes on different fields compile.** A row both accept goes to the earlier path.
- **The value compared** is the field's match-key text, so a decimal at scale 2 is `1.50`, not `1.5`.
- **A row no route accepts** gets its own `LeftOnly` or `RightOnly` entry, marked `Unrouted`.

### `passes`

| Key | Default | What it does |
|---|---|---|
| `id` | | The pass's name, reported as `CommittedBy`. |
| `pass` | | The pass rule. See the table below. |
| `key` | `[]` | Fields that must be equal for two rows to pair. |
| `block` | `[]` | Group passes: fields that must be equal for rows to be grouped. |
| `conserve` | `[]` | Group passes: decimal fields whose values must sum exactly. |
| `many` | | Group passes: the side with several rows, `left` or `right`. |
| `maxGroupSize` | `limits.maxGroupSize` | Group passes: the most rows in one group. Hard cap 16. |

| Pass | Matches | Needs |
|---|---|---|
| `match.sharedRef` | One row to one row, on a shared reference | `key`, or the defaults `Counterparty` and `TradeRef` |
| `match.naturalKey` | One row to one row | `key` |
| `match.reducedKey` | One row to one row. It works as `match.naturalKey` does; use it for a later pass with fewer key fields. | `key` |
| `match.groupSum` | Several rows on the `many` side to one row on the other | `block`, `conserve` and `many` |

- **Passes run in order of preference.** The earliest pass that has a match ready commits it first, and a committed row is gone for every other pass.
- **A pass needs a full key.** A row with a null key field isn't placed by that pass, and stays free for the others.
- **One row per side per key, or nothing.** A key with more than one row on a side is ambiguous. See [RESULTS.md](RESULTS.md#ambiguity).
- **The `TradeRef` veto.** When the profile declares a field named `TradeRef`, `match.naturalKey`, `match.reducedKey` and `match.groupSum` won't pair two rows whose `TradeRef` values differ. A missing value, or an identifier in the scheme `broker`, never vetoes. `match.sharedRef` doesn't apply the veto.
- **A group** has 2 to `maxGroupSize` rows. One row isn't a group. Every conserved field must sum exactly at its stored scale, and a row with a null conserved value isn't searched. There is no sum tolerance.
- **A `key` on a group pass** is checked at compile but not used.
- **Every field a pass names** must be declared (`UnknownField`), not text (`TextKey`), and written by every side (`FieldNotProduced`).

Tests: [CascadeTests.cs](../src/RecDiffy.Tests/CascadeTests.cs), [VerificationTests.cs](../src/RecDiffy.Tests/VerificationTests.cs).

### `diff`

`diff.fields` sets how each compared field is judged. A compared field you don't list is compared exactly.

```json
"diff": {
  "fields": {
    "Price":      { "tolerance": { "relative": 0.0001 } },
    "Accrued":    { "tolerance": { "absolute": 0.01 }, "applicableWhen": { "field": "AssetClass", "equals": "Bond" } },
    "SettleDate": { "tolerance": { "days": 1 } },
    "NetCash":    { "tolerance": { "attributed": "trading.netCash" } }
  }
}
```

| Key | What it does |
|---|---|
| `tolerance` | How far the two values may differ. Without it, the field is compared exactly. |
| `tolerance.absolute` | The largest allowed difference. Inclusive. |
| `tolerance.relative` | The largest allowed difference as a fraction of the larger of the two absolute values. Inclusive. |
| `tolerance.days` | Date fields: the largest allowed gap in days. |
| `tolerance.attributed` | The name of an attribution formula. See [Attributions](#attributions). |
| `tolerance.when` | A condition that must hold on both sides for the tolerance to apply. |
| `attributed` | An attribution formula, set beside a numeric tolerance. It wins over `tolerance.attributed`. |
| `applicableWhen` | The field is compared only when this condition holds on at least one side. Otherwise it is `NotCompared`. |

The tolerance kind comes from the first match in this order:

1. `absolute` and `relative` together: `AbsoluteOrRelative`, which passes when either bound does.
2. `absolute`: `Absolute`.
3. `relative`: `Relative`.
4. `attributed`: `Attributed`.
5. `days`: `Days`.
6. None of these: `Exact`.

So `days` beside `absolute` is stored but doesn't set the kind.

- **Write `absolute` and `relative` as JSON numbers, or as invariant text** such as `"0.05"`. Text such as `"0,05"` is `InvalidParameter`. A bound of any other JSON type is treated as absent.
- **Write `days` as a JSON integer.** Text throws `InvalidOperationException`.
- **The kind must fit the field type** (`ToleranceMismatch`). `Decimal` takes `Exact`, `Absolute`, `Relative`, `AbsoluteOrRelative` and `Attributed`. `Date` takes `Exact` and `Days`. Every other type takes only `Exact`.
- **A diff field must be declared** (`UnknownField`) and written by every side (`FieldNotProduced`).

How the comparison works, rounding first and then the inclusive bound, is in [RESULTS.md](RESULTS.md#how-a-field-is-compared).

## Attributions

An attribution explains one field's difference by the differences in its root fields. Attributions are registered by name, as rules are. The trading pack registers two in `TradingRulePack.AddTo`: [NetCashAttribution.cs](../src/RecDiffy.RulePacks.Trading/NetCashAttribution.cs) and [ProRataToQuantityAttribution.cs](../src/RecDiffy.RulePacks.Trading/ProRataToQuantityAttribution.cs). `RuleRegistry.CreateCore()` registers none. Attributions moved out of the engine and behind these names in [#235](https://github.com/m-vaysman/recdiffy.net/issues/235).

Name an attribution in `tolerance.attributed`, or in `attributed` beside a numeric tolerance. Compile checks every name, in either place:

- **The name is registered** (`UnknownRule`). A trading attribution in a profile compiled without the trading pack fails this way.
- **Its required roots are declared fields** (`UnknownField`).

| Name | Required roots | Explains the difference when | Outcome when explained |
|---|---|---|---|
| `trading.proRataToQuantity` | `Quantity` | The right value equals the left value times the right `Quantity` over the left `Quantity`, at the field's scale. A left quantity of 0 never explains. | The outcome of `Quantity`. |
| `trading.netCash` | `Quantity`, `Price` | The difference, at 2 decimal places, equals the change in principal plus the change in `Accrued` plus the change in `Fees`. Principal is `Quantity` times `Price`, divided by 100 when `AssetClass` is `Bond`. The fee change is negated when `Side` is `SELL`, `REC_FIXED` or `REC`. | The worst outcome among `Quantity`, `Price`, `Accrued` and `Fees`. If none moved, `Break`. |

- **When the formula doesn't explain the difference,** the field keeps its own outcome. A numeric tolerance beside `attributed` still applies.
- **`FieldDiff.AttributedTo`** lists the roots: `Quantity` for pro rata, and the roots that moved, in the order above, for net cash.
- **A missing root value counts as 0** in the net cash formula.
- **A root that isn't a compared field counts as a `Break`.** Net cash reads the outcome of all four roots, but compile requires only `Quantity` and `Price`. So unless `Accrued` and `Fees` are also declared with the role `Key` or `Economic`, an explained net cash difference is still a `Break`.
- **Order doesn't matter.** Every field is measured first. Pro rata is settled from those measurements, and net cash last, so a root with its own attribution counts with the outcome that attribution gives it.
- **Known issue:** `AttributedTo` is filled even when the formula doesn't explain the difference ([#254](https://github.com/m-vaysman/recdiffy.net/issues/254)).

Tests: [AttributedDiffTests.cs](../src/RecDiffy.Tests/AttributedDiffTests.cs), [AttributionCompileTests.cs](../src/RecDiffy.Tests/AttributionCompileTests.cs), [TradingAttributionTests.cs](../src/RecDiffy.Tests/TradingAttributionTests.cs), and `Attributes_a_partial_fill_to_quantity` in [DiffTests.cs](../src/RecDiffy.Tests/DiffTests.cs).

## `limits`: search limits

The limits bound the group search in `match.groupSum`. They don't affect pair passes.

| Key | Default | What it bounds |
|---|---|---|
| `maxGroupSize` | 8 | The most rows in one group, unless a pass sets its own. Hard cap 16 (`LimitExceeded`). |
| `maxBlockItems` | 24 | The most rows on the many side in one connected component of a block. A larger one isn't searched: `SearchIncomplete` with `BlockTooLarge`. |
| `maxTargetsPerComponent` | 8 | The most target rows in one component. A larger one isn't searched: `BlockTooLarge`. |
| `maxNodesPerTarget` | 250000 | The search budget for one target. When it runs out: `SearchIncomplete` with `BudgetExhausted`. |

- **Write each limit as a JSON integer.** A number that isn't an integer falls back to the default. A string throws `InvalidOperationException`.
- **Only `maxGroupSize` has a hard cap.** The others are yours to set, but the search cost grows quickly with block size.

Tests: [AdversarialSearchLimitTests.cs](../src/RecDiffy.Tests/AdversarialSearchLimitTests.cs), and `Commits_blocks_under_the_limit_and_leaves_an_oversize_block_unresolved` in [VerificationTests.cs](../src/RecDiffy.Tests/VerificationTests.cs).

## The profile hash

`CompiledProfile.Hash` is a SHA-256 of the profile's content, and each result records it as `result.Run.ProfileHash`.

- **Field order doesn't change it.** Fields are sorted by name first.
- **Rule order and path order do.** They change what a run does.
- **Whitespace, and the order of keys inside an object, don't.** The hash is built from the parsed document.
- **How a number is written does.** `0.1` and `0.10` hash differently, in a tolerance bound or a rule parameter.
- **Declaring a culture does,** even when it equals the server's. A profile that declares none keeps the hash it had before culture settings existed.

Tests: [ProfileReloadHashTests.cs](../src/RecDiffy.Tests/ProfileReloadHashTests.cs), [ConditionOperandHashTests.cs](../src/RecDiffy.Tests/ConditionOperandHashTests.cs).

## Compile errors

Each error is a `ProfileError` with a `Code` and a `Message`. The message names the field, rule, pass or path.

**Raised by `ProfileDocument.Parse`.** It stops at the first problem, except that missing required keys are reported together.

| Code | Cause | What to do |
|---|---|---|
| `InvalidJson` | The text isn't JSON, or its root isn't an object. | Fix the JSON. The message gives the position. |
| `MissingParameter` | `formatVersion`, `profileId`, `version`, `fields` or `paths` is missing. | Add the key. |
| `InvalidParameter` | An unknown `type`, `role` or `midpoint` name; a `culture` that isn't a string; a `strictDateFormats` that isn't `true` or `false`; a bound written as text that isn't an invariant number; an unknown aggregator. | Use the exact names and formats in this reference. Names are case-sensitive. |

**Raised by `Reconciler.Compile`, all together** (except as noted under the table):

| Code | Cause | What to do |
|---|---|---|
| `UnsupportedFormat` | `formatVersion` isn't 1. | Set it to 1. |
| `DuplicateField` | Two fields share a name. | Rename or remove one. |
| `InvalidParameter` | A `scale` outside 0 to 28; `Sum` or `DifferenceByRole` on a field that isn't `Decimal`; an aggregator role missing from `requiredRoles`; a `date.parse` format with no year. | Fix the value. Add a year to the format, such as `dd/MM/yyyy`. |
| `UnknownCulture` | A `culture` this host doesn't know. A host in invariant-globalization mode, such as the AOT host, knows only `""`. | Use a culture name the host has, or `""`. |
| `UnknownRule` | A rule, route, pass or attribution name isn't registered. | Check the spelling. For trading rules and attributions, compile with `TradingRulePack.AddTo(RuleRegistry.CreateCore())`. |
| `WrongPhase` | A rule is listed under a phase it doesn't belong to. | Move it. The phase of each rule is in the tables above. |
| `MissingParameter` | A rule's required parameter; `route.byField` without `field` or `equals`; a group pass without `block`, `conserve` or `many`; `match.naturalKey` or `match.reducedKey` without `key`; an empty `paths` list; `DifferenceByRole` with one role; a condition with no operand, or an empty `in` list. | Add what the message names. |
| `UnknownField` | A rule, route, pass, diff entry, lifecycle setting, package setting or aggregator names a field that isn't declared, or an attribution's required root isn't declared. | Declare the field, or fix the name. |
| `FieldReadBeforeWrite` | A rule reads a field before any rule writes it. | Move the writing rule earlier, or into an earlier phase. |
| `AmbiguousWrite` | Two rules write one field, and their `when` clauses don't exclude each other. | Remove one, or give them exclusive `when` clauses. |
| `TextKey` | A pass uses a `Text` field, or a field with the role `Text`, in its key, block or conserve list. | Use a `Symbol` or `Identifier` field. |
| `FieldNotProduced` | A pass or diff names a field that a side's rules never write. | Add a rule on that side that writes it. |
| `ToleranceMismatch` | The tolerance kind doesn't fit the field type. | See the fit rules under [`diff`](#diff). |
| `OverlappingRoute` | Two paths' routes overlap. | Give each path a distinct route. |
| `LimitExceeded` | `maxGroupSize` is over 16, in `limits` or on a pass. | Use 16 or less. |

When `date.parse` has a format with no year, compile stops at that error and reports nothing else. When two fields share a name, compile stops after the `DuplicateField` check and reports only `DuplicateField`, `UnsupportedFormat` and scale errors.

**Raised by `ProfileBuilder.Build()`:** these structural codes are in `ProfileProblemCodes`. `Build(registry)` then adds the compile errors above, each with the location `compile`.

| Code | Cause |
|---|---|
| `DuplicateField` | A field is declared more than once. |
| `UndeclaredField` | A route, `required.present`, `formatBy`, lifecycle or package setting names an undeclared field. |
| `UndeclaredRuleTarget` | A rule's `to` isn't a declared field. |
| `UndeclaredPassField` | A pass's key, block or conserve list names an undeclared field. |
| `UndeclaredDiffField` | A diff entry names an undeclared field. |
| `ToleranceMismatch` | The tolerance kind doesn't fit the field type. |
| `DuplicatePath` | Two paths share an id. |
| `DuplicatePass` | Two passes in one path share an id. |
| `MissingPath` | The profile has no path. |

`Reconciler.Compile` doesn't check for duplicate path or pass ids. Only the builder does.

Tests: [ProfileCompileCheckTests.cs](../src/RecDiffy.Tests/ProfileCompileCheckTests.cs), [ProfileCompileGapTests.cs](../src/RecDiffy.Tests/ProfileCompileGapTests.cs), [ConditionOperandCompileTests.cs](../src/RecDiffy.Tests/ConditionOperandCompileTests.cs), [ProfileBuilderTests.cs](../src/RecDiffy.Tests/ProfileBuilderTests.cs), [FoundationTests.cs](../src/RecDiffy.Tests/FoundationTests.cs).

## Run diagnostics

A run reports problems with input rows as diagnostics on `RowResult.Diagnostics`. An `Invalid` diagnostic quarantines the row. A `Warning` doesn't.

| Code | Severity | Raised by | Cause | What to do |
|---|---|---|---|---|
| `DecimalParse` | Invalid | `decimal.parse`, `quantity.absolute` | The text isn't a number in the side's culture. | Check the side's culture in `result.Run.Reading`. `1.5` on a de-DE side isn't a number. |
| `FloatValue` | Invalid | `decimal.parse`, `quantity.absolute` | The source gave a `float` or `double`. | Supply a `decimal` or text. |
| `DateParse` | Invalid | `date.parse` | The text matches no declared format, or no format is declared. With `formatBy`, the row's party is missing or has no format with a year; then even a blank or a typed date is `DateParse`. | Add the format, or the party's format to the reference data. |
| `UnknownSymbol` | Invalid | `symbol.map` | The value isn't in `map`, and `passthrough` is `false`. | Add it to the map. |
| `RequiredMissing` | Invalid | `required.present` | A required field is null. | Fix the source, or drop the field from the list. |
| `UnknownParty` | Invalid | `party.resolveLei`, `party.resolveAlias` | The LEI or name isn't in the reference data. | Add it to `ReferenceDataSet`. |
| `UndeterminedSide` | Invalid | `side.resolve` | The side code isn't one the rule knows, or there is no code and no nonzero quantity to decide from. | Use a known code, or supply the quantity. |
| `SideSignConflict` | Invalid | `side.resolve` | `BUY` or `REC_FIXED` with a negative quantity. | Check the source. |
| `PriceParse` | Invalid | `price.from32nds` | The text isn't a 32nds price such as `99-16` or `99-16+`. | Fix the source, or use `decimal.parse`. |
| `MissingKey` | Invalid | `intake` | The row-key column is blank. | Fill the key, or drop `rowKey` to key by position. |
| `DuplicateConflict` | Invalid | `duplicates` | Two rows on one side share a row key but differ. | Fix the source, or choose a key that is unique. |
| `DuplicateRowCollapsed` | Warning | `duplicates` | Identical rows with one key were collapsed into one. | Nothing, unless the copies are unexpected. |
| `MissingLifecycleTarget` | Invalid | `consolidation` | A `CANC` or `CORR` has no target, or any event names a target that isn't an event in the same scope. | Fix the target or the scope. |
| `LifecycleCycle` | Invalid | `consolidation` | A chain's targets form a loop. | Fix the targets. |
| `LifecycleConflict` | Invalid | `consolidation` | A chain leaves more than one survivor. | Fix the chain so one event survives. |
| `ChainMemberQuarantined` | Invalid | `consolidation` | Another row in the chain is quarantined, or one of its keys has several copies. | Fix that row. Its own diagnostic names the cause. |
| `IncompletePackage` | Invalid | `package` | A required role, or a role an aggregator needs, is missing. | Supply the leg, or change `requiredRoles`. |
| `PackageInconsistent` | Invalid | `package` | Legs disagree on a `Same` field. | Fix the legs, or use another aggregator. |
| `MembersDisagree` | Warning | `aggregate`, `package` | The rows of a group or package hold different values for a field the run uses. The field is unknown on the combined item. | Check whether the field should be a block field or have an aggregator. |
| `AggregateOverflow` | Warning | `aggregate`, `package` | A sum or difference is past the decimal range. The value is unknown. | Check the source amounts. |

`SearchIncomplete` with `BlockTooLarge` or `BudgetExhausted` isn't a diagnostic. It is the detail on an entry's trail. See [RESULTS.md](RESULTS.md#incomplete-search).

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| `ProfileCompileException` | Its `Errors` list. Each message names what to fix. |
| `InvalidOperationException` from `ProfileDocument.Parse` | A value has the wrong JSON type, such as `"scale": "2"` or a limit written as text. |
| A setting seems to have no effect | The key's spelling and case. Unknown keys are ignored. |
| A tolerance has no effect | The field's role. Only `Key` and `Economic` fields are compared. |
| Every row on one side is unmatched | That side's `normalization` block. A side with no rules writes no fields. See [Known gaps](#known-gaps). |
| A row is quarantined | Its diagnostics. See [Run diagnostics](#run-diagnostics). |
| Rows remain unmatched | Their key values in `result.Canonical(row)`, the route, and the pass order. |
| Many rows are `Ambiguous` | Whether the key tells the rows apart. Add a field, or an earlier pass with a stricter key. |
| Many rows are `SearchIncomplete` | The pass's `block`. Narrow it before raising `limits`. |
| Records match but show `Break` | `Diffs`, the tolerances, and whether one side is null. |
| A number that looks right is `DecimalParse` | The side's culture in `result.Run.Reading`. |
| Columns are missing | `ProfileCheck.AgainstHeaders`, before you read the files. |

## Known gaps

These behaviours on `dev` are likely defects. They are listed so you can avoid them until they are fixed.

- **A side with no `normalization` block compiles.** That side writes no fields, so none of its rows can be matched. `FieldNotProduced` checks only the sides that have a block. Declare rules for both sides.
- **`match.sharedRef` with no `key`** uses `Counterparty` and `TradeRef`, but compile doesn't check that they are declared. If either is missing, the run throws `KeyNotFoundException`. Give every `match.sharedRef` pass a `key`.
- **`route.byField` on an `Identifier` field matches no row.** The route compares the scheme and value together against `equals`. Route on a `Symbol` field instead.
- **A `days` tolerance ignores its `when`.** The gap is allowed even when the condition fails on one side or both.
- **`"equals": ""` never holds on a CSV or Excel source.** A data reader turns a blank cell into null, and the check compares with an empty string. It does hold on an object source, whose blank text stays `""`, so the same profile behaves differently by source.
- **`party.resolveAlias` can't follow `party.resolveLei` on the same field.** It writes only when the field is still empty, but compile counts it as a second writer (`AmbiguousWrite`). Give the two rules `when` clauses that exclude each other, or resolve into separate fields.
- **A lifecycle survivor merged into a package loses its history.** The package's entry has no `LifecycleCollapsed` flag, and the superseded and cancel rows get no `EntryId`.
- **`AttributedTo` is filled when the formula doesn't explain the difference.** [#254](https://github.com/m-vaysman/recdiffy.net/issues/254).
