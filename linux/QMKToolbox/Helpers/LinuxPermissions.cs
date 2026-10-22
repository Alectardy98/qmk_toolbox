using System;
using System.IO;
using System.Runtime.InteropServices;

namespace QMK_Toolbox.Helpers;

public static class LinuxPermissions
{
    [DllImport("libc", SetLastError = true)] private static extern int chmod(string pathname, int mode);

    public static void SetPrivateDirectory(string path) => MakeExecutable(path);

    public static void MakeExecutable(string path)
    {
        if (chmod(path, 448) != 0) throw new IOException($"Cannot secure {path}: errno {Marshal.GetLastWin32Error()}");
    }
}
