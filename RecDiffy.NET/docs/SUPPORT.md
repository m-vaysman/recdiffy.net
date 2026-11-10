# Support matrix

This page says where RecDiffy is meant to run, and how each claim is checked today. Every package is a prerelease, so the matrix can change between previews.

Each claim has one of three statuses:

- **Verified in CI:** a GitHub Actions workflow builds or tests it on every pull request into `dev`.
- **Checked by analyzers:** the compiler's analyzers enforce it on every build. That catches most problems, not all.
- **Not verified:** expected to work, but nothing runs it yet. The linked issue tracks the check.

The workflows are in [.github/workflows](../../.github/workflows). Two matter here: [ci.yml](../../.github/workflows/ci.yml) builds the solution and runs the test projects, and [packages.yml](../../.github/workflows/packages.yml) packs the libraries and uses the packages as a consumer would. Both run on every pull request into `dev`.

## Packages

| Package | Target frameworks | Marked AOT-compatible and trimmable | Dependencies |
|---|---|---|---|
| `RecDiffy` | `net8.0`, `net10.0` | Yes | `System.Text.Json` 10.0.14, on `net8.0` only |
| `RecDiffy.RulePacks.Trading` | `net8.0`, `net10.0` | Yes | `RecDiffy` |
| `RecDiffy.IO` | `net8.0`, `net10.0` | No | `RecDiffy`, `Sylvan.Data.Csv` 1.4.4, `Sylvan.Data.Excel` 0.5.8 |
| `RecDiffy.Excel` | `net8.0`, `net10.0` | No | `RecDiffy`, `DocumentFormat.OpenXml` 3.5.1 |

The four ship together, at one version. See [RELEASING.md](RELEASING.md#release-scope).

## The matrix

| Area | Claim | How it is checked | Status |
|---|---|---|---|
| **.NET 10** | Every package runs on `net10.0`. | CI runs `RecDiffy.Tests`, `RecDiffy.IO.Tests` and `RecDiffy.Excel.Tests` for `net10.0`, and runs the samples from the packed packages on `net10.0`. | Verified in CI |
| **.NET 8** | Every package runs on `net8.0`. | The same three test projects, and the samples from the packed packages, also run on `net8.0`. CI installs the .NET 8 SDK beside the pinned .NET 10 SDK. | Verified in CI |
| **.NET 9** | An app on .NET 9 gets the `net8.0` build. | Nothing runs it. | Not verified |
| **.NET Framework, .NET Standard** | Not supported. | No target. | Not supported |
| **Linux** | The libraries build and pass their tests on Linux. | `ci.yml`, `packages.yml` and `release.yml` run on `ubuntu-latest`. | Verified in CI |
| **Windows** | The libraries run on Windows. | No library test suite runs on Windows. The benchmarks run on a self-hosted Windows machine, but they measure speed, not results. The Excel add-in's tests run on `windows-latest` against the published `RecDiffy` and `RecDiffy.IO` packages. | Not verified ([#53](https://github.com/m-vaysman/recdiffy.net/issues/53)) |
| **macOS** | The libraries run on macOS. | Nothing runs on macOS. | Not verified ([#53](https://github.com/m-vaysman/recdiffy.net/issues/53)) |
| **Thread safety** | See [Thread safety](#thread-safety). | Parallel tests in `RecDiffy.Tests`. | Partly verified in CI |
| **Native AOT, trimming** | `RecDiffy` and `RecDiffy.RulePacks.Trading` work in a trimmed, AOT-published app. | The AOT and trim analyzers run on every build. The Packages workflow also publishes the AOT host, and an app built from the packed packages, with Native AOT and trimmed, on `net8.0` and `net10.0`, failing on any warning, and runs each. Only on Linux x64. | Verified in CI, on Linux |
| **AOT and trimming for `RecDiffy.IO`, `RecDiffy.Excel`** | No claim. | These projects aren't marked, so the analyzers don't run on them. | Not claimed |
| **Installing the packages** | A clean project restores the packages from a feed and runs. | The Packages workflow packs the solution, checks that exactly the four libraries come out, restores them into a project with no project references, checks each package's files and dependencies, and runs every sample from them. It checks packages packed from the pull request, not the files a release pushes. | Verified in CI |

## Building from source

- **SDK:** .NET SDK 10.0.403, or a later feature band that [global.json](../global.json) allows (`rollForward: latestFeature`).
- **Runtimes:** the .NET 8 runtime as well, because the tests run once for each framework.
- **The Excel add-in** is a separate solution that builds only on Windows. See [CONTRIBUTING.md](../../CONTRIBUTING.md#the-excel-add-in).

## Thread safety

What holds today, and how each part is checked:

- **A compiled profile can be shared.** `CompiledProfile` doesn't change after `Reconciler.Compile`, and concurrent runs can use one instance. It keeps a reference to the document you compiled, so build that document with `ProfileDocument.Parse` or `ProfileBuilder`, or don't change the collections you built it from. `AdversarialSubsetOracleTests` runs a single compiled profile from many threads at once. *Verified in CI.*
- **A rule registry can be shared.** `RuleRegistry` is an immutable record. `RuleRegistry.CreateCore()` and `TradingRulePack.AddTo` each return a new registry and change none in place. *By code review.*
- **Reference data can be shared** when it comes from `ReferenceDataSet.Create`, which copies each dictionary into an immutable one. If you call the record's constructor with your own dictionaries, don't change them during a run. *By code review.*
- **The engine has no shared mutable state.** Its static fields are set once and never written afterwards. The `System.Diagnostics` activity source and counters it updates are thread-safe. *By code review.*
- **`RunOptions.MaxDegreeOfParallelism`** builds matched entries in parallel inside one run: the combined rows of grouped matches, and the field comparison. It takes effect above 1, when there are at least two matched entries. Matching stays on one thread. The result is the same at any degree: `Stays_the_same_at_one_and_eight_workers` in [ViewTests.cs](../src/RecDiffy.Tests/ViewTests.cs), and the thread variant in [AdversarialSweepTests.cs](../src/RecDiffy.Tests/AdversarialSweepTests.cs). *Verified in CI.*
- **A result can be read from several threads** once `Run` returns, because nothing changes it afterwards. *By code review, not tested.*
- **A record source is only as safe as what it wraps.** `RecordSources.FromObjects` enumerates your collection on each run, and `FromDataReader` calls your `open` function on each run. Create a source per run, or make sure the collection or reader factory is safe to use from several threads. Reading several sheets of one workbook in parallel, each through its own `RecDiffy.IO` source, is tested in [AdversarialWorkbookStressTests.cs](../src/RecDiffy.IO.Tests/AdversarialWorkbookStressTests.cs). *Verified in CI.*
- **The server's culture is read once,** at compile, from the compiling thread's `CultureInfo.CurrentCulture`, for every side that declares no culture. In a multi-threaded host, declare cultures in the profile, so the thread that compiles doesn't matter.
- **`RecDiffy.Excel` hasn't been reviewed** for concurrent use. Write reports from one thread per result until it is.

## Native AOT and trimming

- **The analyzers.** `RecDiffy` and `RecDiffy.RulePacks.Trading` set `IsAotCompatible` and `IsTrimmable`. On `net8.0` and later, that turns on the AOT, trim and single-file analyzers. CI builds with `-warnaserror`, so any new warning from them fails the build.
- **Publishing.** The Packages workflow publishes [RecDiffy.AotHost](../src/RecDiffy.AotHost) with Native AOT for `linux-x64`, runs it, and checks its answer. It also publishes [PackageSmoke/Aot](../tests/PackageSmoke/Aot), which compiles the samples that use only these two packages against the packed packages, with Native AOT and trimmed, for `net8.0` and `net10.0`, and runs every sample. Warnings fail the publish. See [the PackageSmoke README](../tests/PackageSmoke/README.md).
- **What isn't checked.** AOT publishing on Windows and macOS ([#53](https://github.com/m-vaysman/recdiffy.net/issues/53)).
- **Invariant globalization.** The AOT host sets `InvariantGlobalization`, as many AOT apps do. In that mode the only culture a profile can declare is `""`, the invariant culture. Any other name fails compile with `UnknownCulture`.

## Supported use

- **In-process .NET library.** Compile a profile once, then call `Reconciler.Run` or `Reconciler.RunAsync` as often as you like.
- **Sources:** your own objects (`RecordSources.FromObjects`), any `IDataReader` (`RecordSources.FromDataReader`), and CSV and Excel files through `RecDiffy.IO`.
- **Reports:** Excel workbooks through `RecDiffy.Excel`.
- **In memory.** Both sides are read fully into memory before matching starts. Size the host for the largest pair of files.
- **No service or command-line tool** ships with the packages.
- **The public API can change** between previews. Reviewing it before 1.0 is [#45](https://github.com/m-vaysman/recdiffy.net/issues/45), and the compatibility promise is [#74](https://github.com/m-vaysman/recdiffy.net/issues/74).

## How the matrix grows

| Gap | Issue |
|---|---|
| Run the library tests, and the AOT publish, on Windows and macOS | [#53](https://github.com/m-vaysman/recdiffy.net/issues/53) |
| Check the packages a release pushes, not only the ones each pull request packs | No issue yet |

When one of these lands, update the matching row above, and link the workflow step.
