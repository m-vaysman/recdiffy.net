namespace RecDiffy.Extensions;

internal static class Statuses
{
    public const string Ambiguous = "Ambiguous";
    public const string LeftOnly = "LeftOnly";
    public const string RightOnly = "RightOnly";

    private const string Tick = "✅";
    private const string Yellow = "\U0001F7E1";
    private const string Red = "\U0001F534";
    private const string Warning = "⚠️";
    private const string LeftArrow = "←";
    private const string RightArrow = "→";
    private const string LeftEmoji = "⬅️";
    private const string RightEmoji = "➡️";

    public static int Rank(string status) => status switch
    {
        "Exact" => 0,
        "WithinTolerance" => 1,
        "NotCompared" => 2,
        "Break" => 3,
        Ambiguous => 4,
        LeftOnly => 5,
        RightOnly => 6,
        _ => 7,
    };

    // The arrows are plain text glyphs. The emoji arrows are drawn by the system as boxes in which the direction is hard to see.
    public static string Badge(string status) => status switch
    {
        "Exact" => Tick,
        "WithinTolerance" => Yellow,
        "Break" => Red,
        Ambiguous => Warning,
        LeftOnly => LeftArrow,
        RightOnly => RightArrow,
        _ => "",
    };

    // A caller's own grid may use a badge itself as its status, with or without the emoji variation selector.
    public static string RowClass(string status) => status switch
    {
        Tick or "Exact" => "ok",
        Yellow or "WithinTolerance" => "warn",
        Red or "Break" => "bad",
        Warning or "⚠" or Ambiguous => "amb",
        LeftArrow or LeftEmoji or "⬅" or RightArrow or RightEmoji or "➡" or LeftOnly or RightOnly => "orphan",
        _ => "",
    };
}
