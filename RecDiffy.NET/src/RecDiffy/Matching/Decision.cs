namespace RecDiffy;

internal enum DecisionKind { Commit, Contest, Incomplete, Split }

internal sealed record Decision(DecisionKind Kind, MatchProposal[] Commits, int[] Contest, int[] Incomplete, string? Code)
{
    public static Decision Ready(MatchProposal[] commits) => new(DecisionKind.Commit, commits, [], [], null);

    public static Decision Contested(int[] items) => new(DecisionKind.Contest, [], items, [], null);

    public static Decision Exhausted(int[] items) => new(DecisionKind.Incomplete, [], [], items, "BudgetExhausted");

    public static Decision Blocked(int[] items) => new(DecisionKind.Incomplete, [], [], items, "BlockTooLarge");

    public static Decision Mixed(int[] contest, int[] incomplete) => new(DecisionKind.Split, [], contest, incomplete, "BudgetExhausted");
}
