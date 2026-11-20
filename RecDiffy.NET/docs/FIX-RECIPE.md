# Reconciling FIX messages

RecDiffy doesn't read FIX. A transform outside RecDiffy flattens FIX messages into CSV files, and RecDiffy reconciles those files with an ordinary profile. This page is the recipe for that transform: which messages become rows, which columns each row has, and the rules that make the result reconcile correctly.

The sample profiles, fixtures and tests are in [test-data/fix-reconciliation](../test-data/fix-reconciliation) and [FixReconciliationFixtureTests.cs](../src/RecDiffy.IO.Tests/FixReconciliationFixtureTests.cs). The flows follow the order state change matrices in the FIX specification (appendix D of FIX 4.4 and later).

## Who validates the wire

The transform owns everything about the FIX wire: framing, BodyLength (9), CheckSum (10), tag order, repeating-group counts, duplicate tags, data fields (95/96), sequence gaps and possible duplicates (43, 97). RecDiffy only sees the CSV. A transform that writes a clean-looking CSV from a broken message produces a clean-looking reconciliation, so the transform must reject or flag such messages rather than pass them on.

## The rules

1. **One row per business message, one per repeating-group entry.** Session messages (logon, heartbeat, test request, resend request, sequence reset, reject `35=3`) never become rows. An allocation with three accounts (`78=3`) becomes three rows.
2. **Only accepted state changes become rows.** A replace or cancel becomes a row only when the broker confirms it in an execution report. Your own requests (`35=G`, `35=F`) never become rows, and neither does a rejected request (`35=9`, for a replace or a cancel). A rejected request written as a row would be read as a successor and show as a false break; the fixtures show this.
3. **Order rows are keyed by `ExecID`, and `Target` is the previous accepted state's `ExecID`.** This handles every way an order changes:
   - a replace (`150=5`), which has a new `ClOrdID` and `41`
   - a cancel (`150=4`), solicited or unsolicited; an unsolicited cancel has no `41` and keeps its `ClOrdID`
   - a restatement (`150=D`), such as a quantity cut or a stock split, which keeps its `ClOrdID` and has no `41`
   - an expiry (`150=C`), treated as a cancel

   The transform keeps the last accepted `ExecID` of each order to fill in `Target`. `Root` is the order's first `ClOrdID`, so fills booked under any `ClOrdID` of the order group together.
4. **Some reports never become order rows:** pending states (`150=A`, `6`, `E`), done for day (`150=3`), order status replies (`150=I`, and FIX 4.2 `20=3`), and suspended or stopped orders (`150=9`, `150=7`). Fills sent while an order is pending carry its old `ClOrdID`; `Root` keeps them together.
5. **The lifecycle scope is the counterparty, not the day.** A good-till-cancel order replaced on a later day must still collapse to its last version. A scope that includes the day breaks that chain, and the fixtures show it.
6. **IDs must be unique within a counterparty.** FIX only promises a unique `ExecID` within a trading day or the life of a multi-day order. When a counterparty reuses IDs across days, prefix them with the trade date, as the multi-day fixture does.
7. **Timestamps are decimal nanoseconds since the Unix epoch.** RecDiffy dates are calendar days, so a time of day can't travel as a date. Convert `UTCTimestamp` (`yyyyMMdd-HH:mm:ss`, with optional `.sss`, `.ssssss` or `.sssssssss`) to nanoseconds, and never through a binary floating-point type. A decimal field compares exactly to 1 ns, and an absolute tolerance is inclusive: `500000000` accepts exactly half a second.
8. **Counterparty is in every pass key,** so no row can match across counterparties and each counterparty can be reconciled on its own. Every fixture checks this.
9. **Both sides use the same columns.** Your side is what your OMS recorded from the broker; the other side is the broker's report or drop copy. Allocations are the exception, below.

## FIX versions

| Event | FIX 4.3 and later | FIX 4.2 |
|---|---|---|
| Fill | `150=F` | `150=1` or `150=2`, with `20=0` |
| Fill correction | `150=G`, `19` names the corrected fill | `20=2`, `19` names the corrected fill |
| Fill bust | `150=H`, `19` names the busted fill | `20=1`, `19` names the busted fill |
| Order status reply | `150=I` | `20=3` |

`39=5` (replaced) is no longer used from 4.3 on. A replace report's `39` is the order's status after the replace, such as `0` or `1`.

## The files

Each file has a `Case` column naming the fixture case. Profiles ignore it.

### Orders: one row per accepted order event

| Column | FIX source | Example |
|---|---|---|
| `ExecID` | 17 | `M1-2` |
| `ClOrdID` | 11 | `M3` |
| `OrigClOrdID` | 41, informational | `M2` |
| `Root` | the order's first 11 | `M1` |
| `ExecType` | 150, informational | `5` |
| `Action` | `NEW` for 150=0; `CORR` for 150=5 and 150=D; `CANC` for 150=4 and 150=C | `CORR` |
| `Target` | the previous accepted state's 17 | `M1-1` |
| `Counterparty` | 49 or 56, resolved to one name | `MS` |
| `Account` | 1 | `ACC1` |
| `Symbol` | 55 | `IBM` |
| `Side` | 54 | `BUY` |
| `OrderQty` | 38 | `120` |
| `Price` | 44 | `150.00` |
| `OrdStatus` | 39, compared | `0` |

`M1-2, M3, M2, M1, 5, CORR, M1-1, MS, ACC1, IBM, BUY, 120, 150.00, 0` reads back as: Morgan Stanley confirmed the second replace of order M1, which now stands at 120 IBM.

### Fills: one row per execution

| Column | FIX source |
|---|---|
| `ExecID` | 17 |
| `Root` | the order's first 11 |
| `Action` | `NEW` for a fill, `CORR` for a correction, `CANC` for a bust (see FIX versions) |
| `Target` | 19 |
| `Counterparty`, `Account`, `Symbol`, `Side` | as for orders |
| `LastQty` | 32 |
| `LastPx` | 31 |
| `TransactNanos` | 60, as nanoseconds since the epoch |

A correction can itself be corrected: its `Target` is the previous correction, and the chain ends at the last one. A broker that keeps a fill you don't know (`35=Q`) leaves it one-sided until it's busted.

### Booked executions: your average-price booking against the broker's fills

Some firms book one execution per order while the broker reports every fill. This file has `ExecID`, `Root`, `Counterparty`, `Account`, `Symbol`, `Side` and `LastQty`. Its profile matches one booking to one fill, or to several fills whose quantities sum to it. It compares quantity only, because a group sums every compared decimal, and a sum of prices or timestamps means nothing.

### Allocations: one row per account in an allocation

Here your side is your instruction (`35=J`), not an acknowledgement, so a broker that doesn't accept an account shows as a break. The broker's side is what it accepted: `35=AS`, or a `35=P` with `87=0`. `87=3` only means received, so it never becomes a row, and `87=2` rejects some accounts, so only the accepted accounts become rows.

| Column | FIX source |
|---|---|
| `AllocRow` | 70 and 79, joined: `AL1/FUND-A` |
| `AllocID` | 70 |
| `AllocRoot` | the first 70 of the chain |
| `Action` | `NEW` for 71=0, `CORR` for 71=1, `CANC` for 71=2 |
| `Target` | the `AllocRow` it replaces or cancels (72 and 79) |
| `Counterparty` | as for orders |
| `AllocAccount` | 79 |
| `AllocQty` | 80 |
| `AvgPx` | 6 |

### Trade capture: one row per trade report

A report becomes a row on the counterparty's side only once it's accepted: a `35=AR` with `939=0`, or a report with `856=2`. A declined report (`856=3`) never becomes a row, so your report stays one-sided.

| Column | FIX source |
|---|---|
| `TradeReportID` | 571 |
| `TradeRoot` | the first 571 of the chain |
| `Action` | `NEW` for 487=0; `CORR` for 487=2; `CANC` for 487=1, 487=4 (reverse) and 487=5 (back-out) |
| `Target` | 572 |
| `Counterparty`, `Account`, `Symbol`, `Side` | as for orders |
| `LastQty` | 32 |
| `LastPx` | 31 |

## What the fixtures show

| Case | Result |
|---|---|
| New order, partially filled order | Matched exactly |
| Order status differs | A break on `OrdStatus` |
| Replace mid-fill | Matched on the replacement; the original is superseded; fills under both `ClOrdID`s each match |
| Multi-step replace | Matched on the last version |
| Rejected replace or cancel | Matched on the original, because the rejected request never became a row |
| Rejected replace written as a row anyway | A false break on quantity and status |
| Cancel after a partial fill, unsolicited cancel, expiry | The order is suppressed on both sides; its fill still matches |
| Restatement | Matched on the restated quantity |
| Not yet acknowledged | Left only |
| Replace on a later day | Matched on the replacement, and quarantined if the scope includes the day |
| Same IDs at two counterparties | Never matched across them, in every file |
| Busted fill, bust then re-fill | The busted fill is suppressed on both sides; the new fill matches |
| Corrected fill, correction of a correction | Matched on the last correction |
| Identical resend | Collapsed into one match, with a `DuplicateRowCollapsed` warning |
| Resend with a different quantity | Quarantined as `DuplicateConflict`, never matched |
| Times 0.3 s, 0.5 s, 0.5 s + 1 ns and 2 s apart, tolerance 0.5 s | Within, within, break, break |
| Times 1 ns apart | Within tolerance, with a delta of exactly 1 |
| Quantity with the opposite sign | A break of −200 |
| Booking against fills that sum to it | Matched one to many |
| Booking against fills that don't sum to it | Ambiguous, for a person to resolve: 250 booked against 300 filled |
| Allocation split across accounts | Matched account by account |
| Replaced or cancelled allocation | Matched on the replacement, or suppressed on both sides |
| Allocation only received, or one account rejected | That account stays left only |
| Quantity or price difference | A break, with the delta |
| Reversed trade report | Suppressed on both sides |
| Declined trade report | Left only |
