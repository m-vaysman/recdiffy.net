using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// 🔴 identifier.map, symbol.map, text.copy, when clauses and side.resolve still turn a raw number into text with
// value.ToString(), which uses the server's culture. A decimal key 1.5 becomes "1,5" on a de-DE server. Follow-up to #158.
public class ServerCultureLeakTests
{
    [Fact(Skip = "🔴 Defect: identifier.map formats a raw number with the server's culture. Follow-up to #158.")]
    public void A_numeric_identifier_reads_the_same_on_every_server()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Id"] = 1.5m });

            CultureProfile.Rules("en-US")
                .Build("identifier.map", new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Id", ["to"] = "Id", ["scheme"] = "Id" })
                .Apply(ref row);

            Assert.Equal("1.5", row.Display("Id"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
