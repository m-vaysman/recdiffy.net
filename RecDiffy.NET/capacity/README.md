# The capacity harness

Phases 1, 3, 4 and 5 of the capacity and performance work in [#377](https://github.com/m-vaysman/recdiffy.net/issues/377):
a console harness that runs the reconciliation engine through its public API, records what it cost, and writes a run
folder with `results.json`, `report.md` and a static `report.html`. It has two sweeps: sizes with the failure boundary
(phases 1 and 4) and concurrency (phase 3), and it runs on `net10.0` and `net8.0`, JIT, ReadyToRun or Native AOT
(phase 5).

It is **outside `RecDiffy.slnx`**. No workflow builds or runs it, `ci.yml` never sees it, and it is `IsPackable=false`,
so it adds nothing to the packed packages. It lives in this repository so it always measures the engine at the commit
that contains it.

## Run it

From the repository root, with the SDK that `RecDiffy.NET/global.json` pins:

```
dotnet run --project RecDiffy.NET/capacity/RecDiffy.Capacity -- --out G:\RecDiffyCapacity\runs\2026-11-12_2238_abc1234
```

The concurrency sweep:

```
dotnet run --project RecDiffy.NET/capacity/RecDiffy.Capacity -- --sweep concurrency --levels auto --concurrency-rows 10000
```

Without `--out` the run folder is `./capacity-runs/<yyyy-MM-dd_HHmm>_<short-sha>/` under the current directory. That
folder is ignored by git; on the capacity host (COBBLER1) put it on `G:`.

| Option | Meaning | Default |
|---|---|---|
| `--out <folder>` | Run folder | `./capacity-runs/<run-id>` |
| `--sweep <sizes\|concurrency>` | Which sweep to run | `sizes` |
| `--sizes <a,b>` | Sizes sweep: rows per side, ascending | `10000,100000` |
| `--levels <a,b\|auto>` | Concurrency sweep: parallel runs per batch | `auto` (1, 2, 4, 8, cores, 2× cores) |
| `--concurrency-rows <n>` | Concurrency sweep: rows per side in each run | `10000` |
| `--seed <n>` | Dataset seed | `377` |
| `--repeats <n>` | Measured runs per size, or batches per concurrency level | `1` |
| `--break-rate <0..1>` | Share of matched pairs built to break | `0.1` |
| `--unmatched-rate <0..1>` | Share of rows built with no counterparty row | `0.05` |
| `--excel <one\|none>` | Sizes sweep: write the bounded Excel report | `one` |
| `--case-timeout-seconds <n>` | Sizes sweep: cancel a case after n seconds | `0` (none) |
| `--commit <sha>` / `--ref <name>` | Record these instead of asking git | read from git |

`--help` prints the same table.

## What a run folder holds

| File | What it is |
|---|---|
| `results.json` | The source of truth: run, source, host, every case and sample, and the numbers the page shows. |
| `report.md` | The Markdown summary, rendered from `results.json`. This is what the run script posts to #377. |
| `report.html` | One self-contained page, inline CSS, no scripts and no network calls, rendered from `results.json`. |
| `excel/` | Sizes sweep only: the one Excel report each case writes, so the output stage has a real file behind it. |

Both views are rendered from the file on disk, not from the numbers in memory, so the page cannot disagree with the
JSON it came from. Every number on the page is in the JSON, including bar widths, stage shares, throughput shares and
scaling verdicts.

## The sizes sweep (phase 1)

A discarded warmup run of the smallest size runs first, so JIT and the first Excel write are not charged to the first
measured case. Then, for each size:

1. Builds a seeded synthetic workload of the requested size, as the text a CSV or Excel reader would hand the engine.
   Rows stream lazily out of the generator and are materialized by the engine, so the dataset is not held twice.
2. Runs `Reconciler.Run` over it with the profile in `Profile/CapacityProfile.cs`: a natural-key pass, a group-sum pass
   and a reduced-key pass.
3. Writes one Excel report (`SideBySideCompare`), so the output stage is measured but bounded.
4. Compares the engine's summary with the outcome the dataset was built to produce. A mismatch fails the run: a fast
   wrong answer is not a measurement.

Each case reports wall time, engine and Excel time, total rows per second, p50/p95/p99 over the measured runs, peak
managed heap and working set sampled during the run, allocated bytes, GC pause and collection counts.

## The concurrency sweep (phase 3)

One process runs N reconciliations at the same time over the same dataset, for every combination of:

- **fan-out style:** dedicated threads, `Task.Run` fanned out from the default thread pool, and `Parallel.For`;
- **profile sharing:** one `CompiledProfile` shared by every run, and one compiled profile per run (compiled before the
  batch, so compiling is not measured);
- **N:** `--levels`, default 1, 2, 4, 8, the logical processor count and twice that.

Every batch starts its runs together and records wall time, runs per minute, rows per second, scaling efficiency
against the same style and sharing at N = 1, cores used (process CPU time ÷ batch wall time), allocations per second,
GC pause, gen0/1/2 counts, peak managed heap and working set, peak thread-pool threads and queue, peak OS threads and
lock contention. Per-run latency gives p50/p95/p99.

**Every run's result is compared field by field with a single-threaded baseline** — the summary, every entry with its
diffs and both projections, and every row with its disposition and diagnostics, hashed into one fingerprint. A single
difference fails the run. That is the measured thread-safety statement the issue asks for, at the scale of the sweep.

Rows per run are fixed, so total work grows with N; the curve is throughput, not batch time. One discarded batch per
style and sharing warms JIT and thread start-up. A first-batch penalty can still make one combination's N = 1
throughput lower than another's, so compare efficiency within a combination, not across.

## The failure boundary (phase 4)

The sizes sweep is also the failure probe: any row count can be passed, and each case ends in exactly one recorded
outcome rather than a hang or a silent exit.

| Outcome | Meaning |
|---|---|
| `Completed` | The run finished and matched the outcome the dataset was built to produce. |
| `CompletedWithSearchIncomplete` | Finished, but the search limits refused rows (`BlockTooLarge` or `BudgetExhausted`). The report shows rows blocked next to rows searched. |
| `OutOfMemory` | A managed allocation failed. The report records the stage (`engine` or `excel`), the time to failure, and the sampled peak heap. |
| `Cancelled` | Only when `--case-timeout-seconds` fires. |

The report states the largest size that completed, the first size that failed with its stage and time, and per case:
rows searched and blocked, `AggregateOverflow` diagnostics, bytes of peak heap per input row, and the retained heap
after the result is dropped and two full blocking collections run.

A managed heap cap is a property of the process, not a switch the harness can flip. Set it in the environment and the
run records it in the host header:

```
$env:DOTNET_GCHeapHardLimit = '0x400000000'   # 16 GB; also 0x200000000 (8 GB) and 0x800000000 (32 GB)
dotnet run --project RecDiffy.NET/capacity/RecDiffy.Capacity -- --sizes 100000,1000000 --excel none
```

Use `--excel none` from 1M rows: the Excel writer holds a second copy of the data, and the issue reports it as its own
stage. The 10M probe is the same command with `--sizes 10000000`; on a 64 GB host it is expected to end `OutOfMemory`,
which is the answer, not a harness failure.

An OS kill cannot be caught in the process: if the host kills the run, no report is written. The run script records
that case from the job (phase 6), which is why every capacity run is started by the script rather than by hand.

The per-stage search counters (`Components`, `SubsetSearches`, `DirtyResearches`, `IndexBuilds`) live on the matcher's
internal `MatchCounters`, which a package consumer cannot see. The harness reports rows searched and blocked from the
public result instead. Reading the counters needs an `InternalsVisibleTo` line in `RecDiffy.csproj`, a decision for the
lead, not taken here.

## Environments (phase 5)

The harness targets **`net10.0`** (primary, including Native AOT) and **`net8.0`** (spot-check). On `net8.0` the
harness references `System.Text.Json`; that package is in the harness only, never in a shipping package.

```
dotnet run --project RecDiffy.NET/capacity/RecDiffy.Capacity -f net8.0 -- --sizes 1000
```

The reports serialize through a source-generated `JsonSerializerContext`, so they work under Native AOT, where
reflection-based `System.Text.Json` is off. Publish and run:

```
# ReadyToRun: JIT-capable, precompiled
dotnet publish RecDiffy.NET/capacity/RecDiffy.Capacity -c Release -f net10.0 -r win-x64 --self-contained true -p:PublishReadyToRun=true -o G:\RecDiffyCapacity\publish\r2r
G:\RecDiffyCapacity\publish\r2r\RecDiffy.Capacity.exe --out G:\RecDiffyCapacity\runs\<id>

# Native AOT: no JIT
dotnet publish RecDiffy.NET/capacity/RecDiffy.Capacity -c Release -f net10.0 -r win-x64 -p:PublishAot=true -o G:\RecDiffyCapacity\publish\aot
G:\RecDiffyCapacity\publish\aot\RecDiffy.Capacity.exe --out G:\RecDiffyCapacity\runs\<id>
```

The host section records which one ran: **Dynamic code** is `no, Native AOT` under AOT and `yes, JIT` otherwise, next
to the runtime, SDK, GC mode and GC heap limit.

**GC modes.** `GCSettings` is process-wide, so a mode is chosen when the process starts:

```
$env:DOTNET_gcServer = '1'   # server GC; unset or 0 for workstation
dotnet run --project RecDiffy.NET/capacity/RecDiffy.Capacity -- --sizes 10000 --out G:\RecDiffyCapacity\runs\<id>
```

The report records `server` or `workstation`; run the sweep once per mode and compare the two reports.

**Cold and warm.** A cold run is a fresh process: one measured run, no warmup (`--repeats 1`, and ignore the discarded
warmup by reading the second run of a two-run process). A warm run is the median of `--repeats` after the discarded
warmup, which is the default shape. `--repeats 5` is the issue's default for a case under a minute.

Every environment run that produces capacity numbers still goes through the COBBLER1 run script (phase 6): this
machine is Windows like COBBLER1, but its numbers are not capacity evidence.

## What the harness does not measure yet

- **Stages inside `Reconciler.Run`.** Intake, normalization, consolidation, matching, diff and result building are one
  *engine* stage, because the public API exposes no split and the capacity project does not take `InternalsVisibleTo`.
  The per-stage benchmarks are phase 2; the large-size split is phase 4.
- **Concurrency leftovers.** Culture isolation across threads, a second host (N processes with one run each), and a
  `dotnet-trace` race-detection pass are phase 3's remaining items; the sweep and the field-by-field baseline check are
  in.
- **The size axis matrix.** Cardinality, record width, duplicate ratios, group shapes and raised limits, each swept on
  its own and then in combination, are the rest of phase 4; the failure boundary above is the size axis only.
- **Heap caps and GC modes.** The caps are set by the launcher (`DOTNET_GCHeapHardLimit`) and recorded; comparing server
  and workstation GC is phase 5, and needs one process per mode.
- **Environment numbers.** ReadyToRun, Native AOT, `net8.0` and both GC modes can be run (see above), but the reports
  that answer capacity questions must come from COBBLER1 through the run script (phase 6), because one host cannot
  compare machines.
- **The run script and the GitHub comment.** Phase 6.
- **Comparison with the previous run.** The run script will hand over the previous run's `results.json`; the harness
  has no history yet.
- **The production sizing estimate.** Little's law needs the plateau from the concurrency sweep and the latency and
  memory per size from phase 4. With one host it would be an invented number, which the issue forbids.

Numbers from a development desktop are a smoke test, not capacity evidence. Every run records the host, runtime, SDK
and GC mode in the header, and numbers are only comparable on the same host.
