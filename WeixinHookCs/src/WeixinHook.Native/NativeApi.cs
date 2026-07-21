using System.Runtime.InteropServices;

namespace WeixinHook.Native;

internal static partial class NativeApi
{
    public const uint DllProcessAttach = 1;
    public const uint ExceptionBreakpoint = 0x80000003;
    public const uint ExceptionSingleStep = 0x80000004;
    public const uint ExceptionContinueExecution = unchecked((uint)-1);
    public const uint ExceptionContinueSearch = 0;

    public const uint ContextAmd64 = 0x00100000;
    public const uint ContextControl = ContextAmd64 | 0x00000001;
    public const uint ContextInteger = ContextAmd64 | 0x00000002;
    public const uint ContextSegments = ContextAmd64 | 0x00000004;
    public const uint ContextFloatingPoint = ContextAmd64 | 0x00000008;
    public const uint ContextDebugRegisters = ContextAmd64 | 0x00000010;
    public const uint ContextFull = ContextControl | ContextInteger | ContextFloatingPoint;

    public const uint MemCommit = 0x1000;
    public const uint MemReserve = 0x2000;
    public const uint PageExecuteReadwrite = 0x40;

    public const uint ThreadSuspendResume = 0x0002;
    public const uint ThreadGetContext = 0x0008;
    public const uint ThreadSetContext = 0x0010;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    public static extern IntPtr GetModuleHandleA(string lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern void GetSystemInfo(out SystemInfo lpSystemInfo);

    [DllImport("kernel32.dll")]
    public static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll")]
    public static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll")]
    public static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

    [DllImport("kernel32.dll")]
    public static extern IntPtr AddVectoredExceptionHandler(uint first, IntPtr handler);

    [DllImport("kernel32.dll")]
    public static extern uint RemoveVectoredExceptionHandler(IntPtr handle);

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll")]
    public static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll")]
    public static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll")]
    public static extern bool GetThreadContext(IntPtr hThread, ref Context64 ctx);

    [DllImport("kernel32.dll")]
    public static extern bool SetThreadContext(IntPtr hThread, ref Context64 ctx);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    public static extern void Sleep(uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    public static extern IntPtr CreateThread(
        IntPtr lpThreadAttributes,
        UIntPtr dwStackSize,
        IntPtr lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId);

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr malloc(UIntPtr size);

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free(IntPtr ptr);

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemInfo
    {
        public ushort ProcessorArchitecture;
        public ushort Reserved;
        public uint PageSize;
        public IntPtr MinimumApplicationAddress;
        public IntPtr MaximumApplicationAddress;
        public IntPtr ActiveProcessorMask;
        public uint NumberOfProcessors;
        public uint ProcessorType;
        public uint AllocationGranularity;
        public ushort ProcessorLevel;
        public ushort ProcessorRevision;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExceptionRecord
    {
        public int ExceptionCode;
        public uint ExceptionFlags;
        public IntPtr ExceptionRecordPtr;
        public IntPtr ExceptionAddress;
        public uint NumberParameters;
        public ulong ExceptionInformation0;
        public ulong ExceptionInformation1;
        public ulong ExceptionInformation2;
        public ulong ExceptionInformation3;
        public ulong ExceptionInformation4;
        public ulong ExceptionInformation5;
        public ulong ExceptionInformation6;
        public ulong ExceptionInformation7;
        public ulong ExceptionInformation8;
        public ulong ExceptionInformation9;
        public ulong ExceptionInformation10;
        public ulong ExceptionInformation11;
        public ulong ExceptionInformation12;
        public ulong ExceptionInformation13;
        public ulong ExceptionInformation14;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExceptionPointers
    {
        public IntPtr ExceptionRecord;
        public IntPtr ContextRecord;
    }

    /// <summary>
    /// Full AMD64 CONTEXT (winnt.h). Truncating this breaks Get/SetThreadContext
    /// for HW breakpoints — the previous short layout was why send hung with no effect.
    /// Size must be 1232 (0x4D0).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 0x4D0)]
    public struct Context64
    {
        [FieldOffset(0x00)] public ulong P1Home;
        [FieldOffset(0x08)] public ulong P2Home;
        [FieldOffset(0x10)] public ulong P3Home;
        [FieldOffset(0x18)] public ulong P4Home;
        [FieldOffset(0x20)] public ulong P5Home;
        [FieldOffset(0x28)] public ulong P6Home;
        [FieldOffset(0x30)] public uint ContextFlags;
        [FieldOffset(0x34)] public uint MxCsr;
        [FieldOffset(0x38)] public ushort SegCs;
        [FieldOffset(0x3A)] public ushort SegDs;
        [FieldOffset(0x3C)] public ushort SegEs;
        [FieldOffset(0x3E)] public ushort SegFs;
        [FieldOffset(0x40)] public ushort SegGs;
        [FieldOffset(0x42)] public ushort SegSs;
        [FieldOffset(0x44)] public uint EFlags;
        [FieldOffset(0x48)] public ulong Dr0;
        [FieldOffset(0x50)] public ulong Dr1;
        [FieldOffset(0x58)] public ulong Dr2;
        [FieldOffset(0x60)] public ulong Dr3;
        [FieldOffset(0x68)] public ulong Dr6;
        [FieldOffset(0x70)] public ulong Dr7;
        [FieldOffset(0x78)] public ulong Rax;
        [FieldOffset(0x80)] public ulong Rcx;
        [FieldOffset(0x88)] public ulong Rdx;
        [FieldOffset(0x90)] public ulong Rbx;
        [FieldOffset(0x98)] public ulong Rsp;
        [FieldOffset(0xA0)] public ulong Rbp;
        [FieldOffset(0xA8)] public ulong Rsi;
        [FieldOffset(0xB0)] public ulong Rdi;
        [FieldOffset(0xB8)] public ulong R8;
        [FieldOffset(0xC0)] public ulong R9;
        [FieldOffset(0xC8)] public ulong R10;
        [FieldOffset(0xD0)] public ulong R11;
        [FieldOffset(0xD8)] public ulong R12;
        [FieldOffset(0xE0)] public ulong R13;
        [FieldOffset(0xE8)] public ulong R14;
        [FieldOffset(0xF0)] public ulong R15;
        [FieldOffset(0xF8)] public ulong Rip;
        // 0x100..0x4CF: XMM_SAVE_AREA32 + VectorRegister — left as implicit padding via Size=0x4D0
    }
}
