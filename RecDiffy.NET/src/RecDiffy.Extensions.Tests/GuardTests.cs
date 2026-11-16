using System.Data;
using RecDiffy.Results;

namespace RecDiffy.Extensions.Tests;

public sealed class GuardTests
{
    [Fact]
    public void Every_method_rejects_a_null_receiver()
    {
        RecResult<OmsFill, BrokerConfirm> result = null!;
        ISideBySideView view = null!;
        DataTable table = null!;
        IEnumerable<StatusLine> rows = null!;
        var spec = new SideBySideSpec([], [], [], null);

        Action[] tableCalls =
        [
            () => result.ToReconciliationTable().Dispose(),
            () => result.ToSummaryTable().Dispose(),
            () => result.ToStatusCountsTable().Dispose(),
            () => result.ToSideBySideTable(spec).Dispose(),
            () => view.ToSideBySideTable().Dispose(),
        ];
        Func<string>[] htmlCalls =
        [
            () => result.ToReconciliationHtml(),
            () => result.ToSummaryHtml(),
            () => result.ToStatusCountsHtml(),
            () => result.ToSideBySideHtml(spec),
            () => view.ToSideBySideHtml(),
            () => table.ToHtmlTable(),
            () => rows.ToHtmlTable(),
        ];

        Assert.All(tableCalls, call => Assert.Throws<ArgumentNullException>(call));
        Assert.All(htmlCalls, call => Assert.Throws<ArgumentNullException>(call));
    }

    [Fact]
    public void A_null_spec_is_rejected()
    {
        var result = Samples.OmsBroker();

        Assert.Throws<ArgumentNullException>(() => result.ToSideBySideTable(null!).Dispose());
        Assert.Throws<ArgumentNullException>(() => result.ToSideBySideHtml(null!));
    }
}
