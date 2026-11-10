using System.Runtime.InteropServices;
using ExcelDna.Integration.CustomUI;

namespace RecDiffy.ExcelAddIn.Tests.ComGuard;

internal sealed record ComApproval(string Site, string Api, string Link);

internal static class ApprovedComUses
{
    public const string Where = "AGENTS.md, under \"Excel-DNA add-in\" and \"Approved COM uses\"";

    public const string WpfIncrementalBuildXamlHelper = "XamlGeneratedNamespace.GeneratedInternalTypeHelper";

    private const string Ribbon = "https://github.com/m-vaysman/recdiffy.net/issues/192#issuecomment-6742644969";

    public static IReadOnlyList<ComApproval> All { get; } =
    [
        new(Name<RecDiffyRibbon>(), Name<ExcelRibbon>(), Ribbon),
        new(Name<RecDiffyRibbon>(), Name<IRibbonControl>(), Ribbon),
        new(Name<RecDiffyRibbon>(), Name<ComVisibleAttribute>(), Ribbon),
    ];

    public static bool Covers(ComUse use) => use switch
    {
        { Site: WpfIncrementalBuildXamlHelper, Api: "System.Type", Member: "InvokeMember" } => true,
        { Member: "" } => All.Any(approval => approval.Site == use.Site && approval.Api == use.Api),
        _ => false,
    };

    private static string Name<T>() => typeof(T).FullName ?? typeof(T).Name;
}
