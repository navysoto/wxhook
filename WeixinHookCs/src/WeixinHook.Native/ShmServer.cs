using System.IO.MemoryMappedFiles;
using System.Text;

namespace WeixinHook.Native;

/// <summary>
/// 鍛藉悕鍏变韩鍐呭瓨锛歎I 鍐欏懡浠?+ 浜嬩欢锛孌LL 鐙珛绾跨▼鎵ц鍚庡洖鍐欍€?
/// </summary>
internal static class ShmServer
{
    public const string MapName = "Local\\WeixinHook.Native.Shm";
    public const string CmdEventName = "Local\\WeixinHook.Native.Cmd";
    public const string DoneEventName = "Local\\WeixinHook.Native.Done";

    public const int Magic = 0x57484F4B;
    public const int Version = 1;
    public const int MapSize = 0x9000;

    public const int OffMagic = 0x00;
    public const int OffVersion = 0x04;
    public const int OffCmd = 0x08;
    public const int OffStatus = 0x0C;
    public const int OffCoroTid = 0x10;
    public const int OffRecvTotal = 0x14;
    public const int OffMsgType = 0x18;
    public const int OffHasMsg = 0x1C;
    public const int OffMsgId = 0x20;
    public const int OffTimestamp = 0x28;
    public const int OffWxid = 0x30;
    public const int OffSignature = 0x130;
    public const int OffFrom = 0x230;
    public const int OffContent = 0x330;
    public const int ContentCap = 0x8000;
    public const int StrCap = 256;

    public const int CmdNone = 0;
    public const int CmdSend = 1;
    public const int CmdPop = 2;
    public const int CmdStatus = 3;
    public const int CmdPing = 4;

    public const int StIdle = 0;
    public const int StBusy = 1;
    public const int StOk = 2;
    public const int StFail = 3;

    private static MemoryMappedFile? _mmf;
    private static MemoryMappedViewAccessor? _view;
    private static EventWaitHandle? _cmdEvent;
    private static EventWaitHandle? _doneEvent;
    private static volatile bool _running;

    public static void Start()
    {
        if (_running) return;

        _mmf = MemoryMappedFile.CreateOrOpen(MapName, MapSize, MemoryMappedFileAccess.ReadWrite);
        _view = _mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
        _cmdEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CmdEventName);
        _doneEvent = new EventWaitHandle(false, EventResetMode.AutoReset, DoneEventName);

        WriteI32(OffMagic, Magic);
        WriteI32(OffVersion, Version);
        WriteI32(OffCmd, CmdNone);
        WriteI32(OffStatus, StIdle);

        _running = true;
        new Thread(WorkerLoop) { IsBackground = true, Name = "ShmWorker" }.Start();

    }

    private static void WorkerLoop()
    {
        while (_running)
        {
            try
            {
                if (!_cmdEvent!.WaitOne(500))
                    continue;

                var cmd = ReadI32(OffCmd);
                if (cmd == CmdNone)
                {
                    _doneEvent!.Set();
                    continue;
                }

                WriteI32(OffStatus, StBusy);

                switch (cmd)
                {
                    case CmdPing:
                    case CmdStatus:
                        WriteI32(OffCoroTid, (int)MsgSendHook.CoroThreadId);
                        WriteI32(OffRecvTotal, MsgRecvHook.Count());
                        WriteI32(OffStatus, StOk);
                        break;

                    case CmdSend:
                    {
                        var wxid = ReadString(OffWxid, StrCap);
                        var content = ReadString(OffContent, ContentCap);
                        var ok = false;
                        try { ok = MsgSendHook.SendText(wxid, content); }
                        catch { /* ignore */ }
                        // 失败时把 last_step 写回 content，方便 UI 看卡在哪一步
                        if (!ok)
                        {
                            var step = MsgSendHook.LastStep;
                            var why = step switch
                            {
                                0 => "未执行(协程未捕获或超时)",
                                101 => "MsgCtor异常",
                                11 => "GetCoroCtx空",
                                21 => "GetMsgSvc空",
                                3 => "GetMsgCtx异常(半截栈/需HWBP)",
                                31 => "GetMsgCtx空",
                                41 => "DoSend异常",
                                5 => "DoSend已返回",
                                200 => "Kick:无tid/stub",
                                201 => "Kick:OpenThread失败",
                                202 => "Kick:Suspend失败",
                                203 => "Kick:GetContext失败",
                                204 => "Kick:SetContext失败",
                                205 => "已挂HWBP,请在微信点一下触发",
                                _ => $"step={step}"
                            };
                            WriteString(OffContent, $"fail {why} coro={MsgSendHook.CoroThreadId}", ContentCap);
                        }
                        WriteI32(OffCoroTid, (int)MsgSendHook.CoroThreadId);
                        WriteI32(OffStatus, ok ? StOk : StFail);
                        break;
                    }

                    case CmdPop:
                    {
                        MsgRecvHook.ProcessPending();
                        if (MsgRecvHook.TryPop(out var msg) && msg is not null)
                        {
                            WriteI32(OffHasMsg, 1);
                            WriteI32(OffMsgType, msg.Type);
                            WriteU64(OffMsgId, msg.MsgId);
                            WriteU64(OffTimestamp, msg.Timestamp);
                            WriteString(OffWxid, msg.Wxid, StrCap);
                            WriteString(OffFrom, msg.From, StrCap);
                            WriteString(OffSignature, msg.Signature, StrCap);
                            WriteString(OffContent, msg.Content, ContentCap);
                        }
                        else
                        {
                            WriteI32(OffHasMsg, 0);
                        }
                        WriteI32(OffCoroTid, (int)MsgSendHook.CoroThreadId);
                        WriteI32(OffRecvTotal, MsgRecvHook.Count());
                        WriteI32(OffStatus, StOk);
                        break;
                    }

                    default:
                        WriteI32(OffStatus, StFail);
                        break;
                }

                WriteI32(OffCmd, CmdNone);
                _doneEvent!.Set();
            }
            catch
            {
                try
                {
                    WriteI32(OffStatus, StFail);
                    WriteI32(OffCmd, CmdNone);
                    _doneEvent?.Set();
                }
                catch { /* ignore */ }
            }
        }
    }

    private static int ReadI32(int off) => _view!.ReadInt32(off);
    private static void WriteI32(int off, int v) => _view!.Write(off, v);
    private static void WriteU64(int off, ulong v) => _view!.Write(off, unchecked((long)v));

    private static string ReadString(int off, int cap)
    {
        var buf = new byte[cap];
        _view!.ReadArray(off, buf, 0, cap);
        var n = Array.IndexOf(buf, (byte)0);
        if (n < 0) n = cap;
        if (n == 0) return "";
        return Encoding.UTF8.GetString(buf, 0, n);
    }

    private static void WriteString(int off, string? s, int cap)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        var n = Math.Min(bytes.Length, cap - 1);
        var buf = new byte[cap];
        if (n > 0) Buffer.BlockCopy(bytes, 0, buf, 0, n);
        _view!.WriteArray(off, buf, 0, cap);
    }
}
