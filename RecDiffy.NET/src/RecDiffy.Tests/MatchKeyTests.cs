namespace RecDiffy.Tests;

public class MatchKeyTests
{
    [Theory]
    [InlineData("a", "A")]
    [InlineData("isin\u001eX", "cusip\u001eX")]
    [InlineData("A\u001fB", "A\\u001fB")]
    [InlineData("A\\\u001fB", "A\u001f\\B")]
    [InlineData("\u00e9", "e\u0301")]
    [InlineData("", "a")]
    [InlineData("same", "same")]
    public void Equality_comparison_operators_and_comparers_match_ordinal_string_semantics(string leftText, string rightText)
    {
        var left = MatchKey.FromEncoded(leftText);
        var right = MatchKey.FromEncoded(rightText);
        var expectedEqual = StringComparer.Ordinal.Equals(leftText, rightText);
        var expectedSign = Math.Sign(string.CompareOrdinal(leftText, rightText));

        Assert.Equal(expectedEqual, left.Equals(right));
        Assert.Equal(expectedEqual, left == right);
        Assert.Equal(!expectedEqual, left != right);
        Assert.Equal(expectedEqual, MatchKey.OrdinalEquality.Equals(left, right));

        Assert.Equal(expectedSign, Math.Sign(left.CompareTo(right)));
        Assert.Equal(expectedSign, Math.Sign(MatchKey.Ordinal.Compare(left, right)));

        Assert.Equal(expectedSign < 0, left < right);
        Assert.Equal(expectedSign <= 0, left <= right);
        Assert.Equal(expectedSign > 0, left > right);
        Assert.Equal(expectedSign >= 0, left >= right);

        if (expectedEqual)
        {
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
            Assert.Equal(MatchKey.OrdinalEquality.GetHashCode(left), MatchKey.OrdinalEquality.GetHashCode(right));
        }
    }

    [Fact]
    public void FromEncoded_rejects_null_and_accepts_an_empty_string()
    {
        Assert.Throws<ArgumentNullException>(() => MatchKey.FromEncoded(null!));

        var empty = MatchKey.FromEncoded("");
        Assert.Equal("", empty.Encoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("isin\u001eUS0378331005\u001fRATES")]
    public void ToString_returns_Encoded(string encoded)
    {
        var key = MatchKey.FromEncoded(encoded);

        Assert.Equal(encoded, key.Encoded);
        Assert.Equal(encoded, key.ToString());
    }
}
