using System.Collections.Immutable;

namespace RecDiffy.Sessions;

/// <summary>A reconciliation that keeps the rows pushed into it and reconciles them on demand. Every call returns a new session; a session never changes.</summary>
public sealed class RecSession
{
    private const string LeftSide = "left";

    private const string RightSide = "right";

    private readonly RecView? _last;

    private RecSession(CompiledProfile profile, ReferenceDataSet reference, ImmutableList<SessionRow> left, ImmutableList<SessionRow> right, int version, RecView? last)
    {
        Profile = profile;
        Reference = reference;
        Left = left;
        Right = right;
        Version = version;
        _last = last;
    }

    public CompiledProfile Profile { get; }

    public ReferenceDataSet Reference { get; }

    /// <summary>The left rows pushed so far, in push order.</summary>
    public ImmutableList<SessionRow> Left { get; }

    /// <summary>The right rows pushed so far, in push order.</summary>
    public ImmutableList<SessionRow> Right { get; }

    /// <summary>How many pushes the session holds.</summary>
    public int Version { get; }

    /// <summary>Whether the last snapshot covers every push, so the next snapshot runs nothing.</summary>
    public bool IsCurrent => _last is { } view && view.Version == Version;

    public static RecSession Start(CompiledProfile profile, ReferenceDataSet? reference = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new RecSession(profile, reference ?? ReferenceDataSet.Empty, [], [], 0, null);
    }

    /// <summary>Adds rows to each side. A row is its source cells by column, as text.</summary>
    public RecSession Push(IEnumerable<IReadOnlyDictionary<string, string?>> left, IEnumerable<IReadOnlyDictionary<string, string?>> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new RecSession(Profile, Reference, Left.AddRange(left.Select(SessionRow.Of)), Right.AddRange(right.Select(SessionRow.Of)), Version + 1, _last);
    }

    /// <summary>Adds the rows a previous result left open: rows of unmatched entries, with the rows their lifecycle chains replaced, and quarantined rows.</summary>
    public RecSession Push(IReconciliationResult previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return new RecSession(
            Profile,
            Reference,
            Left.AddRange(Leftovers.Of(previous, Profile.Document, LeftSide)),
            Right.AddRange(Leftovers.Of(previous, Profile.Document, RightSide)),
            Version + 1,
            _last);
    }

    /// <summary>Reconciles every row pushed so far, or returns the last view when nothing was pushed since.</summary>
    public (RecView View, RecSession Session) Snapshot() =>
        _last switch
        {
            { } view when view.Version == Version => (view, this),
            _ => Run(),
        };

    private (RecView View, RecSession Session) Run()
    {
        var result = Reconciler.Run(Profile, SessionRow.Source(LeftSide, Left), SessionRow.Source(RightSide, Right), Reference);
        var view = new RecView(result, Version);
        return (view, new RecSession(Profile, Reference, Left, Right, Version, view));
    }
}
