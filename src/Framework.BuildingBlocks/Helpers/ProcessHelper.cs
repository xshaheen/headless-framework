using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Framework.BuildingBlocks.Helpers;

// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
public static class ProcessHelper
{
    [SupportedOSPlatform("windows")]
    public static IEnumerable<ProcessEntry> GetProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Only supported on Windows");
        }

        using var snapShotHandle = CreateToolhelp32Snapshot(SnapshotFlags.TH32CS_SNAPPROCESS, 0);

        var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32)), };

        var result = Process32First(snapShotHandle, ref entry);

        while (result != 0)
        {
            yield return new ProcessEntry(entry.th32ProcessID, entry.th32ParentProcessID);

            result = Process32Next(snapShotHandle, ref entry);
        }
    }

    #region Private Types

    private const int _MaxPath = 260;

    // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682489.aspx
    private enum SnapshotFlags : uint
    {
        TH32CS_SNAPHEAPLIST = 0x00000001,
        TH32CS_SNAPPROCESS = 0x00000002,
        TH32CS_SNAPTHREAD = 0x00000004,
        TH32CS_SNAPMODULE = 0x00000008,
        TH32CS_SNAPMODULE32 = 0x00000010,
        TH32CS_INHERIT = 0x80000000,
    }

    // https://msdn.microsoft.com/en-us/library/windows/desktop/ms684839.aspx
    [UsedImplicitly]
    private sealed class SnapshotSafeHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct ProcessEntry32
    {
#pragma warning disable IDE1006 // Naming Styles
        public uint dwSize;
        public uint cntUsage;
        public int th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public int th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = _MaxPath)]
        public string szExeFile;
#pragma warning restore IDE1006 // Naming Styles
    }

    #endregion

    #region Extern Methods

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Process32First(SnapshotSafeHandle handle, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Process32Next(SnapshotSafeHandle handle, ref ProcessEntry32 entry);

    // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682489.aspx
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SnapshotSafeHandle CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint th32ProcessID);

    #endregion
}

#region Types

[StructLayout(LayoutKind.Auto)]
public readonly struct ProcessEntry : IEquatable<ProcessEntry>
{
    internal ProcessEntry(int processId, int parentProcessId)
    {
        ProcessId = processId;
        ParentProcessId = parentProcessId;
    }

    public int ProcessId { get; }

    public int ParentProcessId { get; }

    public override bool Equals(object? obj)
    {
        return obj is ProcessEntry entry && Equals(entry);
    }

    public bool Equals(ProcessEntry other)
    {
        return ProcessId == other.ProcessId && ParentProcessId == other.ParentProcessId;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ProcessId, ParentProcessId);
    }

    public Process ToProcess()
    {
        return Process.GetProcessById(ProcessId);
    }

    public static bool operator ==(ProcessEntry left, ProcessEntry right) => left.Equals(right);

    public static bool operator !=(ProcessEntry left, ProcessEntry right) => !(left == right);
}

#endregion
