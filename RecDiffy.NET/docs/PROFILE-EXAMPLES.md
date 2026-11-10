# Profile examples

Each example on this page is a complete profile, the rows it reads, and the output the engine gives. [ProfileExamplesTests.cs](../src/RecDiffy.Tests/ProfileExamplesTests.cs) reads every profile, row table and output block from this file, runs the profile, and fails if the output differs. So a change in the engine fails a test before it makes this page wrong. The test also fails if a profile on this page has no output block.

For every setting and its default, and every error and diagnostic code, see the [Profile reference](PROFILES.md). To read a result, see [What a result means](RESULTS.md).

To run the examples, from `RecDiffy.NET`:

```shell
dotnet test src/RecDiffy.Tests/RecDiffy.Tests.csproj --filter FullyQualifiedName~ProfileExamplesTests
```

1. [A first profile](#a-first-profile)
2. [What compile fills in](#what-compile-fills-in)
3. [Row keys and duplicates](#row-keys-and-duplicates)
4. [Values a rule can't read](#values-a-rule-cant-read)
5. [Key text](#key-text)
6. [Routing](#routing)
7. [Conditions](#conditions)
8. [Tolerances after rounding](#tolerances-after-rounding)
9. [Relative tolerances](#relative-tolerances)
10. [Search limits](#search-limits)
11. [A lifecycle chain](#a-lifecycle-chain)
12. [Every compile error at once](#every-compile-error-at-once)

## How to read the output

- **Rows.** Each table is one file. A blank cell is a missing value, as the CSV and Excel readers deliver it.
- **Entries.** A run prints one line per result entry: its alignment, composition and outcome, then the left rows and the right rows by row key. When a profile has more than one path, the line starts with the path, or `-` for none. A code from the entry's explanation, such as `Unrouted`, follows in brackets.
- **Fields.** Each field that isn't `Exact` follows its entry, with both values.
- **Diagnostics.** Last come the rows that report a diagnostic or were suppressed: the side, the row's position in its file counting from 1, its row key, its disposition, and each diagnostic's code, severity, field and raw text.
- **No rows.** A profile with no rows prints what compile resolved, or every compile error.
- **Compile.** Each profile is compiled with `RuleRegistry.CreateCore()`. Every profile here declares its culture, so the output is the same on every machine.

## A first profile

A ledger and a bank statement both list invoices. The ledger writes dates as `2025-06-01`, and the bank as `06/01/2025`. Amounts may differ by a cent, and due dates by two days.

<!-- example: first-profile -->
```json
{
  "formatVersion": 1,
  "profileId": "invoices",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": {
    "left": { "rowKey": "InvoiceNo" },
    "right": { "rowKey": "Ref" }
  },
  "fields": [
    { "name": "Invoice", "type": "Symbol", "role": "Key" },
    { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
    { "name": "DueDate", "type": "Date", "role": "Economic" }
  ],
  "normalization": {
    "left": {
      "identify": [ { "rule": "symbol.map", "from": "InvoiceNo", "to": "Invoice" } ],
      "parse": [
        { "rule": "decimal.parse", "from": "Total", "to": "Amount" },
        { "rule": "date.parse", "from": "Due", "to": "DueDate", "formats": ["yyyy-MM-dd"] }
      ]
    },
    "right": {
      "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Invoice" } ],
      "parse": [
        { "rule": "decimal.parse", "from": "Amount", "to": "Amount" },
        { "rule": "date.parse", "from": "DueOn", "to": "DueDate", "formats": ["MM/dd/yyyy"] }
      ]
    }
  },
  "paths": [
    {
      "id": "invoices",
      "route": { "rule": "route.all" },
      "passes": [ { "id": "by-invoice", "pass": "match.naturalKey", "key": ["Invoice"] } ],
      "diff": {
        "fields": {
          "Amount": { "tolerance": { "absolute": 0.01 } },
          "DueDate": { "tolerance": { "days": 2 } }
        }
      }
    }
  ]
}
```

The ledger is the left file:

<!-- left: first-profile -->
| InvoiceNo | Total | Due |
|---|---|---|
| INV-1 | 100.00 | 2025-06-01 |
| INV-2 | 250.00 | 2025-06-10 |
| INV-3 | 75.50 | 2025-06-15 |

The bank statement is the right file:

<!-- right: first-profile -->
| Ref | Amount | DueOn |
|---|---|---|
| INV-1 | 100.01 | 06/01/2025 |
| INV-2 | 250.00 | 06/13/2025 |
| INV-4 | 40.00 | 06/30/2025 |

<!-- expected: first-profile -->
```text
Matched   OneToOne WithinTolerance INV-1 | INV-1
  Amount WithinTolerance: 100.00 vs 100.01
Matched   OneToOne Break           INV-2 | INV-2
  DueDate Break: 2025-06-10 vs 2025-06-13
LeftOnly  None     NotCompared     INV-3 | -
RightOnly None     NotCompared     - | INV-4
```

- **INV-1** is within tolerance: the amounts differ by 0.01, and the tolerance is inclusive.
- **INV-2** is a break: the due dates are three days apart, and the tolerance allows two.
- **INV-3** and **INV-4** have no partner.

Reference: [`sources`](PROFILES.md#sources-row-keys-and-reading), [Core rules](PROFILES.md#core-rules), [`passes`](PROFILES.md#passes) and [`diff`](PROFILES.md#diff).

## What compile fills in

This profile declares only what has no default, plus the run's culture and the right side's culture. A profile with no rows prints what compile resolved.

<!-- example: defaults -->
```json
{
  "formatVersion": 1,
  "profileId": "defaults",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "right": { "culture": "de-DE" } },
  "fields": [
    { "name": "Sku", "type": "Symbol", "role": "Key" },
    { "name": "Quantity", "type": "Decimal", "role": "Economic" },
    { "name": "Note", "type": "Text", "role": "Text" }
  ],
  "normalization": {
    "left": {
      "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" }, { "rule": "text.copy", "from": "Note", "to": "Note" } ],
      "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
    },
    "right": {
      "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" }, { "rule": "text.copy", "from": "Note", "to": "Note" } ],
      "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
    }
  },
  "paths": [
    {
      "id": "stock",
      "route": { "rule": "route.all" },
      "passes": [ { "id": "sku", "pass": "match.naturalKey", "key": ["Sku"] } ]
    }
  ]
}
```

<!-- expected: defaults -->
```text
rounding.midpoint: ToEven
limits: maxGroupSize 8, maxBlockItems 24, maxTargetsPerComponent 8, maxNodesPerTarget 250000
sources.left: rowKey (position); culture "en-US" from Run, decimal ".", group ","; strictDateFormats true
sources.right: rowKey (position); culture "de-DE" from Side, decimal ",", group "."; strictDateFormats true
field Sku: Symbol, Key, scale 0
field Quantity: Decimal, Economic, scale 0
field Note: Text, Text, scale 0
path stock compares: Sku Exact, Quantity Exact
```

- With no `rowKey`, each row is identified by its position in its file, counting from 0.
- The left side takes the run's culture, and the right side its own. Date formats are strict on both.
- `Quantity` has scale 0, so `decimal.parse` rounds it to a whole number.
- `Note` isn't compared: only `Key` and `Economic` fields are. A compared field with no tolerance is compared exactly.

Reference: [Top-level settings](PROFILES.md#top-level-settings) and [`fields`](PROFILES.md#fields-the-canonical-schema).

## Row keys and duplicates

Left rows 2 and 3 share the key `2` and hold the same cells. Rows 4 and 5 share the key `3` but hold different quantities. Row 6 has no key.

<!-- example: row-keys -->
```json
{
  "formatVersion": 1,
  "profileId": "row-keys",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
  "fields": [
    { "name": "Sku", "type": "Symbol", "role": "Key" },
    { "name": "Qty", "type": "Decimal", "role": "Economic" }
  ],
  "normalization": {
    "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Qty" } ] },
    "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Qty" } ] }
  },
  "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "sku", "pass": "match.naturalKey", "key": ["Sku"] } ] } ]
}
```

<!-- left: row-keys -->
| Id | Sku | Qty |
|---|---|---|
| 1 | A | 5 |
| 2 | B | 7 |
| 2 | B | 7 |
| 3 | C | 1 |
| 3 | C | 2 |
|  | D | 4 |

<!-- right: row-keys -->
| Id | Sku | Qty |
|---|---|---|
| 1 | A | 5 |
| 2 | B | 7 |
| 3 | C | 1 |
| 4 | D | 4 |

<!-- expected: row-keys -->
```text
Matched   OneToOne Exact       1 | 1
Matched   OneToOne Exact       2 + 2 | 2
RightOnly None     NotCompared - | 3
RightOnly None     NotCompared - | 4
left row 6 (no key): Quarantined; MissingKey Invalid
left row 2 (2): Matched; DuplicateRowCollapsed Warning "2"
left row 3 (2): Matched; DuplicateRowCollapsed Warning "2"
left row 4 (3): Quarantined; DuplicateConflict Invalid "3"
left row 5 (3): Quarantined; DuplicateConflict Invalid "3"
```

- **Key 2:** the copies are one row, each with a warning. It matches, and the entry lists both copies.
- **Key 3:** both copies are quarantined, so the right row 3 has no partner.
- **Row 6:** a blank key quarantines the row. Rows with no key are listed first.

Reference: [`sources`](PROFILES.md#sources-row-keys-and-reading) and [Run diagnostics](PROFILES.md#run-diagnostics).

## Values a rule can't read

The left side maps its codes with `passthrough` off, reads `1,5` with an en-US culture, reads a date that doesn't fit its format, and requires two fields.

<!-- example: diagnostics -->
```json
{
  "formatVersion": 1,
  "profileId": "diagnostics",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
  "fields": [
    { "name": "Sku", "type": "Symbol", "role": "Key" },
    { "name": "Qty", "type": "Decimal", "role": "Economic" },
    { "name": "Shipped", "type": "Date", "role": "Economic" }
  ],
  "normalization": {
    "left": {
      "identify": [ { "rule": "symbol.map", "from": "Code", "to": "Sku", "map": { "W": "WIDGET", "G": "GADGET" }, "passthrough": false } ],
      "parse": [
        { "rule": "decimal.parse", "from": "Qty", "to": "Qty" },
        { "rule": "date.parse", "from": "Shipped", "to": "Shipped", "formats": ["yyyy-MM-dd"] }
      ],
      "validate": [ { "rule": "required.present", "fields": ["Qty", "Shipped"] } ]
    },
    "right": {
      "identify": [ { "rule": "symbol.map", "from": "Code", "to": "Sku" } ],
      "parse": [
        { "rule": "decimal.parse", "from": "Qty", "to": "Qty" },
        { "rule": "date.parse", "from": "Shipped", "to": "Shipped", "formats": ["yyyy-MM-dd"] }
      ]
    }
  },
  "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "sku", "pass": "match.naturalKey", "key": ["Sku"] } ] } ]
}
```

<!-- left: diagnostics -->
| Id | Code | Qty | Shipped |
|---|---|---|---|
| 1 | W | 5 | 2025-06-01 |
| 2 | X | 5 | 2025-06-01 |
| 3 | G | 1,5 | 2025-06-01 |
| 4 | G | 3 | 06/01/2025 |
| 5 | G |  | 2025-06-02 |

<!-- right: diagnostics -->
| Id | Code | Qty | Shipped |
|---|---|---|---|
| 1 | WIDGET | 5 | 2025-06-01 |

<!-- expected: diagnostics -->
```text
Matched OneToOne Exact 1 | 1
left row 2 (2): Quarantined; UnknownSymbol Invalid on Sku "X"
left row 3 (3): Quarantined; DecimalParse Invalid on Qty "1,5"; RequiredMissing Invalid on Qty
left row 4 (4): Quarantined; DateParse Invalid on Shipped "06/01/2025"; RequiredMissing Invalid on Shipped
left row 5 (5): Quarantined; RequiredMissing Invalid on Qty
```

- **Row 2:** `X` isn't in the map, and `passthrough` is off.
- **Row 3:** a thousands separator must sit where the culture puts it, so `1,5` isn't 15.
- **Rows 3 and 4:** a rule that can't read a value leaves the field blank, so `required.present` reports it too.
- **Row 5:** a blank quantity is no value, not zero.

Reference: [Core rules](PROFILES.md#core-rules) and [Run diagnostics](PROFILES.md#run-diagnostics).

## Key text

A pass compares rows by the text of their key fields, made from the canonical values. Here the key is a mapped symbol and a decimal at scale 1.

<!-- example: keys -->
```json
{
  "formatVersion": 1,
  "profileId": "keys",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
  "fields": [
    { "name": "Sku", "type": "Symbol", "role": "Key" },
    { "name": "Size", "type": "Decimal", "scale": 1, "role": "Key" }
  ],
  "normalization": {
    "left": {
      "identify": [ { "rule": "symbol.map", "from": "Code", "to": "Sku", "map": { "WDG": "WIDGET" } } ],
      "parse": [ { "rule": "decimal.parse", "from": "Size", "to": "Size" } ]
    },
    "right": {
      "identify": [ { "rule": "symbol.map", "from": "Code", "to": "Sku" } ],
      "parse": [ { "rule": "decimal.parse", "from": "Size", "to": "Size" } ]
    }
  },
  "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "sku-size", "pass": "match.naturalKey", "key": ["Sku", "Size"] } ] } ]
}
```

<!-- left: keys -->
| Id | Code | Size |
|---|---|---|
| L1 | WDG | 1.5 |
| L2 | GDG | 2.04 |
| L3 |  | 3.0 |

<!-- right: keys -->
| Id | Code | Size |
|---|---|---|
| R1 | WIDGET | 1.50 |
| R2 | GDG | 2 |
| R3 | SPR | 3 |

<!-- expected: keys -->
```text
Matched   OneToOne Exact       L2 | R2
Matched   OneToOne Exact       L1 | R1
LeftOnly  None     NotCompared L3 | -
RightOnly None     NotCompared - | R3
```

- **L1 and R1:** `symbol.map` maps `WDG` to `WIDGET`, and 1.5 and 1.50 are both `1.5` at scale 1.
- **L2 and R2:** 2.04 rounds to 2.0 at scale 1, the same key as 2.
- **L3:** its code is blank, so the pass doesn't place it. It is left only, not quarantined.
- **Order:** a pass commits its matches in the order of their key text, so `GDG` comes before `WIDGET`.

An identifier's scheme is part of its key, so both sides must declare the same scheme. Here they differ only in case:

<!-- example: identifier-scheme -->
```json
{
  "formatVersion": 1,
  "profileId": "identifier-scheme",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
  "fields": [ { "name": "Isin", "type": "Identifier", "role": "Key" } ],
  "normalization": {
    "left": { "identify": [ { "rule": "identifier.map", "from": "Isin", "to": "Isin", "scheme": "ISIN" } ] },
    "right": { "identify": [ { "rule": "identifier.map", "from": "Isin", "to": "Isin", "scheme": "Isin" } ] }
  },
  "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "isin", "pass": "match.naturalKey", "key": ["Isin"] } ] } ]
}
```

<!-- left: identifier-scheme -->
| Id | Isin |
|---|---|
| L1 | US0378331005 |

<!-- right: identifier-scheme -->
| Id | Isin |
|---|---|
| R1 | US0378331005 |

<!-- expected: identifier-scheme -->
```text
LeftOnly  None NotCompared L1 | -
RightOnly None NotCompared - | R1
```

Reference: [`fields`](PROFILES.md#fields-the-canonical-schema) and [`passes`](PROFILES.md#passes).

## Routing

Each row goes to the first path whose route accepts it. Bonds match on their ISIN, equities on their order reference, and an FX row has no path.

<!-- example: routing -->
```json
{
  "formatVersion": 1,
  "profileId": "routing",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
  "fields": [
    { "name": "AssetClass", "type": "Symbol", "role": "Identity" },
    { "name": "Isin", "type": "Symbol", "role": "Key" },
    { "name": "OrderRef", "type": "Symbol", "role": "Key" },
    { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
  ],
  "normalization": {
    "left": {
      "identify": [
        { "rule": "symbol.map", "from": "Class", "to": "AssetClass" },
        { "rule": "symbol.map", "from": "Isin", "to": "Isin" },
        { "rule": "symbol.map", "from": "Order", "to": "OrderRef" }
      ],
      "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
    },
    "right": {
      "identify": [
        { "rule": "symbol.map", "from": "Class", "to": "AssetClass" },
        { "rule": "symbol.map", "from": "Isin", "to": "Isin" },
        { "rule": "symbol.map", "from": "Order", "to": "OrderRef" }
      ],
      "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
    }
  },
  "paths": [
    {
      "id": "bonds",
      "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Bond" },
      "passes": [ { "id": "isin", "pass": "match.naturalKey", "key": ["Isin"] } ]
    },
    {
      "id": "equities",
      "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Equity" },
      "passes": [ { "id": "order", "pass": "match.naturalKey", "key": ["OrderRef"] } ]
    }
  ]
}
```

<!-- left: routing -->
| Id | Class | Isin | Order | Amount |
|---|---|---|---|---|
| L1 | Bond | US912828XG55 | O-1 | 100.00 |
| L2 | Equity | US0378331005 | O-2 | 50.00 |
| L3 | FX |  | O-3 | 10.00 |

<!-- right: routing -->
| Id | Class | Isin | Order | Amount |
|---|---|---|---|---|
| R1 | Bond | US912828XG55 | O-1 | 100.00 |
| R2 | Equity | US0378331005 | O-2 | 50.00 |

<!-- expected: routing -->
```text
bonds    Matched  OneToOne Exact       L1 | R1
equities Matched  OneToOne Exact       L2 | R2
-        LeftOnly None     NotCompared L3 | - (Unrouted)
```

- `AssetClass` has the role `Identity`, so it routes but isn't compared.
- **L3:** no route accepts `FX`, so it reaches no pass. It gets its own entry with no path, and the code `Unrouted`.
- Route on a `Symbol`, `Decimal` or `Date` field. A route on an `Identifier` field accepts no row today: [#278](https://github.com/m-vaysman/recdiffy.net/issues/278).

Reference: [`route`](PROFILES.md#route).

## Conditions

A tolerance's `when` must hold on both sides for the tolerance to apply. A field's `applicableWhen` leaves the field uncompared when it fails on both sides. Both read `Kind` and `Currency` from the raw columns, because the profile declares no such fields.

<!-- example: conditions -->
```json
{
  "formatVersion": 1,
  "profileId": "conditions",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
  "fields": [
    { "name": "Ref", "type": "Symbol", "role": "Key" },
    { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
    { "name": "Fee", "type": "Decimal", "scale": 2, "role": "Economic" }
  ],
  "normalization": {
    "left": {
      "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Ref" } ],
      "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fee" } ]
    },
    "right": {
      "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Ref" } ],
      "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fee" } ]
    }
  },
  "paths": [
    {
      "id": "all",
      "route": { "rule": "route.all" },
      "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ],
      "diff": {
        "fields": {
          "Amount": { "tolerance": { "absolute": 1, "when": { "field": "Currency", "equals": "JPY" } } },
          "Fee": { "applicableWhen": { "field": "Kind", "equals": "Trade" } }
        }
      }
    }
  ]
}
```

<!-- left: conditions -->
| Id | Kind | Currency | Amount | Fee |
|---|---|---|---|---|
| A | Trade | JPY | 1000.00 | 5.00 |
| B | Trade | USD | 1000.00 | 5.00 |
| C | Transfer | USD | 50.00 | 1.00 |

<!-- right: conditions -->
| Id | Kind | Currency | Amount | Fee |
|---|---|---|---|---|
| A | Trade | JPY | 1001.00 | 5.00 |
| B | Trade | USD | 1001.00 | 6.00 |
| C | Transfer | USD | 50.00 | 0.00 |

<!-- expected: conditions -->
```text
Matched OneToOne WithinTolerance A | A
  Amount WithinTolerance: 1000.00 vs 1001.00
Matched OneToOne Break           B | B
  Amount Break: 1000.00 vs 1001.00
  Fee Break: 5.00 vs 6.00
Matched OneToOne Exact           C | C
```

- **A:** the one-unit tolerance applies to yen, so the amounts are within tolerance.
- **B:** the tolerance doesn't apply to dollars, so the amounts break, and so do the fees.
- **C:** a transfer's fee isn't compared, so the entry is exact.
- A blank check, `"equals": ""`, doesn't hold on a blank cell from a file today: [#280](https://github.com/m-vaysman/recdiffy.net/issues/280).

Reference: [Conditions](PROFILES.md#conditions) and [`diff`](PROFILES.md#diff).

## Tolerances after rounding

Both values are rounded to the field's scale before they are subtracted, and the bound is inclusive. This profile rounds halves away from zero, and allows a cent.

<!-- example: tolerance-rounding -->
```json
{
  "formatVersion": 1,
  "profileId": "tolerance-rounding",
  "version": "1.0.0",
  "culture": "en-US",
  "rounding": { "midpoint": "AwayFromZero" },
  "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
  "fields": [
    { "name": "Ref", "type": "Symbol", "role": "Key" },
    { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
  ],
  "normalization": {
    "left": { "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] },
    "right": { "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] }
  },
  "paths": [
    {
      "id": "all",
      "route": { "rule": "route.all" },
      "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ],
      "diff": { "fields": { "Amount": { "tolerance": { "absolute": 0.01 } } } }
    }
  ]
}
```

<!-- left: tolerance-rounding -->
| Id | Amount |
|---|---|
| A | 10.004 |
| B | 10.005 |
| C | 10.006 |
| D | 10.00 |

<!-- right: tolerance-rounding -->
| Id | Amount |
|---|---|
| A | 10.016 |
| B | 10.015 |
| C | 10.014 |
| D | 10.01 |

<!-- expected: tolerance-rounding -->
```text
Matched OneToOne Break           A | A
  Amount Break: 10.00 vs 10.02
Matched OneToOne WithinTolerance B | B
  Amount WithinTolerance: 10.01 vs 10.02
Matched OneToOne Exact           C | C
Matched OneToOne WithinTolerance D | D
  Amount WithinTolerance: 10.00 vs 10.01
```

- **A** breaks: the raw difference is 0.012, but 10.00 and 10.02 differ by 0.02.
- **B** is within tolerance: away from zero, 10.005 is 10.01 and 10.015 is 10.02. With the default `ToEven`, they would be 10.00 and 10.02, and break.
- **C** is exact: the raw difference is 0.008, but both round to 10.01.
- **D** is within tolerance: the difference equals the bound.

Reference: [`diff`](PROFILES.md#diff) and [Top-level settings](PROFILES.md#top-level-settings).

## Relative tolerances

A relative bound is a fraction of the larger of the two values. With both an absolute and a relative bound, either one is enough.

<!-- example: tolerance-relative -->
```json
{
  "formatVersion": 1,
  "profileId": "tolerance-relative",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
  "fields": [
    { "name": "Ref", "type": "Symbol", "role": "Key" },
    { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
    { "name": "Fee", "type": "Decimal", "scale": 2, "role": "Economic" }
  ],
  "normalization": {
    "left": {
      "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Ref" } ],
      "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fee" } ]
    },
    "right": {
      "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Ref" } ],
      "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fee" } ]
    }
  },
  "paths": [
    {
      "id": "all",
      "route": { "rule": "route.all" },
      "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ],
      "diff": {
        "fields": {
          "Amount": { "tolerance": { "relative": 0.001 } },
          "Fee": { "tolerance": { "absolute": 0.05, "relative": 0.01 } }
        }
      }
    }
  ]
}
```

<!-- left: tolerance-relative -->
| Id | Amount | Fee |
|---|---|---|
| A | 1000.00 | 2.00 |
| B | 1000.00 | 20.00 |

<!-- right: tolerance-relative -->
| Id | Amount | Fee |
|---|---|---|
| A | 999.00 | 2.05 |
| B | 998.99 | 20.20 |

<!-- expected: tolerance-relative -->
```text
Matched OneToOne WithinTolerance A | A
  Amount WithinTolerance: 1000.00 vs 999.00
  Fee WithinTolerance: 2.00 vs 2.05
Matched OneToOne Break           B | B
  Amount Break: 1000.00 vs 998.99
  Fee WithinTolerance: 20.00 vs 20.20
```

- **A:** 0.1% of 1000.00 is 1.00, so a difference of 1.00 is within tolerance. The fees differ by 0.05, the absolute bound.
- **B:** 1.01 is more than 1.00, so the amounts break. The fees differ by 0.20, over the absolute bound but within 1% of 20.20.

Reference: [`diff`](PROFILES.md#diff).

## Search limits

`match.groupSum` matches several bins to one book position when their quantities add up. Here `maxBlockItems` is 3, and the pass allows groups of two.

<!-- example: limits -->
```json
{
  "formatVersion": 1,
  "profileId": "limits",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "Bin" }, "right": { "rowKey": "Position" } },
  "fields": [
    { "name": "Sku", "type": "Symbol", "role": "Key" },
    { "name": "Quantity", "type": "Decimal", "role": "Economic" }
  ],
  "normalization": {
    "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
    "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
  },
  "paths": [
    {
      "id": "stock",
      "route": { "rule": "route.all" },
      "passes": [ { "id": "bins", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"], "maxGroupSize": 2 } ]
    }
  ],
  "limits": { "maxBlockItems": 3 }
}
```

<!-- left: limits -->
| Bin | Sku | Quantity |
|---|---|---|
| A1 | WIDGET | 1 |
| A2 | WIDGET | 2 |
| A3 | WIDGET | 3 |
| A4 | WIDGET | 4 |
| B1 | GADGET | 2 |
| B2 | GADGET | 3 |
| C1 | GIZMO | 1 |
| C2 | GIZMO | 1 |
| C3 | GIZMO | 1 |

<!-- right: limits -->
| Position | Sku | Quantity |
|---|---|---|
| BOOK-1 | WIDGET | 10 |
| BOOK-2 | GADGET | 5 |
| BOOK-3 | GIZMO | 3 |

<!-- expected: limits -->
```text
Matched          ManyToOne Exact       B1 + B2 | BOOK-2
SearchIncomplete None      NotCompared A1 + A2 + A3 + A4 | BOOK-1 (BlockTooLarge)
LeftOnly         None      NotCompared C1 | -
LeftOnly         None      NotCompared C2 | -
LeftOnly         None      NotCompared C3 | -
RightOnly        None      NotCompared - | BOOK-3
```

- **GADGET:** two bins add up to the book position, inside every limit.
- **WIDGET:** four candidate bins are more than `maxBlockItems` allows, so the block isn't searched. That is `SearchIncomplete`, not a non-match.
- **GIZMO:** only all three bins add up to 3, and the pass allows groups of two, so no group is found.

Reference: [`limits`](PROFILES.md#limits-search-limits) and [`passes`](PROFILES.md#passes).

## A lifecycle chain

E2 corrects E1, and E3 cancels E9, which isn't in the file. Lifecycle reads canonical fields, so the profile declares and writes the three it uses: `EventAction`, `EventTarget` and `Counterparty`.

<!-- example: lifecycle -->
```json
{
  "formatVersion": 1,
  "profileId": "lifecycle",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "rowKey": "EventId" }, "right": { "rowKey": "TradeId" } },
  "fields": [
    { "name": "Isin", "type": "Symbol", "role": "Key" },
    { "name": "Quantity", "type": "Decimal", "role": "Economic" },
    { "name": "Counterparty", "type": "Symbol", "role": "Identity" },
    { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
    { "name": "EventTarget", "type": "Symbol", "role": "Lifecycle" }
  ],
  "normalization": {
    "left": {
      "identify": [
        { "rule": "symbol.map", "from": "Isin", "to": "Isin" },
        { "rule": "symbol.map", "from": "Party", "to": "Counterparty" },
        { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
        { "rule": "symbol.map", "from": "Target", "to": "EventTarget" }
      ],
      "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
    },
    "right": {
      "identify": [ { "rule": "symbol.map", "from": "Isin", "to": "Isin" } ],
      "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
    }
  },
  "consolidation": { "left": { "lifecycle": {} } },
  "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "isin", "pass": "match.naturalKey", "key": ["Isin"] } ] } ]
}
```

<!-- left: lifecycle -->
| EventId | Party | Action | Target | Isin | Qty |
|---|---|---|---|---|---|
| E1 | ACME | NEWT |  | US0378331005 | 100 |
| E2 | ACME | CORR | E1 | US0378331005 | 120 |
| E3 | ACME | CANC | E9 | US5949181045 | 50 |

<!-- right: lifecycle -->
| TradeId | Isin | Qty |
|---|---|---|
| T1 | US0378331005 | 120 |

<!-- expected: lifecycle -->
```text
Matched OneToOne, LifecycleCollapsed Exact E2 | T1
left row 1 (E1): Suppressed (Superseded)
left row 3 (E3): Quarantined; MissingLifecycleTarget Invalid
```

- **E1 and E2:** the correction supersedes E1, so E2 stands for the chain and matches T1. E1 is suppressed, not unmatched.
- **E3:** it cancels an event that doesn't exist, so it is quarantined.
- `"lifecycle": {}` takes every default: `EventAction`, `EventTarget`, `Counterparty` and `CANC`.

Reference: [`lifecycle`](PROFILES.md#lifecycle).

## Every compile error at once

This profile has one mistake of each common kind. Compile reports them all in one `ProfileCompileException`, and the output is its `Errors` list:

<!-- example: mistakes -->
```json
{
  "formatVersion": 1,
  "profileId": "mistakes",
  "version": "1.0.0",
  "culture": "en-US",
  "sources": { "left": { "culture": "en-UK" } },
  "fields": [
    { "name": "Ref", "type": "Text", "role": "Key" },
    { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
    { "name": "Booked", "type": "Date", "role": "Economic" }
  ],
  "normalization": {
    "left": {
      "identify": [
        { "rule": "text.copy", "from": "Ref", "to": "Ref" },
        { "rule": "text.copy", "from": "OurRef", "to": "Ref" },
        { "rule": "decimal.parse", "from": "Amt", "to": "Amount" }
      ],
      "parse": [
        { "rule": "decimal.parse", "from": "Amt", "to": "Amount", "when": { "field": "Kind" } },
        { "rule": "date.parse", "from": "Booked" },
        { "rule": "money.parse", "from": "Amt", "to": "Amount" },
        { "rule": "decimal.parse", "from": "Fee", "to": "Fees" }
      ],
      "validate": [ { "rule": "required.present", "fields": ["Booked"] } ]
    },
    "right": {
      "identify": [ { "rule": "text.copy", "from": "Ref", "to": "Ref" } ],
      "parse": [
        { "rule": "decimal.parse", "from": "Amt", "to": "Amount" },
        { "rule": "date.parse", "from": "Booked", "to": "Booked", "formats": ["yyyy-MM-dd"] }
      ]
    }
  },
  "paths": [
    {
      "id": "all",
      "route": { "rule": "route.all" },
      "passes": [ { "id": "by-ref", "pass": "match.naturalKey", "key": ["Ref"] } ],
      "diff": {
        "fields": {
          "Booked": { "tolerance": { "absolute": 1 } },
          "Amount": { "attributed": "trading.netCash" }
        }
      }
    },
    {
      "id": "big",
      "route": { "rule": "route.byField", "field": "Ref", "equals": "BIG" },
      "passes": [ { "id": "groups", "pass": "match.groupSum", "block": ["Booked"], "conserve": ["Amount"], "maxGroupSize": 20 } ]
    }
  ]
}
```

<!-- expected: mistakes -->
```text
UnknownCulture: sources.left.culture 'en-UK' is not a culture this host knows.
AmbiguousWrite: left field 'Ref' is written by 'text.copy' and 'text.copy'.
WrongPhase: Rule 'decimal.parse' is listed under identify but belongs to Parse.
MissingParameter: left.date.parse is missing 'to'.
UnknownRule: Rule 'money.parse' is not registered.
UnknownField: left.decimal.parse writes undeclared field 'Fees'.
FieldReadBeforeWrite: left.required.present reads 'Booked' before any rule writes it.
OverlappingRoute: Path 'big' overlaps another route.
TextKey: Path 'all' uses text field 'Ref' as a key.
MissingParameter: Pass 'groups' requires block, conserve, and many.
LimitExceeded: Pass 'groups' maxGroupSize exceeds the hard cap of 16.
FieldNotProduced: Path 'big' keys 'Booked', which a side does not write.
ToleranceMismatch: Tolerance 'Absolute' does not fit field 'Booked'.
MissingParameter: left.decimal.parse when on 'Kind' needs equals, notEquals or in.
UnknownRule: Path 'all' attributes 'Amount' by 'trading.netCash', which is not registered.
```

| Error | The mistake |
|---|---|
| `UnknownCulture` | `en-UK` isn't a culture. The United Kingdom's is `en-GB`. |
| `AmbiguousWrite` | Two `text.copy` rules write `Ref`, with no `when` to tell them apart. |
| `WrongPhase` | `decimal.parse` belongs under `parse`, not `identify`. |
| `MissingParameter` | `date.parse` has no `to`. |
| `UnknownRule` | No rule is named `money.parse`. |
| `UnknownField` | `Fees` isn't declared. |
| `FieldReadBeforeWrite` | The left side never writes `Booked`, because its `date.parse` failed, so `required.present` has nothing to read. |
| `OverlappingRoute` | `route.all` overlaps every other route. |
| `TextKey` | `Ref` is a `Text` field, which can't be a key. |
| `MissingParameter` | The group pass has no `many`. |
| `LimitExceeded` | A group of 20 is over the hard cap of 16. |
| `FieldNotProduced` | The group pass blocks on `Booked`, which the left side never writes. |
| `ToleranceMismatch` | `absolute` doesn't fit a `Date` field. Use `days`. |
| `MissingParameter` | The `when` has a field but no operand. |
| `UnknownRule` | `trading.netCash` is registered only by the trading pack, and this profile is compiled without it. |

Two mistakes stop compile before it can report the rest, so they aren't in this profile: a field declared twice (`DuplicateField`), and a `date.parse` format with no year (`InvalidParameter`).

Reference: [Compile errors](PROFILES.md#compile-errors).
