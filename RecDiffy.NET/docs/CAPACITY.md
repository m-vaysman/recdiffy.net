# Capacity and concurrency

This guide answers three questions for someone planning a deployment: how large a reconciliation can be, how many can run at once, and how to turn those numbers into a number of processes or hosts. It reports what was measured, not what is promised; every table names the machine, the runtime, the GC mode and the date, and a number that has not been measured is marked **TBD**.

The numbers come from the capacity harness ([`capacity/README.md`](../capacity/README.md), issue #377) on COBBLER1: two Intel Xeon E5-2680 v2 sockets, 20 physical and 40 logical cores, 64 GB, Windows 10 Pro 22H2 build 19045.6466, .NET 10.0.14 on SDK 10.0.403, x64 RyuJIT AVX, `dev` at `2d16975`, measured on 13 November 2026. Unless a table says otherwise, the GC is concurrent workstation.

The micro-benchmarks and their method are in [BENCHMARKS.md](BENCHMARKS.md). This guide does not repeat them; it links to them where the same figure matters.

## How many runs at once

The concurrency sweep ran batches of 10,000-row-per-side reconciliations at levels 1 to 40, with three batches per level, four fan-out styles (threads or tasks, each with one shared `CompiledProfile` or one profile per run). Every run produced the same result as the baseline fingerprint.

| Runs in flight | Rows/s | Per-run p50 | Concurrency efficiency |
|---|---|---|---|
| 1 | 7,998 | 2.3 s | 100% |
| 2 | 10,875 | 3.4 s | 68% |
| 4 | 13,741 | 5.6 s | 43% |
| 8 | 14,446 | 10.4 s | 23% |
| 10 | 14,916 | 13.0 s | 19% |
| 20 | 15,085 | 25.8 s | 9.4% |
| 40 | 13,178 | 59.3 s | 4.1% |

*COBBLER1, 40 logical cores, 64 GB, .NET 10.0.14, workstation GC, 13 November 2026. Threads with one shared `CompiledProfile`; the other three fan-out styles (tasks, separate profiles) agree within about 20%. Rows/s counts both sides: 10,000 rows per side is 20,000 rows per run.*

What this means:

- **One run at a time sustains about 8,000 rows/s** (20,000 rows in 2.5 s). That is the solo number, not the capacity of the host.
- **Throughput plateaus at about 15,000 rows/s** with 10 to 20 runs in flight. More runs than that do not help: at 40, throughput falls to about 13,000 rows/s and each run takes about a minute instead of 2.5 s.
- **Concurrency efficiency collapses.** At 20 runs in flight, each one gets about 9% of the solo rate. The box reports only 3.6 of 40 logical cores busy at 20 runs and 6.3 at 40, so the limit is not core count: the engine is single-threaded per run, and the shared GC and memory path saturate before the cores do.
- **A `CompiledProfile` is safe to share** across concurrent runs; compile it once and keep it alive. The contract and its tests are in [SUPPORT.md](SUPPORT.md#thread-safety).
- **One run uses about one core for matching.** `RunOptions.MaxDegreeOfParallelism` builds the matched entries (the combined rows and the field comparison) in parallel; matching itself stays on one thread, so it does not make a single run use more cores for the matcher.
- **`RunAsync` is not faster.** It is the same work with an async signature. The thread-safety contract for sharing profiles, registries and results is in [SUPPORT.md](SUPPORT.md#thread-safety).

GC choice at 100,000 rows per side, with the Excel report on:

| GC | Engine | Peak heap | Peak working set |
|---|---|---|---|
| Workstation | 25.7 s | 4.76 GB | 4.83 GB |
| Server | 21.7 s | 5.11 GB | 5.65 GB |

*COBBLER1, .NET 10.0.14, JIT, 13 November 2026, `--sizes 10000,100000`, three repeats, Excel report on.*

Server GC was about 15% faster in wall time and used about 7% more memory at 100,000 rows per side. Choose it when memory is ample and latency matters; keep workstation GC when memory is tight. Server GC at 1,000,000 rows per side is **TBD**.

## The largest reconciliation

Both sides are read fully into memory before matching starts, so the ceiling is the pair of files, not one file ([SUPPORT.md](SUPPORT.md#supported-use)). Measured with the report off, one run at a time:

| Rows per side | Outcome | Engine | Peak heap | Bytes per input row | Retained heap after the result is dropped |
|---|---|---|---|---|---|
| 10,000 | Completed | 2.3 s | 0.20 GB | 10.2 kB | 14 MB |
| 100,000 | Completed | 24.7 s | 2.14 GB | 11.2 kB | 93 MB |
| 1,000,000 | Completed | 297.9 s | 18.27 GB | 9.6 kB | 186 MB |

*COBBLER1, .NET 10.0.14, workstation GC, 13 November 2026, no heap cap, no Excel report, three measured runs per size. Bytes per input row counts both sides: 1,000,000 rows per side is 2,000,000 input rows.*

The same 1,000,000-row pair under a heap cap:

| Heap cap | 10,000 rows | 100,000 rows | 1,000,000 rows |
|---|---|---|---|
| 8 GB | Completed | Completed, 26.0 s, 1.75 GB peak | **OutOfMemory** after 110.7 s at 8.09 GB peak |
| 16 GB | Completed | Completed, 26.1 s, 1.75 GB peak | Completed, 333.0 s, 16.16 GB peak |
| 32 GB | Completed | Completed, 26.2 s, 1.75 GB peak | Completed, 315.2 s, 16.34 GB peak |

*COBBLER1, .NET 10.0.14, workstation GC, 13 November 2026, one measured run per size, no Excel report. A managed `OutOfMemoryException` is recorded as the case outcome; the run exits successfully and the report says `OutOfMemory`. A host OOM kill is not observed here.*

What this means:

- **One million rows per side fits in a 16 GB heap** on this host, with about 16.2 GB of peak heap, and costs about 12% more wall time than the uncapped run (333 s against 298 s) because the cap keeps the GC busier.
- **Eight gigabytes is enough for 100,000 rows per side** (1.75 GB peak) but not for one million: the run fails with `OutOfMemory` after about 111 s, before it can finish.
- **The memory is about 10 kB per input row for this profile, both sides included.** That figure is for the capacity profile's record, which is narrow; a wider record costs more, and the per-record figure for narrow, typical and wide records is **TBD**.
- **The Excel report adds memory and time.** At 100,000 rows per side, the report raised the peak from 2.14 GB to 4.76 GB and added about 35 s (25.7 s engine against 60.6 s total). Above a million rows, write the report only if you need it.
- **The peak comes from** both sides resident at once, the normalized row contexts the result retains for `Projection`, `Trace` and `Raw`, and, with the Excel report, the writer's copy of the workbook.
- **Failure modes:** a managed `OutOfMemoryException` is recorded and the report says `OutOfMemory`; a host OOM kill kills the process without a report; there is no wall-clock timeout unless the caller sets `--case-timeout-seconds` (harness) or a cancellation token, and cancellation during consolidation is **TBD**.
- **Search limits protect the host.** A block over `maxBlockItems` ends `BlockTooLarge`, and a search over `maxNodesPerTarget` ends `BudgetExhausted`; both are recorded in the result. The limits and their mechanics are in [PROFILES.md](PROFILES.md#limits-search-limits) and [RESULTS.md](RESULTS.md#incomplete-search). The measured cost of raising each limit is **TBD**.
- **One block's components are built in one pass.** A block of `T` targets and `E` many-side rows costs `Θ(T + E)` to divide into components and to collect each component's eligible rows, not the `Θ(T²)` scans and copies it cost before #407. The budget is a single block, 8× the targets, allocated bytes under 16×; the three shapes the micro-benchmarks use hold it, and the match counts they commit are unchanged. The gate is [ComponentPartitionCostTests](../src/RecDiffy.Tests/ComponentPartitionCostTests.cs), and [GroupComponentWidthCostTests](../src/RecDiffy.Tests/GroupComponentWidthCostTests.cs) checks the same 8× scale through the group pass.

## Production sizing

The method is Little's law over the measured curve, not the solo rate:

1. **Pick the target latency per run.** The table above gives p50 at each level: 10 runs in flight give about 13 s for a 10,000-row run; 20 give about 26 s.
2. **Read the throughput at that level.** About 15,000 rows/s at 10 to 20 runs in flight on this host, with 10,000-row runs.
3. **Compute the runs in flight.** `L = throughput x latency / rows per run`. At the 20-run point: `15,085 x 25.8 / 20,000 = 19.5`, which matches the 20 runs measured. That is the check that the table and the law agree.
4. **Multiply for the customer's peak.** A peak of 30,000 rows/s needs two processes of this size (`30,000 / 15,000`) with headroom, and each concurrent run needs its own memory: two concurrent 100,000-row runs need about 4.3 GB, and two 1,000,000-row runs need about 32 GB.

**The latest estimate:** one process on a 40-logical-core host sustains about 15,000 rows/s with 10 to 20 concurrent 10,000-row runs in flight. That is 375 rows/s per logical core for this profile, and it is the number to divide a peak load by.

Caveats, stated rather than hidden:

- **The estimate is for one process.** Whether two processes scale linearly on one host, or across hosts, is **TBD** (the second-host item of phase 3 in #377).
- **The estimate is for 10,000-row runs.** Concurrency at 100,000 and 1,000,000 rows per side is **TBD**; memory grows with the run, so fewer of them fit.
- **The hardware is a 2012-era Xeon.** A modern core is faster per core. The per-core figure is for COBBLER1; other hosts are **TBD**.
- **Do not size from the solo rate** (about 8,000 rows/s): it ignores queueing, and the measured plateau is nearly twice that.
- **Do not size from a single 1,000,000-row run.** Its 298 s is one run with no contention, and its 18 GB peak is not multiplied by concurrency in that measurement.

## Throughput

| Case | Rows/s | Engine | Peak heap | Bytes per input row | GC | Runtime |
|---|---|---|---|---|---|---|
| 10,000 rows per side | 7,738 | 2.6 s | 0.20 GB | 10.2 kB | Workstation | .NET 10.0.14 |
| 100,000 rows per side | 8,099 | 24.7 s | 2.14 GB | 11.2 kB | Workstation | .NET 10.0.14 |
| 1,000,000 rows per side | 6,713 | 297.9 s | 18.27 GB | 9.6 kB | Workstation | .NET 10.0.14 |
| 100,000 rows per side, 8 GB cap | 7,692 | 26.0 s | 1.75 GB | 9.2 kB | Workstation | .NET 10.0.14 |
| 1,000,000 rows per side, 16 GB cap | 6,006 | 333.0 s | 16.16 GB | 8.5 kB | Workstation | .NET 10.0.14 |
| 1,000,000 rows per side, 32 GB cap | 6,344 | 315.2 s | 16.34 GB | 8.6 kB | Workstation | .NET 10.0.14 |
| 100,000 rows per side, Excel on | 7,789 | 60.6 s total | 4.76 GB | 25.0 kB | Workstation | .NET 10.0.14 |
| 100,000 rows per side, Excel on | 9,222 | 48.0 s total | 5.11 GB | 26.8 kB | Server | .NET 10.0.14 |

*COBBLER1, Windows 10 19045, 40 logical cores, 64 GB, JIT, 13 November 2026. Rows/s counts both sides. The Excel rows include the writer's time and memory; the byte figures there count the report's copy too.*

What this means:

- **Throughput is roughly flat from 10,000 to 1,000,000 rows per side** (6,700 to 8,100 rows/s), so the engine is linear in rows at these sizes. The largest run is the slowest per row because of GC pressure, not because of an algorithmic cliff.
- **A heap cap costs time.** The 16 GB cap at 1,000,000 rows takes 333 s against 298 s uncapped, and the 32 GB cap 315 s. If the run fits, a cap is a safety net, not a speed setting.
- **The Excel report roughly doubles wall time at these sizes** (60.6 s against 25.7 s at 100,000 rows) and adds about 2.2 GB of peak memory.
- **Per-stage timings at these sizes are TBD.** The harness reports the engine total; the 1,000 and 4,000-row split by stage is in [BENCHMARKS.md](BENCHMARKS.md#per-stage-numbers-pipelinestagebench-377-p2), and the large-size split is the remaining phase-4 item.
- **Percentiles:** in the concurrency sweep, p50, p95 and p99 agree within about 1% at every level. These are 10,000-row runs on an idle box; a longer tail at large sizes is **TBD**.
- **Warm versus cold:** the harness does not report a separate cold-start measurement; cold-start times are **TBD**.
- **A refused search is not a fast reconciliation.** A run that ends every row `BlockTooLarge` or `BudgetExhausted` stopped early; see [RESULTS.md](RESULTS.md#incomplete-search) for what the result records.

## What is not measured yet

These are the open items; each is **TBD** until a run fills it in:

- Concurrency at 100,000 and 1,000,000 rows per side, and with server GC.
- The per-record memory for narrow, typical and wide records.
- The second host, and whether processes scale linearly on one host.
- Native AOT and `net8.0` numbers on COBBLER1 (the harness can run both; the first capacity runs used JIT and `net10.0`).
- The measured cost of raising `maxBlockItems` and `maxNodesPerTarget`.
- Per-stage timings at large sizes, and cold-start times.
- Cancellation during consolidation, and a host OOM kill (the harness records a managed `OutOfMemoryException`, not a kill).
