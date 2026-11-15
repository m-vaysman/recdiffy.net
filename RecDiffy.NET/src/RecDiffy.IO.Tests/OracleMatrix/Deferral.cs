namespace RecDiffy.IO.Tests.OracleMatrix;

// Why a test class leaves a case out of its passing checks. Each class checks every case in a skipped test, so a deferred
// case is never unchecked: it waits on the rule, the defect or the lead's decision its reason names.
internal static class Deferral
{
    public const string DirtyToClean = "this profile doesn't use price.dirtyToClean: a dirty street price is compared as written";

    public const string InferFees = "this profile doesn't use fees.inferFromNet: fees are not compared, so trading.netCash can't settle net cash as the oracle does";

    public const string CashToPerspective = "this profile doesn't use cash.toPerspective: a dealer-view row's cash is not negated";

    // True when any of the case's right rows holds the value in the column.
    public static bool RightHas(OracleDataset dataset, OracleCase item, string column, string value) =>
        item.Right.Any(key => dataset.RightRow(key) is { } row && SheetRows.Text(row.GetValueOrDefault(column)) == value);
}
