using System.Collections.Immutable;
using System.Globalization;
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
    private readonly ImmutableList<string> _summed;
    private readonly bool _atKey;

    internal RecBuilder(ImmutableArray<TLeft> left, ImmutableArray<TRight> right, string profileId, string version, ImmutableList<ImmutableList<TypedPair>> passes, ImmutableList<TypedPair> compared, ImmutableList<string>? summed = null, bool atKey = false)
    {
        _left = left;
        _right = right;
        _profileId = profileId;
        _version = version;
        _passes = passes;
        _compared = compared;
        _summed = summed ?? [];
        _atKey = atKey;
    }

    /// <summary>The profile this reconciliation runs, for <see cref="ProfileWriter.ToJson"/>.</summary>
    public ProfileDocument Profile => Build().Profile;

    /// <summary>Names the profile that <see cref="Profile"/> writes.</summary>
    public RecBuilder<TLeft, TRight> Named(string profileId, string version)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(version);
        return new(_left, _right, profileId, version, _passes, _compared, _summed, _atKey);
    }

    /// <summary>Adds a natural-key pass on one pair of members.</summary>
    public RecBuilder<TLeft, TRight> MatchOn<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right) =>
        new(_left, _right, _profileId, _version, _passes.Add([TypedPair.Of(left, right, key: true)]), _compared, _summed, atKey: true);

    /// <summary>Adds the next natural-key pass, tried on what earlier passes left unmatched.</summary>
    public RecBuilder<TLeft, TRight> ThenMatchOn<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right) =>
        _passes.IsEmpty
            ? throw new InvalidOperationException("Call MatchOn before ThenMatchOn.")
            : MatchOn(left, right);

    /// <summary>Adds another member pair to the key of the latest pass, so that pass matches on all of them.</summary>
    public RecBuilder<TLeft, TRight> And<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right) =>
        _passes.IsEmpty
            ? throw new InvalidOperationException("Call MatchOn before And.")
            : new(_left, _right, _profileId, _version, _passes.SetItem(_passes.Count - 1, _passes[^1].Add(TypedPair.Of(left, right, key: true))), _compared, _summed, atKey: true);

    /// <summary>
    /// Names a value of the string member just added to the key, and the spellings that count as it, on both sides. Call it
    /// once for each value. A spelling is matched exactly as written, after trimming, so list each case you expect. A row
    /// whose spelling nobody listed is quarantined with <c>UnknownSymbol</c>, never guessed. Call it right after
    /// <c>MatchOn</c>, <c>ThenMatchOn</c> or <c>And</c>, on a <c>string</c> member.
    /// </summary>
    /// <param name="value">The value the spellings mean, such as <c>"BUY"</c>.</param>
    /// <param name="accepts">Every spelling that means <paramref name="value"/>, on either side. Include the value itself if it is one.</param>
    /// <example>
    /// <code>
    /// RecBuilder.Between(ours, confirms)
    ///     .MatchOn(trade => trade.Isin, confirm => confirm.Isin)
    ///         .And(trade => trade.Side, confirm => confirm.BuySell)
    ///             .Value("BUY",  accepts: ["B", "BOT", "BUY"])
    ///             .Value("SELL", accepts: ["S", "SLD", "SELL"])
    ///     .Run();
    /// // A trade booked "B" matches a confirm that says "BOT": both mean BUY.
    /// </code>
    /// </example>
    public RecBuilder<TLeft, TRight> Value(string value, IReadOnlyList<string> accepts)
    {
        ArgumentNullException.ThrowIfNull(accepts);
        return accepts.Count == 0
            ? throw new ArgumentException("List at least one spelling.", nameof(accepts))
            : Meaning(nameof(Value), meaning => meaning.Value(value, accepts, accepts));
    }

    /// <summary>
    /// Names a value of the string member just added to the key, with the spellings each side uses for it. Use it when the two
    /// systems write the same value differently. The rules of <see cref="Value(string, IReadOnlyList{string})"/> apply.
    /// </summary>
    /// <param name="value">The value the spellings mean, such as <c>"BUY"</c>.</param>
    /// <param name="ours">The spellings in the left items that mean <paramref name="value"/>.</param>
    /// <param name="theirs">The spellings in the right items that mean <paramref name="value"/>.</param>
    /// <example>
    /// <code>
    /// RecBuilder.Between(ours, confirms)
    ///     .MatchOn(trade => trade.Side, confirm => confirm.BuySell)
    ///         .Value("BUY",  ours: ["BUY"],  theirs: ["B", "BOT"])
    ///         .Value("SELL", ours: ["SELL"], theirs: ["S", "SLD"])
    ///     .Run();
    /// </code>
    /// </example>
    public RecBuilder<TLeft, TRight> Value(string value, IReadOnlyList<string> ours, IReadOnlyList<string> theirs)
    {
        ArgumentNullException.ThrowIfNull(ours);
        ArgumentNullException.ThrowIfNull(theirs);
        return ours.Count + theirs.Count == 0
            ? throw new ArgumentException("List at least one spelling.", nameof(ours))
            : Meaning(nameof(Value), meaning => meaning.Value(value, ours, theirs));
    }

    /// <summary>
    /// Decides the value of every spelling of the string member just added to the key, with one function for both sides. The
    /// function returns the value a spelling means, or <c>null</c> for a spelling that is not accepted. It is asked once about
    /// each distinct spelling found in the two lists, when the profile is built, and its answers are written into the profile
    /// as a table. A spelling that appears only in later data is not in the table, so its row is quarantined.
    /// </summary>
    /// <param name="spelling">Returns the value a spelling means, or <c>null</c> when it is not accepted.</param>
    /// <example>
    /// <code>
    /// RecBuilder.Between(ours, confirms)
    ///     .MatchOn(trade => trade.Side, confirm => confirm.BuySell)
    ///         .Value(text => text switch
    ///         {
    ///             "B" or "BOT" or "BUY" => "BUY",
    ///             "S" or "SLD" or "SELL" => "SELL",
    ///             _ => null,
    ///         })
    ///     .Run();
    /// </code>
    /// </example>
    public RecBuilder<TLeft, TRight> Value(Func<string, string?> spelling) => Value(spelling, spelling);

    /// <summary>
    /// Decides the value of every spelling of the string member just added to the key, with a function for each side. Use it
    /// when the two systems write the same value differently. The rules of <see cref="Value(Func{string, string?})"/> apply.
    /// </summary>
    /// <param name="ours">Returns the value a left spelling means, or <c>null</c> when it is not accepted.</param>
    /// <param name="theirs">Returns the value a right spelling means, or <c>null</c> when it is not accepted.</param>
    /// <example>
    /// <code>
    /// RecBuilder.Between(ours, confirms)
    ///     .MatchOn(trade => trade.Side, confirm => confirm.BuySell)
    ///         .Value(
    ///             ours: text => text,                      // BUY and SELL are already the values
    ///             theirs: text => text switch
    ///             {
    ///                 "B" or "BOT" => "BUY",
    ///                 "S" or "SLD" => "SELL",
    ///                 _ => null,
    ///             })
    ///     .Run();
    /// </code>
    /// </example>
    public RecBuilder<TLeft, TRight> Value(Func<string, string?> ours, Func<string, string?> theirs) =>
        Meaning(nameof(Value), meaning => meaning.Decided(ours, theirs));

    /// <summary>
    /// Says that two values of the string member just added to the key are opposites, so a left item with one matches a right
    /// item with the other: a buy on one side is a sell on the other. The values are the ones named with <c>Value</c>; without
    /// <c>Value</c> they are the spellings in the data, each accepted as written. The profile it writes reads each right spelling
    /// as the left value that matches it, so the key itself is still an exact match. A right row with a spelling nobody listed is
    /// quarantined with <c>UnknownSymbol</c>. Two items that agree, such as two buys, do not match.
    /// </summary>
    /// <param name="value">One value of the pair.</param>
    /// <param name="opposite">The value that matches it. Each is the opposite of the other, so <c>Opposite("BUY", "SELL")</c> covers both ways.</param>
    /// <example>
    /// <code>
    /// RecBuilder.Between(ours, confirms)
    ///     .MatchOn(trade => trade.Isin, confirm => confirm.Isin)
    ///         .And(trade => trade.Side, confirm => confirm.BuySell)
    ///             .Value("BUY",  accepts: ["B", "BOT", "BUY"])
    ///             .Value("SELL", accepts: ["S", "SLD", "SELL"])
    ///             .Opposite("BUY", "SELL")
    ///     .Run();
    /// // We bought and the broker sold: our "B" matches their "SLD". Both sold, or both bought, do not match.
    /// </code>
    /// </example>
    public RecBuilder<TLeft, TRight> Opposite(string value, string opposite)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(opposite);
        return Meaning(nameof(Opposite), meaning => meaning.Opposite([(value, opposite), (opposite, value)]));
    }

    /// <summary>
    /// Says which value of the right items matches which value of the left items, one pair at a time: a pair is the left value
    /// first, then the right value that matches it. Use it when the two sides use different words and you do not want to name
    /// the values with <c>Value</c>. The rules of <see cref="Opposite(string, string)"/> apply.
    /// </summary>
    /// <param name="pairs">Each pair is (a left value, the right value that matches it).</param>
    /// <example>
    /// <code>
    /// RecBuilder.Between(ours, confirms)
    ///     .MatchOn(trade => trade.Isin, confirm => confirm.Isin)
    ///         .And(trade => trade.Side, confirm => confirm.BuySell)
    ///             .Opposite(("BUY", "S"), ("SELL", "B"))
    ///     .Run();
    /// // Our "BUY" matches their "S", and our "SELL" matches their "B". Any other spelling is quarantined.
    /// </code>
    /// </example>
    public RecBuilder<TLeft, TRight> Opposite(params (string Ours, string Theirs)[] pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        return Meaning(nameof(Opposite), meaning => meaning.Opposite(pairs));
    }

    /// <summary>
    /// Decides which values are opposites with a function. It is asked about every left value paired with every right value, once,
    /// when the profile is built, and the pairs it answers true for go into the profile. The values are the ones named with
    /// <c>Value</c>, or the distinct spellings in the data when none are named. The rules of
    /// <see cref="Opposite(string, string)"/> apply.
    /// </summary>
    /// <param name="areOpposite">Returns true when a left value and a right value match as opposites.</param>
    /// <example>
    /// <code>
    /// RecBuilder.Between(ours, confirms)
    ///     .MatchOn(trade => trade.Isin, confirm => confirm.Isin)
    ///         .And(trade => trade.Side, confirm => confirm.BuySell)
    ///             .Opposite((ours, theirs) => (ours, theirs) is ("BUY", "S") or ("SELL", "B"))
    ///     .Run();
    /// </code>
    /// </example>
    public RecBuilder<TLeft, TRight> Opposite(Func<string, string, bool> areOpposite) =>
        Meaning(nameof(Opposite), meaning => meaning.Opposite(areOpposite));

    /// <summary>Compares a member pair on matched items. A decimal may differ by up to <paramref name="tolerance"/>, inclusive.</summary>
    public RecBuilder<TLeft, TRight> Compare<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right, decimal? tolerance = null)
    {
        var pair = TypedPair.Of(left, right, key: false) with { Tolerance = tolerance };
        return pair is { Tolerance: not null, Kind: not TypedKind.Decimal }
            ? throw new ArgumentException($"A tolerance applies to decimal members; '{pair.Field}' is not one.", nameof(tolerance))
            : new(_left, _right, _profileId, _version, _passes, _compared.Add(pair), _summed);
    }

    /// <summary>
    /// Sums a decimal member pair, so several rows on one side can match one row on the other. The pair is compared on matched
    /// items, and two group-sum passes follow the natural-key passes: first several left rows against one right row, then
    /// several right rows against one left row. Each blocks on the key of the latest <c>MatchOn</c> or <c>ThenMatchOn</c>,
    /// with its <c>And</c> pairs, and matches only when every summed pair adds up exactly. Call <c>Sum</c> again to conserve
    /// another pair in the same passes.
    /// </summary>
    public RecBuilder<TLeft, TRight> Sum<TLeftValue, TRightValue>(Expression<Func<TLeft, TLeftValue>> left, Expression<Func<TRight, TRightValue>> right)
    {
        if (_passes.IsEmpty)
            throw new InvalidOperationException("Call MatchOn before Sum.");
        var pair = TypedPair.Of(left, right, key: false);
        return pair is not { Kind: TypedKind.Decimal }
            ? throw new ArgumentException($"Sum adds up decimal members; '{pair.Field}' is not one.", nameof(left))
            : new(_left, _right, _profileId, _version, _passes, _compared.Add(pair), _summed.Add(pair.Field));
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

    // Changes what the latest key member's text means. The latest key pair is the one MatchOn, ThenMatchOn or And just added.
    private RecBuilder<TLeft, TRight> Meaning(string method, Func<TypedMeaning, TypedMeaning> change)
    {
        if (!_atKey)
            throw new InvalidOperationException($"Call {method} right after MatchOn, ThenMatchOn or And.");
        var latest = _passes[^1][^1];
        if (latest.Kind is not TypedKind.Text)
            throw new InvalidOperationException($"{method} names the values of a string member; '{latest.Field}' is not one.");
        var changed = latest with { Meaning = change(latest.Meaning ?? TypedMeaning.Empty) };
        var pass = _passes[^1].SetItem(_passes[^1].Count - 1, changed);
        return new(_left, _right, _profileId, _version, _passes.SetItem(_passes.Count - 1, pass), _compared, _summed, atKey: true);
    }

    // The spellings found in the items, trimmed, with blanks left out. A function that decides is asked about these.
    private static IEnumerable<string> Spellings<T>(ImmutableArray<T> items, Func<object, object?> read) =>
        items
            .Select(item => item is null ? null : read(item) as string)
            .Select(text => text?.Trim())
            .OfType<string>()
            .Where(text => text.Length > 0);

    private TypedField Resolved(TypedField field) =>
        field.Pair.Meaning is { } meaning
            ? field with { Maps = meaning.Resolve(Spellings(_left, field.Pair.ReadLeft), Spellings(_right, field.Pair.ReadRight)) }
            : field;

    private (ProfileDocument Profile, SourceMap<TLeft> Left, SourceMap<TRight> Right) Build()
    {
        if (_passes.IsEmpty)
            throw new InvalidOperationException("Call MatchOn at least once: a reconciliation needs a way to match.");
        var keys = _passes.SelectMany(pass => pass).ToArray();
        var fields = keys
            .Concat(_compared)
            .GroupBy(pair => pair.Field, StringComparer.Ordinal)
            .Select(group => Resolved(Declare(group.Key, group.ToArray())))
            .ToArray();
        var passes = _passes
            .Select((pass, index) => (Id: "P" + (index + 1).ToString(CultureInfo.InvariantCulture), Key: pass.Select(pair => pair.Field).ToArray()))
            .Aggregate(PathBuilder.Create("typed"), (path, pass) => path.NaturalKey(pass.Id, pass.Key));
        var path = _compared
            .Where(pair => pair.Tolerance is not null)
            .Aggregate(GroupSums(passes), (current, pair) => current.Tolerance(pair.Field, absolute: pair.Tolerance));
        var declared = fields.Aggregate(ProfileBuilder.Create(_profileId, _version), (profile, field) => profile.Field(field.Pair.Field, field.Type, field.Role, field.Scale));
        var built = declared
            .Left(fields.Aggregate(SideBuilder.Empty, (side, field) => Normalize(side, field, field.Pair.LeftColumn, field.Maps?.Left)))
            .Right(fields.Aggregate(SideBuilder.Empty, (side, field) => Normalize(side, field, field.Pair.RightColumn, field.Maps?.Right)))
            .Path(path)
            .Build(RuleRegistry.CreateCore());
        return built.Succeeded
            ? (built.Document, Map<TLeft>(fields, field => field.Pair.LeftColumn, field => field.Pair.ReadLeft), Map<TRight>(fields, field => field.Pair.RightColumn, field => field.Pair.ReadRight))
            : throw new ProfileCompileException([.. built.Problems.Select(problem => new ProfileError(problem.Code, $"{problem.Message} ({problem.Location})"))]);
    }

    private PathBuilder GroupSums(PathBuilder path) =>
        _summed.IsEmpty
            ? path
            : new[] { Side.Left, Side.Right }
                .Select((many, index) => (Many: many, Id: "P" + (_passes.Count + index + 1).ToString(CultureInfo.InvariantCulture)))
                .Aggregate(path, (current, pass) => current.GroupSum(pass.Id, pass.Many, [.. _passes[^1].Select(pair => pair.Field)], [.. _summed.Distinct(StringComparer.Ordinal)]));

    // A member used both as a key and in Compare is declared once, as a key; keys are compared too.
    // A decimal is declared at scale 28, so parsing keeps the full value.
    // A member that names its values is declared as a symbol, and the first key use that names them is the one that counts.
    private static TypedField Declare(string name, TypedPair[] uses)
    {
        var key = uses.FirstOrDefault(use => use.Key);
        var pair = uses.FirstOrDefault(use => use is { Key: true, Meaning: not null }) ?? key ?? uses[0];
        if (uses.FirstOrDefault(use => use.RightColumn != pair.RightColumn) is { } other)
            throw new ArgumentException($"Left member '{name}' is paired with right members '{pair.RightColumn}' and '{other.RightColumn}'; pair it with one.", nameof(uses));
        return pair.Kind switch
        {
            TypedKind.Decimal => new(pair, FieldType.Decimal, key is null ? FieldRole.Economic : FieldRole.Key, 28),
            TypedKind.Date => new(pair, FieldType.Date, key is null ? FieldRole.Economic : FieldRole.Key, 0),
            TypedKind.Text when pair.Meaning is not null => new(pair, FieldType.Symbol, FieldRole.Key, 0),
            TypedKind.Text when key is not null => new(pair, FieldType.Identifier, FieldRole.Key, 0),
            _ => new(pair, FieldType.Text, FieldRole.Economic, 0),
        };
    }

    // An identifier's scheme is the left member name, so two fields do not share an intern bucket.
    // A symbol is read through its table, and a spelling that is not in it is not passed through.
    private static SideBuilder Normalize(SideBuilder side, TypedField field, string column, ImmutableDictionary<string, string>? values) => field.Type switch
    {
        FieldType.Decimal => side.ParseDecimal(column, field.Pair.Field),
        FieldType.Date => side.ParseDate(column, field.Pair.Field, []),
        FieldType.Symbol => side.MapSymbol(column, field.Pair.Field, values, passthrough: false),
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

    private sealed record TypedField(TypedPair Pair, FieldType Type, FieldRole Role, int Scale, TypedMaps? Maps = null);
}

internal enum TypedKind
{
    Decimal,
    Date,
    Text,
}

// One selected member pair: the field it becomes, the column each side reads, how to read it, and its kind.
internal sealed record TypedPair(string Field, string LeftColumn, string RightColumn, Func<object, object?> ReadLeft, Func<object, object?> ReadRight, TypedKind Kind, bool Key, decimal? Tolerance, TypedMeaning? Meaning = null)
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
    internal RecResult(IReconciliationResult result, ProfileDocument profile, ImmutableArray<TLeft> left, ImmutableArray<TRight> right)
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
    public IReconciliationResult Result { get; }

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

    private static IEnumerable<int> Only(IReconciliationResult result, string side, Disposition disposition) =>
        result.Rows.Where(row => row.Row.Source == side && row.Disposition == disposition).Select(row => row.Row.Ordinal).Order();
}

/// <summary>A matched group of your items. With natural-key passes each side holds one item.</summary>
public sealed record RecMatch<TLeft, TRight>(IReadOnlyList<TLeft> Left, IReadOnlyList<TRight> Right, FieldOutcome Outcome, IReadOnlyList<FieldDiff> Diffs);

/// <summary>Items that compete for the same match.</summary>
public sealed record RecGroup<TLeft, TRight>(IReadOnlyList<TLeft> Left, IReadOnlyList<TRight> Right);
