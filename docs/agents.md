# Use RecDiffy.NET with your AI agent

Give your coding agent one link, then ask it for what you need. The link is the root of a tree of `llms.txt` files. The agent reads the root, then follows links to the pages its task needs, and nothing else.

## 1. Point your agent to the docs

```text
https://m-vaysman.github.io/recdiffy.net/docs/llms.txt
```

Paste this into the agent's chat:

```text
Read https://m-vaysman.github.io/recdiffy.net/docs/llms.txt and follow its links to the pages each task needs.
Use RecDiffy.NET for my reconciliations. Install its packages with --prerelease.
```

For a coding agent that works in one repository, put that line in the repository's own agent instructions (`AGENTS.md`, `CLAUDE.md`, `.cursorrules` or `.github/copilot-instructions.md`), so every session starts from it.

## 2. Ask

Some prompts to start from. Name your own files, types and fields.

**Build a reconciliation**

```text
Using RecDiffy, build a profile that reconciles our trade blotter (blotter.csv) against the broker's confirms
(confirms.xlsx, sheet "Confirms"). Match on the trade reference first, then on ISIN and side. Compare quantity
exactly and price to 0.0001.
```

```text
Using RecDiffy, reconcile these two lists of C# records, List<Position> from our books and List<CustodyLine>
from the custodian, by fund and ISIN, and show me every break and every one-sided row.
```

```text
Using RecDiffy, reconcile our cash ledger against the bank statement. Several ledger lines can add up to one
statement line. Amounts may differ by 0.01.
```

**Understand a result**

```text
Explain why this row is a break: <paste the entry, or the row keys on each side>.
```

```text
Why is row T-1043 quarantined, and what do I change in the profile to fix it?
```

```text
Why are these rows Ambiguous instead of matched? Suggest a stricter key.
```

**FIX messages**

```text
Reconcile these FIX execution reports from our OMS against the broker's drop copy, and list every
message RecDiffy rejected or skipped, with the reason.
```

**Change what you have**

```text
Rewrite this JSON profile with ProfileBuilder, and check it compiles to the same hash.
```

```text
The broker sends cancels and corrections. Collapse them before matching, so only the surviving trade is compared.
```

## What your agent will find

| Section | What it covers |
|---|---|
| [Start here](start.md) | Install, which API fits which job, and the rules that keep a result correct |
| [Engine](engine/llms.txt) | Compile and run, read the result, push rows in batches |
| [Profiles](profiles/llms.txt) | Fields and roles, normalization rules, consolidation, matching passes, tolerances, errors and diagnostics |
| [Profiles in code](builders/llms.txt) | RecBuilder with typed lambdas; ProfileBuilder, SideBuilder and PathBuilder |
| [Files](io/llms.txt) | CSV and Excel files, objects and data readers |
| [FIX](fix/llms.txt) | RecDiffy.Fix and the FIX reconciliation recipe |

Everything is also in one file, [llms-full.txt](https://m-vaysman.github.io/recdiffy.net/llms-full.txt), for an agent that would rather load it all at once.

This page is for people who use RecDiffy.NET. An agent that changes the RecDiffy.NET repository itself follows [AGENTS.md](../AGENTS.md).
