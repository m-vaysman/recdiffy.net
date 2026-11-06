using RecDiffy.Results;

namespace RecDiffy.Excel.Facts;

/// <summary>One canonical value, read through the public CanonicalRow API.</summary>
internal sealed record FieldValue(string Text, decimal? Number, DateOnly? Date)
{
    public static FieldValue Null { get; } = new("", null, null);

    public bool IsNull => Number is null && Date is null && Text.Length == 0;

    /// <summary>The value as a typed cell: a number, a date, text, or nothing.</summary>
    public object? Cell => (Number, Date) switch
    {
        ({ } number, _) => number,
        (_, { } date) => date,
        _ when Text.Length > 0 => Text,
        _ => null,
    };

    public static FieldValue Read(CanonicalRow row, string? field)
    {
        if (field is null || row.IsNull(field))
            return Null;
        var value = row[field];
        return value.Type switch
        {
            FieldType.Decimal => new FieldValue(row.Text(field), value.Number, null),
            FieldType.Date => new FieldValue(row.Text(field), null, value.Date),
            _ => new FieldValue(row.Text(field), null, null),
        };
    }
}

/// <summary>The values a side carries for each mapped report role.</summary>
internal sealed record RoleValues(IReadOnlyDictionary<ReportRole, FieldValue> Values)
{
    public static RoleValues None { get; } = new(new Dictionary<ReportRole, FieldValue>());

    public bool Any => Values.Count > 0;

    public FieldValue this[ReportRole role] => Values.TryGetValue(role, out var value) ? value : FieldValue.Null;

    public static RoleValues Read(CanonicalRow row, FieldMap map) =>
        new(map.Fields.ToDictionary(pair => pair.Key, pair => FieldValue.Read(row, pair.Value)));
}
