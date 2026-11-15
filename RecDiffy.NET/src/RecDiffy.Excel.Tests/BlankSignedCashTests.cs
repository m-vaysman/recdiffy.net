using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

// #429: Result_Rows.signedCash is blank, not 0, for a row whose net cash is unknown; BO6 reads it only through SUMIFS, which skips text.
public class BlankSignedCashTests
{
    [Fact]
    public void Signed_cash_is_blank_not_zero_when_net_cash_is_not_a_number()
    {
        var outcome = Assert.Single(SyntheticData.Result.WriteAll(Paths.Scratch("signed-cash"), SyntheticData.Options(ReportKind.CashLadder)));
        var rows = Book.Read(outcome.Path!)["Result_Rows"];
        var column = new string([.. rows.Values.Single(cell => cell.Value == "signedCash").Address.TakeWhile(char.IsLetter)]);
        var formula = rows[$"{column}2"].Formula;

        Assert.StartsWith("IF(AND(ISNUMBER(", formula, StringComparison.Ordinal);
        Assert.EndsWith(",\"\")", formula, StringComparison.Ordinal);
    }
}
