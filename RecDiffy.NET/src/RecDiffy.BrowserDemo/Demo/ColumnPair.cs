namespace RecDiffy.BrowserDemo.Demo;

/// <summary>A left column and the right column it lines up with.</summary>
/// <param name="Left">The left file's column, or empty when none is chosen yet.</param>
/// <param name="Right">The right file's column, or empty when none is chosen yet.</param>
/// <param name="Number">For a comparison: true to compare as numbers, false to compare as text.</param>
/// <param name="Tolerance">For a number comparison: the largest difference still accepted.</param>
public sealed record ColumnPair(string Left, string Right, bool Number = false, decimal Tolerance = 0m)
{
    public bool IsComplete => Left.Length > 0 && Right.Length > 0;
}
