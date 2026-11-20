namespace RecDiffy.Fix.Tests;

public class FixReaderTests
{
    [Fact]
    public void A_well_formed_message_is_accepted_with_its_fields_in_order()
    {
        var reading = Assert.IsType<FixAccepted>(FixReader.ReadMessage(Wire.Ack("0", "E1", "C1", null, "100"), Wire.Options));

        Assert.Equal(("8", "C1", "100"), (reading.Message.MsgType, reading.Message.Get(11), reading.Message.Get(38)));
        Assert.Equal([8, 9, 35], reading.Message.Fields.Take(3).Select(field => field.Tag));
    }

    [Theory]
    [InlineData("10=000", "CheckSum (10)")]
    [InlineData("9=999", "BodyLength (9)")]
    public void A_wrong_checksum_or_body_length_is_rejected(string tamper, string reason)
    {
        var good = Wire.Ack("0", "E1", "C1", null, "100");
        var tag = tamper.Split('=')[0] + "=";
        var start = good.IndexOf(FixOptions.Soh + tag, StringComparison.Ordinal) + 1;
        var bad = good[..start] + tamper + good[good.IndexOf(FixOptions.Soh, start)..];

        var rejected = Assert.IsType<FixRejected>(FixReader.ReadMessage(bad, Wire.Options));

        Assert.StartsWith(reason, rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Latin_1_text_is_accepted_and_anything_beyond_it_is_rejected()
    {
        var latin1 = Wire.Message("8", (11, "C1"), (17, "E1"), (150, "0"), (58, "Société Générale"));
        var beyond = Wire.Message("8", (11, "C1"), (17, "E1"), (150, "0"), (58, "Ŝ"));

        Assert.Equal("Société Générale", Assert.IsType<FixAccepted>(FixReader.ReadMessage(latin1, Wire.Options)).Message.Get(58));
        Assert.Contains("Latin-1", Assert.IsType<FixRejected>(FixReader.ReadMessage(beyond, Wire.Options)).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_value_is_rejected()
    {
        var text = Wire.Message("8", (11, "C1"), (17, "E1"), (150, "0"), (58, ""));

        Assert.Equal("field 58 has an empty value", Assert.IsType<FixRejected>(FixReader.ReadMessage(text, Wire.Options)).Reason);
    }

    [Fact]
    public void A_header_out_of_order_is_rejected()
    {
        var good = Wire.Ack("0", "E1", "C1", null, "100");
        var bad = good.Replace("\u00019=", "\u0001X=", StringComparison.Ordinal);

        Assert.IsType<FixRejected>(FixReader.ReadMessage(bad, Wire.Options));
    }

    [Fact]
    public void A_pipe_delimited_log_is_checked_as_if_it_held_soh()
    {
        var text = Wire.Message("8", "FIX.4.4", '|', Wire.Broker, Wire.Us, (11, "C1"), (17, "E1"), (150, "0"));

        Assert.IsType<FixAccepted>(FixReader.ReadMessage(text, Wire.Options with { Delimiter = '|' }));
    }

    [Fact]
    public void A_repeated_tag_outside_a_group_is_rejected_but_a_group_may_repeat()
    {
        var duplicate = Wire.Message("8", (11, "C1"), (11, "C2"), (17, "E1"), (150, "0"));
        var group = Wire.Message("J", (70, "AL1"), (71, "0"), (78, "2"), (79, "FUND-A"), (80, "100"), (79, "FUND-B"), (80, "50"));

        Assert.Contains("tag 11 repeats", Assert.IsType<FixRejected>(FixReader.ReadMessage(duplicate, Wire.Options)).Reason, StringComparison.Ordinal);
        Assert.IsType<FixAccepted>(FixReader.ReadMessage(group, Wire.Options));
    }

    [Fact]
    public void An_equity_allocation_instruction_with_orders_accounts_parties_and_fees_is_accepted()
    {
        var text = Wire.Message("J",
        [
            (70, "AL1"), (71, "0"), (626, "1"), (54, "1"), (55, "IBM"), (53, "300"), (6, "150.25"), (75, "20261119"),
            (73, "2"), (11, "C1"), (37, "O1"), (38, "200"), (11, "C2"), (37, "O2"), (38, "100"),
            (78, "2"),
            (79, "FUND-A"), (80, "200"), (467, "IA1"), (539, "1"), (524, "BRK"), (525, "D"), (538, "4"), (136, "1"), (137, "12.50"), (138, "USD"), (139, "7"),
            (79, "FUND-B"), (80, "100"), (467, "IA2"), (539, "1"), (524, "BRK"), (525, "D"), (538, "4"), (136, "1"), (137, "6.25"), (138, "USD"), (139, "7"),
            (453, "2"), (448, "US"), (447, "D"), (452, "13"), (448, "GS"), (447, "D"), (452, "1"),
        ]);

        Assert.IsType<FixAccepted>(FixReader.ReadMessage(text, Wire.Options));
        Assert.Equal(["AL1/FUND-A", "AL1/FUND-B"], FixRows.Read(text, Wire.Options).Allocations.Select(row => row["AllocRow"]), StringComparer.Ordinal);
    }

    [Fact]
    public void A_confirmation_with_settlement_parties_nested_two_deep_is_accepted()
    {
        var text = Wire.Message("AK",
        [
            (664, "CF1"), (666, "0"), (773, "2"), (665, "4"), (70, "AL1"), (467, "IA1"), (79, "FUND-A"), (80, "200"), (6, "150.25"),
            (381, "30050"), (118, "30062.50"),
            (862, "1"), (528, "A"), (863, "200"),
            (2639, "1"), (2640, "12.50"), (2641, "3"),
            (136, "2"), (137, "1.00"), (139, "4"), (137, "0.50"), (139, "2"),
            (85, "1"), (165, "1"), (787, "S"), (781, "3"), (782, "DTCYUS33"), (784, "10"), (782, "BRKUS33"), (784, "27"), (782, "AGENTUS33"), (784, "30"), (801, "1"), (785, "123"), (786, "10"),
            (1907, "1"), (1903, "UTI1"), (1905, "1"), (1904, "1"), (1906, "0"),
        ]);

        Assert.IsType<FixAccepted>(FixReader.ReadMessage(text, Wire.Options));
    }

    [Fact]
    public void A_nested_group_whose_counts_do_not_add_up_is_rejected()
    {
        var text = Wire.Message("J",
        [
            (70, "AL1"), (71, "0"), (78, "2"),
            (79, "FUND-A"), (80, "200"), (136, "1"), (137, "12.50"), (139, "7"),
            (79, "FUND-B"), (80, "100"), (136, "1"), (137, "6.25"), (139, "7"), (137, "1.00"), (139, "4"),
        ]);

        Assert.Equal("group 136 declares 2 entries but has 3", Assert.IsType<FixRejected>(FixReader.ReadMessage(text, Wire.Options)).Reason);
    }

    [Fact]
    public void A_group_with_the_wrong_number_of_entries_is_rejected()
    {
        var text = Wire.Message("J", (70, "AL1"), (71, "0"), (78, "3"), (79, "FUND-A"), (80, "100"), (79, "FUND-B"), (80, "50"));

        Assert.Contains("declares 3 entries but has 2", Assert.IsType<FixRejected>(FixReader.ReadMessage(text, Wire.Options)).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_data_field_may_hold_the_delimiter()
    {
        var text = Wire.Message("8", (11, "C1"), (17, "E1"), (150, "0"), (95, "5"), (96, "ab\u0001cd"));

        var accepted = Assert.IsType<FixAccepted>(FixReader.ReadMessage(text, Wire.Options));

        Assert.Equal("ab\u0001cd", accepted.Message.Get(96));
    }

    [Fact]
    public void Concatenated_frames_split_by_body_length_and_a_truncated_one_is_rejected()
    {
        var first = Wire.Ack("0", "E1", "C1", null, "100");
        var second = Wire.Ack("0", "E2", "C2", null, "200");
        var truncated = Wire.Ack("0", "E3", "C3", null, "300")[..40];

        var readings = FixReader.Read(first + second + "\n" + truncated, Wire.Options);

        Assert.Equal([typeof(FixAccepted), typeof(FixAccepted), typeof(FixRejected)], readings.Select(reading => reading.GetType()));
        Assert.Contains("truncated", ((FixRejected)readings[2]).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Throw_on_rejected_throws_instead_of_labelling()
    {
        var bad = Wire.Ack("0", "E1", "C1", null, "100")[..^4] + "999\u0001";

        var thrown = Assert.Throws<FixRejectedException>(() => FixReader.ReadMessage(bad, Wire.Options with { ThrowOnRejected = true }));

        Assert.StartsWith("CheckSum (10)", thrown.Rejected.Reason, StringComparison.Ordinal);
    }
}
