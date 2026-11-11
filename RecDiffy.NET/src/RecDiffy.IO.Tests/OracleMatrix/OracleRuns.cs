using RecDiffy.Results;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Each dataset's profile and reference data, as a run over any two sources.
internal static class OracleRuns
{
    public static Func<RecordSource, RecordSource, ReconciliationResult> Of(string name) => name switch
    {
        "claude-sonnet-5.5" => SonnetOracleTests.RunOn,
        "fixture 2.0" => Fixture20OracleTests.RunOn,
        "fixture v1" => FixtureV1.Run,
        "claude-opus-5.5" => OpusOracleTests.RunOn,
        "fixture v2" => FixtureV2OracleTests.RunOn,
        "qwen-3.8" => QwenOracleTests.RunOn,
        "deepseek-v4-pro" => DeepSeekOracleTests.RunOn,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No profile for this dataset yet."),
    };
}
