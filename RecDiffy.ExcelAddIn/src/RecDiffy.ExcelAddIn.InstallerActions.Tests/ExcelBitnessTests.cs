using System.IO;
using RecDiffy.ExcelAddIn.InstallerActions;

namespace RecDiffy.ExcelAddIn.InstallerActions.Tests;

public sealed class ExcelBitnessTests
{
    [Theory]
    [InlineData((ushort)0x8664, "x64")]
    [InlineData((ushort)0xAA64, "x64")]
    [InlineData((ushort)0x014C, "x86")]
    [InlineData((ushort)0x01C4, null)]
    public void Each_machine_type_has_a_bitness(ushort machine, string? bitness) =>
        Assert.Equal(bitness, ExcelBitness.Of(machine));

    [Fact]
    public void An_unknown_executable_has_no_bitness() => Assert.Null(ExcelBitness.Of(null));

    [Theory]
    [InlineData("x64", ExcelBitness.AddIn64)]
    [InlineData("x86", ExcelBitness.AddIn32)]
    [InlineData(null, null)]
    public void Each_bitness_installs_its_own_add_in(string? bitness, string? addIn) =>
        Assert.Equal(addIn, ExcelBitness.AddInFor(bitness));

    [Fact]
    public void The_machine_type_is_read_from_a_real_executable()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The test host has no path.");
        using var file = File.OpenRead(path);

        Assert.Equal(Environment.Is64BitProcess ? "x64" : "x86", ExcelBitness.Of(ExcelBitness.MachineType(file)));
    }

    [Fact]
    public void A_file_that_is_not_an_executable_has_no_machine_type()
    {
        using var text = new MemoryStream("This is not an executable, but it is long enough to have a header."u8.ToArray());

        Assert.Null(ExcelBitness.MachineType(text));
    }

    [Fact]
    public void A_header_that_points_past_the_end_has_no_machine_type()
    {
        var bytes = new byte[0x40];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x7FFF_0000).CopyTo(bytes, 0x3C);
        using var stream = new MemoryStream(bytes);

        Assert.Null(ExcelBitness.MachineType(stream));
    }
}
