# Benchmarks

This note says what the RecDiffy benchmarks measure, how to run them, what one local run gave, and what the numbers do not show. It covers #64 (datasets), #41 (group construction) and #66 (this note).

The numbers come from one machine. They are not a comparison with any other tool, and they are not a promise about your hardware.

## What is measured

The benchmarks are in `RecDiffy.NET/src/RecDiffy.Benchmarks` and use BenchmarkDotNet. Every class has `[MemoryDiagnoser]`, so each result has a mean time, the bytes allocated per operation, and GC counts.

| Class | What one operation does | Sizes |
|---|---|---|
| `ExactDiffBench` | `FieldDiffer.Compare` on one exact pair with three decimal fields. | One pair |
| `CascadeScaleBench` | `CascadeMatcher.Run` with one natural-key pass on one-to-one pairs. | 1,000 and 4,000 rows per side |
| `ExecutionModeBench` | `Reconciler.Run` and `RunAsync` end to end: intake, normalization, matching and results. `Parallel(degree)` sets `MaxDegreeOfParallelism`, which parallelizes field comparison, not matching. | 1,000 and 4,000 rows per side |
| `DatasetBench` (#64) | `CascadeMatcher.Run` with four passes on one of seven dataset shapes. | 1,000 and 4,000 rows per side |
| `GroupConstructionBench` (#41) | `Build`: `GroupIndex.Create` alone. `Match`: `CascadeMatcher.Run` with the group-sum pass alone. | 4,000 rows per side, widths 8 and 64 |

`DatasetBench`, `GroupConstructionBench` and `CascadeScaleBench` time the matcher only. Their rows start as consolidated rows and stop at the match report. Intake, normalization, field comparison and result building are not in those numbers. `ExecutionModeBench` is the only end-to-end benchmark, and it uses exact pairs only.

`GroupConstructionBench.Build` covers block placement, component construction, and the first search of each component. `Match` adds the commits. After each commit the pass evaluates the committed rows' block again.

## The datasets (#64)

`BenchDatasets.Create(shape, rows, width)` builds each dataset in code. It uses `System.Random` with the fixed seed `64`, so the same shape, size and width always give the same rows. It also shuffles each side, so row order is not sorted.

- **Rows** is half the total row count. A dataset at 4,000 rows has 8,000 rows in all, though the two sides are not always equal.
- **Width** is the number of group targets that share one block key.

The passes and schema are the ones in `CascadeTests`:

| Pass | Rule | Key or block |
|---|---|---|
| P1 | `match.sharedRef` | Counterparty and TradeRef |
| P2 | `match.naturalKey` | Counterparty, Instrument, TradeDate, Side, Quantity |
| P3 | `match.groupSum` | Block on Counterparty, Instrument, TradeDate, Side, Price. Conserve Quantity and NetCash. The many side is left. |
| P4 | `match.reducedKey` | Counterparty, Instrument, TradeDate, Side |

The search limits are `SearchLimits.Default`:

- a group holds at most 8 rows;
- a component holds at most 24 many-side rows and 8 targets;
- each target's search may visit at most 250,000 nodes.

A group target is one right row. Its allocations are left rows with the same block key.

| Shape | What it holds | Outcome it is built for |
|---|---|---|
| `ExactPairs` | One pair per instrument, with the same values on both sides. | Every row matches on P2. |
| `Unmatched` | Half the rows are pairs. The rest are left-only and right-only rows, each on its own instrument. | Half match on P2. The rest are `LeftOnly` and `RightOnly`. |
| `SmallAllocations` | One target per block, with 2 to 4 allocations and no TradeRef. | Every row matches on P3. |
| `SplittableBlocks` | 12 targets per block, each with 4 to 8 allocations that carry its own shared TradeRef. A full block is over both limits: 12 targets and at least 48 rows. Each component is within them. | Every row matches on P3. |
| `OversizedComponents` | One target per block, with 25 to 40 allocations and no TradeRef. | Every row is `SearchIncomplete` with code `BlockTooLarge`. Nothing is searched. |
| `DisjointTargets` | 32 targets per block (the width), each with 2 to 4 allocations that carry its own shared TradeRef. | Every row matches on P3. |
| `WildcardHeavy` | 4 targets per block (the width). Targets carry broker TradeRefs, which are wildcards, and allocations carry none. Each block is one component, so every target searches every allocation in its block. | Every row matches on P3. |
| `RepeatedIdentity` | Every row in a block carries the same shared TradeRef, so each block is one component. Used by `GroupConstructionBench` only. | With at most 8 targets and 24 allocations, every row matches. A wider block is `BlockTooLarge`. |

### Why the outcomes are certain

- Allocation quantities are whole cents in [1,000.00, 2,000.00). A target is the sum of two or more, so it never equals one allocation. P2 can never pair an allocation with a target.
- Net cash is quantity times price, rounded to cents, plus a fee of up to 49.99. Another subset with the same quantity almost never has the same cash. So each target's allocations are its only conserving subset.
- Every pair and every block has its own instrument, so rows from different blocks never share a key.

### How the outcomes are checked

- Each dataset carries the outcome it was built for, computed from its construction and the limits.
- The benchmark setup runs the matcher once and throws if the outcome differs. A dataset that drifts fails the run; it is never timed as something else.
- `BenchDatasetTests` runs in `dotnet test`, so it runs in CI. It checks every shape at 150 rows through the four-pass cascade, and the group shapes through the group pass alone. It also checks that the same seed gives the same rows.

## How to run

From `RecDiffy.NET/src/RecDiffy.Benchmarks`:

```
dotnet run -c Release -- --filter '*' --exporters json
```

To run one class, pass `--filter '*DatasetBench*'`. To write the Bencher report from the results:

```
dotnet run -c Release --no-build -- --bmf BenchmarkDotNet.Artifacts/results bencher.bmf.json
```

The scaled classes use the short job in `BenchScale`: one launch, one warmup and three iterations. `ExactDiffBench` uses the BenchmarkDotNet default job.

`benchmarks.yml` runs the same command on the COBBLER1 runner on every push to `dev` and `main`, and uploads to the [Bencher dashboard](https://bench.cobblerhill.net/perf/recdiffy). New classes need no change to the workflow, because `--filter '*'` picks them up. Bencher alerts are recorded and do not fail the job.

## One local run

### Machine and software

| | |
|---|---|
| CPU | Intel Core i9-12900K: 16 cores (8 performance, 8 efficiency), 24 threads, 3.2 GHz base |
| Memory | 64 GB |
| OS | Windows 10 Pro 22H2, build 19045.6466 |
| .NET | SDK 10.0.403, runtime 10.0.14, x64 RyuJIT AVX2, concurrent workstation GC |
| BenchmarkDotNet | 0.15.2, Release build |
| Code | This branch, based on `dev` at `03ab20b` |
| When | 10 November 2026, 23:28 to 23:33 UTC |
| Load | A shared developer machine. CPU load from other work averaged 71% while the run went, with a peak of 100%. |

Times are the mean and standard deviation of three iterations. MB is millions of bytes allocated per operation, and kB is thousands of bytes. A per-row figure divides by all the rows, which is 8,000 at 4,000 rows per side.

### Datasets through the four-pass cascade (`DatasetBench`)

If the work is linear, 4× the rows costs about 4×.

| Dataset | 1,000 rows, ms | 4,000 rows, ms | Time ratio | 1,000 rows, MB | 4,000 rows, MB | Allocation ratio | µs per row at 4,000 | kB per row at 4,000 | Outcome |
|---|---|---|---|---|---|---|---|---|---|
| `ExactPairs` | 37.5 ± 0.4 | 191.4 ± 2.6 | 5.1x | 47.0 | 200.2 | 4.3x | 23.9 | 25.0 | All matched (P2) |
| `Unmatched` | 28.8 ± 0.2 | 153.0 ± 2.8 | 5.3x | 39.0 | 163.2 | 4.2x | 19.1 | 20.4 | Half matched, half one-sided |
| `SmallAllocations` | 90.0 ± 14.4 | 1,067.1 ± 87.7 | 11.9x | 62.2 | 544.6 | 8.8x | 133.4 | 68.1 | All matched (P3) |
| `SplittableBlocks` | 96.6 ± 10.4 | 452.0 ± 28.6 | 4.7x | 70.1 | 343.7 | 4.9x | 56.5 | 43.0 | All matched (P3) |
| `OversizedComponents` | 13.0 ± 1.2 | 77.3 ± 12.5 | 6.0x | 20.0 | 82.4 | 4.1x | 9.7 | 10.3 | All `BlockTooLarge`, not searched |
| `DisjointTargets` | 190.5 ± 6.8 | 1,145.6 ± 81.2 | 6.0x | 168.0 | 856.1 | 5.1x | 143.2 | 107.0 | All matched (P3) |
| `WildcardHeavy` | 134.4 ± 20.5 | 964.0 ± 5.3 | 7.2x | 75.6 | 544.4 | 7.2x | 120.5 | 68.0 | All matched (P3) |

What this shows:

- **The pair shapes grow about linearly.** That is `ExactPairs` and `Unmatched`: 4.2x to 4.3x in allocations.
- **`SmallAllocations` and `WildcardHeavy` grow faster than their rows.** Their search counts stay linear: `SmallAllocations` runs 2,000 searches for 2,000 targets and never repeats one. The extra cost is #273: `GroupIndex.Drop` walks every ready proposal each time it evaluates a block, so the group pass is quadratic in group targets.
- **`DisjointTargets` costs the most per row.** At width 32 the group pass runs 32,872 searches for 2,000 targets, because each commit searches every other component in its block again (#41).
- **`OversizedComponents` costs the least per row, because nothing is searched.** That speed is the cost of refusing to search, not of reconciling. See [Limitations](#limitations).

### Group construction (`GroupConstructionBench`, #41)

All rows are at 4,000 rows per side. Build is `GroupIndex.Create`. Pass is the whole group-sum pass.

| Shape | Width | Build, ms | Build, µs per row | Build, kB per row | Pass, ms | Pass, µs per row | Pass, kB per row | Searches per target | Outcome |
|---|---|---|---|---|---|---|---|---|---|
| `DisjointTargets` | 8 | 90.0 ± 5.5 | 11.3 | 8.6 | 624.1 ± 18.4 | 78.0 | 47.0 | 4.5 | All matched |
| `DisjointTargets` | 64 | 50.6 ± 5.2 | 6.3 | 7.9 | 1,701.7 ± 305.0 | 212.7 | 175.8 | 32.3 | All matched |
| `WildcardHeavy` | 8 | 640.3 ± 3.8 | 80.0 | 14.1 | 2,495.7 ± 535.7 | 312.0 | 68.9 | 4.5 | All matched |
| `WildcardHeavy` | 64 | 12.1 ± 0.4 | 1.5 | 3.1 | 23.1 ± 1.5 | 2.9 | 3.7 | 0 | All `BlockTooLarge` |
| `RepeatedIdentity` | 8 | 762.2 ± 52.5 | 95.3 | 14.2 | 1,879.7 ± 28.8 | 235.0 | 69.5 | 4.5 | All matched |
| `RepeatedIdentity` | 64 | 15.1 ± 0.7 | 1.9 | 3.2 | 29.4 ± 0.6 | 3.7 | 3.8 | 0 | All `BlockTooLarge` |

Searches per target come from the matcher's own counters (`SubsetSearches` over targets) in a separate run of the same datasets.

What this shows:

- **For disjoint targets, building is cheap and does not grow with width; the pass does.** Build costs 11.3 µs per row at width 8 and 6.3 at width 64. The pass grows from 78 to 213 µs per row, and from 47 to 176 kB per row, because searches per target rise from 4.5 to 32.3. Disjoint components share no rows, so a commit in one cannot change another's search. Those repeat searches are the avoidable work #41 names.
- **Narrow glued blocks spend their time in the subset search.** At width 8, up to 8 targets each search up to 24 rows. Repeat searches inside a glued component are needed there, because a commit removes rows the other targets could draw.
- **Wide glued blocks are refused before any search.** They build in 1.5 to 1.9 µs per row, and every row ends `BlockTooLarge`.

### Other benchmarks

| Benchmark | Parameters | Mean | MB |
|---|---|---|---|
| `ExactDiffBench.ExactPair` | | 113.7 ± 0.8 ns | 0 |
| `CascadeScaleBench.Match` | Rows=1000 | 7.3 ± 0.1 ms | 13.8 |
| `CascadeScaleBench.Match` | Rows=4000 | 51.7 ± 0.3 ms | 59.5 |
| `ExecutionModeBench.Sync` | Rows=1000 | 28.2 ± 1.2 ms | 35.6 |
| `ExecutionModeBench.Sync` | Rows=4000 | 181.0 ± 21.5 ms | 144.8 |
| `ExecutionModeBench.Async` | Rows=1000 | 25.4 ± 0.7 ms | 35.4 |
| `ExecutionModeBench.Async` | Rows=4000 | 186.9 ± 54.6 ms | 144.7 |
| `ExecutionModeBench.Parallel` | Rows=1000, degree 1 / 2 / 4 / 8 | 27.0 / 25.4 / 25.6 / 24.4 ms | 35.4 to 35.6 |
| `ExecutionModeBench.Parallel` | Rows=4000, degree 1 / 2 / 4 / 8 | 189.8 / 228.7 / 215.5 / 175.9 ms | 144.8 to 149.7 |

At these sizes the degree of parallelism makes no difference that this run can see. The 4,000-row differences are inside the noise, which reaches ±59 ms.

## Proposed budget for group construction (#41)

This is a proposal. #41 is done when construction meets a documented budget, so the lead has to approve or change these numbers first.

The budget is measured with `GroupConstructionBench` at 4,000 rows per side (8,000 rows).

- **Allocations per row are the main gate.** They do not depend on the machine or its load.
- **Time per row applies to COBBLER1 only.** Read it from Bencher, not from a developer machine. The time ceilings below come from this loaded run, so check them against the first COBBLER1 numbers before you approve them.

| Case | Build, per row | Pass, per row | Today |
|---|---|---|---|
| `DisjointTargets`, widths 8 and 64 | ≤ 15 µs, ≤ 12 kB | ≤ 40 µs, ≤ 25 kB | Build meets it: at most 11.3 µs and 8.6 kB. The pass does not: up to 212.7 µs and 175.8 kB. |
| `WildcardHeavy` and `RepeatedIdentity`, width 64 (refused) | ≤ 5 µs, ≤ 5 kB | ≤ 10 µs, ≤ 6 kB | Meets it: Build at most 1.9 µs and 3.2 kB, pass at most 3.7 µs and 3.8 kB. |
| `WildcardHeavy` and `RepeatedIdentity`, width 8 (searched) | ≤ 150 µs, ≤ 18 kB | ≤ 400 µs, ≤ 85 kB | Meets it: Build at most 95.3 µs and 14.2 kB, pass at most 312.0 µs and 69.5 kB. |

Two scaling rules go with the table:

1. **Rows.** For every `DatasetBench` dataset, 4,000 rows per side allocates at most 5x and takes at most 6x what 1,000 rows does. Today `SmallAllocations` (8.8x, 11.9x) and `WildcardHeavy` (7.2x, 7.2x) fail. That is #273.
2. **Width.** For `DisjointTargets`, the pass per row at width 64 costs at most 1.5x what it costs at width 8. Today it is 2.7x in time and 3.7x in allocations, because of the repeat searches.

How the numbers were chosen:

- The construction ceilings are today's figures rounded up, with at least 25% headroom. They hold now and would catch a regression.
- The disjoint pass ceilings assume a pass that searches each disjoint component once, as Build does. Such a pass costs Build plus the commits, about three times Build.
- The searched glued pass ceilings are today's figures with at least 20% headroom, because the repeat searches there are needed.

## Limitations

- **One machine, one run, three iterations.** Some rows have a standard deviation near 30% of the mean. Small differences mean nothing.
- **A loaded machine.** Other work kept the CPU at 71% on average during the run. Times are noisier than they would be on an idle machine. Allocations are not affected.
- **A hybrid CPU on Windows 10.** Windows 10 does not place threads by core type the way Windows 11 does, so an iteration can land on a slower efficiency core.
- **Matcher only.** The dataset and group benchmarks leave out intake, parsing, normalization, field comparison and result building. End-to-end numbers exist only for exact pairs, in `ExecutionModeBench`.
- **Synthetic data.** One counterparty, one trade date, one side, uniform amounts, and every block on its own instrument. Real files have skewed keys, text to parse and more fields. No client data is used.
- **Small sizes.** CI runs 1,000 and 4,000 rows per side so the job stays short. Larger sizes are not measured here. Because of #273, the group pass on `SmallAllocations` at 8,000 rows per side took 8 to 20 seconds per run in a local probe.
- **Completed work versus bounded work.** A time measures a completed reconciliation only when no row ends `SearchIncomplete`.
  - `OversizedComponents` and the width-64 glued blocks end every row `BlockTooLarge`. The engine refused to search them, so their speed is the cost of refusing, not of reconciling.
  - A target whose search reaches the 250,000-node budget ends `BudgetExhausted`. Its time is the cost of stopping, and says nothing about how long a full search would take. No dataset here reaches the node budget.
- **Default limits only.** Raising `MaxBlockItems` or `MaxNodesPerTarget` changes both the outcome and the cost.
- **No comparisons.** These numbers are not compared with other tools, with earlier RecDiffy versions, or across machines.
- **Missing throughput on Bencher.** Benchmarks with more than one parameter get no throughput measure (#265). That includes `DatasetBench`, `GroupConstructionBench` and `ExecutionModeBench.Parallel`. Their latency and allocations still upload.
- **No split by stage.** Time in intake, construction, search, comparison and output is not separated. That is #65.

## Reproducing this run

1. Run the command in [How to run](#how-to-run) with `--filter '*'`. The whole run took about six minutes here.
2. Compare the outcome column, not only the times. The datasets do not depend on the machine: the same seed gives the same rows and the same outcome everywhere, and the setup check fails if they differ.
3. Compare allocations before times. Allocations depend on the code and the runtime, not on the machine's speed or load, so on .NET 10.0.14 they should be close to this note. Times will not.
