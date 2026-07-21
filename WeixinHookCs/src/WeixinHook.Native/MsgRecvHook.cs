using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace WeixinHook.Native;

internal sealed class WxMessage
{
    public ulong MsgId;
    public int Type;
    public ulong Timestamp;
    public string From = "";
    public string Wxid = "";
    public string Content = "";
    public string Signature = "";
}

/// <summary>
/// DO_ADD_MSG mid-hook via INT3 + VEH. Dedupes by msgId because WeChat may hit the site repeatedly.
/// </summary>
internal static class MsgRecvHook
{
    private const int PendingCap = 128;
    private const int ContentCap = 4096;

    private static readonly ConcurrentQueue<WxMessage> Queue = new();
    private static readonly object ProcessLock = new();
    private static int _msgCount;

    private static IntPtr _hookPoint;
    private static byte _origByte;
    private static bool _installed;
    private static volatile bool _singleStep;
    private static IntPtr _vehHandle;

    private static readonly PendingSlot[] Pending = new PendingSlot[PendingCap];
    private static int _writeIdx;
    private static int _readIdx;

    // 去重：同一 msgId 只入队一次
    private static long _lastMsgId;
    private static int _lastFinger;

    private sealed class PendingSlot
    {
        public ulong MsgId;
        public int Type;
        public ulong Timestamp;
        public readonly byte[] From = new byte[256];
        public readonly byte[] Wxid = new byte[256];
        public readonly byte[] Content = new byte[ContentCap];
        public readonly byte[] Signature = new byte[256];
        public int FromLen;
        public int WxidLen;
        public int ContentLen;
        public int SignatureLen;
        public int Valid;
    }

    static MsgRecvHook()
    {
        for (var i = 0; i < PendingCap; i++)
            Pending[i] = new PendingSlot();
    }

    public static bool Install(ulong weixinBase)
    {
        if (_installed) return true;
        if (weixinBase == 0 || WeixinOffsets.Msg.DoAddMsg == 0) return false;

        _hookPoint = (IntPtr)(long)(weixinBase + WeixinOffsets.Msg.DoAddMsg);

        if (!NativeApi.VirtualProtect(_hookPoint, (UIntPtr)1, NativeApi.PageExecuteReadwrite, out var old))
            return false;
        _origByte = Marshal.ReadByte(_hookPoint);
        Marshal.WriteByte(_hookPoint, 0xCC);
        NativeApi.VirtualProtect(_hookPoint, (UIntPtr)1, old, out _);
        NativeApi.FlushInstructionCache(NativeApi.GetCurrentProcess(), _hookPoint, (UIntPtr)1);

        unsafe
        {
            delegate* unmanaged[Cdecl]<IntPtr, int> handler = &VehHandler;
            _vehHandle = NativeApi.AddVectoredExceptionHandler(1, (IntPtr)handler);
        }

        _installed = _vehHandle != IntPtr.Zero;
        return _installed;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int VehHandler(IntPtr infoPtr)
    {
        if (!_installed || infoPtr == IntPtr.Zero)
            return unchecked((int)NativeApi.ExceptionContinueSearch);

        var info = (NativeApi.ExceptionPointers*)infoPtr;
        if (info->ExceptionRecord == IntPtr.Zero || info->ContextRecord == IntPtr.Zero)
            return unchecked((int)NativeApi.ExceptionContinueSearch);

        var rec = (NativeApi.ExceptionRecord*)info->ExceptionRecord;
        var ctx = (NativeApi.Context64*)info->ContextRecord;
        var code = rec->ExceptionCode;
        var addr = rec->ExceptionAddress;

        if (code == unchecked((int)NativeApi.ExceptionBreakpoint) && addr == _hookPoint)
        {
            HookUtils.PatchByte(_hookPoint, _origByte);
            try { CaptureFromRsi(ctx->Rsi); }
            catch { /* never throw into WeChat */ }

            ctx->Rip = (ulong)_hookPoint;
            ctx->EFlags |= 0x100;
            _singleStep = true;
            return unchecked((int)NativeApi.ExceptionContinueExecution);
        }

        if (code == unchecked((int)NativeApi.ExceptionSingleStep) && _singleStep)
        {
            // 不要吞掉发消息模块的硬件断点单步（Dr0 / TF 混用时会崩）
            if ((ctx->Dr6 & 0x1) != 0)
                return unchecked((int)NativeApi.ExceptionContinueSearch);

            _singleStep = false;
            // 清 TF，避免单步风暴
            ctx->EFlags &= ~0x100u;
            HookUtils.PatchByte(_hookPoint, 0xCC);
            return unchecked((int)NativeApi.ExceptionContinueExecution);
        }

        return unchecked((int)NativeApi.ExceptionContinueSearch);
    }

    private static void CaptureFromRsi(ulong rsi)
    {
        if (rsi < 0x10000) return;
        if (!HookUtils.SafeReadU64((IntPtr)(long)(rsi + 0x18), out var msg) || msg < 0x10000)
            return;

        HookUtils.SafeReadU64((IntPtr)(long)(msg + WeixinOffsets.Msg.MsgId), out var msgId);
        HookUtils.SafeReadI32((IntPtr)(long)(msg + WeixinOffsets.Msg.Type), out var type);

        // 先读一小段 content 做指纹，msgId 为 0 时也能去重
        Span<byte> probe = stackalloc byte[64];
        var probeLen = ReadWxStringTo(msg + WeixinOffsets.Msg.Content, probe);
        var finger = type;
        for (var i = 0; i < probeLen; i++)
            finger = (finger * 31) + probe[i];

        if (msgId != 0)
        {
            if ((long)msgId == Volatile.Read(ref _lastMsgId))
                return;
        }
        else if (finger == Volatile.Read(ref _lastFinger))
        {
            return;
        }

        Volatile.Write(ref _lastMsgId, (long)msgId);
        Volatile.Write(ref _lastFinger, finger);

        var idx = Interlocked.Increment(ref _writeIdx) - 1;
        var slot = Pending[idx % PendingCap];

        Volatile.Write(ref slot.Valid, 0);
        slot.MsgId = msgId;
        slot.Type = type;
        HookUtils.SafeReadU64((IntPtr)(long)(msg + WeixinOffsets.Msg.Timestamp), out slot.Timestamp);
        slot.FromLen = ReadWxString(msg + WeixinOffsets.Msg.From, slot.From);
        slot.WxidLen = ReadWxString(msg + WeixinOffsets.Msg.Wxid, slot.Wxid);
        slot.ContentLen = ReadWxString(msg + WeixinOffsets.Msg.Content, slot.Content);
        slot.SignatureLen = ReadWxString(msg + WeixinOffsets.Msg.Signature, slot.Signature);
        Volatile.Write(ref slot.Valid, 1);
        Interlocked.Increment(ref _msgCount);
    }

    private static int ReadWxStringTo(ulong fieldAddr, Span<byte> dst)
    {
        if (!HookUtils.SafeReadU64((IntPtr)(long)(fieldAddr + 0x10), out var lenU)) return 0;
        if (!HookUtils.SafeReadU64((IntPtr)(long)(fieldAddr + 0x18), out var capU)) return 0;
        var len = (int)lenU;
        if (len <= 0) return 0;
        if (len > dst.Length) len = dst.Length;

        if (capU >= 16)
        {
            if (!HookUtils.SafeReadU64((IntPtr)(long)fieldAddr, out var ptr) || ptr < 0x10000)
                return 0;
            if (!HookUtils.SafeRead((IntPtr)(long)ptr, dst[..len]))
                return 0;
        }
        else
        {
            if (!HookUtils.SafeRead((IntPtr)(long)fieldAddr, dst[..len]))
                return 0;
        }
        return len;
    }

    private static int ReadWxString(ulong fieldAddr, byte[] dst)
        => ReadWxStringTo(fieldAddr, dst);

    public static void ProcessPending()
    {
        lock (ProcessLock)
        {
            var write = Volatile.Read(ref _writeIdx);
            while (_readIdx < write)
            {
                var idx = _readIdx++;
                var slot = Pending[idx % PendingCap];
                if (Volatile.Read(ref slot.Valid) == 0)
                    continue;

                var wx = new WxMessage
                {
                    MsgId = slot.MsgId,
                    Type = slot.Type,
                    Timestamp = slot.Timestamp,
                    From = Encoding.UTF8.GetString(slot.From, 0, slot.FromLen),
                    Wxid = Encoding.UTF8.GetString(slot.Wxid, 0, slot.WxidLen),
                    Content = Encoding.UTF8.GetString(slot.Content, 0, slot.ContentLen),
                    Signature = Encoding.UTF8.GetString(slot.Signature, 0, slot.SignatureLen),
                };
                Volatile.Write(ref slot.Valid, 0);
                Queue.Enqueue(wx);
            }
        }
    }

    public static bool TryPop(out WxMessage? msg)
    {
        ProcessPending();
        if (Queue.TryDequeue(out var m))
        {
            msg = m;
            return true;
        }
        msg = null;
        return false;
    }

    public static int Count()
    {
        ProcessPending();
        return _msgCount;
    }
}
