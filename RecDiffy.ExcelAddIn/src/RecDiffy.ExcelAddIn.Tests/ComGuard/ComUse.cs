namespace RecDiffy.ExcelAddIn.Tests.ComGuard;

internal sealed record ComUse(string Site, string Api, string Member, string Reason)
{
    public override string ToString() =>
        Member is "" ? $"{Site} uses {Api}, which {Reason}" : $"{Site} uses {Api}.{Member}, which {Reason}";
}
