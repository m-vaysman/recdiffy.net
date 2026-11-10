using System.Collections.Immutable;

namespace RecDiffy;

internal static class AttributionValidation
{
    public static IEnumerable<ProfileError> Validate(ProfileDocument document, FieldSchema schema, AttributionRegistry attributions) =>
        document.Paths.SelectMany(path => (path.Diff?.Fields ?? ImmutableDictionary<string, FieldDiffDeclaration>.Empty)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => Named(pair.Value).SelectMany(name => Check(path.Id, pair.Key, name, schema, attributions))));

    private static IEnumerable<string> Named(FieldDiffDeclaration diff) =>
        new[] { diff.Tolerance.Attributed, diff.Attributed }.OfType<string>().Distinct(StringComparer.Ordinal);

    private static IEnumerable<ProfileError> Check(string pathId, string field, string name, FieldSchema schema, AttributionRegistry attributions) =>
        attributions.Find(name) switch
        {
            null => [new ProfileError("UnknownRule", $"Path '{pathId}' attributes '{field}' by '{name}', which is not registered.")],
            var attribution => attribution.Roots
                .Where(root => schema.TryGet(root, out _) is false)
                .Select(root => new ProfileError("UnknownField", $"Path '{pathId}' attributes '{field}' by '{name}', whose root '{root}' is not declared.")),
        };
}
