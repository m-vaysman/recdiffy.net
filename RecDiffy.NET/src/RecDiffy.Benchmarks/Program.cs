using BenchmarkDotNet.Running;

namespace RecDiffy.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args is ["--bmf", var resultsDirectory, var outputPath])
        {
            BmfReport.Write(resultsDirectory, outputPath);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
