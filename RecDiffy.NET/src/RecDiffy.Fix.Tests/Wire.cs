namespace RecDiffy.Fix.Tests;

// Builds well-formed FIX messages: BodyLength and CheckSum are computed, so a test breaks only what it means to.
internal static class Wire
{
    public const string Us = "US";

    public const string Broker = "GS";

    public static readonly FixOptions Options = new(Us);

    public static string Message(string msgType, params (int Tag, string Value)[] body) => Message(msgType, "FIX.4.4", FixOptions.Soh, Broker, Us, body);

    public static string Message(string msgType, string version, char delimiter, string sender, string target, params (int Tag, string Value)[] body)
    {
        var fields = string.Concat(body.Select(field => $"{field.Tag.ToInvariant()}={field.Value}{delimiter}"));
        var text = $"35={msgType}{delimiter}49={sender}{delimiter}56={target}{delimiter}34=1{delimiter}52=20261119-15:00:00.000{delimiter}{fields}";
        var head = $"8={version}{delimiter}9={text.Length.ToInvariant()}{delimiter}";
        var sum = (head + text).Sum(c => c == delimiter ? 1 : c) % 256;
        return $"{head}{text}10={sum.ToInvariant("000")}{delimiter}";
    }

    public static string Ack(string execType, string execId, string clOrdId, string? origClOrdId, string qty, string status = "0") =>
        Message("8",
        [
            (37, "O-" + clOrdId), (11, clOrdId), .. origClOrdId is null ? Array.Empty<(int, string)>() : [(41, origClOrdId)],
            (17, execId), (150, execType), (39, status), (1, "ACC1"), (55, "IBM"), (54, "1"), (38, qty), (44, "150.25"),
            (60, "20261119-15:00:01.123456789"),
        ]);

    public static string Fill(string execType, string execId, string clOrdId, string qty, string price, string? refId = null) =>
        Message("8",
        [
            (37, "O-" + clOrdId), (11, clOrdId), (17, execId), (150, execType), (39, "1"), .. refId is null ? Array.Empty<(int, string)>() : [(19, refId)],
            (1, "ACC1"), (55, "IBM"), (54, "1"), (32, qty), (31, price), (60, "20261119-15:00:02.000"),
        ]);
}
