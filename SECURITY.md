# Security policy

RecDiffy reads files that other people produce, such as a broker's CSV or a counterparty's workbook. A flaw in how it reads them, or in the reports it writes, can reach everyone who runs it. This page says how to report one privately, and what happens next.

## What's covered

- The NuGet packages:
  - `RecDiffy`
  - `RecDiffy.IO`
  - `RecDiffy.Excel`
  - `RecDiffy.RulePacks.Trading`
- The Excel add-in, in [RecDiffy.ExcelAddIn](RecDiffy.ExcelAddIn).

## Supported versions

Every release is a prerelease for now, so only the latest prerelease gets fixes. A fix ships as a new prerelease.

| Component | Gets fixes |
|---|---|
| The four packages, latest prerelease on [nuget.org](https://www.nuget.org/packages/RecDiffy) | Yes |
| The four packages, any earlier prerelease | No. Update to the latest. |
| The `1.1.0-preview.<run>` builds that CI publishes to GitHub Packages from `main` | No. They aren't releases. Use the nuget.org prerelease. |
| The Excel add-in | It has no releases yet. Only a build from the latest `main` gets fixes. |

The add-in uses the published `RecDiffy` and `RecDiffy.IO` packages. A fix in those packages reaches the add-in when the add-in moves to the fixed prerelease.

## Report a vulnerability

Report it privately, through GitHub:

1. Open the repository's [Security tab](https://github.com/m-vaysman/recdiffy.net/security).
2. Choose **Report a vulnerability**. This opens a [private report](https://github.com/m-vaysman/recdiffy.net/security/advisories/new) that only you and the maintainer can see.

**Never report a vulnerability in a public issue, discussion or pull request.** Those are public, so everyone learns about the flaw before there's a fix.

## What to include

- **The package or the add-in, and its version.** For the add-in, the commit or the CI run you built it from.
- **The input or the steps that show the problem,** such as a file, a profile, or the code that sets up the run. If the input is a file you can't paste as text, include a short script or the steps that build it.
- **What an attacker would gain,** such as a crash, a hang, a formula in a report, or data sent somewhere.

Use synthetic data only. Never attach real client files, names, account numbers or trade IDs. Make up a file that shows the problem.

## What to expect

RecDiffy has one maintainer, so these are best-effort targets, not guarantees.

- **Acknowledgement:** within 7 days of your report.
- **Progress:** in the comments on your private report. You'll hear whether the maintainer accepts it as a vulnerability, how severe it is, and when a fix is planned.
- **A fix, or a plan for one:** within 90 days of your report. If a fix takes longer, the maintainer tells you why and when to expect it.
- **Not a vulnerability:** the maintainer says why and closes the report. If it's an ordinary bug, you can then report it in a public issue.

## How disclosure works

Disclosure is coordinated.

1. **The fix is made privately.** A GitHub security advisory comes with its own temporary private fork for this.
2. **The fix and the advisory are published together,** with the new prerelease.
3. **You're credited** in the advisory, if you want to be.

A published advisory for a NuGet package can enter the [GitHub Advisory Database](https://github.com/advisories). `dotnet restore` checks that database through [NuGet Audit](https://learn.microsoft.com/nuget/concepts/auditing-packages), so anyone who uses an affected version gets a warning.

Please don't disclose the vulnerability yourself until the advisory is published, or until you and the maintainer agree on a date.

## In scope

For example:

- An input file or a profile that crashes RecDiffy, makes it hang, or runs it out of memory, such as a zip bomb disguised as an `.xlsx`.
- A value from an input file that becomes a formula or a link in a written Excel report.
- The add-in reading or writing outside the files and ranges you picked.
- RecDiffy or the add-in sending your data anywhere.

## Out of scope

- Problems that need an already-compromised machine.
- Excel's own Trust Center behaviour, such as the notice it shows for an unsigned add-in.
- Vulnerabilities in RecDiffy's dependencies that are already reported upstream. Dependabot alerts cover those. If the way RecDiffy uses a dependency is what makes it exploitable, that's in scope.
