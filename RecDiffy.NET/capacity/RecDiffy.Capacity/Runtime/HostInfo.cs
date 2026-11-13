using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RecDiffy.Capacity;

internal static class HostInfo
{
    public static ReportHost Capture() => new(
        Environment.MachineName,
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        RuntimeInformation.FrameworkDescription,
        SdkVersion(),
        GCSettings.IsServerGC ? "server" : "workstation",
        GCSettings.LatencyMode.ToString(),
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        !RuntimeFeature.IsDynamicCodeSupported,
        RuntimeFeature.IsDynamicCodeCompiled);

    private static string SdkVersion() =>
        typeof(HostInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key is "RecDiffySdkVersion")?.Value is { Length: > 0 } version
            ? version
            : "unknown";
}
