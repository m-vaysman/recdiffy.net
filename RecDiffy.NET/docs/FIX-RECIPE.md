# Reconciling FIX messages

RecDiffy doesn't read FIX. A transform outside RecDiffy flattens FIX messages into CSV files, and RecDiffy reconciles those files with an ordinary profile. This page is the recipe for that transform: which rows it writes, which columns each row has, and the rules that make the result reconcile correctly.

The sample profiles, fixtures and tests are in [test-data/fix-reconciliation](../test-data/fix-reconciliation) and [FixReconciliationFixtureTests.cs](../src/RecDiffy.IO.Tests/FixReconciliationFixtureTests.cs).

## The rules

1. **One row per business message.** Session messages (logon, heartbeat, resend request) never become rows.
2. **One row per repeating-group entry.** An allocation with three accounts (`78=3`) becomes three rows.
3. **Only acknowledged changes enter a chain.** A replace or cancel becomes a row only when the broker confirms it: an execution report with `150=5` (replaced) or `150=4` (canceled), or an allocation ack. Your own request (`35=G`, `35=F`) never becomes a row. A rejected request (`35=9`) therefore never reaches RecDiffy, and the original order still matches.
4. **Every row names its chain.** `Action` is `NEW`, `CORR` (replace or correction) or `CANC` (cancel or bust). `Target` names the row it replaces or cancels. `Root` names the first order of the chain, so fills booked before and after a replace group together.
5. **The lifecycle scope is the counterparty, not the day.** A good-till-cancel order replaced on a later day still collapses to its last version. IDs must be unique within a counterparty; if a firm reuses `ClOrdID` daily, prefix it with the date of the original order.
6. **Timestamps are decimal nanoseconds since the Unix epoch.** RecDiffy dates are calendar days, so a time of day can't travel as a date. A decimal field compares exactly to 1 ns and takes an absolute tolerance, such as `500000000` for half a second.
7. **Counterparty is in every pass key.** Then no row can match across counterparties, and each counterparty can be reconciled on its own.
8. **Both sides use the same columns.** Your side is what your OMS recorded from the broker's acks; the other side is the broker's report or drop copy.

## The files

Each file has a `Case` column naming the fixture case. Profiles ignore it.

### Orders: one row per acknowledged order event

| Column | FIX source | Example |
|---|---|---|
| `ClOrdID` | 11 | `M3` |
| `Root` | the first 11 of the chain | `M1` |
| `Action` | `NEW` for 150=0, `CORR` for 150=5, `CANC` for 150=4 | `CORR` |
| `Target` | 41 | `M2` |
| `Counterparty` | 49 or 56, resolved to one name | `MS` |
| `Account` | 1 | `ACC1` |
| `Symbol` | 55 | `IBM` |
| `Side` | 54 | `BUY` |
| `OrderQty` | 38 | `120` |
| `Price` | 44 | `150.00` |
| `OrdStatus` | 39 | `5` |

`M3, M1, CORR, M2, MS, ACC1, IBM, BUY, 120, 150.00, 5` reads back as: Morgan Stanley confirmed the second replace of order M1, which now stands at 120 IBM.

### Fills: one row per execution

| Column | FIX source |
|---|---|
| `ExecID` | 17 |
| `Root` | the first 11 of the order's chain |
| `Action` | `NEW` for 150=F, `CORR` for 150=G (trade correct), `CANC` for 150=H (trade cancel) |
| `Target` | 19 |
| `Counterparty`, `Account`, `Symbol`, `Side` | as for orders |
| `LastQty` | 32 |
| `LastPx` | 31 |
| `TransactNanos` | 60, as nanoseconds since the epoch |

### Booked executions: your average-price booking against the broker's fills

Some firms book one execution per order while the broker reports every fill. This file has `ExecID`, `Root`, `Counterparty`, `Account`, `Symbol`, `Side` and `LastQty`. Its profile matches one booking to one fill, or to several fills whose quantities sum to it. It compares quantity only, because a group sums every compared decimal, and a sum of prices or timestamps means nothing.

### Allocations: one row per account in an allocation

| Column | FIX source |
|---|---|
| `AllocRow` | 70 and 79, joined: `AL1/FUND-A` |
| `AllocID` | 70 |
| `AllocRoot` | the first 70 of the chain |
| `Action` | `NEW` for 71=0, `CORR` for 71=1, `CANC` for 71=2, once acknowledged |
| `Target` | the `AllocRow` it replaces or cancels |
| `Counterparty` | as for orders |
| `AllocAccount` | 79 |
| `AllocQty` | 80 |
| `AvgPx` | 6 |

### Trade capture: one row per trade report

| Column | FIX source |
|---|---|
| `TradeReportID` | 571 |
| `TradeRoot` | the first 571 of the chain |
| `Action` | `NEW` for 487=0, `CORR` for 487=2, `CANC` for 487=1 |
| `Target` | 572 |
| `Counterparty`, `Account`, `Symbol`, `Side` | as for orders |
| `LastQty` | 32 |
| `LastPx` | 31 |

## What the fixtures show

| Case | Result |
|---|---|
| New order, partially filled order | Matched exactly |
| Replace mid-fill | Matched on the replacement; the original is superseded; fills on both sides of the replace each match |
| Multi-step replace | Matched on the last version |
| Rejected replace | Matched on the original, because the rejected request never became a row |
| Cancel after a partial fill | The order is suppressed on both sides; its fill still matches |
| Not yet acknowledged | Left only |
| Replace on a later day | Matched on the replacement |
| Same order at two counterparties | Never matched across them |
| Busted fill | Suppressed on both sides |
| Corrected fill | Matched on the correction |
| Identical resend | Collapsed into one match |
| Times 0.3 s and 2 s apart, tolerance 0.5 s | Within tolerance, and a break |
| Booking against fills that sum to it | Matched one to many |
| Booking against fills that don't sum to it | Ambiguous, for a person to resolve |
| Allocation split across accounts | Matched account by account |
| Replaced or cancelled allocation | Matched on the replacement, or suppressed |
| Quantity or price difference | A break, with the delta |
