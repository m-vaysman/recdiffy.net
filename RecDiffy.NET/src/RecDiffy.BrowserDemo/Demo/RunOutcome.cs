namespace RecDiffy.BrowserDemo.Demo;

/// <summary>What one run gave: the result, or why there is none.</summary>
/// <param name="Result">The reconciliation result, or <see langword="null"/> when the run failed.</param>
/// <param name="Setup">The setup the run used.</param>
/// <param name="Error">Why the run failed, or <see langword="null"/>.</param>
/// <param name="Elapsed">How long the run took in this browser.</param>
public sealed record RunOutcome(IReconciliationResult? Result, RecSetup Setup, string? Error, TimeSpan Elapsed)
{
    public static RunOutcome Run(LoadedFile left, LoadedFile right, RecSetup setup, Func<TimeSpan> clock)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(clock);

        var started = clock();
        try
        {
            var profile = Reconciler.Compile(ProfileDocument.Parse(ProfileJson.Build(setup, left, right)), RuleRegistry.CreateCore());
            using var leftStream = left.Open();
            using var rightStream = right.Open();
            var result = Reconciler.Run(profile, left.Source("left", leftStream), right.Source("right", rightStream), ReferenceDataSet.Empty);
            return new RunOutcome(result, setup, null, clock() - started);
        }
        catch (ProfileCompileException e)
        {
            return new RunOutcome(null, setup, e.Message, clock() - started);
        }
        catch (InvalidDataException e)
        {
            return new RunOutcome(null, setup, e.Message, clock() - started);
        }
    }
}
