namespace RecDiffy.IO.Tests.OracleMatrix;

internal static class OracleAssert
{
    public static void None(IReadOnlyList<string> disagreements) =>
        Assert.True(
            disagreements.Count == 0,
            $"{disagreements.Count} disagreements with the oracle:{Environment.NewLine}{string.Join(Environment.NewLine, disagreements.Take(100))}");
}
