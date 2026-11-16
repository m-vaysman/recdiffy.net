using System.Data;
using RecDiffy.Results;

namespace RecDiffy.Extensions;

/// <summary>
/// Each table of a <see cref="RecResult{TLeft, TRight}"/> as a <see cref="DataTable"/> (<c>ToXxxTable</c>) or as an HTML
/// table (<c>ToXxxHtml</c>). Numbers and dates are written culture-invariantly.
/// </summary>
public static class RecResultExtensions
{
    private const string Separator = " · ";

    /// <summary>
    /// One row per left/right pair: <c>Group</c>, <c>Status</c> (Exact, WithinTolerance, Break, Ambiguous, LeftOnly, RightOnly),
    /// every public property of <typeparamref name="TLeft"/> as <c>Left.Name</c> and of <typeparamref name="TRight"/> as
    /// <c>Right.Name</c>, then <c>Diffs</c>. A group with several items on one side (a sum match, ambiguous candidates)
    /// spans several rows that share one <c>Group</c> number. The properties are read by reflection, so the table is not trim-safe.
    /// </summary>
    public static DataTable ToReconciliationTable<TLeft, TRight>(this RecResult<TLeft, TRight> result, string tableName = "Reconciliation")
    {
        ArgumentNullException.ThrowIfNull(result);

        var leftProperties = Properties.Of<TLeft>();
        var rightProperties = Properties.Of<TRight>();

        var columns = new (string Name, Type Type)[] { ("Group", typeof(int)), ("Status", typeof(string)) }
            .Concat(leftProperties.Select(property => (Name: $"Left.{property.Name}", Type: Properties.ColumnType(property))))
            .Concat(rightProperties.Select(property => (Name: $"Right.{property.Name}", Type: Properties.ColumnType(property))))
            .Append((Name: "Diffs", Type: typeof(string)));

        var rows = ResultGroups.Of(result)
            .SelectMany(group => Enumerable
                .Range(0, Math.Max(group.Left.Count, group.Right.Count))
                .Select(index => (object?[])
                [
                    group.Group,
                    group.Status,
                    .. leftProperties.Select(property => Properties.Value(property, group.Left, index)),
                    .. rightProperties.Select(property => Properties.Value(property, group.Right, index)),
                    index == 0 ? group.Diffs : "",
                ]));

        return Tables.Build(tableName, columns, rows);
    }

    /// <summary>
    /// One row per group: <c>Group</c>, <c>Status</c>, <c>Shape</c> (the left and right counts, such as <c>3 ↔ 1</c>),
    /// <c>Left</c> and <c>Right</c> (the items, joined with " · "), and <c>Diffs</c>. An empty side is <see cref="DBNull"/>.
    /// </summary>
    /// <param name="result">The result of a run.</param>
    /// <param name="leftText">How to show a left item. The default is its <c>ToString()</c>.</param>
    /// <param name="rightText">How to show a right item. The default is its <c>ToString()</c>.</param>
    /// <param name="tableName">The <see cref="DataTable.TableName"/>.</param>
    public static DataTable ToSummaryTable<TLeft, TRight>(
        this RecResult<TLeft, TRight> result,
        Func<TLeft, string>? leftText = null,
        Func<TRight, string>? rightText = null,
        string tableName = "Summary")
    {
        ArgumentNullException.ThrowIfNull(result);

        Func<TLeft, string> showLeft = leftText ?? (item => item?.ToString() ?? "");
        Func<TRight, string> showRight = rightText ?? (item => item?.ToString() ?? "");

        (string Name, Type Type)[] columns =
        [
            ("Group", typeof(int)),
            ("Status", typeof(string)),
            ("Shape", typeof(string)),
            ("Left", typeof(string)),
            ("Right", typeof(string)),
            ("Diffs", typeof(string)),
        ];

        var rows = ResultGroups.Of(result)
            .Select(group => (object?[])
            [
                group.Group,
                group.Status,
                $"{group.Left.Count.ToInvariant()} ↔ {group.Right.Count.ToInvariant()}",
                Joined(group.Left, showLeft),
                Joined(group.Right, showRight),
                group.Diffs,
            ]);

        return Tables.Build(tableName, columns, rows);
    }

    /// <summary>The number of groups in each status, from Exact to RightOnly. A status with no groups is left out.</summary>
    public static DataTable ToStatusCountsTable<TLeft, TRight>(this RecResult<TLeft, TRight> result, string tableName = "StatusCounts")
    {
        ArgumentNullException.ThrowIfNull(result);

        (string Name, Type Type)[] columns = [("Status", typeof(string)), ("Groups", typeof(int))];

        var rows = ResultGroups.Of(result)
            .GroupBy(group => group.Status, StringComparer.Ordinal)
            .OrderBy(group => Statuses.Rank(group.Key))
            .Select(group => (object?[])[group.Key, group.Count()]);

        return Tables.Build(tableName, columns, rows);
    }

    /// <summary>
    /// The result's side-by-side view for <paramref name="spec"/>: <c>Row</c>, <c>Column</c>, then six decimal columns per measure.
    /// See <see cref="SideBySideExtensions.ToSideBySideTable(ISideBySideView, string)"/>.
    /// </summary>
    public static DataTable ToSideBySideTable<TLeft, TRight>(this RecResult<TLeft, TRight> result, SideBySideSpec spec, string tableName = "SideBySide")
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(spec);

        return result.Result.BuildSideBySide(spec).ToSideBySideTable(tableName);
    }

    /// <summary>The <see cref="ToReconciliationTable{TLeft, TRight}"/> table as HTML. Add <see cref="HtmlTable.Css"/> to the page.</summary>
    public static string ToReconciliationHtml<TLeft, TRight>(this RecResult<TLeft, TRight> result, string caption = "Reconciliation")
    {
        using var table = result.ToReconciliationTable(caption);
        return table.ToHtmlTable(caption);
    }

    /// <summary>The <see cref="ToSummaryTable{TLeft, TRight}"/> table as HTML. Add <see cref="HtmlTable.Css"/> to the page.</summary>
    public static string ToSummaryHtml<TLeft, TRight>(
        this RecResult<TLeft, TRight> result,
        Func<TLeft, string>? leftText = null,
        Func<TRight, string>? rightText = null,
        string caption = "Summary")
    {
        using var table = result.ToSummaryTable(leftText, rightText, caption);
        return table.ToHtmlTable(caption);
    }

    /// <summary>The <see cref="ToStatusCountsTable{TLeft, TRight}"/> table as HTML. Add <see cref="HtmlTable.Css"/> to the page.</summary>
    public static string ToStatusCountsHtml<TLeft, TRight>(this RecResult<TLeft, TRight> result, string caption = "StatusCounts")
    {
        using var table = result.ToStatusCountsTable(caption);
        return table.ToHtmlTable(caption);
    }

    /// <summary>The <see cref="ToSideBySideTable{TLeft, TRight}"/> table as HTML. Add <see cref="HtmlTable.Css"/> to the page.</summary>
    public static string ToSideBySideHtml<TLeft, TRight>(this RecResult<TLeft, TRight> result, SideBySideSpec spec, string caption = "SideBySide")
    {
        using var table = result.ToSideBySideTable(spec, caption);
        return table.ToHtmlTable(caption);
    }

    private static object Joined<T>(IReadOnlyList<T> items, Func<T, string> text) =>
        items.Count == 0 ? DBNull.Value : string.Join(Separator, items.Select(text));
}
