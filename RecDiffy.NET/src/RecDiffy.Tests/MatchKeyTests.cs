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
    public void Equality_and_order_follow_ordinal_string_comparison(string left, string right)
    {
        var leftKey = MatchKey.FromEncoded(left);
        var rightKey = MatchKey.FromEncoded(right);
        var ordinalEqual = StringComparer.Ordinal.Equals(left, right);
        var expectedSign = Math.Sign(string.CompareOrdinal(left, right));

        Assert.Equal(ordinalEqual, leftKey.Equals(rightKey));
        Assert.Equal(ordinalEqual, leftKey == rightKey);
        Assert.Equal(ordinalEqual, MatchKey.OrdinalEquality.Equals(leftKey, rightKey));
        Assert.Equal(expectedSign, Math.Sign(leftKey.CompareTo(rightKey)));
        Assert.Equal(expectedSign, Math.Sign(MatchKey.Ordinal.Compare(leftKey, rightKey)));
        Assert.Equal(expectedSign < 0, leftKey < rightKey);
        Assert.Equal(expectedSign <= 0, leftKey <= rightKey);
        Assert.Equal(expectedSign > 0, leftKey > rightKey);
        Assert.Equal(expectedSign >= 0, leftKey >= rightKey);
        Assert.Equal(left, leftKey.ToString());
        Assert.Equal(leftKey.Encoded, leftKey.ToString());
        Assert.Equal(right, rightKey.ToString());
        Assert.Equal(rightKey.Encoded, rightKey.ToString());
        if (ordinalEqual)
            Assert.Equal(leftKey.GetHashCode(), rightKey.GetHashCode());
    }

    [Fact]
    public void FromEncoded_rejects_null_and_accepts_empty()
    {
        Assert.Throws<ArgumentNullException>(() => MatchKey.FromEncoded(null!));
        var empty = MatchKey.FromEncoded("");
        Assert.Equal("", empty.Encoded);
        Assert.Equal("", empty.ToString());
    }
}
