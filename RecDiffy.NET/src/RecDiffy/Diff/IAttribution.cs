using System.Collections.Immutable;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal interface IAttribution
{
    string Name { get; }

    ImmutableArray<string> Roots { get; }

    AttributionStage Stage { get; }

    bool Explains(RowContext left, RowContext right, FieldDeclaration field, decimal delta, MidpointMode midpoint);

    FieldOutcome Outcome(RootOutcomes roots);

    ImmutableArray<string> AttributedTo(RootOutcomes roots);
}

internal enum AttributionStage
{
    Measured,
    Finished,
}

internal readonly struct RootOutcomes(DiffStep[] steps, FieldOutcome[] outcomes)
{
    public FieldOutcome Of(string field) =>
        Array.FindIndex(steps, step => step.Field.Name == field) switch
        {
            -1 => FieldOutcome.Break,
            var index => outcomes[index],
        };

    public bool Moved(string field) =>
        steps.Zip(outcomes).Any(pair => pair.First.Field.Name == field && pair.Second is not FieldOutcome.Exact and not FieldOutcome.NotCompared);
}
