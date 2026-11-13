# RecDiffy capacity run 2026-11-13_1257_2d16975

| | |
|---|---|
| Commit | `2d1697519076887d97b0859c4c11e6dff7d00cb8` on `origin/dev`, working tree clean |
| Sweep | concurrency |
| Started (UTC) | 2026-11-13T17:57:07Z |
| Duration | 2332.581 s (compile 0.168 s) |
| Harness | 1.0.0, engine 1.0.0 |
| Profile | capacity.mixed 1.0.0, hash `ea100672412fede34f7ad0f9e703f1d6d75b10d6109cece2c1457d8360adf7d0` |
| Dataset | seed 377, 3 batch(es) per level |
| Command | `G:\RecDiffyCapacity\work\2026-11-13_1256_2d16975\RecDiffy.NET\capacity\RecDiffy.Capacity\bin\Release\net10.0\RecDiffy.Capacity.dll --sweep concurrency --out G:\RecDiffyCapacity\runs\2026-11-13_1256_2d16975 --seed 377 --repeats 3 --break-rate 0.1 --unmatched-rate 0.05 --commit 2d1697519076887d97b0859c4c11e6dff7d00cb8 --ref origin/dev --levels 1,2,4,8,10,20,40 --concurrency-rows 10000` |

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
| threads | shared | 1 | 2.501 | 24.0 | 7998 | 100.0% | 1.1 | 198.9 | 2.296 | 2.296 | 2.296 | yes |
| threads | shared | 2 | 3.678 | 32.6 | 10875 | 68.0% | 1.7 | 354.9 | 3.415 | 3.468 | 3.468 | yes |
| threads | shared | 4 | 5.822 | 41.2 | 13741 | 43.0% | 2.1 | 716.8 | 5.562 | 5.571 | 5.571 | yes |
| threads | shared | 8 | 11.075 | 43.3 | 14446 | 22.6% | 2.5 | 1763.8 | 10.388 | 10.471 | 10.471 | yes |
| threads | shared | 10 | 13.408 | 44.7 | 14916 | 18.6% | 2.6 | 2199.9 | 13.031 | 13.118 | 13.118 | yes |
| threads | shared | 20 | 26.516 | 45.3 | 15085 | 9.4% | 3.6 | 5063.2 | 25.842 | 26.006 | 26.034 | yes |
| threads | shared | 40 | 60.709 | 39.5 | 13178 | 4.1% | 6.3 | 12406.5 | 59.289 | 59.548 | 59.602 | yes |
| threads | separate | 1 | 2.339 | 25.6 | 8549 | 100.0% | 1.2 | 12459.9 | 2.119 | 2.119 | 2.119 | yes |
| threads | separate | 2 | 3.998 | 30.0 | 10004 | 58.5% | 1.6 | 396.8 | 3.759 | 3.786 | 3.786 | yes |
| threads | separate | 4 | 6.341 | 37.9 | 12617 | 36.9% | 2.1 | 636.3 | 5.818 | 5.878 | 5.878 | yes |
| threads | separate | 8 | 10.967 | 43.8 | 14589 | 21.3% | 2.7 | 1650.5 | 10.486 | 10.516 | 10.516 | yes |
| threads | separate | 10 | 13.847 | 43.3 | 14444 | 16.9% | 2.6 | 2508.4 | 13.211 | 13.311 | 13.311 | yes |
| threads | separate | 20 | 27.184 | 44.1 | 14715 | 8.6% | 3.7 | 3999.0 | 26.504 | 26.610 | 26.648 | yes |
| threads | separate | 40 | 61.328 | 39.1 | 13045 | 3.8% | 5.8 | 15379.5 | 60.173 | 60.371 | 60.384 | yes |
| tasks | shared | 1 | 2.697 | 22.2 | 7415 | 100.0% | 1.2 | 14095.3 | 2.475 | 2.475 | 2.475 | yes |
| tasks | shared | 2 | 3.953 | 30.4 | 10120 | 68.2% | 1.5 | 479.9 | 3.590 | 3.605 | 3.605 | yes |
| tasks | shared | 4 | 6.863 | 35.0 | 11656 | 39.3% | 2.0 | 932.9 | 6.585 | 6.626 | 6.626 | yes |
| tasks | shared | 8 | 12.464 | 38.5 | 12837 | 21.6% | 2.4 | 1823.8 | 12.096 | 12.165 | 12.165 | yes |
| tasks | shared | 10 | 15.600 | 38.5 | 12820 | 17.3% | 2.7 | 2032.2 | 14.744 | 15.006 | 15.006 | yes |
| tasks | shared | 20 | 30.456 | 39.4 | 13134 | 8.9% | 3.2 | 3906.7 | 28.277 | 28.679 | 28.854 | yes |
| tasks | shared | 40 | 65.532 | 36.6 | 12208 | 4.1% | 6.5 | 16972.5 | 63.314 | 63.666 | 63.759 | yes |
| tasks | separate | 1 | 2.460 | 24.4 | 8129 | 100.0% | 1.3 | 10231.0 | 2.255 | 2.255 | 2.255 | yes |
| tasks | separate | 2 | 3.883 | 30.9 | 10302 | 63.4% | 1.6 | 1012.9 | 3.655 | 3.670 | 3.670 | yes |
| tasks | separate | 4 | 5.953 | 40.3 | 13440 | 41.3% | 2.1 | 787.1 | 5.700 | 5.714 | 5.714 | yes |
| tasks | separate | 8 | 11.364 | 42.2 | 14079 | 21.6% | 2.4 | 1747.2 | 10.553 | 10.704 | 10.704 | yes |
| tasks | separate | 10 | 13.931 | 43.1 | 14356 | 17.7% | 2.4 | 2033.9 | 13.040 | 13.185 | 13.185 | yes |
| tasks | separate | 20 | 25.868 | 46.4 | 15463 | 9.5% | 3.6 | 6253.7 | 24.864 | 25.103 | 25.103 | yes |
| tasks | separate | 40 | 62.187 | 38.6 | 12864 | 4.0% | 4.6 | 12293.0 | 59.783 | 61.340 | 61.499 | yes |
| parallel | shared | 1 | 4.174 | 14.4 | 4791 | 100.0% | 1.3 | 10609.2 | 3.985 | 3.985 | 3.985 | yes |
| parallel | shared | 2 | 3.982 | 30.1 | 10045 | 104.8% | 1.6 | 911.4 | 3.782 | 3.784 | 3.784 | yes |
| parallel | shared | 4 | 5.987 | 40.1 | 13362 | 69.7% | 2.1 | 824.4 | 5.730 | 5.757 | 5.757 | yes |
| parallel | shared | 8 | 11.339 | 42.3 | 14110 | 36.8% | 2.4 | 1969.2 | 10.512 | 10.715 | 10.715 | yes |
| parallel | shared | 10 | 14.061 | 42.7 | 14224 | 29.7% | 2.7 | 2053.3 | 13.618 | 13.770 | 13.770 | yes |
| parallel | shared | 20 | 27.030 | 44.4 | 14798 | 15.4% | 3.6 | 5014.1 | 26.075 | 26.307 | 26.329 | yes |
| parallel | shared | 40 | 60.049 | 40.0 | 13323 | 7.0% | 6.2 | 12125.8 | 57.809 | 58.007 | 59.183 | yes |
| parallel | separate | 1 | 2.320 | 25.9 | 8622 | 100.0% | 1.2 | 14734.7 | 2.142 | 2.142 | 2.142 | yes |
| parallel | separate | 2 | 3.364 | 35.7 | 11891 | 69.0% | 1.7 | 15290.9 | 3.156 | 3.157 | 3.157 | yes |
| parallel | separate | 4 | 6.225 | 38.6 | 12852 | 37.3% | 2.0 | 982.3 | 5.717 | 5.726 | 5.726 | yes |
| parallel | separate | 8 | 11.169 | 43.0 | 14325 | 20.8% | 2.5 | 1957.8 | 10.299 | 10.459 | 10.459 | yes |
| parallel | separate | 10 | 14.640 | 41.0 | 13661 | 15.8% | 2.6 | 2432.6 | 14.024 | 14.102 | 14.102 | yes |
| parallel | separate | 20 | 26.117 | 45.9 | 15316 | 8.9% | 3.9 | 6246.3 | 25.088 | 25.325 | 25.446 | yes |
| parallel | separate | 40 | 60.713 | 39.5 | 13177 | 3.8% | 5.4 | 14884.6 | 58.917 | 59.204 | 59.297 | yes |

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
