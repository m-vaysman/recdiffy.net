using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.Sessions;

/// <summary>What a snapshot returns: the run's result over every row pushed so far.</summary>
/// <param name="Result">The result of <see cref="Reconciler.Run"/>, unchanged.</param>
/// <param name="Version">How many pushes the result covers.</param>
public sealed record RecView(IReconciliationResult Result, int Version)
{
    /// <summary>The matched entries whose outcome is a break.</summary>
    public ImmutableArray<ResultEntry> Breaks { get; } = [.. Result.Entries.Where(entry => entry.Outcome is FieldOutcome.Break)];
}
