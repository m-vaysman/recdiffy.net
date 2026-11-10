namespace RecDiffy.ExcelAddIn.Tests.ComGuard;

public class ComUsesTests
{
    [Theory]
    [InlineData(typeof(ReadsExcelApplication), "ExcelDna.Integration.ExcelDnaUtil", "get_Application")]
    [InlineData(typeof(ReadsWindowHandle), "ExcelDna.Integration.ExcelDnaUtil", "get_WindowHandle")]
    [InlineData(typeof(ReleasesComObjects), "System.Runtime.InteropServices.Marshal", "ReleaseComObject")]
    [InlineData(typeof(StartsExcelByProgId), "System.Type", "GetTypeFromProgID")]
    [InlineData(typeof(DrivesExcelDynamically), "Microsoft.CSharp.RuntimeBinder.Binder", "")]
    [InlineData(typeof(DrivesExcelByInvokeMember), "System.Type", "InvokeMember")]
    [InlineData(typeof(TakesExcelInterop), "Microsoft.Office.Interop.Excel.Workbook", "")]
    [InlineData(typeof(ReadsRibbonContext), "ExcelDna.Integration.CustomUI.IRibbonControl", "get_Context")]
    [InlineData(typeof(IDeclaredComInterface), "RecDiffy.ExcelAddIn.Tests.ComGuard.IDeclaredComInterface", "")]
    [InlineData(typeof(ExposedToCom), "System.Runtime.InteropServices.ComVisibleAttribute", "")]
    [InlineData(typeof(CallsOle32), "ole32.dll", "CoInitialize")]
    [InlineData(typeof(SecondRibbon), "ExcelDna.Integration.CustomUI.ExcelRibbon", "")]
    public void Each_kind_of_com_use_is_found(Type fixture, string api, string member) =>
        Assert.Contains(ComUses.Of(fixture), use => use.Api == api && use.Member == member);

    [Fact]
    public void The_c_api_wpf_and_user32_are_not_com_uses() =>
        Assert.Empty(ComUses.Of(typeof(UsesTheCApiAndWpf)));

    [Fact]
    public void A_type_hidden_from_com_is_not_a_com_use() =>
        Assert.Empty(ComUses.Of(typeof(HiddenFromCom)));

    [Fact]
    public void A_second_ribbon_is_not_covered_by_the_approval() =>
        Assert.DoesNotContain(ComUses.Of(typeof(SecondRibbon)), ApprovedComUses.Covers);

    [Fact]
    public void The_ribbons_approval_does_not_cover_reading_the_excel_window_from_its_control()
    {
        var ribbon = typeof(RecDiffyRibbon).FullName ?? nameof(RecDiffyRibbon);

        var uncovered = ComUses.Of(typeof(ReadsRibbonContext))
            .Select(use => use with { Site = ribbon })
            .Where(use => !ApprovedComUses.Covers(use))
            .ToList();

        Assert.Equal("get_Context", Assert.Single(uncovered).Member);
    }

    [Fact]
    public void Wpfs_generated_xaml_helper_may_call_invoke_member_and_nothing_else()
    {
        IReadOnlyList<ComUse> uses =
        [
            .. ComUses.Of(typeof(DrivesExcelByInvokeMember)),
            .. ComUses.Of(typeof(ReadsExcelApplication)),
            .. ComUses.Of(typeof(DrivesExcelDynamically)),
        ];

        var covered = uses
            .Select(use => use with { Site = ApprovedComUses.WpfIncrementalBuildXamlHelper })
            .Where(ApprovedComUses.Covers)
            .ToList();

        Assert.Equal("InvokeMember", Assert.Single(covered).Member);
    }

    [Theory]
    [InlineData("Microsoft.Office.Interop.Excel")]
    [InlineData("office")]
    [InlineData("Microsoft.Vbe.Interop")]
    [InlineData("NetOffice")]
    [InlineData("Interop.Excel")]
    [InlineData("stdole")]
    [InlineData("Microsoft.CSharp")]
    public void A_reference_to_an_interop_assembly_or_to_dynamic_is_a_com_use(string assembly) =>
        Assert.NotNull(ComUses.WhyAssembly(assembly));

    [Theory]
    [InlineData("ExcelDna.Integration")]
    [InlineData("PresentationFramework")]
    [InlineData("RecDiffy.IO")]
    public void Other_assembly_references_are_not_com_uses(string assembly) =>
        Assert.Null(ComUses.WhyAssembly(assembly));
}
