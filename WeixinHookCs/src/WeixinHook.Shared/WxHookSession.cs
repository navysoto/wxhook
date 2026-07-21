namespace WeixinHook.Shared;

public sealed record ReceivedMessage(
    ulong MsgId,
    int Type,
    ulong Timestamp,
    string From,
    string Wxid,
    string Content,
    string Signature)
{
    public string DisplayLine()
    {
        var who = string.IsNullOrEmpty(Wxid) ? From : Wxid;
        var body = string.IsNullOrEmpty(Content) ? "(空)" : Content;
        if (body.Length > 500) body = body[..500] + "...";
        return $"[{DateTime.Now:HH:mm:ss}] type={Type} {who}: {body}";
    }
}

/// <summary>
/// UI 侧会话：后台线程轮询 POP，发送走共享内存，不碰微信线程。
/// </summary>
public sealed class WxHookSession : IDisposable
{
    private readonly object _gate = new();
    private ShmClient? _client;
    private CancellationTokenSource? _recvCts;
    private Task? _recvTask;

    public bool IsConnected
    {
        get { lock (_gate) return _client is not null; }
    }

    public event Action<ReceivedMessage>? MessageReceived;
    public event Action<string>? Log;

    public bool Connect(out string error)
    {
        lock (_gate)
        {
            DisconnectCore();
            if (!ShmClient.TryConnect(out var c, out error) || c is null)
                return false;
            _client = c;
            Log?.Invoke("已连接共享内存接口");
            return true;
        }
    }

    public void StartReceiveLoop(int intervalMs = 400)
    {
        lock (_gate)
        {
            StopReceiveLoopCore();
            if (_client is null)
                throw new InvalidOperationException("未连接");

            _recvCts = new CancellationTokenSource();
            var ct = _recvCts.Token;
            _recvTask = Task.Run(() =>
            {
                var errCount = 0;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        ShmClient? client;
                        lock (_gate) client = _client;
                        if (client is null) break;

                        var msg = client.Pop(TimeSpan.FromSeconds(40));
                        errCount = 0;
                        if (msg is not null)
                            MessageReceived?.Invoke(msg);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        // 发送占用通道时排队等待，不要刷屏
                        errCount++;
                        if (errCount == 1 || errCount % 20 == 0)
                            Log?.Invoke($"收消息异常: {ex.Message}");
                        try { Thread.Sleep(500); } catch { break; }
                    }

                    try { Task.Delay(intervalMs, ct).Wait(ct); }
                    catch { break; }
                }
            }, ct);
        }
    }

    public Task<(bool Ok, string Error)> SendTextAsync(string wxid, string content, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ShmClient? client;
            lock (_gate) client = _client;
            if (client is null) throw new InvalidOperationException("未连接");
            var ok = client.SendText(wxid, content, TimeSpan.FromSeconds(35));
            return (ok, ok ? "" : client.LastSendError);
        }, ct);
    }

    public Task<(bool Ok, uint CoroTid, int RecvTotal)> GetStatusAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ShmClient? client;
            lock (_gate) client = _client;
            if (client is null) throw new InvalidOperationException("未连接");
            return client.Status(TimeSpan.FromSeconds(5));
        }, ct);
    }

    public void Disconnect()
    {
        lock (_gate) DisconnectCore();
    }

    private void StopReceiveLoopCore()
    {
        if (_recvCts is null) return;
        try { _recvCts.Cancel(); } catch { }
        _recvCts.Dispose();
        _recvCts = null;
        _recvTask = null;
    }

    private void DisconnectCore()
    {
        StopReceiveLoopCore();
        _client?.Dispose();
        _client = null;
    }

    public void Dispose() => Disconnect();
}
