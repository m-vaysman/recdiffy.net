using System.Globalization;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record SourcePreview(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows, long RowCount)
{
    public const int SampleSize = 5;

    public const int Seed = 192;

    public static SourcePreview Of(SourceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        IReadOnlyList<IReadOnlyList<object?>> rows = [.. SampleIndexes(table.Rows.Count, SampleSize, Seed).Select(index => table.Rows[index])];
        return new(table.Headers, rows, table.Rows.Count);
    }

    public PreviewTable Render(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        IReadOnlyList<IReadOnlyList<string>> rows = [.. Rows.Select(row => (IReadOnlyList<string>)[.. row.Select(value => Text(value, culture))])];
        return new(Headers, rows);
    }

    public static string Text(object? value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value switch
        {
            null => string.Empty,
            DateTime { TimeOfDay.Ticks: 0 } date => date.ToString("d", culture),
            DateTime moment => moment.ToString("g", culture),
            TimeOnly { Second: 0 } time => time.ToString("t", culture),
            TimeOnly time => time.ToString("T", culture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
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

public sealed record PreviewTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);
