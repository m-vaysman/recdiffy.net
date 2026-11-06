# RecDiffy samples

A console program that shows the application API. Each file is one example. `Program.cs` runs them in order and returns 0 when every example has the outcome described below.

From the `RecDiffy.NET` directory:

```powershell
dotnet run --project src/RecDiffy.Samples/RecDiffy.Samples.csproj
```

## 1. Core reconcile

[FirstReconciliation.cs](FirstReconciliation.cs) compiles a profile with `RuleRegistry.CreateCore()`, maps warehouse bins and a book position with `RecordSources.FromObjects`, and calls `Reconciler.Run` with `ReferenceDataSet.Empty`. Bins of 4 and 6 match one book row of 10: one `Matched` entry, `ManyToOne`, `Exact`, and three `Matched` rows.

## 2. Read the result

[ResultReading.cs](ResultReading.cs) stays on the core registry. It prints three runs: a matched pair whose quantity comparison is `Break`, a `LeftOnly` row beside an exact match, and a row quarantined because its source key is missing.

## 3. Side-by-side

[SideBySideReport.cs](SideBySideReport.cs) calls `BuildSideBySide` with a `Quantity` measure. The printed cell splits the quantity into left, right, delta, left-only, right-only, and matched delta.

## 4. Async sources

[AsyncRun.cs](AsyncRun.cs) passes the warehouse rows as an `IAsyncEnumerable` and calls `Reconciler.RunAsync`. The summary matches the synchronous run of the same rows.

## 5. Reference data and the trading pack

[TradingReference.cs](TradingReference.cs) registers the trading rules with `TradingRulePack.AddTo` and passes a `ReferenceDataSet`. The book side resolves the alias `BOFA_MLPF&S` and parses `12/06/2025` with the `BAML` date format. The street side resolves LEI `EL495T1V227XABYD2Q18`, parses `96-02+`, and takes the absolute quantity. Both sides resolve a buy. The canonical counterparty is `BAML`, the price is `96.078125`, and the entry is an exact match.

## 6. Excel reports

[ExcelReports.cs](ExcelReports.cs) calls `RecDiffyExcel.RunAndWrite` on a small book and street file with net cash. It writes one `.xlsx` per report to `recdiffy-samples/excel` under the temp folder and prints each file, or why a report was skipped (no lifecycle chains, allocations or tolerances in this data). The blotter, side-by-side, cash ladder, control totals and scorecard are written.

## 7. Write a profile without JSON

[BuiltProfile.cs](BuiltProfile.cs) builds the warehouse profile with `ProfileBuilder`, prints its JSON with `ToJson()`, and runs it: one `Matched`, `ManyToOne`, `Exact` entry, as in example 1. A misspelled pass field comes back as an `UndeclaredPassField` problem at `paths[typo].passes[P1].key[0]`. `ProfileCheck.AgainstHeaders` reports that a left file with a `Qty` column lacks `Quantity`, and suggests `Qty`.
