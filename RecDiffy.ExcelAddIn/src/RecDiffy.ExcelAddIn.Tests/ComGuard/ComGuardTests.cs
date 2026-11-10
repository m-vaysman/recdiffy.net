using System.Reflection;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests.ComGuard;

public class ComGuardTests
{
    private static readonly Assembly[] AddInAssemblies = [typeof(RecDiffyRibbon).Assembly, typeof(ShellViewModel).Assembly];

    [Fact]
    public void The_add_in_uses_no_com_beyond_the_approved_ribbon()
    {
        var unapproved = AddInAssemblies
            .SelectMany(ComUses.In)
            .Where(use => !ApprovedComUses.Covers(use))
            .Select(use => use.ToString())
            .ToList();

        Assert.True(
            unapproved is [],
            $"""
            COM in the add-in needs the lead's approval, listed in {ApprovedComUses.Where} and in {nameof(ApprovedComUses)}.
            Not approved:
            {string.Join(Environment.NewLine, unapproved)}
            """);
    }

    [Fact]
    public void The_guard_finds_each_approved_use_in_the_ribbon()
    {
        var uses = AddInAssemblies.SelectMany(ComUses.In).ToList();

        var unused = ApprovedComUses.All
            .Where(approval => !uses.Exists(use => use.Site == approval.Site && use.Api == approval.Api))
            .ToList();

        Assert.Empty(unused);
    }
}
