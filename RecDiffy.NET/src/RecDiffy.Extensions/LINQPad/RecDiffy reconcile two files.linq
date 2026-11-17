<Query Kind="Program">
  <NuGetReference Version="1.2.0-preview.7" Prerelease="true">RecDiffy</NuGetReference>
  <NuGetReference Version="0.1.0-preview.3" Prerelease="true">RecDiffy.Extensions</NuGetReference>
  <NuGetReference Version="1.2.0-preview.7" Prerelease="true">RecDiffy.IO</NuGetReference>
  <Namespace>LINQPad.Controls</Namespace>
  <Namespace>RecDiffy</Namespace>
  <Namespace>RecDiffy.Extensions</Namespace>
  <Namespace>RecDiffy.IO</Namespace>
  <Namespace>RecDiffy.Results</Namespace>
  <Namespace>System.Data</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.Text.RegularExpressions</Namespace>
  <RuntimeVersion>8.0</RuntimeVersion>
</Query>

// Reconcile two files you pick: CSV (any other extension) or Excel (.xlsx, .xlsb, .xls; first sheet). Both need a header row.
//
// 1. Pick the left file and the right file. The two sample files in the samples folder next to this query load on start.
// 2. Tick the columns to match on and the columns to compare. Both lists show the columns the two files share:
//    text columns start ticked under "Match on", number columns under "Compare".
// 3. Set the tolerance for number columns and press Reconcile. The grids appear below the button.
//
// Only published packages: RecDiffy and RecDiffy.IO read the files and run the reconciliation, and RecDiffy.Extensions
// renders the grids (ToDataGridHtml) and the totals (ToSideBySideTable). Rows are numbered from 1, the first row under the header.
void Main()
{
    Util.HtmlHead.AddStyles(DataGrid.Css);

    var samples = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath) ?? "", "samples");
    var left = new FilePicker { Text = Path.Combine(samples, "ibor-positions.csv") };
    var right = new FilePicker { Text = Path.Combine(samples, "abor-positions.csv") };
    var choices = new DumpContainer();
    var results = new DumpContainer();

    void Refresh() => choices.Content = Choices(left.Text, right.Text, results);
    left.TextInput += (_, _) => Refresh();
    right.TextInput += (_, _) => Refresh();

    Refresh();
    new FieldSet("Files", new StackPanel(false, new Label("Left file"), left, new Label("Right file"), right)).Dump();
    choices.Dump();
    results.Dump();
}

// The column choices for two files, with a Reconcile button. It also runs once, so results show straight away.
static object Choices(string leftPath, string rightPath, DumpContainer results)
{
    if (!File.Exists(leftPath) || !File.Exists(rightPath))
        return "Pick a left file and a right file.";

    try
    {
        var leftPeek = Peek(leftPath);
        var rightPeek = Peek(rightPath);
        var shared = leftPeek.Headers.Intersect(rightPeek.Headers, StringComparer.Ordinal).ToArray();
        if (shared.Length == 0)
            return "The two files share no column names, so there is nothing to match on.";

        var numbers = shared.Where(column => IsNumeric(leftPeek, column) && IsNumeric(rightPeek, column)).ToHashSet(StringComparer.Ordinal);
        var matchOn = shared.Select(column => new CheckBox(column, !numbers.Contains(column))).ToArray();
        var compare = shared.Select(column => new CheckBox(column, numbers.Contains(column))).ToArray();
        var tolerance = new TextBox("0.01", "6em");

        void Run() => results.Content = Reconcile(
            leftPath,
            rightPath,
            [.. matchOn.Where(box => box.Checked).Select(box => box.Text)],
            [.. compare.Where(box => box.Checked).Select(box => box.Text)],
            numbers,
            tolerance.Text);

        Run();
        return new StackPanel(false,
            new WrapPanel(new FieldSet("Match on", matchOn), new FieldSet("Compare", compare), new FieldSet("Tolerance for numbers", tolerance)),
            new Button("Reconcile", _ => Run()));
    }
    catch (Exception error) when (error is IOException or InvalidDataException or FormatException or ArgumentException or NotSupportedException or InvalidOperationException)
    {
        return $"Could not read the files: {error.Message}";
    }
}

static object Reconcile(string leftPath, string rightPath, string[] keys, string[] compared, IReadOnlySet<string> numbers, string toleranceText)
{
    if (keys.Length == 0)
        return "Tick at least one column under Match on.";
    if (!decimal.TryParse(toleranceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var tolerance) || tolerance < 0)
        return "The tolerance must be zero or a positive number, such as 0.01.";

    var compares = compared.Except(keys, StringComparer.Ordinal).ToArray();
    var decimals = compares.Where(numbers.Contains).ToArray();
    var texts = compares.Where(column => !numbers.Contains(column)).ToArray();
    var fields = keys.Concat(compares).ToDictionary(column => column, FieldName, StringComparer.Ordinal);
    var leftPeek = Peek(leftPath);
    var rightPeek = Peek(rightPath);

    SideBuilder Rules(SideBuilder side) =>
        texts.Aggregate(
            decimals.Aggregate(
                keys.Aggregate(side, (rules, column) => rules.MapSymbol(column, fields[column])),
                (rules, column) => rules.ParseDecimal(column, fields[column])),
            (rules, column) => rules.CopyText(column, fields[column]));

    var profile = texts.Aggregate(
            decimals.Aggregate(
                keys.Aggregate(
                    ProfileBuilder.Create("two-files", "1.0.0").Culture(CultureInfo.InvariantCulture),
                    (builder, column) => builder.Key(fields[column], FieldType.Symbol)),
                (builder, column) => builder.Economic(fields[column], FieldType.Decimal, Math.Max(Scale(leftPeek, column), Scale(rightPeek, column)))),
            (builder, column) => builder.Economic(fields[column], FieldType.Text))
        .Left(Rules)
        .Right(Rules)
        .Path("files", path => decimals.Aggregate(
            path.NaturalKey("byKey", [.. keys.Select(column => fields[column])]),
            (pass, column) => tolerance > 0 ? pass.Tolerance(fields[column], absolute: tolerance) : pass))
        .Build(RuleRegistry.CreateCore());

    if (!profile.Succeeded || profile.Document is null)
        return profile.Problems.Select(problem => new { problem.Code, problem.Location, problem.Message });

    var result = Reconciler.Run(
        Reconciler.Compile(profile.Document, RuleRegistry.CreateCore()),
        Source("left", leftPath),
        Source("right", rightPath),
        ReferenceDataSet.Empty);

    var grids = new List<object>
    {
        Util.RawHtml(StatusCounts(result).ToDataGridHtml("Groups by status")),
        Util.RawHtml(EveryRow(result, keys, compares, decimals, fields).ToDataGridHtml("Every row: a group with several rows on one side spans several lines")),
    };
    if (decimals.Length > 0)
        grids.Add(Util.RawHtml(Totals(result, decimals, fields).ToDataGridHtml("Totals: Delta = LeftOnly + RightOnly + MatchedDelta, ambiguous rows left out")));

    return Util.VerticalRun(grids);
}

// One row per status: how many groups, and how many rows on each side.
static DataTable StatusCounts(IReconciliationResult result)
{
    var table = Table("Groups by status", ("Status", typeof(string)), ("Groups", typeof(int)), ("Left rows", typeof(int)), ("Right rows", typeof(int)));
    foreach (var status in result.Entries.GroupBy(Status).OrderBy(group => Rank(group.Key)))
        table.Rows.Add(status.Key, status.Count(), status.Sum(entry => entry.Left.Length), status.Sum(entry => entry.Right.Length));
    return table;
}

// One line per left/right pair. A group with several rows on one side spans several lines with one Group number.
static DataTable EveryRow(IReconciliationResult result, string[] keys, string[] compares, string[] decimals, IReadOnlyDictionary<string, string> fields)
{
    var labels = fields.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
    var columns = new List<(string, Type)> { ("Group", typeof(int)), ("Status", typeof(string)), ("Left row", typeof(int)), ("Right row", typeof(int)) };
    columns.AddRange(keys.Select(column => (column, typeof(string))));
    columns.AddRange(compares.SelectMany(column => new[] { ($"{column} left", ValueType(column)), ($"{column} right", ValueType(column)) }));
    columns.Add(("Diff", typeof(string)));
    var table = Table("Every row", [.. columns]);

    Type ValueType(string column) => decimals.Contains(column) ? typeof(decimal) : typeof(string);

    object Value(SourceRef? row, string column) => row switch
    {
        null => DBNull.Value,
        { } member when decimals.Contains(column) && result.Canonical(member)[fields[column]] is { IsNull: false } value => value.Number,
        { } member when decimals.Contains(column) => DBNull.Value,
        { } member => result.Canonical(member).Text(fields[column]),
    };

    var groups = result.Entries
        .OrderBy(entry => Rank(Status(entry)))
        .ThenBy(entry => entry.Left.Concat(entry.Right).Min(row => row.Ordinal))
        .Select((entry, index) => (Entry: entry, Group: index + 1));

    foreach (var (entry, group) in groups)
    {
        var diff = string.Join(" · ", entry.Diffs
            .Where(change => change.Outcome is FieldOutcome.Break or FieldOutcome.WithinTolerance)
            .Select(change => $"{labels.GetValueOrDefault(change.Field.Name, change.Field.Name)} {result.Format(change.Left)} → {result.Format(change.Right)}"));

        foreach (var line in Enumerable.Range(0, Math.Max(entry.Left.Length, entry.Right.Length)))
        {
            SourceRef? left = line < entry.Left.Length ? entry.Left[line] : null;
            SourceRef? right = line < entry.Right.Length ? entry.Right[line] : null;
            var keyRow = left ?? right;
            table.Rows.Add([
                group,
                Status(entry),
                left is { } l ? l.Ordinal + 1 : DBNull.Value,
                right is { } r ? r.Ordinal + 1 : DBNull.Value,
                .. keys.Select(column => keyRow is { } k ? result.Canonical(k).Text(fields[column]) : (object)DBNull.Value),
                .. compares.SelectMany(column => new[] { Value(left, column), Value(right, column) }),
                line == 0 ? diff : "",
            ]);
        }
    }
    return table;
}

// The totals of the compared number columns, from the engine's side-by-side view.
static DataTable Totals(IReconciliationResult result, string[] decimals, IReadOnlyDictionary<string, string> fields)
{
    var table = result.BuildSideBySide(new SideBySideSpec([], [], [.. decimals.Select(column => new MeasureSpec(fields[column]))], null)).ToSideBySideTable("Totals");
    table.Columns.Remove("Row");
    table.Columns.Remove("Column");
    return table;
}

static string Status(ResultEntry entry) => entry.Alignment is Alignment.Matched ? entry.Outcome.ToString() : entry.Alignment.ToString();

static int Rank(string status) => status switch
{
    "Exact" => 0,
    "WithinTolerance" => 1,
    "NotCompared" => 2,
    "Break" => 3,
    "Ambiguous" => 4,
    "SearchIncomplete" => 5,
    "LeftOnly" => 6,
    "RightOnly" => 7,
    _ => 8,
};

static DataTable Table(string name, params (string Name, Type Type)[] columns)
{
    var table = new DataTable(name) { Locale = CultureInfo.InvariantCulture };
    table.Columns.AddRange([.. columns.Select(column => new DataColumn(column.Name, column.Type))]);
    return table;
}

// A field name the profile accepts: letters, digits and underscores, not starting with a digit.
static string FieldName(string column) =>
    Regex.Replace(column, "[^A-Za-z0-9_]", "_") switch
    {
        var name when name.Length == 0 || char.IsDigit(name[0]) => "F_" + name,
        var name => name,
    };

static bool IsExcel(string path) => Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsb" or ".xls";

static CsvOptions Csv() => new() { HasHeader = true, Delimiter = CsvDelimiter.DetectAutomatically };

static ExcelOptions Excel() => new() { HasHeader = true, Sheet = ExcelSheet.Numbered(1) };

static TabularPeek Peek(string path) =>
    IsExcel(path) ? FileSources.PeekExcel(path, Excel(), sampleSize: 200, seed: 1) : FileSources.PeekCsv(path, Csv(), sampleSize: 200, seed: 1);

static RecordSource Source(string name, string path) =>
    IsExcel(path) ? FileSources.FromExcel(name, path, Excel()) : FileSources.FromCsv(name, path, Csv());

static IEnumerable<object> Sample(TabularPeek peek, string column)
{
    var index = peek.Headers.ToList().IndexOf(column);
    return peek.Rows.Select(row => row.Values[index]).Where(value => value is not null && Convert.ToString(value, CultureInfo.InvariantCulture) is not "")!;
}

// A column is a number column when every sampled value is a number.
static bool IsNumeric(TabularPeek peek, string column) =>
    Sample(peek, column).ToArray() is { Length: > 0 } values
    && values.All(value => value is decimal or double or int or long
        || (value is string text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _)));

// The most decimal places in the sampled values of a column, so the engine keeps every digit.
static int Scale(TabularPeek peek, string column) =>
    Sample(peek, column)
        .Select(value => value switch
        {
            decimal number => number.Scale,
            string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number.Scale,
            _ => 0,
        })
        .DefaultIfEmpty(0)
        .Max();
