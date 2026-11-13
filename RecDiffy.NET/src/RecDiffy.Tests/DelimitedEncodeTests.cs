namespace RecDiffy.Tests;

// #403: Delimited.Encode no longer builds its escape strings on every call, and returns a value with nothing to
// escape as it is. The text it writes must not change, because keys, fingerprints and the profile hash are made of it.
public class DelimitedEncodeTests
{
    private static readonly char[] Reserved = ['\r', '\n', '\u001f', '|', ',', '='];

    // Half of the alphabet is reserved or a backslash, so most random parts need escaping and many need several kinds.
    private static readonly char[] Alphabet = ['\\', '\r', '\n', '\u001f', '|', ',', '=', '.', ';', 'a', 'B', '7', ' ', 'é'];

    public static TheoryData<char> Separators => new() { '\u001f', '=', ',', '|', '.', ';' };

    [Theory]
    [MemberData(nameof(Separators))]
    public void Join_writes_the_text_it_wrote_before(char separator)
    {
        var random = new Random(403 + separator);
        var tuples = Enumerable.Range(0, 2_000).Select(_ => Enumerable.Range(0, random.Next(1, 5)).Select(_ => Part(random)).ToArray());

        foreach (var tuple in tuples)
            Assert.Equal(JoinBefore(separator, tuple), Delimited.Join(separator, tuple));
    }

    [Fact]
    public void Escape_writes_the_text_it_wrote_before()
    {
        var random = new Random(403);

        foreach (var part in Enumerable.Range(0, 5_000).Select(_ => Part(random)))
            Assert.Equal(EncodeBefore(part, Reserved), Delimited.Escape(part));
    }

    [Theory]
    [InlineData("")]
    [InlineData("XS0123456789")]
    [InlineData("2025-06-10")]
    [InlineData("a.b;c d")]
    public void A_value_with_nothing_to_escape_is_returned_as_it_is(string part) =>
        Assert.Same(part, Delimited.Escape(part));

    // '.' and ';' are not reserved, so a join on one of them has to escape it in the parts. The fast path must not
    // let such a part through.
    [Theory]
    [InlineData('.', "a.b", @"a\.b")]
    [InlineData(';', "a;b", @"a\;b")]
    [InlineData('.', "plain", "plain")]
    public void A_join_on_a_separator_that_is_not_reserved_escapes_it_in_the_parts(char separator, string part, string escaped) =>
        Assert.Equal(escaped + separator + "x", Delimited.Join(separator, [part, "x"]));

    private static string Part(Random random) =>
        new([.. Enumerable.Range(0, random.Next(0, 9)).Select(_ => Alphabet[random.Next(Alphabet.Length)])]);

    // Delimited as it was before #403, kept here as the reference.
    private static string JoinBefore(char separator, IEnumerable<string> parts) =>
        string.Join(separator, parts.Select(part => EncodeBefore(part, Reserved.Contains(separator) ? Reserved : [.. Reserved, separator])));

    private static string EncodeBefore(string part, IReadOnlyList<char> marks)
    {
        var escaped = part.Replace("\\", "\\\\", StringComparison.Ordinal);
        foreach (var mark in marks)
            escaped = escaped.Replace(mark.ToString(), "\\" + mark, StringComparison.Ordinal);
        return escaped;
    }
}
