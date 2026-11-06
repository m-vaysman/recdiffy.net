using System.Data;

namespace RecDiffy.IO;

/// <summary>
/// A file's column names and a seeded random sample of its data rows, drawn from across the whole file so the sample is
/// not just the first rows. The same file, options, size and seed always give the same sample.
/// </summary>
/// <param name="Headers">The column names, as the engine will read them.</param>
/// <param name="Rows">The sampled rows, in file order.</param>
/// <param name="RowCount">How many data rows the file has.</param>
public sealed record TabularPeek(IReadOnlyList<string> Headers, IReadOnlyList<TabularRow> Rows, long RowCount)
{
    internal static TabularPeek Read(IDataReader reader, int sampleSize, int seed, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleSize);
        var headers = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var (kept, count) = Sample(reader, sampleSize, new Random(seed), cancellationToken);
        return new TabularPeek(headers, [.. kept.OrderBy(row => row.Index)], count);
    }

    // Reservoir sampling (Algorithm R): one pass, at most sampleSize rows held, each row equally likely to be kept.
    // The reservoir is local working state; nothing outside sees it until the sample is returned.
    private static (IReadOnlyList<TabularRow> Kept, long Count) Sample(IDataReader reader, int sampleSize, Random random, CancellationToken cancellationToken)
    {
        var reservoir = new List<TabularRow>(sampleSize);
        var index = 0L;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reservoir.Count < sampleSize)
            {
                reservoir.Add(Row(reader, index));
            }
            else
            {
                var slot = random.NextInt64(index + 1);
                if (slot < sampleSize)
                    reservoir[(int)slot] = Row(reader, index);
            }

            index++;
        }

        return (reservoir, index);
    }

    private static TabularRow Row(IDataReader reader, long index) =>
        new(index, [.. Enumerable.Range(0, reader.FieldCount).Select(ordinal => reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal))]);
}

/// <summary>
/// One sampled row: its 0-based position among the data rows, and its cells as the reader returns them. CSV cells are
/// text (an empty cell is empty text); Excel cells are a double, a <see cref="DateTime"/>, text, or null when empty.
/// </summary>
public sealed record TabularRow(long Index, IReadOnlyList<object?> Values);
