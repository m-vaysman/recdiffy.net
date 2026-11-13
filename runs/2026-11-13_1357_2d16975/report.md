# RecDiffy capacity run 2026-11-13_1359_2d16975

| | |
|---|---|
| Commit | `2d1697519076887d97b0859c4c11e6dff7d00cb8` on `origin/dev`, working tree clean |
| Sweep | sizes |
| Started (UTC) | 2026-11-13T18:59:00Z |
| Duration | 18.946 s (compile 0.412 s) |
| Harness | 1.0.0, engine 1.0.0 |
| Profile | capacity.mixed 1.0.0, hash `ea100672412fede34f7ad0f9e703f1d6d75b10d6109cece2c1457d8360adf7d0` |
| Dataset | seed 377, 1 measured run(s) per size |
| Rows per side | 10000 |
| Command | `G:\RecDiffyCapacity\publish\2026-11-13_1357_2d16975-r2r\RecDiffy.Capacity.dll --sweep sizes --out G:\RecDiffyCapacity\runs\2026-11-13_1357_2d16975 --seed 377 --repeats 1 --break-rate 0.1 --unmatched-rate 0.05 --commit 2d1697519076887d97b0859c4c11e6dff7d00cb8 --ref origin/dev --sizes 10000 --case-timeout-seconds 1800` |

## Host

| | |
|---|---|
| Machine | COBBLER1 |
| OS | Microsoft Windows 10.0.19045 |
| Architecture | X64 |
| Logical processors | 40 |
| Runtime | .NET 10.0.14 |
| SDK | 10.0.403 |
| GC | workstation GC, Interactive |
| GC heap limit | 63.9 GB |
| Dynamic code | yes, JIT |

## Largest reconciliation measured

The largest size that completed with the outcome it was built to produce: **10000 rows per side**.

No case failed.

## Cases

| Rows/side | Outcome | Known outcome | Matched | Exact | Break | Left only | Right only | Total s | Engine s | Excel s | Rows/s | Peak heap MB | Peak working set MB | Bytes/row | Retained heap MB | Rows searched | Rows blocked | Aggregate overflow | Failure |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 10000 | Completed | matched | 9750 | 8775 | 975 | 250 | 250 | 6.455 | 2.806 | 3.649 | 7127 | 585.1 | 661.6 | 30675 | 14.5 | 19500 | 0 | 0 | - |

## Not measured yet

| Stage | Why |
|---|---|
| intake | Reconciler.Run materializes both sides; the public API exposes no separate stage. |
| normalization | Inside Reconciler.Run; per-stage benchmarks land in P2. |
| consolidation | Inside Reconciler.Run; per-stage benchmarks land in P2. |
| matching | Inside Reconciler.Run; per-stage benchmarks land in P2. |
| diff | Inside Reconciler.Run; per-stage benchmarks land in P2. |
| results | Inside Reconciler.Run; per-stage benchmarks land in P2. |

## Notes

- Stages inside Reconciler.Run (intake, normalization, consolidation, matching, diff, result building) are inside the engine stage. The public API exposes no split; the per-stage benchmarks are P2 and the large-size split is P4.
- One warmup run of the smallest size is discarded before the measurements, so JIT, compile and the first Excel write are not charged to the first case.
- The dataset is generated while the engine materializes each side, so the engine stage includes the generator's cost. The generator is a seeded integer mixer, not System.Random, so a seed reproduces a dataset on every runtime.
- Excel output writes one report (SideBySideCompare) so the output stage stays bounded. The full report set is P4.
- Capacity numbers are comparable only on the same host, runtime and GC mode. This run's host is in the header.
- A run that does not match its known outcome is a measurement failure, not a fast run. A run too large to complete is recorded as its outcome.
- Rows searched are rows in a matched entry; rows blocked are rows the search limits refused (SearchIncomplete), so a fast blocked run is not read as a fast reconciliation. Aggregate overflow counts diagnostics with that code.
- Retained heap is the heap after every result of the case has been dropped and two full blocking collections have run; the result graph is part of the memory ceiling.
- An OS kill cannot be caught in-process: a run the host killed leaves no report. The run script records it from the job (phase 6). Here, only an out-of-memory exception is recorded as a case outcome.
- Fewer than three measured runs per size: p95 and p99 repeat the samples they fall on, so they are not yet stable.

Run folder: `results.json`, `report.md`, `report.html`. Issue: https://github.com/m-vaysman/recdiffy.net/issues/377
