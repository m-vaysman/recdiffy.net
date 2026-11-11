namespace RecDiffy.IO.Tests.OracleMatrix;

// Spec v3 §6.5 and #39: every oracle label maps onto the result axes, and a label the map doesn't know fails.
public class OracleLabelTests
{
    [Theory]
    [MemberData(nameof(OracleDatasets.Names), MemberType = typeof(OracleDatasets))]
    public void Every_label_in_the_oracle_maps_onto_the_result_axes(string name)
    {
        var cases = OracleDatasets.Named(name).Cases();

        var unknown = cases.Where(item => Record.Exception(() => item.Kind) is not null).Select(item => $"{item.Id}: {item.Label}").ToArray();

        Assert.NotEmpty(cases);
        Assert.True(unknown.Length == 0, string.Join(Environment.NewLine, unknown));
    }

    [Theory]
    [InlineData("MATCHED")]
    [InlineData("match_exact_after_normalization")]
    [InlineData("MATCH_AGGREGATED")]
    [InlineData("MATCH_AGGREGATED (BLOCK_NETTING)")]
    [InlineData("DK_UNCONFIRMED")]
    [InlineData("")]
    public void A_label_outside_the_vocabulary_fails_instead_of_being_skipped(string label)
    {
        var item = new OracleCase("case", label, ["L1"], ["R1"]);

        var error = Assert.Throws<UnknownOracleLabelException>(() => item.Kind);

        Assert.Equal(label, error.Label);
    }
}
