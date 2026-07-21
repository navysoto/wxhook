using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WeixinHook.Native;

internal static class HookUtils
{
    public static void WriteAbsJump(IntPtr from, IntPtr to)
    {
        unsafe
        {
            var p = (byte*)from;
            p[0] = 0xFF; p[1] = 0x25;
            p[2] = 0x00; p[3] = 0x00; p[4] = 0x00; p[5] = 0x00;
            *(ulong*)(p + 6) = (ulong)to;
        }
    }

    public static void PatchByte(IntPtr addr, byte val)
    {
        if (!NativeApi.VirtualProtect(addr, (UIntPtr)1, NativeApi.PageExecuteReadwrite, out var old))
            return;
        unsafe { *(byte*)addr = val; }
        NativeApi.VirtualProtect(addr, (UIntPtr)1, old, out _);
    }

    public static IntPtr AllocNear(IntPtr target, nuint size)
    {
        NativeApi.GetSystemInfo(out var si);
        var gran = si.AllocationGranularity != 0 ? si.AllocationGranularity : 0x10000;
        var span = 0x40000000UL;
        var targetAddr = (ulong)target;

        for (ulong delta = gran; delta < span; delta += gran)
        {
            for (var dir = 0; dir < 2; dir++)
            {
                var addr = dir == 0 ? targetAddr + delta : targetAddr - delta;
                addr &= ~(gran - 1);
                var p = NativeApi.VirtualAlloc((IntPtr)(long)addr, (UIntPtr)size,
                    NativeApi.MemCommit | NativeApi.MemReserve, NativeApi.PageExecuteReadwrite);
                if (p != IntPtr.Zero) return p;
            }
        }

        return NativeApi.VirtualAlloc(IntPtr.Zero, (UIntPtr)size,
            NativeApi.MemCommit | NativeApi.MemReserve, NativeApi.PageExecuteReadwrite);
    }

    public static bool SafeRead(IntPtr src, Span<byte> dst)
    {
        try
        {
            unsafe
            {
                Buffer.MemoryCopy((void*)src, Unsafe.AsPointer(ref dst[0]), dst.Length, dst.Length);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool SafeReadU64(IntPtr src, out ulong value)
    {
        value = 0;
        Span<byte> buf = stackalloc byte[8];
        if (!SafeRead(src, buf)) return false;
        value = BitConverter.ToUInt64(buf);
        return true;
    }

    public static bool SafeReadI32(IntPtr src, out int value)
    {
        value = 0;
        Span<byte> buf = stackalloc byte[4];
        if (!SafeRead(src, buf)) return false;
        value = BitConverter.ToInt32(buf);
        return true;
    }

    /// <summary>Minimal x64 insn length decoder for hook trampoline.</summary>
    public static int InstructionLength(IntPtr ip)
    {
        unsafe
        {
            var p = (byte*)ip;
            var rex = 0;
            if ((*p & 0xF0) == 0x40) { rex = *p++; }

            switch (*p++)
            {
                case 0x50: case 0x51: case 0x52: case 0x53:
                case 0x54: case 0x55: case 0x56: case 0x57:
                case 0x58: case 0x59: case 0x5A: case 0x5B:
                case 0x5C: case 0x5D: case 0x5E: case 0x5F:
                    return (int)((nint)p - (nint)ip);
                case 0x90:
                    return (int)((nint)p - (nint)ip);
                case 0xC3:
                    return (int)((nint)p - (nint)ip);
                case 0xCC:
                    return (int)((nint)p - (nint)ip);
                case 0xE8:
                    return (int)((nint)(p + 4) - (nint)ip);
                case 0xE9:
                    return (int)((nint)(p + 4) - (nint)ip);
                case 0xEB:
                    return (int)((nint)(p + 1) - (nint)ip);
                case 0x48:
                case 0x4C:
                    return InstructionLength((IntPtr)(p - 1));
                case 0x0F:
                {
                    var op2 = *p++;
                    if (op2 is 0x28 or 0x29 or 0x2E or 0x2F)
                    {
                        p += DecodeModRm(p, rex);
                        return (int)((nint)p - (nint)ip);
                    }
                    return 3;
                }
                default:
                    if ((*p & 0xF8) == 0xB8)
                        return (int)((nint)(p + ((rex & 8) != 0 ? 8 : 4)) - (nint)ip);
                    p += DecodeModRm(p, rex);
                    return Math.Max(1, (int)((nint)p - (nint)ip));
            }
        }
    }

    private static unsafe int DecodeModRm(byte* p, int rex)
    {
        var modrm = *p++;
        var mod = modrm >> 6;
        var rm = modrm & 7;
        if (mod != 3 && rm == 4)
        {
            var sib = *p++;
            var baseRm = sib & 7;
            if (mod == 0 && baseRm == 5) p += 4;
        }
        if (mod == 0 && rm == 5) p += 4;
        else if (mod == 1) p += 1;
        else if (mod == 2) p += 4;
        if ((rex & 8) != 0 && rm == 0 && mod != 3) { }
        return (int)((nint)p - (nint)(p - 1));
    }

    public static int CopyInstructions(IntPtr src, IntPtr dst, int minBytes, out int stolen)
    {
        stolen = 0;
        var pos = 0;
        while (stolen < minBytes)
        {
            var len = InstructionLength(src + stolen);
            if (len <= 0 || len > 32) return -1;
            unsafe
            {
                Buffer.MemoryCopy((void*)(src + stolen), (void*)(dst + pos), len, len);
            }
            pos += len;
            stolen += len;
        }
        return pos;
    }
}
