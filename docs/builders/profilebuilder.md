# ProfileBuilder, SideBuilder and PathBuilder: a full profile in code

Part of the [builders section](llms.txt). `ProfileBuilder` builds the same `ProfileDocument` that JSON does, so it can say everything a JSON profile can, except a `days` tolerance on a date: `PathBuilder` has no method for one, so write that profile in JSON. Every builder is immutable, and every call returns a new builder.

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

## ProfileBuilder

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

## SideBuilder

One method per core rule. Each call lands in its rule's phase, whatever order you call them in.

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

## PathBuilder

| Method | JSON it writes |
|---|---|
| `RouteAll()`, `RouteWhen(field, value)` | `route.all`, `route.byField` |
| `SharedRef(id, key...)`, `NaturalKey(id, key...)`, `ReducedKey(id, key...)` | One-to-one passes: `match.sharedRef`, `match.naturalKey`, `match.reducedKey` |
| `GroupSum(id, many, block, conserve, maxGroupSize, key)` | `match.groupSum`: several rows on the `many` side against one on the other. |
| `Tolerance(field, absolute, relative, when, applicableWhen)` | `diff.fields.<field>.tolerance` |
| `AttributedTolerance(field, formula, when, applicableWhen)`, `Attribution(field, formula)` | An attribution by name, such as `trading.netCash`. |
| `ApplicableWhen(field, when)` | Compares the field only when the condition holds on at least one side. |

Conditions are `Condition.Equal(field, value)`, `Condition.NotEqual(field, value)` and `Condition.In(field, values...)`. `PackageBuilder.Create(link, role)` declares a package, with `RequireRoles`, `Same`, `Sum`, `FromRole` and `DifferenceByRole`.

## Examples

- [FixProfilesBuiltInCodeTests.cs](../../RecDiffy.NET/src/RecDiffy.IO.Tests/FixProfilesBuiltInCodeTests.cs) builds the five FIX sample profiles in code: orders, fills, booked executions, allocations and trades. Each compiles to the same hash as its JSON file and gives the same result on the fixtures. Between them they use row keys, a culture, lifecycle consolidation on both sides, a one-directional `GroupSum` and absolute tolerances.
- [BuiltProfile.cs](../../RecDiffy.NET/src/RecDiffy.Samples/BuiltProfile.cs) builds the warehouse profile, prints a problem with its location, and checks a file's headers.
- [ProfileBuilderTests.cs](../../RecDiffy.NET/src/RecDiffy.Tests/ProfileBuilderTests.cs) covers every method, including value maps and conditional tolerances.

The code is in [RecDiffy/Profile](../../RecDiffy.NET/src/RecDiffy/Profile). What every key means is in the [profiles section](../profiles/llms.txt).
