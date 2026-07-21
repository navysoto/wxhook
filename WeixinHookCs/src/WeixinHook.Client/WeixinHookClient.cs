using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeixinHook.Client.Models;

namespace WeixinHook.Client;

/// <summary>
/// 调用 DllRecv 暴露的 HTTP API：发文本 + 收消息轮询。
/// </summary>
public sealed class WeixinHookClient : IDisposable
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultBasePort = 8765;
    public const int DefaultPortScanMax = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public string BaseUrl => _baseUrl;

    public WeixinHookClient(string baseUrl, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl is required", nameof(baseUrl));

        _baseUrl = baseUrl.TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public static async Task<WeixinHookClient> ConnectAsync(
        string host = DefaultHost,
        int basePort = DefaultBasePort,
        int maxTries = DefaultPortScanMax,
        CancellationToken cancellationToken = default)
    {
        var port = await DiscoverPortAsync(host, basePort, maxTries, cancellationToken)
            .ConfigureAwait(false);
        if (port <= 0)
            throw new InvalidOperationException(
                $"未找到可用 Hook 端口（{basePort}..{basePort + maxTries - 1}）");

        return new WeixinHookClient($"http://{host}:{port}");
    }

    public static async Task<int> DiscoverPortAsync(
        string host = DefaultHost,
        int basePort = DefaultBasePort,
        int maxTries = DefaultPortScanMax,
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var i = 0; i < maxTries; i++)
        {
            var port = basePort + i;
            try
            {
                var url = $"http://{host}:{port}/health";
                using var resp = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var health = await resp.Content
                    .ReadFromJsonAsync<HealthResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (health is { Status: "ok" })
                    return health.Port > 0 ? health.Port : port;
            }
            catch
            {
                // try next port
            }
        }

        return 0;
    }

    public async Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<HealthResponse>("/health", cancellationToken).ConfigureAwait(false);
    }

    // ── 发送文本 ─────────────────────────────────────────────────────────

    public Task<SendResult> SendTextAsync(
        string wxid,
        string text,
        CancellationToken cancellationToken = default)
        => PostSendAsync("/api/send/text", new { wxid, text }, cancellationToken);

    public Task<SendResult> SendChatroomTextAsync(
        string roomId,
        string text,
        CancellationToken cancellationToken = default)
        => PostSendAsync("/api/send/chatroom", new { roomid = roomId, content = text }, cancellationToken);

    public Task<SendResult> SendGroupTextAsync(
        string groupId,
        string text,
        CancellationToken cancellationToken = default)
        => PostSendAsync("/api/send/group", new { group_id = groupId, content = text }, cancellationToken);

    public Task<SendResult> SendAtTextAsync(
        string roomId,
        string text,
        string atWxid,
        CancellationToken cancellationToken = default)
        => PostSendAsync("/api/send/at", new { roomid = roomId, content = text, at = atWxid }, cancellationToken);

    /// <summary>第三方兼容：{"roomId","msg","wxids"}，msg 里需含 @</summary>
    public Task<SendResult> SendAtTextCompatAsync(
        string roomId,
        string msg,
        string wxids,
        CancellationToken cancellationToken = default)
        => PostSendAsync("/api/send_at_text", new { roomId, msg, wxids }, cancellationToken);

    public Task<SendResult> SendFileHelperAsync(
        string text,
        CancellationToken cancellationToken = default)
        => PostSendAsync("/api/send/filehelper", new { content = text }, cancellationToken);

    public Task<SendResult> SendChatAsync(
        string target,
        string text,
        CancellationToken cancellationToken = default)
        => PostSendAsync("/api/chat/send", new { wxid = target, content = text }, cancellationToken);

    // ── 收消息 ───────────────────────────────────────────────────────────

    public async Task<SyncPopResult> PopMessageAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<SyncPopResult>("/api/sync_msg/pop", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SyncPopManyResult> PopMessagesAsync(
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0) count = 20;
        if (count > 100) count = 100;
        return await GetAsync<SyncPopManyResult>($"/api/sync_msg/pop_n?n={count}", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SyncStatusResult> GetSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<SyncStatusResult>("/api/sync_msg/status", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ClearSyncQueueAsync(CancellationToken cancellationToken = default)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{_baseUrl}/api/sync_msg/clear", content, cancellationToken)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<int> PullFriendRequestsAsync(CancellationToken cancellationToken = default)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(
            $"{_baseUrl}/api/sync_msg/pull_friend_requests",
            content,
            cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("pulled", out var p) ? p.GetInt32() : 0;
    }

    /// <summary>后台轮询收消息，直到 cancellationToken 取消。</summary>
    public async Task PollMessagesAsync(
        Func<SyncMsgEvent, Task> onMessage,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pop = await PopMessageAsync(cancellationToken).ConfigureAwait(false);
            if (pop is { Ok: true, HasMsg: true, Msg: not null })
                await onMessage(pop.Msg).ConfigureAwait(false);

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<SendResult> PostSendAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var resp = await _http.PostAsync($"{_baseUrl}{path}", content, cancellationToken)
            .ConfigureAwait(false);

        var result = await resp.Content.ReadFromJsonAsync<SendResult>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
            throw new InvalidOperationException($"empty response from {path}");

        return result;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var resp = await _http.GetAsync($"{_baseUrl}{path}", cancellationToken)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
            throw new InvalidOperationException($"empty response from {path}");

        return result;
    }
}
