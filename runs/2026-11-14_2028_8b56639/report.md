# RecDiffy capacity run 2026-11-14_2030_8b56639

| | |
|---|---|
| Commit | `8b56639e3308859efcfcadb8b071efc523f0c4e1` on `origin/dev`, working tree clean |
| Sweep | concurrency |
| Started (UTC) | 2026-11-15T01:30:12Z |
| Duration | 2077.085 s (compile 0.155 s) |
| Harness | 1.0.0, engine 1.0.0 |
| Profile | capacity.mixed 1.0.0, hash `ea100672412fede34f7ad0f9e703f1d6d75b10d6109cece2c1457d8360adf7d0` |
| Dataset | seed 377, 3 batch(es) per level |
| Command | `G:\RecDiffyCapacity\work\2026-11-14_2028_8b56639\RecDiffy.NET\capacity\RecDiffy.Capacity\bin\Release\net10.0\RecDiffy.Capacity.dll --sweep concurrency --out G:\RecDiffyCapacity\runs\2026-11-14_2028_8b56639 --seed 377 --repeats 3 --break-rate 0.1 --unmatched-rate 0.05 --commit 8b56639e3308859efcfcadb8b071efc523f0c4e1 --ref origin/dev --levels 1,2,4,8,10,20,40 --concurrency-rows 10000` |

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

## Concurrency

| | |
|---|---|
| Rows per run | 10000 per side |
| Batches per level | 3 |
| GC | workstation GC |
| Logical processors | 40 |
| Baseline fingerprint | `20A304CC7AD8E65D` |
| Every run matched the baseline | yes |

| Fan-out | Profiles | Runs | Wall s | Runs/min | Rows/s | Efficiency | Cores used | Peak heap MB | p50 s | p95 s | p99 s | Matches |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| threads | shared | 1 | 2.101 | 28.6 | 9518 | 100.0% | 1.2 | 158.4 | 1.929 | 1.929 | 1.929 | yes |
| threads | shared | 2 | 3.391 | 35.4 | 11797 | 62.0% | 1.6 | 397.8 | 3.175 | 3.176 | 3.176 | yes |
| threads | shared | 4 | 5.225 | 45.9 | 15310 | 40.2% | 2.1 | 699.8 | 4.944 | 4.974 | 4.974 | yes |
| threads | shared | 8 | 9.502 | 50.5 | 16839 | 22.1% | 2.8 | 1567.5 | 8.880 | 8.955 | 8.955 | yes |
| threads | shared | 10 | 12.538 | 47.9 | 15952 | 16.8% | 2.4 | 2933.6 | 11.678 | 11.906 | 11.906 | yes |
| threads | shared | 20 | 23.513 | 51.0 | 17012 | 8.9% | 3.8 | 5841.4 | 22.526 | 22.908 | 22.922 | yes |
| threads | shared | 40 | 51.292 | 46.8 | 15597 | 4.1% | 5.6 | 9252.0 | 49.728 | 49.792 | 49.834 | yes |
| threads | separate | 1 | 1.989 | 30.2 | 10057 | 100.0% | 1.1 | 15879.0 | 1.808 | 1.808 | 1.808 | yes |
| threads | separate | 2 | 3.061 | 39.2 | 13069 | 65.0% | 1.7 | 15881.2 | 2.826 | 2.831 | 2.831 | yes |
| threads | separate | 4 | 5.469 | 43.9 | 14629 | 36.4% | 2.0 | 764.4 | 5.185 | 5.218 | 5.218 | yes |
| threads | separate | 8 | 9.688 | 49.5 | 16515 | 20.5% | 2.3 | 1496.0 | 9.361 | 9.409 | 9.409 | yes |
| threads | separate | 10 | 11.999 | 50.0 | 16669 | 16.6% | 2.5 | 1964.1 | 11.608 | 11.645 | 11.645 | yes |
| threads | separate | 20 | 23.328 | 51.4 | 17147 | 8.5% | 3.6 | 6598.1 | 22.444 | 22.629 | 22.731 | yes |
| threads | separate | 40 | 56.431 | 42.5 | 14177 | 3.5% | 4.2 | 16565.2 | 55.009 | 55.170 | 55.367 | yes |
| tasks | shared | 1 | 2.310 | 26.0 | 8657 | 100.0% | 1.2 | 14089.8 | 2.114 | 2.114 | 2.114 | yes |
| tasks | shared | 2 | 3.281 | 36.6 | 12193 | 70.4% | 1.7 | 519.2 | 3.075 | 3.078 | 3.078 | yes |
| tasks | shared | 4 | 5.216 | 46.0 | 15336 | 44.3% | 2.1 | 757.6 | 4.981 | 4.985 | 4.985 | yes |
| tasks | shared | 8 | 9.716 | 49.4 | 16468 | 23.8% | 2.3 | 1962.7 | 9.025 | 9.111 | 9.111 | yes |
| tasks | shared | 10 | 12.562 | 47.8 | 15922 | 18.4% | 2.4 | 2007.8 | 11.972 | 12.127 | 12.127 | yes |
| tasks | shared | 20 | 23.620 | 50.8 | 16935 | 9.8% | 4.0 | 7179.9 | 22.673 | 22.862 | 22.980 | yes |
| tasks | shared | 40 | 55.439 | 43.3 | 14430 | 4.2% | 4.3 | 17626.8 | 54.038 | 54.254 | 54.259 | yes |
| tasks | separate | 1 | 2.030 | 29.6 | 9852 | 100.0% | 1.1 | 17432.3 | 1.844 | 1.844 | 1.844 | yes |
| tasks | separate | 2 | 3.072 | 39.1 | 13022 | 66.1% | 1.6 | 17434.1 | 2.730 | 2.730 | 2.730 | yes |
| tasks | separate | 4 | 5.337 | 45.0 | 14989 | 38.0% | 2.2 | 751.6 | 4.889 | 4.890 | 4.890 | yes |
| tasks | separate | 8 | 9.629 | 49.9 | 16617 | 21.1% | 2.4 | 1497.3 | 9.314 | 9.360 | 9.360 | yes |
| tasks | separate | 10 | 12.601 | 47.6 | 15871 | 16.1% | 2.4 | 1920.5 | 12.212 | 12.346 | 12.346 | yes |
| tasks | separate | 20 | 24.132 | 49.7 | 16575 | 8.4% | 3.3 | 4093.9 | 23.347 | 23.655 | 23.661 | yes |
| tasks | separate | 40 | 53.906 | 44.5 | 14841 | 3.8% | 6.1 | 19392.5 | 52.411 | 52.544 | 52.633 | yes |
| parallel | shared | 1 | 2.678 | 22.4 | 7467 | 100.0% | 1.1 | 4553.4 | 2.487 | 2.487 | 2.487 | yes |
| parallel | shared | 2 | 3.755 | 32.0 | 10651 | 71.3% | 1.6 | 1170.4 | 3.533 | 3.556 | 3.556 | yes |
| parallel | shared | 4 | 5.505 | 43.6 | 14531 | 48.7% | 2.1 | 871.3 | 5.025 | 5.028 | 5.028 | yes |
| parallel | shared | 8 | 9.954 | 48.2 | 16074 | 26.9% | 2.7 | 1527.2 | 9.637 | 9.656 | 9.656 | yes |
| parallel | shared | 10 | 12.580 | 47.7 | 15899 | 21.3% | 2.5 | 1962.0 | 11.967 | 12.111 | 12.111 | yes |
| parallel | shared | 20 | 25.694 | 46.7 | 15568 | 10.4% | 3.7 | 4224.5 | 24.862 | 25.137 | 25.148 | yes |
| parallel | shared | 40 | 58.245 | 41.2 | 13735 | 4.6% | 5.2 | 10779.4 | 56.865 | 57.029 | 57.046 | yes |
| parallel | separate | 1 | 2.872 | 20.9 | 6963 | 100.0% | 1.3 | 1175.8 | 2.675 | 2.675 | 2.675 | yes |
| parallel | separate | 2 | 3.888 | 30.9 | 10287 | 73.9% | 1.6 | 1395.5 | 3.654 | 3.654 | 3.654 | yes |
| parallel | separate | 4 | 5.878 | 40.8 | 13610 | 48.9% | 1.8 | 722.4 | 5.361 | 5.381 | 5.381 | yes |
| parallel | separate | 8 | 10.614 | 45.2 | 15075 | 27.1% | 2.3 | 1605.7 | 10.247 | 10.346 | 10.346 | yes |
| parallel | separate | 10 | 13.948 | 43.0 | 14339 | 20.6% | 2.3 | 2573.6 | 13.363 | 13.495 | 13.495 | yes |
| parallel | separate | 20 | 25.584 | 46.9 | 15635 | 11.2% | 3.5 | 7078.3 | 24.424 | 24.759 | 24.886 | yes |
| parallel | separate | 40 | 55.198 | 43.5 | 14493 | 5.2% | 5.4 | 20383.5 | 53.214 | 53.421 | 53.478 | yes |

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

- A batch starts all N runs together over the same seeded dataset. Rows per run are fixed, so total work grows with N; the numbers are throughput (rows per second and runs per minute), not total batch time.
- One discarded batch per fan-out style runs before the measurements, so JIT and thread start-up are not charged to the first measured batch.
- Scaling efficiency is rows per second at N divided by N times rows per second at 1, for the same fan-out style and profile sharing.
- Every run's result is compared field by field with a single-threaded baseline: the summary, every entry with its diffs and both projections, and every row with its disposition and diagnostics. One difference fails the run.
- Separate profiles are compiled before the batch starts, so the difference between shared and separate is profile sharing, not compile cost.
- The GC mode is set by the process (DOTNET_gcServer). This report records the mode the sweep ran under; run the sweep once per mode.
- CPU is the process's total processor time over the batch divided by the batch wall time, so it counts every thread the run used.
- Not in this phase: culture isolation across threads, a second host, and a dotnet-trace race-detection pass. Both profile sharings and all three fan-out styles are measured.
- Per-run latency is one reconciliation's wall time inside the batch; p50, p95 and p99 are over the runs of the batch.

Run folder: `results.json`, `report.md`, `report.html`. Issue: https://github.com/m-vaysman/recdiffy.net/issues/377
