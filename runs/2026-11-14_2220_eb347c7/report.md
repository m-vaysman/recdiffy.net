# RecDiffy capacity run 2026-11-14_2221_eb347c7

| | |
|---|---|
| Commit | `eb347c7397b00bb7e01413a3e4010b7b0eefedac` on `origin/dev`, working tree clean |
| Sweep | sizes |
| Started (UTC) | 2026-11-15T03:21:35Z |
| Duration | 23408.973 s (compile 0.152 s) |
| Harness | 1.0.0, engine 1.0.0 |
| Profile | capacity.mixed 1.0.0, hash `ea100672412fede34f7ad0f9e703f1d6d75b10d6109cece2c1457d8360adf7d0` |
| Dataset | seed 377, 1 measured run(s) per size |
| Rows per side | 10000000 |
| Command | `G:\RecDiffyCapacity\work\2026-11-14_2220_eb347c7\RecDiffy.NET\capacity\RecDiffy.Capacity\bin\Release\net10.0\RecDiffy.Capacity.dll --sweep sizes --out G:\RecDiffyCapacity\runs\2026-11-14_2220_eb347c7 --seed 377 --repeats 1 --break-rate 0.1 --unmatched-rate 0.05 --commit eb347c7397b00bb7e01413a3e4010b7b0eefedac --ref origin/dev --sizes 10000000 --case-timeout-seconds 28800 --excel none` |

## Host

| | |
|---|---|
| Machine | COBBLER1A |
| OS | Microsoft Windows 10.0.19045 |
| Architecture | X64 |
| Logical processors | 32 |
| Runtime | .NET 10.0.15 |
| SDK | 10.0.404 |
| GC | workstation GC, Interactive |
| GC heap limit | 255.9 GB |
| Dynamic code | yes, JIT |

## Largest reconciliation measured

The largest size that completed with the outcome it was built to produce: **10000000 rows per side**.

No case failed.

## Cases

| Rows/side | Outcome | Known outcome | Matched | Exact | Break | Left only | Right only | Total s | Engine s | Excel s | Rows/s | Peak heap MB | Peak working set MB | Bytes/row | Retained heap MB | Rows searched | Rows blocked | Aggregate overflow | Failure |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 10000000 | Completed | matched | 9750000 | 8775000 | 975000 | 250000 | 250000 | 11904.082 | 11904.082 | 0.000 | 1680 | 149460.4 | 151080.6 | 7836 | 39.5 | 19500000 | 0 | 0 | - |

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
