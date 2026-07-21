using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace WeixinHook.Native;

/// <summary>
/// VEH 只做 boot/INT3；真正 kick/send 全在原生 seh_bridge（16 字节对齐 CONTEXT）。
/// </summary>
internal static class MsgSendHook
{
    private static IntPtr _sendHookAddr;
    private static IntPtr _bootHookAddr;
    private static byte _sendOrigByte;
    private static byte _bootOrigByte;
    private static IntPtr _vehHandle;
    private static bool _sendSingleStep;
    private static bool _bootSingleStep;
    private static volatile bool _bootDone;
    private static volatile uint _coroThreadId;
    private static ulong _hwBpAddr;
    private static volatile bool _hwBpArmed;

    private static IntPtr _drainStub = IntPtr.Zero;
    private static IntPtr _nativeDrainThunk;
    private static bool _inited;

    public static bool Initialize(ulong weixinBase)
    {
        if (weixinBase == 0) return false;

        var pfnGetCoro = (IntPtr)(long)(weixinBase + WeixinOffsets.Send.GetCoroCtx);
        var pfnGetSvc = (IntPtr)(long)(weixinBase + WeixinOffsets.Send.GetMsgSvc);
        var pfnGetCtx = (IntPtr)(long)(weixinBase + WeixinOffsets.Send.GetMsgCtx);
        var pfnDoSend = (IntPtr)(long)(weixinBase + WeixinOffsets.Send.DoSend);
        var pfnMsgCtor = (IntPtr)(long)(weixinBase + WeixinOffsets.Send.MsgCtor);

        _bootHookAddr = pfnGetCoro;
        _hwBpAddr = weixinBase + WeixinOffsets.Send.GetCoroCtx;
        _sendHookAddr = (IntPtr)(long)(weixinBase + WeixinOffsets.Send.SendEntry);

        InstallInt3Hook(_bootHookAddr, ref _bootOrigByte);
        InstallInt3Hook(_sendHookAddr, ref _sendOrigByte);

        if (SehBridge.Init(
                pfnGetCoro, pfnGetSvc, pfnGetCtx, pfnDoSend, pfnMsgCtor,
                WeixinOffsets.Send.Wxid, WeixinOffsets.Send.Content,
                WeixinOffsets.Send.MsgType, WeixinOffsets.Send.MsgTypeAlt,
                WeixinOffsets.Send.ObjSize,
                _sendHookAddr, _sendOrigByte) == 0)
            return false;

        _nativeDrainThunk = SehBridge.GetDrainThunk();
        if (_nativeDrainThunk == IntPtr.Zero) return false;
        if (!BuildDrainStub()) return false;
        SehBridge.SetDrainStub(_drainStub);

        unsafe
        {
            delegate* unmanaged[Cdecl]<IntPtr, int> handler = &SendVehHandler;
            _vehHandle = NativeApi.AddVectoredExceptionHandler(1, (IntPtr)handler);
        }

        _inited = _vehHandle != IntPtr.Zero;
        return _inited;
    }

    public static bool SendText(string wxid, string content)
    {
        if (!_inited) return false;

        if (_coroThreadId == 0)
        {
            if (!WaitForCoroThread(TimeSpan.FromSeconds(15)))
                return false;
        }

        var wx = Encoding.UTF8.GetBytes(wxid ?? "");
        var ct = Encoding.UTF8.GetBytes(content ?? "");
        if (SehBridge.Enqueue(wx, wx.Length, ct, ct.Length) == 0)
            return false;

        // 与 C++ 一致：只在 GetCoroCtx 入口 HWBP 安全点 drain。
        // Kick 强行改 RIP 会让 GetMsgCtx 在半截栈上崩（fail step=3）。
        if (!_hwBpArmed)
        {
            if (SehBridge.ArmHwBp(_coroThreadId, _hwBpAddr) != 0)
                _hwBpArmed = true;
            else
                return false;
        }

        return SehBridge.Wait(30000) != 0;
    }

    public static int LastStep => SehBridge.LastStep();

    private static bool WaitForCoroThread(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_coroThreadId != 0) return true;
            NativeApi.Sleep(50);
        }
        return _coroThreadId != 0;
    }

    public static uint CoroThreadId => _coroThreadId;

    private static void InstallInt3Hook(IntPtr addr, ref byte orig)
    {
        if (!NativeApi.VirtualProtect(addr, (UIntPtr)1, NativeApi.PageExecuteReadwrite, out var old))
            return;
        orig = Marshal.ReadByte(addr);
        Marshal.WriteByte(addr, 0xCC);
        NativeApi.VirtualProtect(addr, (UIntPtr)1, old, out _);
    }

    private static bool BuildDrainStub()
    {
        if (_drainStub != IntPtr.Zero) return true;
        _drainStub = NativeApi.VirtualAlloc(IntPtr.Zero, (UIntPtr)4096,
            NativeApi.MemCommit | NativeApi.MemReserve, NativeApi.PageExecuteReadwrite);
        if (_drainStub == IntPtr.Zero) return false;

        var resumePtr = SehBridge.GetDrainResumePtr();
        if (resumePtr == IntPtr.Zero) return false;

        unsafe
        {
            var s = (byte*)_drainStub;
            var pos = 0;
            ReadOnlySpan<byte> push = [0x50, 0x51, 0x52, 0x41, 0x50, 0x41, 0x51, 0x41, 0x52, 0x41, 0x53];
            push.CopyTo(new Span<byte>(s + pos, push.Length));
            pos += push.Length;

            s[pos++] = 0x48; s[pos++] = 0x81; s[pos++] = 0xEC;
            uint sub = 0x80;
            *(uint*)(s + pos) = sub; pos += 4;

            for (var i = 0; i < 6; i++)
            {
                s[pos++] = 0x0F; s[pos++] = 0x29;
                s[pos++] = (byte)(0x44 | (i << 3));
                s[pos++] = 0x24;
                s[pos++] = (byte)(0x20 + i * 16);
            }

            s[pos++] = 0x48; s[pos++] = 0xB8;
            *(ulong*)(s + pos) = (ulong)_nativeDrainThunk; pos += 8;
            s[pos++] = 0xFF; s[pos++] = 0xD0;

            for (var i = 0; i < 6; i++)
            {
                s[pos++] = 0x0F; s[pos++] = 0x28;
                s[pos++] = (byte)(0x44 | (i << 3));
                s[pos++] = 0x24;
                s[pos++] = (byte)(0x20 + i * 16);
            }

            s[pos++] = 0x48; s[pos++] = 0x81; s[pos++] = 0xC4;
            *(uint*)(s + pos) = sub; pos += 4;

            ReadOnlySpan<byte> pop = [0x41, 0x5B, 0x41, 0x5A, 0x41, 0x59, 0x41, 0x58, 0x5A, 0x59, 0x58];
            pop.CopyTo(new Span<byte>(s + pos, pop.Length));
            pos += pop.Length;

            // mov rax, qword ptr [Seh.drain_resume]; jmp rax
            s[pos++] = 0x48; s[pos++] = 0xA1;
            *(ulong*)(s + pos) = (ulong)resumePtr; pos += 8;
            s[pos++] = 0xFF; s[pos++] = 0xE0;

            NativeApi.FlushInstructionCache(NativeApi.GetCurrentProcess(), _drainStub, (UIntPtr)pos);
        }
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int SendVehHandler(IntPtr infoPtr)
    {
        if (infoPtr == IntPtr.Zero)
            return unchecked((int)NativeApi.ExceptionContinueSearch);

        var info = (NativeApi.ExceptionPointers*)infoPtr;
        if (info->ExceptionRecord == IntPtr.Zero || info->ContextRecord == IntPtr.Zero)
            return unchecked((int)NativeApi.ExceptionContinueSearch);

        var rec = (NativeApi.ExceptionRecord*)info->ExceptionRecord;
        var ctx = (NativeApi.Context64*)info->ContextRecord;
        var code = rec->ExceptionCode;
        var addr = rec->ExceptionAddress;

        if (code == unchecked((int)NativeApi.ExceptionBreakpoint) &&
            addr == _bootHookAddr && !_bootDone)
        {
            _bootDone = true;
            _coroThreadId = NativeApi.GetCurrentThreadId();
            HookUtils.PatchByte(_bootHookAddr, _bootOrigByte);
            ctx->Rip = (ulong)_bootHookAddr;
            ctx->EFlags |= 0x100;
            _bootSingleStep = true;
            return unchecked((int)NativeApi.ExceptionContinueExecution);
        }

        if (code == unchecked((int)NativeApi.ExceptionBreakpoint) &&
            addr == _sendHookAddr)
        {
            HookUtils.PatchByte(_sendHookAddr, _sendOrigByte);
            if (SehBridge.IsOurCall() == 0)
            {
                var tid = NativeApi.GetCurrentThreadId();
                if (tid != _coroThreadId)
                    _coroThreadId = tid;
            }
            ctx->Rip = (ulong)_sendHookAddr;
            ctx->EFlags |= 0x100;
            _sendSingleStep = true;
            return unchecked((int)NativeApi.ExceptionContinueExecution);
        }

        if (code == unchecked((int)NativeApi.ExceptionSingleStep))
        {
            if (_bootSingleStep)
            {
                _bootSingleStep = false;
                return unchecked((int)NativeApi.ExceptionContinueExecution);
            }
            if (_sendSingleStep)
            {
                _sendSingleStep = false;
                HookUtils.PatchByte(_sendHookAddr, 0xCC);
                return unchecked((int)NativeApi.ExceptionContinueExecution);
            }

            var hwHit = (ulong)addr == _hwBpAddr || (ctx->Dr6 & 0x1) != 0;
            if (hwHit && _hwBpAddr != 0)
            {
                ctx->Dr0 = 0;
                ctx->Dr7 &= ~0x1UL;
                ctx->Dr6 = 0;
                if (SehBridge.IsOurCall() != 0)
                    return unchecked((int)NativeApi.ExceptionContinueExecution);

                _hwBpArmed = false;
                if (SehBridge.HasPending() == 0)
                    return unchecked((int)NativeApi.ExceptionContinueExecution);

                if (_sendHookAddr != IntPtr.Zero)
                    HookUtils.PatchByte(_sendHookAddr, _sendOrigByte);

                SehBridge.SetDrainResume(_hwBpAddr);
                ctx->Rip = (ulong)_drainStub;
                ctx->EFlags &= ~0x100u;
                return unchecked((int)NativeApi.ExceptionContinueExecution);
            }
        }

        return unchecked((int)NativeApi.ExceptionContinueSearch);
    }
}
