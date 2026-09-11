using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;

namespace ProxyPilot.Core.Native;

public sealed class ProcessLookup
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const int ProcessQueryLimitedInformation = 0x1000;

    private readonly ConcurrentDictionary<int, string> _names = new();
    private readonly ConcurrentDictionary<(int Pid, ushort LocalPort, uint Remote, ushort RemotePort), int> _recent = new();

    public void Remember(int pid, IPAddress remote, ushort remotePort, ushort localPort)
    {
        if (pid <= 0)
            return;
        var key = (pid, localPort, ToRaw(remote), remotePort);
        _recent[key] = pid;
        _ = GetName(pid);
    }

    public int FindUdpPid(ushort localPort)
    {
        var size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, true, AfInet, 1, 0);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(ptr, ref size, true, AfInet, 1, 0) != 0)
                return 0;
            var count = Marshal.ReadInt32(ptr);
            var offset = ptr + 4;
            for (var i = 0; i < count; i++)
            {
                var localPortRaw = (uint)Marshal.ReadInt32(offset + 4);
                var pid = Marshal.ReadInt32(offset + 8);
                if (Ntohs(localPortRaw) == localPort)
                    return pid;
                offset += 12;
            }
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public int FindPid(IPAddress local, ushort localPort, IPAddress remote, ushort remotePort)
    {
        foreach (var kv in _recent)
        {
            if (kv.Key.LocalPort == localPort && kv.Key.Remote == ToRaw(remote) && kv.Key.RemotePort == remotePort)
                return kv.Value;
        }

        if (!TryGetTable(out var rows))
            return 0;

        var localRaw = ToRaw(local);
        var remoteRaw = ToRaw(remote);
        foreach (var row in rows)
        {
            if (Ntohs(row.LocalPort) != localPort || Ntohs(row.RemotePort) != remotePort)
                continue;
            if (row.RemoteAddr != remoteRaw)
                continue;
            if (row.LocalAddr != 0 && localRaw != 0 && row.LocalAddr != localRaw)
                continue;
            Remember(row.OwningPid, remote, remotePort, localPort);
            return row.OwningPid;
        }
        return 0;
    }

    public string GetName(int pid)
    {
        if (pid <= 0)
            return "unknown";
        if (_names.TryGetValue(pid, out var cached))
            return cached;

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            _names[pid] = "unknown";
            return "unknown";
        }

        try
        {
            var size = 1024;
            var buffer = new System.Text.StringBuilder(size);
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
            {
                _names[pid] = "unknown";
                return "unknown";
            }
            var name = Path.GetFileName(buffer.ToString());
            _names[pid] = name;
            return name;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public void ForgetPid(int pid)
    {
        _names.TryRemove(pid, out _);
        foreach (var key in _recent.Keys.Where(k => k.Pid == pid).ToList())
            _recent.TryRemove(key, out _);
    }

    private static bool TryGetTable(out TcpRow[] rows)
    {
        rows = [];
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, true, AfInet, TcpTableOwnerPidAll, 0);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            var result = GetExtendedTcpTable(ptr, ref size, true, AfInet, TcpTableOwnerPidAll, 0);
            if (result != 0)
                return false;
            var count = Marshal.ReadInt32(ptr);
            rows = new TcpRow[count];
            var offset = ptr + 4;
            var rowSize = Marshal.SizeOf<TcpRow>();
            for (var i = 0; i < count; i++)
            {
                rows[i] = Marshal.PtrToStructure<TcpRow>(offset + i * rowSize);
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static uint ToRaw(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
            return 0;
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static ushort Ntohs(uint port) => (ushort)(((port & 0xFF) << 8) | ((port >> 8) & 0xFF));

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public int OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int af, int tableClass, int reserved);

    [DllImport("dnsapi.dll")]
    public static extern uint DnsFlushResolverCache();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr handle, int flags, System.Text.StringBuilder name, ref int size);
}
