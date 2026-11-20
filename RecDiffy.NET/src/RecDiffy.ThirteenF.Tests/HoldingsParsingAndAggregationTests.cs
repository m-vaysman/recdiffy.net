namespace RecDiffy.ThirteenF.Tests;

public sealed class HoldingsParsingAndAggregationTests
{
    [Fact]
    public void Pre2023_filing_multiplies_values_by_one_thousand_and_aggregates_by_cusip_putcall_and_amount_type()
    {
        var xml = TestFixtures.ReadText("holdings-pre2023-thousands.xml");
        var raw = SecParsers.ParseInformationTableXml(xml, new DateOnly(2022, 11, 14));
        var aggregated = SecParsers.AggregateHoldings(raw);

        Assert.Equal(3, aggregated.Length);

        var common = Assert.Single(aggregated, item => item is { Cusip: "000111AA1", PutCall: null, AmountType: "SH" });
        Assert.Equal("ACME SYNTHETIC CORP", common.Issuer);
        Assert.Equal(16_000m, common.Shares);
        Assert.Equal(2_000_000m, common.ValueUsd);

        var callOption = Assert.Single(aggregated, item => item is { Cusip: "000111AA1", PutCall: "Call", AmountType: "SH" });
        Assert.Equal(5_000m, callOption.Shares);
        Assert.Equal(300_000m, callOption.ValueUsd);

        var notePrincipal = Assert.Single(aggregated, item => item is { Cusip: "000111AA1", PutCall: null, AmountType: "PRN" });
        Assert.Equal("NOTE 2.000%", notePrincipal.TitleOfClass);
        Assert.Equal(5_000_000m, notePrincipal.Shares);
        Assert.Equal(5_000_000m, notePrincipal.ValueUsd);
    }

    [Theory]
    [InlineData("2023-01-02", 1_250_000)]
    [InlineData("2023-01-03", 1_250)]
    [InlineData("2023-02-14", 1_250)]
    public void Thousands_to_dollars_cutoff_triggers_strictly_before_2023_01_03_filing_date(string filedIso, int expectedFirstValueUsd)
    {
        var xml = TestFixtures.ReadText("holdings-pre2023-thousands.xml");
        var filed = DateOnly.ParseExact(filedIso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var raw = SecParsers.ParseInformationTableXml(xml, filed);

        Assert.Equal((decimal)expectedFirstValueUsd, raw[0].ValueUsd);
    }

    [Fact]
    public void Prefixed_namespace_information_table_parses_and_normalizes_putcall_casing()
    {
        var xml = TestFixtures.ReadText("holdings-q2-base.xml");
        var raw = SecParsers.ParseInformationTableXml(xml, new DateOnly(2026, 8, 14));

        Assert.Equal(4, raw.Length);
        var warrantPut = Assert.Single(raw, item => item.Cusip == "555555505");
        Assert.Equal("Put", warrantPut.PutCall);
        Assert.Equal(100m, warrantPut.Shares);
        Assert.Equal(50_000m, warrantPut.ValueUsd);
    }

    [Fact]
    public void Cover_page_xml_is_rejected_by_TryParseInformationTableXml_and_dtd_is_prohibited()
    {
        var coverXml = TestFixtures.ReadText("cover-restatement.xml");
        Assert.False(SecParsers.TryParseInformationTableXml(coverXml, new DateOnly(2026, 11, 16), 0, out _));

        const string DtdXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE informationTable [<!ENTITY xxe "blocked">]>
            <informationTable />
            """;

        Assert.Throws<InvalidDataException>(() => SecParsers.ParseInformationTableXml(DtdXml, new DateOnly(2026, 11, 16)));
    }
}
