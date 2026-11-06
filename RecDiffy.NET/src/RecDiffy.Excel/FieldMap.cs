using System.Collections.Immutable;

namespace RecDiffy.Excel;

/// <summary>
/// Binds report roles to canonical field names. RecDiffy is general-purpose, so reports read fields only through this map;
/// a report whose required roles are unmapped is skipped and the reason is returned.
/// </summary>
public sealed record FieldMap
{
    private static readonly ImmutableDictionary<ReportRole, ImmutableArray<string>> Conventional = new Dictionary<ReportRole, ImmutableArray<string>>
    {
        [ReportRole.Counterparty] = ["Counterparty"],
        [ReportRole.Instrument] = ["Instrument"],
        [ReportRole.Asset] = ["Asset", "AssetClass"],
        [ReportRole.Side] = ["Side"],
        [ReportRole.TradeDate] = ["TradeDate"],
        [ReportRole.SettleDate] = ["SettleDate"],
        [ReportRole.Quantity] = ["Quantity"],
        [ReportRole.Price] = ["Price"],
        [ReportRole.Currency] = ["Currency"],
        [ReportRole.NetCash] = ["NetCash"],
        [ReportRole.Accrued] = ["Accrued"],
        [ReportRole.Fees] = ["Fees"],
        [ReportRole.Book] = ["Book"],
        [ReportRole.LifecycleAction] = ["EventAction"],
        [ReportRole.LifecycleTarget] = ["EventTarget"],
    }.ToImmutableDictionary();

    public FieldMap(IReadOnlyDictionary<ReportRole, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = fields.Where(pair => string.IsNullOrWhiteSpace(pair.Value) is false).ToImmutableDictionary();
    }

    /// <summary>No roles mapped: every report that needs a field is skipped.</summary>
    public static FieldMap Empty { get; } = new(ImmutableDictionary<ReportRole, string>.Empty);

    public IReadOnlyDictionary<ReportRole, string> Fields { get; }

    /// <summary>Maps each role to the profile field with the conventional name (for example Asset or AssetClass), when the profile declares one.</summary>
    public static FieldMap FromProfile(CompiledProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var declared = profile.Document.Fields.Select(field => field.Name).ToImmutableHashSet(StringComparer.Ordinal);
        return new FieldMap(Conventional
            .Select(pair => (Role: pair.Key, Field: pair.Value.FirstOrDefault(declared.Contains)))
            .Where(pair => pair.Field is not null)
            .ToImmutableDictionary(pair => pair.Role, pair => pair.Field!));
    }

    public FieldMap With(ReportRole role, string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new FieldMap(Fields.ToImmutableDictionary().SetItem(role, field));
    }

    public FieldMap Without(ReportRole role) => new(Fields.ToImmutableDictionary().Remove(role));

    public bool IsMapped(ReportRole role) => Fields.ContainsKey(role);

    public string? this[ReportRole role] => Fields.TryGetValue(role, out var field) ? field : null;

    public bool Equals(FieldMap? other) =>
        other is not null && Fields.Count == other.Fields.Count && Fields.All(pair => other.Fields.TryGetValue(pair.Key, out var field) && field == pair.Value);

    public override int GetHashCode() =>
        Fields.OrderBy(pair => pair.Key).Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value));
}
