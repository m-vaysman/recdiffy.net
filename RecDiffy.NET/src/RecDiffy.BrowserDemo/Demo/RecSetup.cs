namespace RecDiffy.BrowserDemo.Demo;

/// <summary>How rows are matched and which values are compared.</summary>
/// <param name="Keys">The columns that must agree for two rows to match.</param>
/// <param name="Compares">The columns compared once rows match.</param>
public sealed record RecSetup(IReadOnlyList<ColumnPair> Keys, IReadOnlyList<ColumnPair> Compares)
{
    public static RecSetup Empty { get; } = new([new ColumnPair("", "")], []);

    public IReadOnlyList<ColumnPair> CompleteKeys => [.. Keys.Where(pair => pair.IsComplete)];

    public IReadOnlyList<ColumnPair> CompleteCompares => [.. Compares.Where(pair => pair.IsComplete)];

    public bool CanRun => CompleteKeys.Count > 0;

    /// <summary>A setup that compares every column both files share by name, as a number when both sides look numeric.</summary>
    public static RecSetup Suggest(LoadedFile left, LoadedFile right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var shared = left.Headers
            .Select(column => (Left: column, Right: right.Headers.FirstOrDefault(other => string.Equals(other, column, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.Right is not null)
            .Select(pair => new ColumnPair(pair.Left, pair.Right!, left.IsNumeric(pair.Left) && right.IsNumeric(pair.Right!)))
            .ToList();

        return new RecSetup([new ColumnPair("", "")], shared);
    }
}
