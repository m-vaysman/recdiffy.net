namespace RecDiffy.Capacity;

internal struct SplitMix64(ulong state)
{
    private ulong _state = state;

    public static ulong Mix(ulong value)
    {
        var mixed = value + 0x9E37_79B9_7F4A_7C15UL;
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return mixed ^ (mixed >> 31);
    }

    public ulong Next()
    {
        _state += 0x9E37_79B9_7F4A_7C15UL;
        return Mix(_state);
    }
}
