# Contributing to RecDiffy.NET

This guide shows you how to build RecDiffy, test it, and submit a small, scoped change.

The rules for changing code live in [AGENTS.md](AGENTS.md). Every AI agent that works here follows them, and so do people. This guide sums up the ones you need and links to each one. When the two differ, AGENTS.md wins.

## Before you start

- **Search first.** Look through the open issues and pull requests. Several people and agents work here at the same time. If a PR already covers your change, build on it instead of opening a second one.
- **Open an issue for anything new.** For a feature or a change in behaviour, use the feature request form, so the scope is agreed before you write code. For a bug, use the bug report form.
- **Keep data synthetic.** Never put real client files, names, account numbers or trade IDs in an issue, a test or a PR. Make up a few rows that show the problem.
- **Keep security reports private.** Don't report a vulnerability in a public issue, discussion or PR. A private way to report one is being set up in [#241](https://github.com/m-vaysman/recdiffy.net/issues/241).

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
