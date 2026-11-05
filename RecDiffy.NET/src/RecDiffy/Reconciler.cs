namespace RecDiffy;

public static class Reconciler
{
    public static CompiledProfile Compile(ProfileDocument document, RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registry);
        // An error list throws. Callers receive a complete profile or an exception.
        return ProfileCompiler.Compile(document, registry) switch
        {
            { Errors: [] } outcome => outcome.Profile!,
            var failed => throw new ProfileCompileException(failed.Errors),
        };
    }
}
