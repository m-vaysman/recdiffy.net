using System.Collections.Immutable;
using System.Linq.Expressions;
using RecDiffy.Results;

namespace RecDiffy;

/// <summary>Reconciles two lists of your own objects, matching and comparing their properties, with no profile JSON to write.</summary>
public static class RecBuilder
{
    /// <summary>Starts a reconciliation of <paramref name="left"/> against <paramref name="right"/>. Both are read once, now.</summary>
    public static RecBuilder<TLeft, TRight> Between<TLeft, TRight>(IEnumerable<TLeft> left, IEnumerable<TRight> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new RecBuilder<TLeft, TRight>([.. left], [.. right], "typed", "1.0.0", [], []);
    }
}

/// <summary>
/// A typed reconciliation. Each <c>MatchOn</c> or <c>ThenMatchOn</c> is a natural-key pass, tried in order; <c>Compare</c> adds a
/// field to compare on matched pairs. Column names come from the selected members; field types come from their types:
/// <c>decimal</c>, <c>int</c> and <c>long</c> are decimals, <c>DateOnly</c> and <c>DateTime</c> are dates, <c>string</c> is an
/// identifier in a key and text otherwise. A field is named after the left member. Every method returns a new builder.
/// </summary>
public sealed class RecBuilder<TLeft, TRight>
{
    private readonly ImmutableArray<TLeft> _left;
    private readonly ImmutableArray<TRight> _right;
    private readonly string _profileId;
    private readonly string _version;
    private readonly ImmutableList<ImmutableList<TypedPair>> _passes;
    private readonly ImmutableList<TypedPair> _compared;

    internal RecBuilder(ImmutableArray<TLeft> left, ImmutableArray<TRight> right, string profileId, string version, ImmutableList<ImmutableList<TypedPair>> passes, ImmutableList<TypedPair> compared)
    {
        _left = left;
        _right = right;
        _profileId = profileId;
        _version = version;
        _passes = passes;
        _compared = compared;
    }

    /// <summary>The profile this reconciliation runs, for <see cref="ProfileWriter.ToJson"/>.</summary>
    public ProfileDocument Profile => Build().Profile;

    /// <summary>Names the profile that <see cref="Profile"/> writes.</summary>
    public RecBuilder<TLeft, TRight> Named(string profileId, string version)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(version);
        return new(_left, _right, profileId, version, _passes, _compared);
    }

    /// <summary>Adds a natural-key pass on one pair of members.</summary>
    public RecBuilder<TLeft, TRight> MatchOn<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right) =>
        new(_left, _right, _profileId, _version, _passes.Add([TypedPair.Of(left, right, key: true)]), _compared);

    /// <summary>Adds the next natural-key pass, tried on what earlier passes left unmatched.</summary>
    public RecBuilder<TLeft, TRight> ThenMatchOn<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right) =>
        _passes.IsEmpty
            ? throw new InvalidOperationException("Call MatchOn before ThenMatchOn.")
            : MatchOn(left, right);

    /// <summary>Adds another member pair to the key of the latest pass, so that pass matches on all of them.</summary>
    public RecBuilder<TLeft, TRight> And<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right) =>
        _passes.IsEmpty
            ? throw new InvalidOperationException("Call MatchOn before And.")
            : new(_left, _right, _profileId, _version, _passes.SetItem(_passes.Count - 1, _passes[^1].Add(TypedPair.Of(left, right, key: true))), _compared);

    /// <summary>Compares a member pair on matched items. A decimal may differ by up to <paramref name="tolerance"/>, inclusive.</summary>
    public RecBuilder<TLeft, TRight> Compare<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right, decimal? tolerance = null)
    {
        var pair = TypedPair.Of(left, right, key: false) with { Tolerance = tolerance };
        return pair is { Tolerance: not null, Kind: not TypedKind.Decimal }
            ? throw new ArgumentException($"A tolerance applies to decimal members; '{pair.Field}' is not one.", nameof(tolerance))
            : new(_left, _right, _profileId, _version, _passes, _compared.Add(pair));
    }

    /// <summary>Runs the reconciliation and maps every result back to your objects.</summary>
    public RecResult<TLeft, TRight> Run(CancellationToken cancellationToken = default)
    {
        var (profile, leftMap, rightMap) = Build();
        var result = Reconciler.Run(
            Reconciler.Compile(profile, RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", _left, leftMap),
            RecordSources.FromObjects("right", _right, rightMap),
            ReferenceDataSet.Empty,
            cancellationToken: cancellationToken);
        return new RecResult<TLeft, TRight>(result, profile, _left, _right);
    }

    private (ProfileDocument Profile, SourceMap<TLeft> Left, SourceMap<TRight> Right) Build()
    {
        if (_passes.IsEmpty)
            throw new InvalidOperationException("Call MatchOn at least once: a reconciliation needs a way to match.");
        var keys = _passes.SelectMany(pass => pass).ToArray();
        var fields = keys
            .Concat(_compared)
            .GroupBy(pair => pair.Field, StringComparer.Ordinal)
            .Select(group => Declare(group.Key, group.ToArray()))
            .ToArray();
        var passes = _passes
            .Select((pass, index) => (Id: $"P{index + 1}", Key: pass.Select(pair => pair.Field).ToArray()))
            .Aggregate(PathBuilder.Create("typed"), (path, pass) => path.NaturalKey(pass.Id, pass.Key));
        var path = _compared
            .Where(pair => pair.Tolerance is not null)
            .Aggregate(passes, (current, pair) => current.Tolerance(pair.Field, absolute: pair.Tolerance));
        var declared = fields.Aggregate(ProfileBuilder.Create(_profileId, _version), (profile, field) => profile.Field(field.Pair.Field, field.Type, field.Role, field.Scale));
        var built = declared
            .Left(fields.Aggregate(SideBuilder.Empty, (side, field) => Normalize(side, field, field.Pair.LeftColumn)))
            .Right(fields.Aggregate(SideBuilder.Empty, (side, field) => Normalize(side, field, field.Pair.RightColumn)))
            .Path(path)
            .Build(RuleRegistry.CreateCore());
        return built.Succeeded
            ? (built.Document, Map<TLeft>(fields, field => field.Pair.LeftColumn, field => field.Pair.ReadLeft), Map<TRight>(fields, field => field.Pair.RightColumn, field => field.Pair.ReadRight))
            : throw new ProfileCompileException([.. built.Problems.Select(problem => new ProfileError(problem.Code, $"{problem.Message} ({problem.Location})"))]);
    }

    // A member used both as a key and in Compare is declared once, as a key; keys are compared too.
    // A decimal is declared at scale 28, so parsing keeps the full value.
    private static TypedField Declare(string name, TypedPair[] uses)
    {
        var key = uses.FirstOrDefault(use => use.Key);
        var pair = key ?? uses[0];
        if (uses.FirstOrDefault(use => use.RightColumn != pair.RightColumn) is { } other)
            throw new ArgumentException($"Left member '{name}' is paired with right members '{pair.RightColumn}' and '{other.RightColumn}'; pair it with one.", nameof(uses));
        return pair.Kind switch
        {
            TypedKind.Decimal => new(pair, FieldType.Decimal, key is null ? FieldRole.Economic : FieldRole.Key, 28),
            TypedKind.Date => new(pair, FieldType.Date, key is null ? FieldRole.Economic : FieldRole.Key, 0),
            TypedKind.Text when key is not null => new(pair, FieldType.Identifier, FieldRole.Key, 0),
            _ => new(pair, FieldType.Text, FieldRole.Economic, 0),
        };
    }

    // An identifier's scheme is the left member name, so two fields do not share an intern bucket.
    private static SideBuilder Normalize(SideBuilder side, TypedField field, string column) => field.Type switch
    {
        FieldType.Decimal => side.ParseDecimal(column, field.Pair.Field),
        FieldType.Date => side.ParseDate(column, field.Pair.Field, []),
        FieldType.Identifier => side.MapIdentifier(column, field.Pair.Field, field.Pair.Field),
        _ => side.CopyText(column, field.Pair.Field),
    };

    private static SourceMap<T> Map<T>(IEnumerable<TypedField> fields, Func<TypedField, string> column, Func<TypedField, Func<object, object?>> read) =>
        fields.Aggregate(new SourceMap<T>(), (map, field) => (field.Pair.Kind, read(field)) switch
        {
            (TypedKind.Decimal, var value) => map.Decimal(column(field), row => Typed.Decimal(value(row!))),
            (TypedKind.Date, var value) => map.Date(column(field), row => Typed.Date(value(row!))),
            (_, var value) => map.Text(column(field), row => value(row!) as string),
        });

    private sealed record TypedField(TypedPair Pair, FieldType Type, FieldRole Role, int Scale);
}

internal enum TypedKind
{
    Decimal,
    Date,
    Text,
}

// One selected member pair: the field it becomes, the column each side reads, how to read it, and its kind.
internal sealed record TypedPair(string Field, string LeftColumn, string RightColumn, Func<object, object?> ReadLeft, Func<object, object?> ReadRight, TypedKind Kind, bool Key, decimal? Tolerance)
{
    public static TypedPair Of<TLeft, TLeftValue, TRight, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right, bool key)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var leftKind = Typed.KindOf(typeof(TLeftValue), nameof(left));
        var rightKind = Typed.KindOf(typeof(TRightValue), nameof(right));
        if (leftKind != rightKind)
            throw new ArgumentException($"'{Typed.MemberPath(left)}' is a {leftKind} and '{Typed.MemberPath(right)}' is a {rightKind}; a pair must be the same kind.", nameof(right));
        var readLeft = left.Compile();
        var readRight = right.Compile();
        return new TypedPair(
            Typed.MemberPath(left),
            Typed.MemberPath(left),
            Typed.MemberPath(right),
            row => readLeft((TLeft)row),
            row => readRight((TRight)row),
            leftKind,
            key,
            null);
    }
}

internal static class Typed
{
    // Nullable unwraps to the inner type. Nullability does not change the kind.
    public static TypedKind KindOf(Type type, string parameter) => (Nullable.GetUnderlyingType(type) ?? type) switch
    {
        var t when t == typeof(decimal) || t == typeof(int) || t == typeof(long) => TypedKind.Decimal,
        var t when t == typeof(DateOnly) || t == typeof(DateTime) => TypedKind.Date,
        var t when t == typeof(string) => TypedKind.Text,
        var t => throw new ArgumentException($"A {t.Name} member cannot be matched or compared. Use decimal, int, long, DateOnly, DateTime or string.", parameter),
    };

    // The member chain from the parameter, joined with dots: row => row.Ref is "Ref", row => row.Header.Ref is "Header.Ref".
    public static string MemberPath(LambdaExpression selector)
    {
        var members = new List<string>();
        var current = selector.Body;
        while (current is MemberExpression member)
        {
            members.Insert(0, member.Member.Name);
            current = member.Expression;
        }

        return current is ParameterExpression && members.Count > 0
            ? Delimited.Join('.', members)
            : throw new ArgumentException($"'{selector}' must select a property or field of its argument, such as row => row.Amount.", nameof(selector));
    }

    public static decimal? Decimal(object? value) => value switch
    {
        null => null,
        decimal number => number,
        int number => number,
        long number => number,
        _ => throw new InvalidCastException($"Expected a decimal, int or long, not {value.GetType().Name}."),
    };

    // A DateTime loses its time of day.
    public static DateOnly? Date(object? value) => value switch
    {
        null => null,
        DateOnly date => date,
        DateTime time => DateOnly.FromDateTime(time),
        _ => throw new InvalidCastException($"Expected a DateOnly or DateTime, not {value.GetType().Name}."),
    };
}

/// <summary>A typed reconciliation's result, mapped back to the objects you passed in. <see cref="Result"/> holds everything else.</summary>
public sealed class RecResult<TLeft, TRight>
{
    // Ordinal is the index into the list passed to Between. Matched pairs are ordered by the earliest left ordinal.
    internal RecResult(ReconciliationResult result, ProfileDocument profile, ImmutableArray<TLeft> left, ImmutableArray<TRight> right)
    {
        Result = result;
        Profile = profile;
        var matched = result.Entries
            .Where(entry => entry.Alignment is Alignment.Matched)
            .OrderBy(entry => entry.Left.Min(member => member.Ordinal))
            .Select(entry => new RecMatch<TLeft, TRight>([.. entry.Left.Select(member => left[member.Ordinal])], [.. entry.Right.Select(member => right[member.Ordinal])], entry.Outcome, entry.Diffs))
            .ToArray();
        Matched = matched;
        Breaks = [.. matched.Where(match => match.Outcome is FieldOutcome.Break)];
        Ambiguous = [.. result.Entries
            .Where(entry => entry.Alignment is Alignment.Ambiguous)
            .Select(entry => new RecGroup<TLeft, TRight>([.. entry.Left.Select(member => left[member.Ordinal])], [.. entry.Right.Select(member => right[member.Ordinal])]))];
        LeftOnly = [.. Only(result, "left", Disposition.LeftOnly).Select(ordinal => left[ordinal])];
        RightOnly = [.. Only(result, "right", Disposition.RightOnly).Select(ordinal => right[ordinal])];
    }

    /// <summary>The engine's full result: every entry, row disposition, diagnostic and explanation.</summary>
    public ReconciliationResult Result { get; }

    /// <summary>The profile that ran, for <see cref="ProfileWriter.ToJson"/>.</summary>
    public ProfileDocument Profile { get; }

    /// <summary>Every matched pair, exact, within tolerance or with a break, in the order of the left items.</summary>
    public IReadOnlyList<RecMatch<TLeft, TRight>> Matched { get; }

    /// <summary>The matched pairs whose compared fields break.</summary>
    public IReadOnlyList<RecMatch<TLeft, TRight>> Breaks { get; }

    /// <summary>Items that could match more than one item on the other side, so none was chosen.</summary>
    public IReadOnlyList<RecGroup<TLeft, TRight>> Ambiguous { get; }

    public IReadOnlyList<TLeft> LeftOnly { get; }

    public IReadOnlyList<TRight> RightOnly { get; }

    private static IEnumerable<int> Only(ReconciliationResult result, string side, Disposition disposition) =>
        result.Rows.Where(row => row.Row.Source == side && row.Disposition == disposition).Select(row => row.Row.Ordinal).Order();
}

/// <summary>A matched group of your items. With natural-key passes each side holds one item.</summary>
public sealed record RecMatch<TLeft, TRight>(IReadOnlyList<TLeft> Left, IReadOnlyList<TRight> Right, FieldOutcome Outcome, IReadOnlyList<FieldDiff> Diffs);

/// <summary>Items that compete for the same match.</summary>
public sealed record RecGroup<TLeft, TRight>(IReadOnlyList<TLeft> Left, IReadOnlyList<TRight> Right);
