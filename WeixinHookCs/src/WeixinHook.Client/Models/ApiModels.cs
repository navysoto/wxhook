namespace WeixinHook.Client.Models;

public sealed class HealthResponse
{
    public string Status { get; set; } = "";
    public int Port { get; set; }
}

public sealed class SendResult
{
    public bool Ok { get; set; }
    public bool Sent { get; set; }
    public string Api { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class SyncMsgEvent
{
    public ulong MsgId { get; set; }
    public int MsgType { get; set; }
    public ulong CreateTime { get; set; }
    public int EventType { get; set; }
    public string EventDesc { get; set; } = "";
    public string From { get; set; } = "";
    public string FromWxid { get; set; } = "";
    public string Content { get; set; } = "";
    public string Signature { get; set; } = "";
    public string NewFriendWxid { get; set; } = "";
    public string V3 { get; set; } = "";
    public string V4 { get; set; } = "";
    public string Source { get; set; } = "";

    public bool IsText => MsgType == 1;
    public bool IsFriendRequest => MsgType == 37 || EventType == 37;
}

public sealed class SyncPopResult
{
    public bool Ok { get; set; }
    public bool HasMsg { get; set; }
    public int Pending { get; set; }
    public SyncMsgEvent? Msg { get; set; }
}

public sealed class SyncPopManyResult
{
    public bool Ok { get; set; }
    public int Count { get; set; }
    public int Pending { get; set; }
    public List<SyncMsgEvent> Msgs { get; set; } = new();
}

public sealed class SyncStatusResult
{
    public bool Ok { get; set; }
    public int Pending { get; set; }
    public bool PrintPbOn { get; set; }
    public bool EnqueueOn { get; set; }
    public ulong PushedTotal { get; set; }
    public ulong PushedType37 { get; set; }
}
