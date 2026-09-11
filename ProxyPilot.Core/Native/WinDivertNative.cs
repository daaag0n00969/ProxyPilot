using System.Runtime.InteropServices;

namespace ProxyPilot.Core.Native;

internal static class WinDivertNative
{
    public const int LayerNetwork = 0;
    public const int LayerSocket = 3;
    public const ulong FlagSniff = 0x0001;
    public const ulong FlagRecvOnly = 0x0004;
    public const int ParamQueueLength = 0;
    public const int ParamQueueTime = 1;
    public const int ParamQueueSize = 2;
    public const int ShutdownBoth = 3;
    public const int EventSocketConnect = 4;

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertRecv(IntPtr handle, byte[] packet, uint packetLen, out uint recvLen, ref WinDivertAddress addr);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertSend(IntPtr handle, byte[] packet, uint packetLen, out uint sendLen, ref WinDivertAddress addr);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertClose(IntPtr handle);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertShutdown(IntPtr handle, int how);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertSetParam(IntPtr handle, int param, ulong value);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertHelperCalcChecksums(byte[] packet, uint packetLen, ref WinDivertAddress addr, ulong flags);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern bool WinDivertHelperCompileFilter(string filter, int layer, byte[]? obj, uint objLen, out IntPtr errorStr, out uint errorPos);

    public static bool IsInvalid(IntPtr handle) => handle == IntPtr.Zero || handle == new IntPtr(-1);

    public static string? DescribeFilterError(string filter, int layer = LayerNetwork)
    {
        if (WinDivertHelperCompileFilter(filter, layer, null, 0, out var err, out var pos))
            return null;
        var msg = err == IntPtr.Zero ? "ошибка разбора" : Marshal.PtrToStringAnsi(err);
        return $"{msg} (позиция {pos}): {filter}";
    }
}

[StructLayout(LayoutKind.Explicit, Size = 80)]
internal struct WinDivertAddress
{
    [FieldOffset(0)] public long Timestamp;
    [FieldOffset(8)] public uint LayerEventFlags;
    [FieldOffset(12)] public uint Reserved2;
    [FieldOffset(16)] public uint IfIdx;
    [FieldOffset(20)] public uint SubIfIdx;
    [FieldOffset(32)] public uint ProcessId;
    [FieldOffset(36)] public uint LocalAddr0;
    [FieldOffset(52)] public uint RemoteAddr0;
    [FieldOffset(68)] public ushort LocalPort;
    [FieldOffset(70)] public ushort RemotePort;
    [FieldOffset(72)] public byte Protocol;

    public byte Layer => (byte)(LayerEventFlags & 0xFF);
    public byte Event => (byte)((LayerEventFlags >> 8) & 0xFF);
    public bool Outbound
    {
        get => ((LayerEventFlags >> 17) & 1) != 0;
        set
        {
            if (value) LayerEventFlags |= 1u << 17;
            else LayerEventFlags &= ~(1u << 17);
        }
    }
    public bool Loopback => ((LayerEventFlags >> 18) & 1) != 0;
    public bool Impostor => ((LayerEventFlags >> 19) & 1) != 0;
}
