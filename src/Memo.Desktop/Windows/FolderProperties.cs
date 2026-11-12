using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Memo.Desktop.Windows;

internal static class FolderProperties
{
    public static void Open(string path, nint owner)
    {
        var request = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Mask = 0x0000000c, // SEE_MASK_INVOKEIDLIST
            Owner = owner,
            Verb = "properties",
            File = path,
            Show = 1
        };
        if (!ShellExecuteEx(ref request))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public nint Owner;
        public string? Verb, File, Parameters, Directory;
        public int Show;
        public nint Instance, IdList;
        public string? Class;
        public nint ClassKey;
        public uint HotKey;
        public nint IconOrMonitor, Process;
    }

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo request);
}
