namespace RecDiffy.Fix;

/// <summary>One tag=value field of a FIX message.</summary>
public sealed record FixField(int Tag, string Value);

/// <summary>A FIX message that passed every check, with its fields in wire order.</summary>
public sealed record FixMessage(IReadOnlyList<FixField> Fields, string Text)
{
    public string BeginString => Get(8) ?? "";

    public string MsgType => Get(35) ?? "";

    /// <summary>The first value of a tag, or null when the message lacks it.</summary>
    public string? Get(int tag) => Fields.FirstOrDefault(field => field.Tag == tag)?.Value;

    /// <summary>Every value of a tag, in wire order.</summary>
    public IReadOnlyList<string> GetAll(int tag) => [.. Fields.Where(field => field.Tag == tag).Select(field => field.Value)];
}

/// <summary>What reading one frame gave: an accepted message, or a rejection with its reason.</summary>
public abstract record FixReading(string Text);

public sealed record FixAccepted(FixMessage Message) : FixReading(Message.Text);

public sealed record FixRejected(string Text, string Reason) : FixReading(Text);

/// <summary>A message that was read but makes no row, and why.</summary>
public sealed record FixSkipped(FixMessage Message, string Reason);

/// <summary>How to read and flatten FIX.</summary>
/// <param name="OurCompId">Your own SenderCompID/TargetCompID; the other party of each message is its counterparty.</param>
/// <param name="Delimiter">The field separator: SOH on the wire, often '|' in logs. A '|' log is checked as if it held SOH.</param>
/// <param name="ThrowOnRejected">Throw on the first rejected frame or message instead of labelling it.</param>
public sealed record FixOptions(string OurCompId, char Delimiter = FixOptions.Soh, bool ThrowOnRejected = false)
{
    public const char Soh = '\u0001';
}

/// <summary>Thrown when <see cref="FixOptions.ThrowOnRejected"/> is set and a frame or message is rejected.</summary>
public sealed class FixRejectedException(FixRejected rejected) : Exception($"FIX message rejected: {rejected.Reason}")
{
    public FixRejected Rejected { get; } = rejected;
}
