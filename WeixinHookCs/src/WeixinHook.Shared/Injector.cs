using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WeixinHook.Shared;

public static class Injector
{
    public static (bool Ok, string Message) Inject(string processName, string dllPath)
    {
        var target = Process.GetProcessesByName(processName).FirstOrDefault();
        if (target is null)
            return (false, $"未找到进程: {processName}");

        if (!File.Exists(dllPath))
            return (false, $"DLL 不存在: {dllPath}");

        var hProcess = OpenProcess(ProcessAccessFlags.All, false, target.Id);
        if (hProcess == IntPtr.Zero)
            return (false, "OpenProcess 失败");

        try
        {
            var dllBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
            var remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)dllBytes.Length,
                AllocationType.Commit | AllocationType.Reserve, MemoryProtection.ReadWrite);
            if (remoteMem == IntPtr.Zero)
                return (false, "VirtualAllocEx 失败");

            if (!WriteProcessMemory(hProcess, remoteMem, dllBytes, dllBytes.Length, out _))
                return (false, "WriteProcessMemory 失败");

            var hKernel32 = GetModuleHandle("kernel32.dll");
            var loadLibraryW = GetProcAddress(hKernel32, "LoadLibraryW");
            var hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryW, remoteMem, 0, IntPtr.Zero);
            if (hThread == IntPtr.Zero)
                return (false, "CreateRemoteThread 失败");

            CloseHandle(hThread);
            return (true, $"已注入 PID={target.Id}");
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    public static string ResolveNativeDll()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "WeixinHook.Native.dll"),
            Path.Combine(baseDir, "..", "WeixinHook.Native", "WeixinHook.Native.dll"),
            Path.Combine(baseDir, "..", "..", "WeixinHook.Native", "bin", "Release", "net8.0", "win-x64", "publish", "WeixinHook.Native.dll"),
            Path.Combine(baseDir, "..", "..", "..", "WeixinHook.Native", "bin", "Release", "net8.0", "win-x64", "WeixinHook.Native.dll"),
            @"C:\Temp\WxHookOut\WeixinHook.Native.dll",
        };
        foreach (var p in candidates)
        {
            var full = Path.GetFullPath(p);
            if (File.Exists(full)) return full;
        }
        return Path.GetFullPath(Path.Combine(baseDir, "WeixinHook.Native.dll"));
    }

    [Flags]
    private enum ProcessAccessFlags : uint { All = 0x001F0FFF }

    [Flags]
    private enum AllocationType : uint { Commit = 0x1000, Reserve = 0x2000 }

    private enum MemoryProtection : uint { ReadWrite = 0x04 }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(ProcessAccessFlags dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
        AllocationType flAllocationType, MemoryProtection flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
        int nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
        IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
