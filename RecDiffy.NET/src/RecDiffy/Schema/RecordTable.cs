using System.Collections.Immutable;

namespace RecDiffy;

internal abstract record Column(string Name, FieldType Type)
{
    public static Column From(FieldDeclaration field, IReadOnlyList<IReadOnlyDictionary<string, CanonicalCell>> rows) =>
        field.Type switch
        {
            FieldType.Decimal => new DecimalColumn(field.Name, Values(rows, field.Name, cell => cell.Number), Nulls(rows, field.Name)),
            FieldType.Date => new DateColumn(field.Name, Values(rows, field.Name, cell => cell.Date), Nulls(rows, field.Name)),
            FieldType.Symbol or FieldType.Identifier => new IntColumn(field.Name, field.Type, Values(rows, field.Name, cell => cell.Interned), Nulls(rows, field.Name)),
            FieldType.Text => new TextColumn(field.Name, Values(rows, field.Name, cell => cell.Text), Nulls(rows, field.Name)),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unknown field type."),
        };

    private static ImmutableArray<T> Values<T>(IReadOnlyList<IReadOnlyDictionary<string, CanonicalCell>> rows, string name, Func<CanonicalCell, T> select) =>
        [.. rows.Select(row => row.TryGetValue(name, out var cell) && !cell.IsNull ? select(cell) : default)!];

    private static ImmutableArray<bool> Nulls(IReadOnlyList<IReadOnlyDictionary<string, CanonicalCell>> rows, string name) =>
        [.. rows.Select(row => !row.TryGetValue(name, out var cell) || cell.IsNull)];
}

internal sealed record DecimalColumn(string Name, ImmutableArray<decimal> Values, ImmutableArray<bool> Nulls) : Column(Name, FieldType.Decimal);

internal sealed record DateColumn(string Name, ImmutableArray<DateOnly> Values, ImmutableArray<bool> Nulls) : Column(Name, FieldType.Date);

internal sealed record IntColumn(string Name, FieldType Type, ImmutableArray<int> Values, ImmutableArray<bool> Nulls) : Column(Name, Type);

internal sealed record TextColumn(string Name, ImmutableArray<string?> Values, ImmutableArray<bool> Nulls) : Column(Name, FieldType.Text);

internal sealed record RecordTable(IReadOnlyDictionary<string, Column> Columns, int Count)
{
    public static RecordTable FromRows(IReadOnlyList<FieldDeclaration> fields, IReadOnlyList<IReadOnlyDictionary<string, CanonicalCell>> rows) =>
        new(
            fields.ToImmutableDictionary(field => field.Name, field => Column.From(field, rows), StringComparer.Ordinal),
            rows.Count);

    public bool IsNull(int row, string field) => Columns[field] switch
    {
        DecimalColumn column => column.Nulls[row],
        DateColumn column => column.Nulls[row],
        IntColumn column => column.Nulls[row],
        TextColumn column => column.Nulls[row],
        _ => true,
    };

    public decimal Decimal(int row, string field) => Columns[field] switch
    {
        DecimalColumn column => column.Values[row],
        var other => throw new ArgumentException($"'{other.Name}' is {other.Type}, not a decimal column.", nameof(field)),
    };

    public ReadOnlySpan<decimal> DecimalColumn(string field) => Columns[field] switch
    {
        DecimalColumn column => column.Values.AsSpan(),
        var other => throw new ArgumentException($"'{other.Name}' is {other.Type}, not a decimal column.", nameof(field)),
    };
}
