namespace RecDiffy;

public static class Reconciler
{
    public static CompiledProfile Compile(ProfileDocument document, RuleRegistry registry) =>
        ProfileCompiler.Compile(document, registry) switch
        {
            { Errors: [] } outcome => outcome.Profile!,
            var failed => throw new ProfileCompileException(failed.Errors),
        };
}
