namespace WeixinHook.Native;

/// <summary>
/// MSVC std::string (x64). After MsgCtor the field is a live WeChat-CRT string.
/// Never free the old buffer with our CRT — that heap-corrupts Weixin.dll.
/// Only overwrite the 32-byte header; SSO empty from MsgCtor is the common case.
/// </summary>
internal static class MsvcString
{
    public static void Assign(IntPtr fieldAddr, ReadOnlySpan<byte> utf8)
    {
        unsafe
        {
            var p = (byte*)fieldAddr;
            // Intentionally do NOT free old Cap>=16 pointer (WeChat CRT ≠ our CRT).
            // Leak any prior heap block; MsgCtor usually leaves SSO empty anyway.
            var len = (ulong)utf8.Length;
            if (len <= 15)
            {
                for (ulong i = 0; i < len; i++)
                    p[i] = utf8[(int)i];
                p[len] = 0;
                *(ulong*)(p + 16) = len;
                *(ulong*)(p + 24) = 15;
                return;
            }

            var newCap = len | 15;
            var buf = NativeApi.malloc((UIntPtr)(newCap + 1));
            if (buf == IntPtr.Zero)
                return;

            var dst = (byte*)buf;
            for (ulong i = 0; i < len; i++)
                dst[i] = utf8[(int)i];
            dst[len] = 0;
            *(IntPtr*)p = buf;
            *(ulong*)(p + 16) = len;
            *(ulong*)(p + 24) = newCap;
        }
    }

    public static bool TryRead(IntPtr fieldAddr, Span<byte> output, out int length)
    {
        length = 0;
        if (!HookUtils.SafeReadU64(fieldAddr + 16, out var lenU)) return false;
        if (!HookUtils.SafeReadU64(fieldAddr + 24, out var capU)) return false;
        var len = (int)lenU;
        if (len <= 0 || len >= output.Length) return false;

        ReadOnlySpan<byte> src;
        if (capU >= 16)
        {
            if (!HookUtils.SafeReadU64(fieldAddr, out var ptr) || ptr < 0x10000) return false;
            var tmp = new byte[len];
            if (!HookUtils.SafeRead((IntPtr)(long)ptr, tmp)) return false;
            src = tmp;
        }
        else
        {
            var tmp = new byte[len];
            if (!HookUtils.SafeRead(fieldAddr, tmp)) return false;
            src = tmp;
        }

        src.CopyTo(output);
        length = len;
        return true;
    }
}
