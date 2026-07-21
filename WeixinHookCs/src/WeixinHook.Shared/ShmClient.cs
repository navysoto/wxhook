using System.IO.MemoryMappedFiles;
using System.Text;

namespace WeixinHook.Shared;

/// <summary>
/// 共享内存客户端。发/收在本进程内用 SemaphoreSlim 串行，
/// 不再用跨进程 Mutex（避免发送占锁时接收一直报「忙」）。
/// </summary>
public sealed class ShmClient : IDisposable
{
    public const string MapName = "Local\\WeixinHook.Native.Shm";
    public const string CmdEventName = "Local\\WeixinHook.Native.Cmd";
    public const string DoneEventName = "Local\\WeixinHook.Native.Done";

    public const int Magic = 0x57484F4B;
    public const int MapSize = 0x9000;

    public const int OffMagic = 0x00;
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

    public const int CmdSend = 1;
    public const int CmdPop = 2;
    public const int CmdStatus = 3;
    public const int CmdPing = 4;

    public const int StOk = 2;
    public const int StFail = 3;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private readonly EventWaitHandle _cmdEvent;
    private readonly EventWaitHandle _doneEvent;
    private readonly SemaphoreSlim _io = new(1, 1);

    public ShmClient()
    {
        _mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite);
        _view = _mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
        _cmdEvent = EventWaitHandle.OpenExisting(CmdEventName);
        _doneEvent = EventWaitHandle.OpenExisting(DoneEventName);

        if (_view.ReadInt32(OffMagic) != Magic)
            throw new InvalidOperationException("共享内存 magic 不匹配，DLL 可能未就绪");
    }

    public static bool TryConnect(out ShmClient? client, out string error)
    {
        client = null;
        error = "";
        try
        {
            client = new ShmClient();
            if (!client.Ping(TimeSpan.FromSeconds(5)))
            {
                client.Dispose();
                client = null;
                error = "PING 失败";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool Ping(TimeSpan timeout)
        => Invoke(CmdPing, timeout, null, null) == StOk;

    public (bool Ok, uint CoroTid, int RecvTotal) Status(TimeSpan timeout)
    {
        uint tid = 0;
        var total = 0;
        var st = Invoke(CmdStatus, timeout, null, () =>
        {
            tid = (uint)_view.ReadInt32(OffCoroTid);
            total = _view.ReadInt32(OffRecvTotal);
        });
        return (st == StOk, tid, total);
    }

    public bool SendText(string wxid, string content, TimeSpan timeout)
    {
        string? failInfo = null;
        var st = Invoke(CmdSend, timeout, () =>
        {
            WriteString(OffWxid, wxid, StrCap);
            WriteString(OffContent, content, ContentCap);
        }, () =>
        {
            if (_view.ReadInt32(OffStatus) == StFail)
                failInfo = ReadString(OffContent, 128);
        });
        if (st != StOk && !string.IsNullOrEmpty(failInfo))
            LastSendError = failInfo;
        else
            LastSendError = st == StOk ? "" : "send failed";
        return st == StOk;
    }

    public string LastSendError { get; private set; } = "";

    public ReceivedMessage? Pop(TimeSpan timeout)
    {
        ReceivedMessage? msg = null;
        var st = Invoke(CmdPop, timeout, null, () =>
        {
            if (_view.ReadInt32(OffHasMsg) == 0) return;
            msg = new ReceivedMessage(
                unchecked((ulong)_view.ReadInt64(OffMsgId)),
                _view.ReadInt32(OffMsgType),
                unchecked((ulong)_view.ReadInt64(OffTimestamp)),
                ReadString(OffFrom, StrCap),
                ReadString(OffWxid, StrCap),
                ReadString(OffContent, ContentCap),
                ReadString(OffSignature, StrCap));
        });
        return st == StOk ? msg : null;
    }

    private int Invoke(int cmd, TimeSpan timeout, Action? writeArgs, Action? readResult)
    {
        if (!_io.Wait(timeout))
            throw new TimeoutException("接口排队超时");

        try
        {
            _doneEvent.Reset();
            writeArgs?.Invoke();
            _view.Write(OffStatus, 0);
            _view.Write(OffCmd, cmd);
            Thread.MemoryBarrier();
            _cmdEvent.Set();

            if (!_doneEvent.WaitOne(timeout))
                throw new TimeoutException("DLL 未响应");

            var st = _view.ReadInt32(OffStatus);
            readResult?.Invoke();
            return st;
        }
        finally
        {
            _io.Release();
        }
    }

    private string ReadString(int off, int cap)
    {
        var buf = new byte[cap];
        _view.ReadArray(off, buf, 0, cap);
        var n = Array.IndexOf(buf, (byte)0);
        if (n < 0) n = cap;
        if (n == 0) return "";
        return Encoding.UTF8.GetString(buf, 0, n);
    }

    private void WriteString(int off, string s, int cap)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        var n = Math.Min(bytes.Length, cap - 1);
        var buf = new byte[cap];
        if (n > 0) Buffer.BlockCopy(bytes, 0, buf, 0, n);
        _view.WriteArray(off, buf, 0, cap);
    }

    public void Dispose()
    {
        _io.Dispose();
        _view.Dispose();
        _mmf.Dispose();
        _cmdEvent.Dispose();
        _doneEvent.Dispose();
    }
}
