using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class RulesViewModel : ObservableObject
{
    private readonly SourcesViewModel _sources;
    private readonly CultureInfo _culture;
    private ProfileDocument? _kept;
    private bool _refreshing;

    [ObservableProperty]
    private IReadOnlyList<string> leftColumns = [];

    [ObservableProperty]
    private IReadOnlyList<string> rightColumns = [];

    [ObservableProperty]
    private IReadOnlyList<string> problems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private int problemCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyCanExecuteChangedFor(nameof(PairSameNamedColumnsCommand), nameof(AddFieldCommand), nameof(AddPassCommand))]
    private string? notEditable;

    public RulesViewModel(SourcesViewModel sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _culture = sources.ExcelCulture.ToCulture();
        _sources.Left.PropertyChanged += OnSourcesChanged;
        _sources.Right.PropertyChanged += OnSourcesChanged;
        Refresh();
    }

    public ObservableCollection<FieldRuleViewModel> Fields { get; } = [];

    public ObservableCollection<PassRuleViewModel> Passes { get; } = [];

    public ObservableCollection<ComparisonRuleViewModel> Comparisons { get; } = [];

    public bool IsValid => ProblemCount == 0;

    public bool IsEditable => NotEditable is null;

    public bool HasFields => Fields.Count > 0;

    public bool HasComparisons => Comparisons.Count > 0;

    public string AmountExample => 0.05m.ToString(_culture);

    public RulesState ToState() => new(
        [.. Fields.Select(field => field.ToRule())],
        [.. Passes.Select(pass => pass.ToRule())],
        [.. Comparisons.Select(comparison => comparison.ToRule(_culture))]);

    public ProfileDocument ToProfile(ProfileDocument baseProfile)
    {
        ArgumentNullException.ThrowIfNull(baseProfile);
        return _kept is { } kept
            ? kept with { SourceReadings = baseProfile.SourceReadings }
            : RulesProfiles.Document(ToState(), baseProfile);
    }

    public void Apply(ProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var rules = RulesProfiles.Read(profile);
        (_kept, NotEditable) = rules is null
            ? (profile, "These rules use options this tab can't edit yet. They are kept as they are.")
            : ((ProfileDocument?)null, (string?)null);

        _refreshing = true;
        try
        {
            foreach (var field in Fields)
                field.PropertyChanged -= OnRowChanged;
            foreach (var pass in Passes)
                pass.PropertyChanged -= OnRowChanged;
            Fields.Clear();
            Passes.Clear();
            Comparisons.Clear();
            foreach (var field in (rules ?? RulesState.Empty).Fields)
                Fields.Add(Watch(FieldRuleViewModel.From(field, RemoveField)));
            foreach (var pass in (rules ?? RulesState.Empty).Passes)
            {
                var added = Watch(new PassRuleViewModel(RemovePass));
                added.Offer(pass.Key, pass.Key);
                Passes.Add(added);
            }
        }
        finally
        {
            _refreshing = false;
        }

        Refresh();
        foreach (var comparison in (rules ?? RulesState.Empty).Comparisons)
            Comparisons.FirstOrDefault(row => string.Equals(row.Field, comparison.Field, StringComparison.Ordinal))?.Apply(comparison, _culture);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void PairSameNamedColumns()
    {
        var usedLeft = Fields.Select(field => field.LeftColumn).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedRight = Fields.Select(field => field.RightColumn).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = Fields.Select(field => field.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pairs = LeftColumns
            .Where(left => !usedLeft.Contains(left) && !names.Contains(left))
            .Select(left => (Left: left, Right: RightColumns.FirstOrDefault(right => !usedRight.Contains(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.Right is not null)
            .ToArray();
        foreach (var (left, right) in pairs)
            Fields.Add(Watch(new FieldRuleViewModel(RemoveField) { Name = left, LeftColumn = left, RightColumn = right }));

        Refresh();
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void AddField()
    {
        Fields.Add(Watch(new FieldRuleViewModel(RemoveField)));
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void AddPass()
    {
        Passes.Add(Watch(new PassRuleViewModel(RemovePass)));
        Refresh();
    }

    private T Watch<T>(T row)
        where T : ObservableObject
    {
        row.PropertyChanged += OnRowChanged;
        return row;
    }

    private void RemoveField(FieldRuleViewModel field)
    {
        field.PropertyChanged -= OnRowChanged;
        Fields.Remove(field);
        Refresh();
    }

    private void RemovePass(PassRuleViewModel pass)
    {
        pass.PropertyChanged -= OnRowChanged;
        Passes.Remove(pass);
        Refresh();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(FieldRuleViewModel.Problem))
            Refresh();
    }

    private void OnComparisonChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ComparisonRuleViewModel.Problem))
            Refresh();
    }

    private void OnSourcesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SideViewModel.Preview) or nameof(SideViewModel.Culture))
            Refresh();
    }

    private void Refresh()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        try
        {
            LeftColumns = _sources.Left.Preview?.Headers ?? [];
            RightColumns = _sources.Right.Preview?.Headers ?? [];
            SyncPasses();
            SyncComparisons();
            Validate();
            OnPropertyChanged(nameof(HasFields));
            OnPropertyChanged(nameof(HasComparisons));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SyncPasses()
    {
        IReadOnlyList<string> keys = [.. Fields.Where(field => field.Role is FieldRole.Key).Select(field => field.Name.Trim()).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal)];
        foreach (var (pass, index) in Passes.Select((pass, index) => (pass, index)))
        {
            pass.Number = index + 1;
            pass.Offer(keys, pass.SelectedKeys);
        }
    }

    private void SyncComparisons()
    {
        var compared = Fields.Where(field => field.Role is FieldRole.Economic && field.Name.Trim().Length > 0).ToArray();
        var kept = Comparisons.Where(row => compared.Any(field => string.Equals(field.Name.Trim(), row.Field, StringComparison.Ordinal))).ToArray();
        foreach (var gone in Comparisons.Except(kept).ToArray())
        {
            gone.PropertyChanged -= OnComparisonChanged;
            Comparisons.Remove(gone);
        }

        foreach (var (field, index) in compared.Select((field, index) => (field, index)))
        {
            var name = field.Name.Trim();
            var row = Comparisons.FirstOrDefault(existing => string.Equals(existing.Field, name, StringComparison.Ordinal));
            if (row is null)
            {
                row = new ComparisonRuleViewModel(name);
                row.PropertyChanged += OnComparisonChanged;
                Comparisons.Insert(Math.Min(index, Comparisons.Count), row);
            }

            row.Columns = $"{field.LeftColumn ?? "?"} ↔ {field.RightColumn ?? "?"}";
            row.Kinds = ComparisonChoice.For(field.Type);
            if (!row.Kinds.Any(choice => choice.Kind == row.Kind))
                row.Kind = ComparisonKind.Exact;
        }
    }

    private void Validate()
    {
        IReadOnlyList<string> data = _sources.Left.HasData && _sources.Right.HasData ? [] : ["Choose the left and right data on the Sources tab first."];
        IReadOnlyList<string> general = IsEditable ? EditableProblems(data) : KeptProblems(data);
        Problems = general;
        ProblemCount = general.Count + RowProblems();
    }

    private IReadOnlyList<string> KeptProblems(IReadOnlyList<string> data) =>
        data.Count > 0 ? data : EngineProblems();

    private IReadOnlyList<string> EditableProblems(IReadOnlyList<string> data)
    {
        foreach (var field in Fields)
            field.Problem = FieldProblem(field);
        foreach (var pass in Passes)
            pass.Problem = PassProblem(pass);
        foreach (var comparison in Comparisons)
            comparison.Problem = ComparisonProblem(comparison);

        IReadOnlyList<string> local =
        [
            .. data,
            .. Fields.Count == 0 ? ["Add the fields to match and compare: pair same-named columns, or add a field."] : Array.Empty<string>(),
            .. Passes.Count == 0 ? ["Add a matching pass, and choose its key fields."] : Array.Empty<string>(),
        ];
        return local.Count == 0 && RowProblems() == 0 ? EngineProblems() : local;
    }

    private IReadOnlyList<string> EngineProblems() =>
        [.. RulesProfiles.Check(ToProfile(_sources.ToSetup().Profile)).Select(problem => problem.Message)];

    private int RowProblems() =>
        Fields.Count(field => field.Problem is not null)
        + Passes.Count(pass => pass.Problem is not null)
        + Comparisons.Count(comparison => comparison.Problem is not null);

    private string? FieldProblem(FieldRuleViewModel field) => field switch
    {
        { Name: var name } when string.IsNullOrWhiteSpace(name) => "Name the field.",
        _ when Fields.Count(other => string.Equals(other.Name.Trim(), field.Name.Trim(), StringComparison.OrdinalIgnoreCase)) > 1 => "Another field already has this name.",
        { LeftColumn: null } => "Choose the left column.",
        { RightColumn: null } => "Choose the right column.",
        { LeftColumn: { } left } when LeftColumns.Count > 0 && !LeftColumns.Contains(left, StringComparer.Ordinal) => $"The left data has no column '{left}' any more.",
        { RightColumn: { } right } when RightColumns.Count > 0 && !RightColumns.Contains(right, StringComparer.Ordinal) => $"The right data has no column '{right}' any more.",
        { IsDecimal: true, ScaleText: var scale } when FieldRuleViewModel.ScaleOf(scale) is null => "The decimal places must be a whole number from 0 to 28.",
        { Role: FieldRole.Key, Type: FieldType.Text } => "A key can't be Text. Choose Identifier, Code, Number or Date.",
        _ => null,
    };

    private static string? PassProblem(PassRuleViewModel pass) => (pass.Keys.Count, pass.SelectedKeys.Count) switch
    {
        (0, _) => "Set a field's role to Key, then choose it here.",
        (_, 0) => "Choose at least one key field.",
        _ => null,
    };

    private string? ComparisonProblem(ComparisonRuleViewModel comparison) => comparison switch
    {
        { UsesAmount: true, AmountText: var amount } when ComparisonRuleViewModel.AmountOf(amount, _culture) is null => $"Enter the amount as a number, for example {AmountExample}.",
        { UsesPercentage: true, PercentageText: var percentage } when ComparisonRuleViewModel.AmountOf(percentage, _culture) is null => $"Enter the percentage as a number, for example {AmountExample}.",
        { UsesDays: true, DaysText: var days } when ComparisonRuleViewModel.DaysOf(days) is null => "Enter the days as a whole number, for example 2.",
        _ => null,
    };
}
