using System.IO;

namespace RecDiffy.ExcelAddIn.InstallerActions;

public static class ExcelBitness
{
    public const string AddIn64 = "RecDiffy.ExcelAddIn-AddIn64-packed.xll";

    public const string AddIn32 = "RecDiffy.ExcelAddIn-AddIn-packed.xll";

    private const ushort Amd64 = 0x8664;
    private const ushort Arm64 = 0xAA64;
    private const ushort I386 = 0x014C;

    public static ushort? MachineType(Stream executable)
    {
        if (executable is null)
            throw new ArgumentNullException(nameof(executable));

        using var reader = new BinaryReader(executable, System.Text.Encoding.ASCII, leaveOpen: true);
        if (executable.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
            return null;

        executable.Position = 0x3C;
        var header = reader.ReadInt32();
        if (header < 0 || header > executable.Length - 6)
            return null;

        executable.Position = header;
        return reader.ReadUInt32() == 0x00004550 ? reader.ReadUInt16() : null;
    }

    public static string? Of(ushort? machine) => machine switch
    {
        Amd64 or Arm64 => "x64",
        I386 => "x86",
        _ => null,
    };

    public static string? AddInFor(string? bitness) => bitness switch
    {
        "x64" => AddIn64,
        "x86" => AddIn32,
        _ => null,
    };
}
