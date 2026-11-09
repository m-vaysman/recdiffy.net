using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Resources;

namespace RecDiffy.ExcelAddIn.Tests;

public class RibbonIconsTests
{
    [Theory]
    [InlineData(96u, 32)]
    [InlineData(120u, 40)]
    [InlineData(144u, 48)]
    [InlineData(168u, 64)]
    [InlineData(192u, 64)]
    [InlineData(288u, 64)]
    [InlineData(72u, 32)]
    public void The_icon_size_follows_the_display_scaling(uint dpi, int expected) =>
        Assert.Equal(expected, RibbonIcons.SizeFor(dpi));

    [Fact]
    public void Every_ribbon_size_is_packed_square_with_a_transparent_outside_and_an_opaque_middle()
    {
        foreach (var size in RibbonIcons.Sizes)
        {
            using var icon = RibbonIcons.Load(size);

            Assert.Equal((size, size), (icon.Width, icon.Height));
            Assert.Equal(PixelFormat.Format32bppArgb, icon.PixelFormat);
            Assert.Equal(0, icon.GetPixel(0, 0).A);
            Assert.Equal(255, icon.GetPixel(size / 2, size / 2).A);
        }
    }

    [Fact]
    public void An_unknown_size_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var icon = RibbonIcons.Load(33);
        });

    [Fact]
    public void The_window_icon_is_packed_as_a_wpf_resource()
    {
        var resources = new ResourceManager("RecDiffy.ExcelAddIn.g", typeof(RibbonIcons).Assembly);
        using var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);

        Assert.NotNull(set);
        Assert.Contains("resources/recdiffy.ico", set.Cast<DictionaryEntry>().Select(entry => (string)entry.Key), StringComparer.Ordinal);
    }
}
