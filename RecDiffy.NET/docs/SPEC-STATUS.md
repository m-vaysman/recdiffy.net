# Specification status

The architecture spec in force is **v3.1** (2026-11-13). Like v3 and v2, it is kept out of git; ask the maintainer for
`ARCHITECTURE-SPEC-v3.1.md` and read it before changing behavior it covers. The committed
[ARCHITECTURE-SPEC.md](ARCHITECTURE-SPEC.md) is v1 (2026-11-04), historical, and does not govern.

Tests cite the spec by section: §9.1 and §9.2 for profiles, §10.1 for determinism, §12.1 for verification status,
§12.2 for datasets. Keep those numbers stable when the spec is amended.

## Where v3.1 was amended to shipped behavior

v3.1 amends the spec to the behavior that ships rather than changing the code. Each amendment is recorded in the
spec's §14.1; this table is the short version, so a reader of the code does not "fix" it to match older spec text.

| Topic | v3 said | Shipped |
|---|---|---|
| Threading | normalization, consolidation, index build, candidate generation and diff run in parallel; `MaxDegreeOfParallelism` defaults to the processor count | every stage before result building runs on the calling thread; result building is parallel over committed entries; the effective degree defaults to 1 |
| Telemetry | spans per run, stage and pass, and a long list of counters; row keys with `RunOptions.TelemetryIncludesRowKeys` | one `RecDiffy.Run` span and three counters; the row-key option is reserved and has no effect |
| Target frameworks | `net10.0` | `net8.0` and `net10.0`; .NET 9 apps use the `net8.0` build; the `net8.0` target stays as long as customers need it |
| Result immutability | `ReconciliationResult` is immutable | a read-only view over the produced entries and rows |

## Rules

- **Report a divergence; don't resolve it silently.** The spec outranks the code (see [AGENTS.md](../../AGENTS.md),
  "Source of truth"). Changing the code to match a spec is a scoped task; amending the spec is a decision recorded in
  §14.1 with the evidence.
- **Support follows customers, not Microsoft's lifecycle.** `net8.0` and `net10.0` are both built and shipped, and an
  app on .NET 9 consumes the `net8.0` build. The `net8.0` target stays for as long as customers need it; there is no
  date-based drop when a runtime leaves Microsoft support.
- **Do not implement v1.** It documents APIs that never shipped, such as `RunOptions.FailFast` and the generic §11
  surface; it is kept for history only.
- **Say where a claim is checked.** A verified claim names a test, a CI job or a file; an unverified one stays
  unverified, as §12.1 does for the acceptance criteria.
