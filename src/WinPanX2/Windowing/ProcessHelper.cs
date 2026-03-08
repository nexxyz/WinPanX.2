using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinPanX2.Windowing;

internal static class ProcessHelper
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    public static int? GetParentProcessId(int pid)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot.ToInt64() == -1)
            return null;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };

            if (!Process32First(snapshot, ref entry))
                return null;

            do
            {
                if (entry.th32ProcessID == pid)
                    return (int)entry.th32ParentProcessID;
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return null;
    }

    public static string? GetProcessName(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private sealed class NameCacheEntry
    {
        public string? Name { get; }
        public long Tick { get; }

        public NameCacheEntry(string? name, long tick)
        {
            Name = name;
            Tick = tick;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, NameCacheEntry> _nameCache = new();

    public static int NameCacheCount => _nameCache.Count;

    public static string? GetProcessNameCached(int pid, long nowTick, int ttlMs = 5000)
    {
        if (_nameCache.TryGetValue(pid, out var cached))
        {
            if (nowTick - cached.Tick <= ttlMs)
                return cached.Name;
        }

        var name = GetProcessName(pid);
        _nameCache[pid] = new NameCacheEntry(name, nowTick);
        return name;
    }

    public static void PruneNameCache(long nowTick, int ttlMs = 60_000)
    {
        foreach (var kvp in _nameCache)
        {
            if (nowTick - kvp.Value.Tick <= ttlMs)
                continue;

            _nameCache.TryRemove(kvp.Key, out _);
        }
    }
}
