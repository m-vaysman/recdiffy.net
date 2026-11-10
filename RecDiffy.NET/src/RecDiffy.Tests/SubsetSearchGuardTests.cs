namespace RecDiffy.Tests;

// The input checks on SubsetSearch.Search asked for in the #245 review: they throw before the walk starts.
public class SubsetSearchGuardTests
{
    private static readonly ConservedClass[] Two = [new([1m], [0]), new([2m], [1])];

    [Fact]
    public void Null_classes_throw() =>
        Assert.Equal("classes", Assert.Throws<ArgumentNullException>(() => SubsetSearch.Search(null!, [3m], 8, 250_000)).ParamName);

    [Fact]
    public void A_null_target_throws() =>
        Assert.Equal("target", Assert.Throws<ArgumentNullException>(() => SubsetSearch.Search(Two, null!, 8, 250_000)).ParamName);

    [Fact]
    public void An_empty_target_throws() =>
        Assert.Equal("target", Assert.Throws<ArgumentException>(() => SubsetSearch.Search(Two, [], 8, 250_000)).ParamName);

    private static readonly Dictionary<string, ConservedClass[]> BadClasses = new(StringComparer.Ordinal)
    {
        ["a null class"] = [new([1m], [0]), null!],
        ["a class with null values"] = [new(null!, [0])],
        ["a class with null items"] = [new([1m], null!)],
        ["a class with no items"] = [new([1m], [0]), new([2m], [])],
        ["a class with fewer values than the target"] = [new([], [0])],
        ["a class with more values than the target"] = [new([1m, 2m], [0])],
    };

    [Theory]
    [InlineData("a null class", "A class is null.")]
    [InlineData("a class with null values", "A class has no conserved values.")]
    [InlineData("a class with null items", "A class has no items.")]
    [InlineData("a class with no items", "A class has no items.")]
    [InlineData("a class with fewer values than the target", "A class holds 0 conserved values and the target holds 1.")]
    [InlineData("a class with more values than the target", "A class holds 2 conserved values and the target holds 1.")]
    public void A_malformed_class_throws(string what, string message)
    {
        var failure = Assert.Throws<ArgumentException>(() => SubsetSearch.Search(BadClasses[what], [3m], 8, 250_000));

        Assert.True(failure is { ParamName: "classes" } && failure.Message.StartsWith(message, StringComparison.Ordinal), $"{what}: {failure.Message}");
    }

    // A block whose candidates all lack a conserved value has no classes. That is a search with no group.
    [Fact]
    public void No_classes_is_a_search_with_no_group()
    {
        var search = SubsetSearch.Search([], [3m], 8, 250_000);

        Assert.Equal(SearchState.None, search.State);
        Assert.Empty(search.DrawnItems);
    }

    // The argument checks come first, so a malformed call throws ArgumentException even when the run is cancelled.
    [Fact]
    public void An_argument_problem_is_reported_before_cancellation() =>
        Assert.Throws<ArgumentException>(() => SubsetSearch.Search(Two, [], 8, 250_000, new CancellationToken(true)));

    // Cancellation is still observed before an empty search returns, as it was before the checks.
    [Fact]
    public void A_cancelled_search_with_no_classes_still_throws() =>
        Assert.Throws<OperationCanceledException>(() => SubsetSearch.Search([], [3m], 8, 250_000, new CancellationToken(true)));
}
