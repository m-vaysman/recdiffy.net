# Contributing to RecDiffy.NET

This guide shows you how to build RecDiffy, test it, and submit a small, scoped change.

The rules for changing code live in [AGENTS.md](AGENTS.md). Every AI agent that works here follows them, and so do people. This guide sums up the ones you need and links to each one. When the two differ, AGENTS.md wins.

## Before you start

- **Search first.** Look through the open issues and pull requests. Several people and agents work here at the same time. If a PR already covers your change, build on it instead of opening a second one.
- **Open an issue for anything new.** For a feature or a change in behaviour, use the feature request form, so the scope is agreed before you write code. For a bug, use the bug report form.
- **Keep data synthetic.** Never put real client files, names, account numbers or trade IDs in an issue, a test or a PR. Make up a few rows that show the problem.
- **Keep security reports private.** Don't report a vulnerability in a public issue, discussion or PR. Report it privately, as [SECURITY.md](SECURITY.md) describes.

## What you need

- The .NET SDK **10.0.403**, or a later feature band that [global.json](RecDiffy.NET/global.json) allows.
- The .NET 8 runtime. The libraries and their tests target `net8.0` and `net10.0`, and the tests run once for each.
- Git.
- **Optional:** LibreOffice, for the Excel report recalc tests. Put `soffice` on `PATH`, or set `RECDIFFY_SOFFICE` to its path. Without it, those tests skip.
- **For the Excel add-in only:** Windows. To try the add-in, you also need 64-bit Excel.

## Get the code

Fork the repository, then clone your fork and branch from `dev`. If you can push to this repository, clone it directly instead.

```
git clone https://github.com/<you>/recdiffy.net.git
cd recdiffy.net
git switch dev
git switch -c <your-branch>
```

## Build and test

Run these from the repository root.

Build. The build must be clean, with no warnings:

```
dotnet build RecDiffy.NET/RecDiffy.slnx -c Release -warnaserror
```

Test:

```
dotnet test RecDiffy.NET/RecDiffy.slnx -c Release
```

The Excel tests skip the LibreOffice recalc tests when `soffice` isn't installed.

Before you push, do a full build. Incremental builds can skip analyzers:

```
dotnet build RecDiffy.NET/RecDiffy.slnx -c Release -warnaserror --no-incremental
```

**The SDK pin.** `dotnet` looks for `global.json` from the current directory upward. From the root it doesn't find the pin in `RecDiffy.NET/global.json`, so it uses your newest SDK. If that causes trouble, run the same commands from `RecDiffy.NET` and drop the `RecDiffy.NET/` prefix.

**What CI runs.** [CI](.github/workflows/ci.yml) runs on every PR into `dev`. It builds the solution with `-warnaserror` and runs `RecDiffy.Tests`, `RecDiffy.IO.Tests` and `RecDiffy.Excel.Tests`. It installs LibreOffice first, so the recalc tests run there even when they skip on your machine.

### Where CI runs

Some jobs run on the self-hosted Windows machine COBBLER1, and fall back to their GitHub-hosted image when it is unavailable. Nothing about what those jobs build, test or publish changes; only the machine does.

GitHub has no fallback in `runs-on`. An array of labels means "all of these", not "any of these", so the choice is made by a job instead:

1. [`pick-runner.yml`](.github/workflows/pick-runner.yml) is a reusable workflow. A workflow calls it as a job and reads its `runs_on_json` output.
2. The real job declares `needs: pick-runner` and `runs-on: ${{ fromJSON(needs.pick-runner.outputs.runs_on_json) }}`.
3. `pick-runner` answers `["self-hosted", "cobbler1-bench"]` only when the event is not a pull request from a fork, the token below is set, and the runner is online and idle. Otherwise it answers that job's own hosted image and says why in the job log and the run summary. A missing token, an offline runner, a busy runner and a failed API call are all fallbacks, never a failure.

**`RUNNER_STATUS_TOKEN`.** A fine-grained personal access token scoped to this repository with **Administration: read**, stored as an Actions secret. It is the only way a workflow can list the repository's runners. Without it, or if the call fails, every job takes its hosted image, which is what happened before this routing existed. Nothing else reads it.

**Fork pull requests never use COBBLER1.** The repository is public, so a pull request from a fork always gets the hosted image. That is checked before the token is, so a fork run cannot reach the self-hosted machine at all.

**One slot.** COBBLER1 runs one job at a time. `pick-runner` reads `busy` once, so a job can still queue behind [`benchmarks.yml`](.github/workflows/benchmarks.yml) if the runner becomes busy a moment later. A job that queues waits for the slot; it is not moved to the hosted image. If a run sits queued for a long time, wait or cancel and re-run it.

**A job that stays hosted** has a comment at its `runs-on` line saying why. The reason is always that running it on Windows would change what it builds or tests: an `apt-get` step such as the LibreOffice install, a shell script that needs `unzip` or `python3`, a `-r linux-x64` Native AOT publish, or a coverage difference.

**Bash on COBBLER1.** A routed job whose steps were already bash keeps `shell: bash`, so it runs the same script on either machine. `bash` on a Windows runner is Git Bash, from Git for Windows. COBBLER1 has it, because it checks out this repository. If it ever stops being present, that job fails at its first step; the fix is to install Git for Windows on COBBLER1, not to rewrite the script.

**Which jobs route, today.** `benchmarks.yml` already ran on COBBLER1 and is unchanged.

| Workflow | Self-hosted first | Hosted-only, with the reason in the file |
|---|---|---|
| `ci.yml` | — | `build` |
| `packages.yml` | `samples` | `pack`, `consumer`, `aot-packages`, `aot-host` |
| `sonarqube.yml` | — | `sonarqube` |
| `excel-addin.yml` | `build`, `publish` | — |
| `release.yml` | — | `release` |
| `main-source-guard.yml` | `main-source-is-dev` | — |
| `mutation.yml` | `mutate` | — |

### The Excel add-in

The add-in is a separate solution, and it builds only on Windows:

```
dotnet build RecDiffy.ExcelAddIn/RecDiffy.ExcelAddIn.slnx -c Release -warnaserror
dotnet test RecDiffy.ExcelAddIn/RecDiffy.ExcelAddIn.slnx -c Release
```

- **Its own CI.** [A separate workflow](.github/workflows/excel-addin.yml) builds and tests it. It runs only when `RecDiffy.ExcelAddIn/` changes.
- **Published packages.** The add-in uses the published `RecDiffy` and `RecDiffy.IO` prerelease packages, not the projects in `RecDiffy.NET`. An engine change reaches the add-in only after a new prerelease ships.
- **Read first.** Read the [add-in README](RecDiffy.ExcelAddIn/README.md) and the [Excel-DNA rules](AGENTS.md#excel-dna-add-in). In short, don't write Excel COM code unless AGENTS.md lists that use as approved. Ask the lead first.

## Branches and pull requests

- **Branch from `dev`, and open your PR into `dev`.**
- **`main` takes release merges from `dev` only.** A check rejects any other PR into `main`.
- **Never force-push or rebase `dev`, and don't create tags.** The maintainer cuts releases. Every release is a prerelease for now.
- **One change per PR.** Link the issue it addresses. Leave out cleanups the issue doesn't ask for.
- **Don't merge your own PR.**
- **Fork PRs skip the test report.** A PR from a fork doesn't upload the Allure test report, because GitHub doesn't pass the secrets to it. The build and tests still run.

## The rules for a change

Each rule below is a summary. Read the linked section of AGENTS.md before you start.

### Extend, not edit

Code on `main` has shipped, so it's closed. Add new code beside it: a new class, a new implementation of an existing interface, a wrapper, or a registration line. Don't add to or change a shipped type, unless the issue says CHANGE and names that type. A type that exists only on your branch is still yours to change.

New rules are new classes, registered by name. A core rule gets a `Register(...)` line in `RuleRegistry.CreateCore`, and a trading rule gets one in `TradingRulePack.AddTo`.

See [Extend, not edit](AGENTS.md#extend-not-edit) and [Where changes go](AGENTS.md#where-changes-go).

### Replace and repoint

To fix a bug in a shipped rule, don't edit it. Instead:

1. Write a new class in a new file. Don't call or wrap the old one.
2. Point the same registered name at the new class.
3. Tag the old class `[Obsolete]`, with the replacement's name, the defect and the issue.
4. Add a test file for the new class.

Profiles refer to rules by name, so every profile that uses the name gets the fix, and no profile changes. A maintainer deletes the old class later.

See [Replace and repoint](AGENTS.md#replace-and-repoint).

### New tests go in new files

Others add tests to the shared test files at the same time as you. Put your tests in a new file.

See [Working beside other agents](AGENTS.md#working-beside-other-agents).

### Report bugs with a skipped failing test

You may find a bug while you work on something else. Don't fix a shipped rule in the same PR. Instead:

1. Open an issue for it.
2. Mark it with 🔴 in your PR.
3. Add a test in a new file that fails because of the bug. Show that it fails, then skip it.

The fix is a replace-and-repoint PR of its own.

See [Test gotchas](AGENTS.md#test-gotchas).

### Also read

- [Functional C# first](AGENTS.md#functional-c-first): how C# is written here.
- [Test gotchas](AGENTS.md#test-gotchas): culture, the profile corpus, and testing obsolete rules.
- [Build gotcha](AGENTS.md#build-gotcha): no `[GeneratedRegex]` in test projects.

## Report a bug

Use the bug report form. It asks for:

- the package and its version;
- your configuration (the profile);
- what you expected and what happened;
- a minimal reproduction, with synthetic data only.

The smaller the reproduction, the faster the fix. A few rows per side and the shortest profile that still shows the bug are best. A failing xunit test is better still.
