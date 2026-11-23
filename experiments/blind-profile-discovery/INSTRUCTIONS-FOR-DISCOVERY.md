# Blind profile discovery

Two CSV files in each `pairs/p01` … `pairs/p08` directory (`left.csv`, `right.csv`) are the same records, differing only in how they are written. Discover a RecDiffy profile for each pair from the harness below. Do not open `experiments/blind-profile-discovery/_hidden/`.

The profile language is [RecDiffy.NET/docs/PROFILES.md](../../RecDiffy.NET/docs/PROFILES.md). The harness is not part of `RecDiffy.slnx`. Run it from the repository root.

## peek

```shell
dotnet run --project experiments/blind-profile-discovery/harness -- peek <file> [more files] [--sample N] [--seed N]
```

Prints, for each file, the row count, the headers in file order, a seeded sample, and full-file column stats: null rate, distinct ratio, distinct count, an inferred type, value length, and either every distinct value (when there are 16 or fewer) or five examples. Numeric columns also get a min..max range.

`--sample` defaults to 6 and is capped at 12. `--seed` defaults to 1. The same file, sample size, and seed always return the same rows. Stats are computed over every data row.

CSV is comma-separated with a header row. `.xlsx`, `.xlsm`, `.xls`, and `.xlsb` are read from the first sheet.

## validate

```shell
dotnet run --project experiments/blind-profile-discovery/harness -- validate <profile.json> <left> <right>
```

Parses the profile, compiles it with the core rules plus the trading pack, and checks the columns it reads against both headers. Exit 0 when all three pass.

## run

```shell
dotnet run --project experiments/blind-profile-discovery/harness -- run <profile.json> <left> <right>
```

Does what `validate` does, then runs one reconciliation and prints a digest:

- the run summary
- composition counts, when any are non-zero
- a diagnostic histogram, when any were raised
- one line per field that broke or fell inside a tolerance: break count, null-versus-value count, same-sign rate, delta quantiles, and the constant right/left ratio when one ratio covers at least half of the numeric breaks
- up to three example row keys for each non-empty unmatched alignment (`leftOnly`, `rightOnly`, `ambiguous`, `searchIncomplete`)

Example keys are the profile's `sources.*.rowKey` values, or the row number when the profile sets none. A pass key or a search detail is appended when the engine recorded one. Delta means right minus left, after normalization.

Exit 0 when the profile ran, including when the digest shows breaks. Exit 2 when the profile or a file is rejected.

## Bounds

One profile edit per pass, and at most six passes per pair. Keep a log of each pass: what changed, why, and the digest counts.

Do not widen a tolerance, lower a scale, demote a field, or add a rule in order to hide a break. A tolerance or a scale belongs in the profile only when it describes how the two files represent the same number.
