# Blind profile discovery, round 2: builder rules

These are the rules from issue #553 for the builder (agent 2). The auditor (agent 3) rules on every logged action against them.

## Setup

- The repository is on `origin/dev`. Read `AGENTS.md`.
- The agent docs are built from `dev` under `experiments/blind-profile-r2/_site`. The live site lags `dev`, so read `https://m-vaysman.github.io/recdiffy.net/<path>` as `_site/<path>`, and a `raw.githubusercontent.com/.../main/<path>` or `github.com/.../blob/main/<path>` link as the same `<path>` in the repository. Start at `_site/llms.txt`.
- Harness: `dotnet experiments/blind-profile-r2/_bin/BlindDiscovery.Harness.dll <command>`. The commands are:
  - `headers <file>...`: prints the column headers and nothing else.
  - `validate <profile> <left> <right>`
  - `run <profile> <left> <right>`: prints the summary, composition, diagnostics, field breaks with up to three engine-reported values, and up to three canonical example rows for each unmatched alignment.
- Your pair: `experiments/blind-profile-r2/pairs/pNN/left.csv` and `right.csv`. Your workspace: `experiments/blind-profile-r2/builder/pNN/`.

## Allowed

- Reading the column headers through `headers`.
- Running `validate` and `run`, and reading what they print.
- Reading the docs, `AGENTS.md`, the engine source and the harness source.
- Writing `profile-pass-N.json` (one per attempt), `log.md` and `report.md` in your own workspace.

## Not allowed

1. Opening, scanning or bulk-reading a pair's CSV other than through `headers`, `validate` and `run`. That covers Read, `cat`, `head`, `tail`, `grep`, `wc`, `Import-Csv`, `Get-Content`, a spreadsheet, and any other tool.
2. Writing scripts, code or queries that match, transform or reconcile the data outside RecDiffy.
3. Inserting tables, lookup lists or hand-built mappings that list individual values to force a match. A map over a small, closed code vocabulary is allowed, for example side codes, currency codes or status codes. Its entries must be codes that every row of the column draws from. A map whose entries are record identifiers, or whose size grows with the number of rows, is not allowed. Report a pattern you can't express as a general rule as an engine gap.
4. Reading anything outside the repository's `dev` tree as checked out, plus your own workspace. That includes `_hidden/`, any generator, answer key or reference profile, other branches (`git show`/`git checkout` of anything other than the current branch), other pairs' builder workspaces, the round-1 experiment (`experiments/blind-profile-discovery`, PRs #545 and #546), temp or scratch folders, and agent transcripts.
5. Changing engine code, rules, the harness or its build output.

## Log

Append every action to `log.md` as you go, numbered, in this order:

- the action: a file read (with its path), a harness command (verbatim), or a profile change,
- for a harness command, its `summary` line or its failure,
- for a profile change, what changed and why.

## Report

`report.md` holds:

- the final profile's file name, and the match rate after each pass,
- a confidence level and the evidence behind it,
- either "Ready for human approval." or "My confidence is not high. Here is the profile I built, and here is where a human needs to finish," followed by the specific open items,
- any engine gaps, and the rule that would close each one.
