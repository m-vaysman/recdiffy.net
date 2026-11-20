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
