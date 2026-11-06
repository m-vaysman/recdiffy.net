namespace RecDiffy.Samples;

public static class Program
{
    public static async Task<int> Main()
    {
        var outcomes = new[]
        {
            FirstReconciliation.Run(),
            ResultReading.Run(),
            SideBySideReport.Run(),
            await AsyncRun.Run().ConfigureAwait(false),
            TradingReference.Run(),
            ExcelReports.Run(),
            FileReaders.Run(),
        };
        return outcomes.All(outcome => outcome) ? 0 : 1;
    }
}
