# The capacity harness

Phase 1 of the capacity and performance work in [#377](https://github.com/m-vaysman/recdiffy.net/issues/377): a
console harness that runs the reconciliation engine through its public API, records what it cost, and writes a run
folder with `results.json`, `report.md` and a static `report.html`.

It is **outside `RecDiffy.slnx`**. No workflow builds or runs it, `ci.yml` never sees it, and it is `IsPackable=false`,
so it adds nothing to the packed packages. It lives in this repository so it always measures the engine at the commit
that contains it.

## Run it

From the repository root, with the SDK that `RecDiffy.NET/global.json` pins:

```
dotnet run --project RecDiffy.NET/capacity/RecDiffy.Capacity -- --out G:\RecDiffyCapacity\runs\2026-11-12_2238_abc1234
```

Without `--out` the run folder is `./capacity-runs/<yyyy-MM-dd_HHmm>_<short-sha>/` under the current directory. That
folder is ignored by git; on the capacity host (COBBLER1) put it on `G:`.

| Option | Meaning | Default |
|---|---|---|
| `--out <folder>` | Run folder | `./capacity-runs/<run-id>` |
| `--sizes <a,b>` | Rows per side, ascending | `10000,100000` |
| `--seed <n>` | Dataset seed | `377` |
| `--repeats <n>` | Measured runs per size | `1` |
| `--break-rate <0..1>` | Share of matched pairs built to break | `0.1` |
| `--unmatched-rate <0..1>` | Share of rows built with no counterparty row | `0.05` |
| `--excel <one\|none>` | Write the bounded Excel report | `one` |
| `--case-timeout-seconds <n>` | Cancel a case after n seconds | `0` (none) |
| `--commit <sha>` / `--ref <name>` | Record these instead of asking git | read from git |

`--help` prints the same table.

## What a run folder holds

| File | What it is |
|---|---|
| `results.json` | The source of truth: run, source, host, every case and sample, and the numbers the page shows. |
| `report.md` | The Markdown summary, rendered from `results.json`. This is what the run script posts to #377. |
| `report.html` | One self-contained page, inline CSS, no scripts and no network calls, rendered from `results.json`. |
| `excel/` | The one Excel report each case writes, so the output stage has a real file behind it. |

Both views are rendered from the file on disk, not from the numbers in memory, so the page cannot disagree with the
JSON it came from. Every number on the page is in the JSON, including the bar widths, stage shares and scaling verdicts.

## What one case does

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

## What phase 1 does not measure

- **Stages inside `Reconciler.Run`.** Intake, normalization, consolidation, matching, diff and result building are one
  *engine* stage, because the public API exposes no split and the capacity project does not take `InternalsVisibleTo`.
  The per-stage benchmarks are phase 2; the large-size split is phase 4.
- **Concurrency.** Phase 3 runs N reconciliations at once, with shared and separate profiles and the three fan-out
  styles.
- **Large sizes and the memory ceiling.** Phase 4 sweeps 1M and probes 10M, under heap caps, and records how a run
  fails.
- **Heap caps and GC modes.** Phase 4/5 set `DOTNET_GCHeapHardLimit` and compare server and workstation GC.
- **Cold and warm runs, ReadyToRun, Native AOT, `net8.0`.** Phase 5.
- **The run script and the GitHub comment.** Phase 6.
- **Comparison with the previous run.** The run script will hand over the previous run's `results.json`; phase 1 has no
  history to compare against.
- **The production sizing estimate.** Little's law needs the concurrency plateau from phase 3 and the latency and
  memory per size from phase 4. With one host and one measured run per size it would be an invented number, which the
  issue forbids.

Numbers from a development desktop are a smoke test, not capacity evidence. Every run records the host, runtime, SDK
and GC mode in the header, and numbers are only comparable on the same host.
