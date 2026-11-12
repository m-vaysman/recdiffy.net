namespace RecDiffy;

internal readonly record struct MatchKey : IComparable<MatchKey>
{
    private MatchKey(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        Encoded = encoded;
    }

    public string Encoded { get; }
    public static IComparer<MatchKey> Ordinal { get; } = Comparer<MatchKey>.Default;
    public static IEqualityComparer<MatchKey> OrdinalEquality { get; } = EqualityComparer<MatchKey>.Default;
    internal static MatchKey FromEncoded(string encoded) => new(encoded);
    public int CompareTo(MatchKey other) => string.CompareOrdinal(Encoded, other.Encoded);
    public static bool operator <(MatchKey left, MatchKey right) => left.CompareTo(right) < 0;
    public static bool operator <=(MatchKey left, MatchKey right) => left.CompareTo(right) <= 0;
    public static bool operator >(MatchKey left, MatchKey right) => left.CompareTo(right) > 0;
    public static bool operator >=(MatchKey left, MatchKey right) => left.CompareTo(right) >= 0;
    public override string ToString() => Encoded;
}
