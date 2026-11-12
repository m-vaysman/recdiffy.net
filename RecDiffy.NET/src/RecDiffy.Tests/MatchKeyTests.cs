namespace RecDiffy.Tests;

// #350 Phase 1: MatchKey equality, order and hashing are ordinal, and "" is a valid key while null is not.
public class MatchKeyTests
{
    public static TheoryData<string, string> Pairs => new()
    {
        { "a", "A" },
        { "isin\u001eX", "cusip\u001eX" },
        { "A\u001fB", "A\\u001fB" },
        { "A\\\u001fB", "A\u001f\\B" },
        { "\u00e9", "e\u0301" },
        { "", "a" },
        { "same", "same" },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Equality_and_ordinal_equality_follow_string_ordinal(string first, string second)
    {
        var left = MatchKey.FromEncoded(first);
        var right = MatchKey.FromEncoded(second);
        var expected = StringComparer.Ordinal.Equals(first, second);

        Assert.Equal(expected, left.Equals(right));
        Assert.Equal(expected, left == right);
        Assert.Equal(expected, MatchKey.OrdinalEquality.Equals(left, right));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void CompareTo_and_ordinal_comparer_follow_string_compare_ordinal(string first, string second)
    {
        var left = MatchKey.FromEncoded(first);
        var right = MatchKey.FromEncoded(second);
        var expected = Math.Sign(string.CompareOrdinal(first, second));

        Assert.Equal(expected, Math.Sign(left.CompareTo(right)));
        Assert.Equal(expected, Math.Sign(MatchKey.Ordinal.Compare(left, right)));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Comparison_operators_agree_with_the_ordinal_sign(string first, string second)
    {
        var left = MatchKey.FromEncoded(first);
        var right = MatchKey.FromEncoded(second);
        var expected = Math.Sign(string.CompareOrdinal(first, second));

        Assert.Equal(expected < 0, left < right);
        Assert.Equal(expected <= 0, left <= right);
        Assert.Equal(expected > 0, left > right);
        Assert.Equal(expected >= 0, left >= right);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Equal_keys_have_equal_hashes(string first, string second)
    {
        var left = MatchKey.FromEncoded(first);
        var right = MatchKey.FromEncoded(second);

        if (StringComparer.Ordinal.Equals(first, second))
        {
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }
    }

    [Fact]
    public void FromEncoded_rejects_null_and_accepts_the_empty_key()
    {
        Assert.Throws<ArgumentNullException>(() => MatchKey.FromEncoded(null!));

        Assert.Equal("", MatchKey.FromEncoded("").Encoded);
    }

    [Fact]
    public void ToString_returns_the_encoded_key()
    {
        Assert.Equal("isin\u001eX", MatchKey.FromEncoded("isin\u001eX").ToString());
        Assert.Equal("", MatchKey.FromEncoded("").ToString());
    }
}
