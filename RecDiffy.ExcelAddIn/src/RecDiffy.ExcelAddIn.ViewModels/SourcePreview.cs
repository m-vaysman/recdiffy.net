using System.Globalization;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record SourcePreview(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, long RowCount)
{
    public const int SampleSize = 5;

    public const int Seed = 192;

    public static SourcePreview Of(SourceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        IReadOnlyList<IReadOnlyList<string>> rows = [.. SampleIndexes(table.Rows.Count, SampleSize, Seed).Select(index => Texts(table.Rows[index]))];
        return new(table.Headers, rows, table.Rows.Count);
    }

    public static IReadOnlyList<string> Texts(IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return [.. values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)];
    }

    public static IReadOnlyList<int> SampleIndexes(int count, int size, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (count <= size)
            return [.. Enumerable.Range(0, count)];

        var random = new Random(seed);
        var chosen = new HashSet<int>();
        for (var candidate = count - size; candidate < count; candidate++)
        {
            var pick = random.Next(candidate + 1);
            chosen.Add(chosen.Contains(pick) ? candidate : pick);
        }

        return [.. chosen.Order()];
    }
}
