using System.Drawing;
using System.Runtime.InteropServices;

namespace RecDiffy.ExcelAddIn;

public static class RibbonIcons
{
    public const string RecDiffy = "RecDiffy";

    public static IReadOnlyList<int> Sizes { get; } = [32, 40, 48, 64];

    public static int SizeFor(uint dpi) =>
        Sizes.Where(size => size * 96L >= 32L * dpi).DefaultIfEmpty(Sizes[^1]).First();

    public static Bitmap Load(int size)
    {
        using var stream = typeof(RibbonIcons).Assembly.GetManifestResourceStream($"RecDiffy.ExcelAddIn.Resources.recdiffy-{size}.png")
            ?? throw new ArgumentOutOfRangeException(nameof(size), size, "There is no ribbon icon of that size.");
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    public static Bitmap ForThisDisplay() => Load(SizeFor(GetDpiForSystem()));

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForSystem();
}
