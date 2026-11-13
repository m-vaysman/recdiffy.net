# Benchmarks

This note says what the RecDiffy benchmarks measure, how to run them, what the COBBLER1 run of 13 November 2026 gave at commit `2d16975`, and what the numbers do not show. It covers #64 (datasets), #41 (group construction), #66 (this note), and #377 (the per-stage numbers and this refresh).

It has two parts. **For users** answers "what should I expect for a few thousand rows", with the headline numbers and how to read them. **For developers** is the method: the classes, the datasets, how to run them, the full tables, the proposed #41 budget and the limitations.

The numbers come from one machine, COBBLER1. They are not a comparison with any other tool, they are not a promise about your hardware, and the previous note's times do not compare: that run was on a different CPU and under load. Allocations are the number to compare across notes; times are not.

Large sizes are not here. They live in [the capacity harness](../capacity/README.md) (#377), which runs 10,000 to 1,000,000 rows per side and records where a run stops fitting.

## For users: what to expect

### The short version

- 1,000 rows per side of exact pairs, whole engine: about 0.12 s and 42 MB.
- 4,000 rows per side: about 0.39 s and 165 MB. Four times the rows costs about 4.2 times the memory.
- Matching and normalization dominate; field comparison is almost free.
- The shape of the data costs more than the size alone: refused oversized blocks are the cheapest, disjoint group targets the most expensive per row.
- These numbers come from an old server CPU (two 2012-era Xeon sockets). A modern desktop is faster. The MB figures are the ones that travel.

### End to end

`ExecutionModeBench` runs the whole engine (`Reconciler.Run` and `RunAsync`) on exact pairs, so it includes intake, parsing, normalization, matching, field comparison and result building. Rows are per side; the allocation is for both sides together.

| Rows per side | Call | Mean | Allocated |
|---|---|---|---|
| 1,000 | `Run` | 119.1 ms | 41.9 MB |
| 1,000 | `RunAsync` | 113.8 ms | 40.7 MB |
| 4,000 | `Run` | 391.2 ms | 165.1 MB |
| 4,000 | `RunAsync` | 417.7 ms | 165.5 MB |

`RunAsync` is not faster; it is the same work behind an async wrapper. `MaxDegreeOfParallelism` parallelizes field comparison only, and at these sizes the difference is inside the noise: degrees 1, 2, 4 and 8 gave 397 to 468 ms at 4,000 rows.

### Where the time goes

`PipelineStageBench` measures each stage on its own, on the same generated trades. It is not a split of one run: the stages are measured separately, so their times show relative cost, not parts of a whole. At 4,000 rows per side:

| Stage | Mean | Allocated | kB per row |
|---|---|---|---|
| Normalize (intake + rules) | 211.1 ms | 140.1 MB | 17.9 |
| Consolidate | 15.4 ms | 8.8 MB | 1.1 |
| Packages | 30.2 ms | 16.7 MB | 2.1 |
| Match (three passes) | 400.8 ms | 186.8 MB | 23.9 |
| Diff (field comparison) | 7.2 ms | 0.6 MB | 0.1 |
| Build (result model) | 153.1 ms | 35.8 MB | 4.6 |

Normalization and matching are where the bytes go. Field comparison is almost free. If you are tuning a profile, the number of fields you parse and the number of passes you route rows through matter more than the number of diff fields.

### What the data shape costs

At 4,000 rows per side, `DatasetBench` runs four passes over one of seven synthetic shapes. Per row (8,000 rows in all):

| Shape | µs per row | kB per row | What happens |
|---|---|---|---|
| `OversizedComponents` | 25.9 | 9.8 | Refused before search (`BlockTooLarge`) |
| `SmallAllocations` | 47.2 | 21.3 | Matched on the group pass |
| `Unmatched` | 49.8 | 19.7 | Half matched, half one-sided |
| `ExactPairs` | 49.9 | 24.7 | Matched on P2 |
| `SplittableBlocks` | 85.0 | 44.4 | Matched on the group pass, blocks split |
| `WildcardHeavy` | 86.7 | 40.2 | Matched, wildcard targets |
| `DisjointTargets` | 214.4 | 105.5 | Matched; every commit re-searches other components |

The refused case is cheap because it stops, not because it reconciles. The disjoint case costs the most because each commit searches the other components in its block again (#41).

### How to read these numbers

- **Allocations first.** They depend on the code and the runtime, not on the machine's speed or load, so they are what to compare between this note and the next one.
- **Times are COBBLER1 times.** They will not transfer to your machine, and the standard deviations in the tables are large: some rows vary by 30% or more. Small differences mean nothing.
- **Refused work is not reconciliation.** `OversizedComponents` and the width-64 glued blocks end every row `BlockTooLarge`. Their speed is the cost of refusing to search.
- **Synthetic data.** One counterparty, one trade date, one side, uniform amounts, every block on its own instrument. Real files have skewed keys, text to parse and more fields.
- **Matcher-only tables.** The dataset and group tables leave out intake and result building. Only the end-to-end table includes them.
- **Large sizes are elsewhere.** See [the capacity harness](../capacity/README.md) for 10,000+ rows per side and the failure boundary.

### What changed since the previous note

- **The quadratic group pass is fixed (#273).** Every dataset now allocates 4.0 to 4.2 times as much for four times the rows. The previous note had 8.8 times for `SmallAllocations` and 7.2 times for `WildcardHeavy`. The fix is in #323/#324.
- **The linear group builder allocates more on glued width-8 blocks.** Per row, build went from 14.1 to 48.7 kB and the pass from 68.9 to 121.6 kB. The disjoint case improved: build 8.6 to 7.0 kB, pass 47.0 to 26.5 kB. That is the trade #323 made, and #41 owns the remaining decision.
- **Do not compare times with the previous note.** The previous run was on an i9-12900K under 71% background load; this one is two Xeon E5-2680 v2 sockets. Only allocations are comparable across the two notes.

## For developers: method and numbers

### Classes

The benchmarks are in `RecDiffy.NET/src/RecDiffy.Benchmarks` and use BenchmarkDotNet. Every class has `[MemoryDiagnoser]`, so each result has a mean time, the bytes allocated per operation, and GC counts.

| Class | What one operation does | Sizes |
|---|---|---|
| `ExactDiffBench` | `FieldDiffer.Compare` on one exact pair with three decimal fields. | One pair |
| `CascadeScaleBench` | `CascadeMatcher.Run` with one natural-key pass on one-to-one pairs. | 1,000, 4,000 and 100,000 rows per side |
| `ExecutionModeBench` | `Reconciler.Run` and `RunAsync` end to end: intake, normalization, matching and results. `Parallel(degree)` sets `MaxDegreeOfParallelism`, which parallelizes field comparison, not matching. | 1,000 and 4,000 rows per side |
| `DatasetBench` (#64) | `CascadeMatcher.Run` with four passes on one of seven dataset shapes. | 1,000 and 4,000 rows per side |
| `GroupConstructionBench` (#41) | `Build`: `GroupIndex.Create` alone. `Match`: `CascadeMatcher.Run` with the group-sum pass alone. | 4,000 rows per side, widths 8 and 64 |
| `PipelineStageBench` (#377 P2) | One pipeline stage per operation: `Intake.Normalize`, `Consolidation.Run` over NEW/CORR chains, `Packages.Pack` over two-leg packages, `CascadeMatcher.Run` with three passes, `FieldDiffer.Compare` over the committed pairs, and `ResultBuilder.Build`. Every stage runs on the same generated trades, so the per-stage costs are comparable. | 1,000 and 4,000 rows per side |

`DatasetBench`, `GroupConstructionBench` and `CascadeScaleBench` time the matcher only. Their rows start as consolidated rows and stop at the match report. Intake, normalization, field comparison and result building are not in those numbers. `ExecutionModeBench` is the only end-to-end benchmark, and it uses exact pairs only.

`GroupConstructionBench.Build` covers block placement, component construction, and the first search of each component. `Match` adds the commits. After each commit the pass evaluates the committed rows' block again.

### The datasets (#64)

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

### How to run

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

### The run

| | |
|---|---|
| CPU | Intel Xeon E5-2680 v2: two sockets, 20 physical cores, 40 logical, 2.80 GHz |
| Memory | 64 GB |
| OS | Windows 10 Pro 22H2, build 19045.6466 |
| .NET | SDK 10.0.403, runtime 10.0.14, x64 RyuJIT AVX, concurrent workstation GC |
| BenchmarkDotNet | 0.15.2, Release build |
| Code | `dev` at `2d16975`, which includes #323/#324 |
| When | 13 November 2026, 16:26 to 16:46 UTC |
| Job | `benchmarks.yml` on the `cobbler1` runner: 54 benchmarks, about 9 minutes of measurement |
| Load | Not recorded. The large standard deviations below show the box was not quiet. |

Times are the mean and standard deviation of three iterations. MB is millions of bytes allocated per operation, and kB is thousands of bytes. A per-row figure divides by all the rows, which is 8,000 at 4,000 rows per side. The MB column is the BenchmarkDotNet "Allocated" column: managed allocations per operation, not resident memory.

### Datasets through the four-pass cascade (`DatasetBench`)

If the work is linear, 4× the rows costs about 4×.

| Dataset | 1,000 rows, ms | 4,000 rows, ms | Time ratio | 1,000 rows, MB | 4,000 rows, MB | Allocation ratio | µs per row at 4,000 | kB per row at 4,000 | Outcome |
|---|---|---|---|---|---|---|---|---|---|
| `ExactPairs` | 123.9 ± 32.9 | 398.9 ± 34.7 | 3.2x | 45.5 | 193.2 | 4.2x | 49.9 | 24.7 | All matched (P2) |
| `Unmatched` | 134.5 ± 32.0 | 398.2 ± 63.7 | 3.0x | 36.9 | 154.3 | 4.2x | 49.8 | 19.7 | Half matched, half one-sided |
| `SmallAllocations` | 180.1 ± 27.4 | 377.5 ± 77.5 | 2.1x | 39.7 | 166.4 | 4.2x | 47.2 | 21.3 | All matched (P3) |
| `SplittableBlocks` | 271.4 ± 61.3 | 680.0 ± 48.4 | 2.5x | 82.2 | 347.2 | 4.2x | 85.0 | 44.4 | All matched (P3) |
| `OversizedComponents` | 28.0 ± 0.9 | 207.4 ± 26.2 | 7.4x | 19.2 | 77.0 | 4.0x | 25.9 | 9.8 | All `BlockTooLarge`, not searched |
| `DisjointTargets` | 418.0 ± 28.1 | 1,715.1 ± 113.5 | 4.1x | 195.5 | 823.9 | 4.2x | 214.4 | 105.5 | All matched (P3) |
| `WildcardHeavy` | 261.6 ± 28.0 | 693.4 ± 17.3 | 2.7x | 75.9 | 314.2 | 4.1x | 86.7 | 40.2 | All matched (P3) |

What this shows:

- **Allocations are linear in rows for every shape.** 4.0x to 4.2x for 4x the rows. That is the #273 fix; the previous note had 8.8x (`SmallAllocations`) and 7.2x (`WildcardHeavy`), and the group pass was quadratic in group targets.
- **Time ratios are noisy.** They range from 2.1x to 4.1x, except `OversizedComponents` at 7.4x: 28 ms to 207 ms, with a ±26 ms standard deviation on the 4,000-row mean. That case is refused work and its absolute cost is the smallest, so treat the ratio as noise.
- **`DisjointTargets` costs the most per row.** Its commits search the other components in the block again; that repeat search is the work #41 names. It is still linear in rows here (4.1x time, 4.2x allocation).
- **`OversizedComponents` costs the least per row, because nothing is searched.** That speed is the cost of refusing to search, not of reconciling. See [Limitations](#limitations).

### Per-stage numbers (`PipelineStageBench`, #377 P2)

Each row is one stage measured on the same generated trades, not a split of a single run.

| Stage | 1,000 rows, ms | 1,000 rows, MB | 4,000 rows, ms | 4,000 rows, MB | kB per row at 4,000 |
|---|---|---|---|---|---|
| `Normalize` | 36.9 | 35.2 | 211.1 | 140.1 | 17.9 |
| `Consolidate` | 1.8 | 2.4 | 15.4 | 8.8 | 1.1 |
| `Packages` | 4.8 | 4.2 | 30.2 | 16.7 | 2.1 |
| `Match` | 106.8 | 44.2 | 400.8 | 186.8 | 23.9 |
| `Diff` | 1.2 | 0.2 | 7.2 | 0.6 | 0.1 |
| `Build` | 15.4 | 9.4 | 153.1 | 35.8 | 4.6 |

What this shows:

- **Normalization allocates 18 kB per input row** and matching allocates 24 kB per input row at 4,000 rows per side. Those two are the profile's cost.
- **Consolidation, packaging and diffing are small.** Diffing allocates 0.1 kB per row: field comparison is not where a run spends its memory.
- **The allocation per row is flat between 1,000 and 4,000 rows** (normalize 35.2 MB for 2,000 rows and 140.1 MB for 8,000), so there is no size-dependent blow-up at these sizes.

### Group construction (`GroupConstructionBench`, #41)

All rows are at 4,000 rows per side. Build is `GroupIndex.Create`. Pass is the whole group-sum pass. Per-row figures divide by 8,000 rows.

| Shape | Width | Build, ms | Build, µs per row | Build, kB per row | Pass, ms | Pass, µs per row | Pass, kB per row | Outcome |
|---|---|---|---|---|---|---|---|---|
| `DisjointTargets` | 8 | 85.4 | 10.7 | 7.0 | 370.3 | 46.3 | 26.5 | All matched |
| `DisjointTargets` | 64 | 98.3 | 12.3 | 8.9 | 2,245.0 | 280.6 | 197.4 | All matched |
| `WildcardHeavy` | 8 | 1,926.7 | 240.8 | 48.7 | 4,048.3 | 506.0 | 121.6 | All matched |
| `WildcardHeavy` | 64 | 30.3 | 3.8 | 3.1 | 50.1 | 6.3 | 3.6 | All `BlockTooLarge` |
| `RepeatedIdentity` | 8 | 1,866.5 | 233.3 | 48.8 | 4,143.3 | 517.9 | 122.1 | All matched |
| `RepeatedIdentity` | 64 | 33.3 | 4.2 | 3.2 | 77.7 | 9.7 | 3.7 | All `BlockTooLarge` |

What this shows:

- **For disjoint targets, building is cheap and flat in width; the pass is not.** Build costs 10.7 µs and 7.0 kB per row at width 8, and 12.3 µs and 8.9 kB at width 64. The pass grows from 46.3 to 280.6 µs per row, and from 26.5 to 197.4 kB, because each commit searches the other components in the block again. Disjoint components share no rows, so those repeat searches cannot find anything new. That is the avoidable work #41 names.
- **Narrow glued blocks spend their time in the subset search.** At width 8, up to 8 targets each search up to 24 rows, and a commit removes rows the other targets could draw, so the repeat searches are needed there.
- **The linear builder (#323) allocates more on glued width-8 blocks than the previous note.** Build went from 14.1 to 48.7 kB per row and the pass from 68.9 to 121.6. On the disjoint shape it allocates less: build 8.6 to 7.0, pass 47.0 to 26.5. The trade is linear time in exchange for more allocation on the glued path, and #41 owns the decision to keep or trim it.
- **Wide glued blocks are refused before any search.** They build in 3.8 to 4.2 µs per row and 3.1 to 3.2 kB, and every row ends `BlockTooLarge`.

### Other benchmarks

| Benchmark | Parameters | Mean | MB |
|---|---|---|---|
| `ExactDiffBench.ExactPair` | | 329.2 ± 5.2 ns | 0 |
| `CascadeScaleBench.Match` | Rows=1000 | 20.16 ± 0.24 ms | 13.18 |
| `CascadeScaleBench.Match` | Rows=4000 | 208.29 ± 17.60 ms | 57.32 |
| `CascadeScaleBench.Match` | Rows=100000 | 3,898.06 ± 120.56 ms | 1,686.92 |
| `ExecutionModeBench.Sync` | Rows=1000 | 119.1 ± 36.9 ms | 41.88 |
| `ExecutionModeBench.Sync` | Rows=4000 | 391.2 ± 101.1 ms | 165.14 |
| `ExecutionModeBench.Async` | Rows=1000 | 113.8 ± 15.1 ms | 40.65 |
| `ExecutionModeBench.Async` | Rows=4000 | 417.7 ± 41.2 ms | 165.48 |
| `ExecutionModeBench.Parallel` | Rows=1000, degree 1 / 2 / 4 / 8 | 128.2 / 125.4 / 118.9 / 124.0 ms | 40.89 to 41.97 |
| `ExecutionModeBench.Parallel` | Rows=4000, degree 1 / 2 / 4 / 8 | 406.5 / 446.1 / 467.7 / 397.4 ms | 164.63 to 165.70 |

At these sizes the degree of parallelism makes no difference that this run can see. The 4,000-row differences are inside the noise, which reaches ±87 ms.

### Proposed budget for group construction (#41)

This is a proposal. #41 is done when construction meets a documented budget, so the lead has to approve or change these numbers first.

The budget is measured with `GroupConstructionBench` at 4,000 rows per side (8,000 rows).

- **Allocations per row are the main gate.** They do not depend on the machine or its load.
- **Time per row applies to COBBLER1 only.** The ceilings below were written against the previous note's loaded developer run; this run is the first COBBLER1 measurement, so check them against the table before approving them.

| Case | Build, per row | Pass, per row | Today |
|---|---|---|---|
| `DisjointTargets`, widths 8 and 64 | ≤ 15 µs, ≤ 12 kB | ≤ 40 µs, ≤ 25 kB | Build meets it: 10.7 µs and 7.0 kB at width 8, 12.3 µs and 8.9 kB at width 64. The pass does not: 46.3 µs and 26.5 kB at width 8, 280.6 µs and 197.4 kB at width 64. |
| `WildcardHeavy` and `RepeatedIdentity`, width 64 (refused) | ≤ 5 µs, ≤ 5 kB | ≤ 10 µs, ≤ 6 kB | Meets it: Build 3.8 to 4.2 µs and 3.1 to 3.2 kB, pass 6.3 to 9.7 µs and 3.6 to 3.7 kB. |
| `WildcardHeavy` and `RepeatedIdentity`, width 8 (searched) | ≤ 150 µs, ≤ 18 kB | ≤ 400 µs, ≤ 85 kB | Fails it: Build 233.3 to 240.8 µs and 48.7 to 48.8 kB, pass 506.0 to 517.9 µs and 121.6 to 122.1 kB. |

Two scaling rules go with the table:

1. **Rows.** For every `DatasetBench` dataset, 4,000 rows per side allocates at most 5x and takes at most 6x what 1,000 rows does. Today every allocation ratio passes, at 4.0x to 4.2x. Time ratios pass except `OversizedComponents` at 7.4x, which is a ±26 ms mean on the smallest absolute cost and is noise, not a regression.
2. **Width.** For `DisjointTargets`, the pass per row at width 64 costs at most 1.5x what it costs at width 8. Today it is 6.1x in time and 7.4x in allocations, because of the repeat searches. The previous note had 2.7x and 3.7x.

How the numbers were chosen:

- The construction ceilings are the previous note's figures rounded up, with at least 25% headroom. They still hold for the disjoint and refused cases. The searched glued cases now exceed them: the linear builder allocates 48.7 kB per row at build and 121.6 at pass, against ceilings of 18 and 85. That is the trade #323 made, not a new defect, and it is #41's decision whether the builder is trimmed or the ceilings move. This note does not move them.
- The disjoint pass ceilings assume a pass that searches each disjoint component once, as Build does. Such a pass costs Build plus the commits, about three times Build. Today's pass is 4.3x Build at width 8 and 22.8x at width 64.
- The searched glued pass ceilings are the previous note's figures with at least 20% headroom, because the repeat searches there are needed.

### Limitations

- **One machine, one run, three iterations.** Some rows have a standard deviation near 30% of the mean, and the 4,000-row `OversizedComponents` mean varies by ±26 ms. Small differences mean nothing.
- **A machine that was not quiet.** The job does not record background load, and the standard deviations show it was not idle. Allocations are not affected.
- **Times are not comparable with the previous note.** The previous run used an i9-12900K under 71% load; this one uses two Xeon E5-2680 v2 sockets. Compare allocations, not times.
- **Matcher only.** The dataset and group benchmarks leave out intake, parsing, normalization, field comparison and result building. End-to-end numbers exist only for exact pairs, in `ExecutionModeBench`.
- **Synthetic data.** One counterparty, one trade date, one side, uniform amounts, and every block on its own instrument. Real files have skewed keys, text to parse and more fields. No client data is used.
- **Small sizes.** CI runs 1,000 and 4,000 rows per side so the job stays short. Larger sizes are measured by the capacity harness, not here.
- **Completed work versus bounded work.** A time measures a completed reconciliation only when no row ends `SearchIncomplete`.
  - `OversizedComponents` and the width-64 glued blocks end every row `BlockTooLarge`. The engine refused to search them, so their speed is the cost of refusing, not of reconciling.
  - A target whose search reaches the 250,000-node budget ends `BudgetExhausted`. Its time is the cost of stopping, and says nothing about how long a full search would take. No dataset here reaches the node budget.
- **Default limits only.** Raising `MaxBlockItems` or `MaxNodesPerTarget` changes both the outcome and the cost.
- **No comparisons.** These numbers are not compared with other tools, with earlier RecDiffy versions, or across machines.
- **Throughput on Bencher.** A benchmark with a `Rows` parameter gets a throughput measure, and the converter reads `Rows` out of a multi-parameter name such as `Rows=1000&Stage=Normalize` (#265). Latency, allocated bytes and GC counts are uploaded for every benchmark. This run's upload logged a measure-creation conflict on `gen2-collections`, so the dashboard may not carry this exact run; the tables above come from the job log.

### Reproducing this run

1. Run the command in [How to run](#how-to-run) with `--filter '*'`. There are 54 benchmarks and the measurement took about nine minutes on COBBLER1.
2. Compare the outcome column, not only the times. The datasets do not depend on the machine: the same seed gives the same rows and the same outcome everywhere, and the setup check fails if they differ.
3. Compare allocations before times. Allocations depend on the code and the runtime, not on the machine's speed or load, so on .NET 10.0.14 they should be close to this note. Times will not.
