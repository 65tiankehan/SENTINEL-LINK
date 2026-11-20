using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SentinelLink.Server.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;

namespace SentinelLink.Server.Services;

/// <summary>
/// Link 与 Hub 的连接器。
///
/// 生命周期：<see cref="Start"/> 起一个后台循环 → /v1/hello 拿 token → /v1/ws 长连 →
/// 收帧（welcome / presence / chat / ack）写进 <see cref="LinkState"/> → 掉了就退避重连。
/// 所有网络动作都在后台任务里，绝不阻塞 SPT 主线程；任何异常都只记一条日志然后重试。
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class LinkHubClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ISptLogger<LinkHubClient> _logger;
    private readonly LinkState _state;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private ClientWebSocket? _socket;
    private string _inviteKey = "";

    public LinkHubClient(ISptLogger<LinkHubClient> logger, LinkState state)
    {
        _logger = logger;
        _state = state;
    }

    public bool IsOnline => _socket is { State: WebSocketState.Open };

    /// <summary>从配置文件读设置并开始连接。文件不存在时写一份默认的，方便用户改。</summary>
    public void Start(string modDirectory, CancellationToken lifetimeToken)
    {
        string configPath = Path.Combine(modDirectory, "config.json");
        HubConfig config = HubConfig.LoadOrCreate(configPath);

        _state.HubUrl = config.HubUrl;
        _state.Realm = string.IsNullOrWhiteSpace(config.Realm) ? "default" : config.Realm;
        _state.Nickname = string.IsNullOrWhiteSpace(config.Nickname)
            ? _state.ServerId
            : config.Nickname;
        _inviteKey = config.InviteKey ?? "";

        if (!config.Enabled)
        {
            _state.HubState = "unlinked";
            _logger.Info("[SENTINEL Link] config.json 里 enabled=false，不连 Hub");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.HubUrl))
        {
            _state.HubState = "unlinked";
            _logger.Warning("[SENTINEL Link] 未配置 hubUrl，P0 阶段只做本地路由");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _ = Task.Run(() => RunLoopAsync(config, _cts.Token), CancellationToken.None);
    }

    private async Task RunLoopAsync(HubConfig config, CancellationToken token)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(2);
        while (!token.IsCancellationRequested)
        {
            try
            {
                _state.HubState = "handshaking";
                string tokenText = await HelloAsync(config, token).ConfigureAwait(false);
                if (tokenText.Length == 0)
                {
                    _state.HubState = "auth-failed";
                    await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                    continue;
                }

                using ClientWebSocket socket = new();
                string wsBase = config.HubUrl
                    .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
                    .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                    .TrimEnd('/');
                await socket.ConnectAsync(new Uri($"{wsBase}/v1/ws?token={tokenText}"), token)
                    .ConfigureAwait(false);

                _socket = socket;
                _state.HubState = "online";
                _state.LastError = "";
                backoff = TimeSpan.FromSeconds(2);
                _logger.Success($"[SENTINEL Link] 已连上 Hub {config.HubUrl}（realm={_state.Realm}）");

                await PumpAsync(socket, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _state.HubState = "unlinked";
                _state.LastError = ex.Message;
                _logger.Warning($"[SENTINEL Link] Hub 连接中断：{ex.Message}（{backoff.TotalSeconds:0}s 后重试）");
            }
            finally
            {
                _socket = null;
                _state.ClearPeers();
            }

            try
            {
                await Task.Delay(backoff, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    private async Task<string> HelloAsync(HubConfig config, CancellationToken token)
    {
        HttpResponseMessage response = await _http
            .PostAsJsonAsync($"{config.HubUrl.TrimEnd('/')}/v1/hello", new
            {
                serverId = _state.ServerId,
                inviteKey = _inviteKey,
                realm = _state.Realm,
                nickname = _state.Nickname,
                version = _state.Version,
                sptVersion = _state.SptVersion
            }, Json, token)
            .ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _state.LastError = $"hello {(int)response.StatusCode}";
            _logger.Warning($"[SENTINEL Link] hello 被拒 {(int)response.StatusCode}：{body}");
            return "";
        }

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string tokenText = root.TryGetProperty("token", out JsonElement t)
            ? t.GetString() ?? ""
            : "";
        _state.LastHelloMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _state.LastFrameMs = _state.LastHelloMs;

        if (root.TryGetProperty("peers", out JsonElement peers) && peers.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement peer in peers.EnumerateArray())
            {
                ApplyPeer(peer);
            }
        }

        return tokenText;
    }

    private async Task PumpAsync(ClientWebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await socket
                .ReceiveAsync(new ArraySegment<byte>(buffer), token)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            _state.LastFrameMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            HandleFrame(text);
        }
    }

    private void HandleFrame(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            string type = root.TryGetProperty("type", out JsonElement typeElement)
                ? typeElement.GetString() ?? ""
                : "";

            switch (type)
            {
                case "welcome":
                case "peer-list":
                    if (root.TryGetProperty("peers", out JsonElement peers) &&
                        peers.ValueKind == JsonValueKind.Array)
                    {
                        _state.ClearPeers();
                        foreach (JsonElement peer in peers.EnumerateArray())
                        {
                            ApplyPeer(peer);
                        }
                    }

                    break;

                case "presence":
                    ApplyPeer(root);
                    break;

                case "chat":
                    LinkChatMessage message = new()
                    {
                        From = Str(root, "from"),
                        Nickname = Str(root, "nickname"),
                        Text = Str(root, "text"),
                        At = Long(root, "at")
                    };
                    _state.AddChat(message);
                    // P0 没有客户端界面，先落一行日志当验收证据（P1 起由面板消费 LinkState）。
                    _logger.Info($"[SENTINEL Link] 世界聊天 {message.Nickname}: {message.Text}");
                    break;

                case "dm":
                    LinkDirectMessage direct = new()
                    {
                        Id = Str(root, "id"),
                        From = Str(root, "from"),
                        FromNick = Str(root, "fromNick"),
                        To = Str(root, "to"),
                        Text = Str(root, "text"),
                        At = Long(root, "at"),
                        Replayed = root.TryGetProperty("replayed", out JsonElement replayed) &&
                                   replayed.ValueKind == JsonValueKind.True
                    };
                    _state.AddDirect(direct);
                    _logger.Info($"[SENTINEL Link] 私聊 {direct.FromNick}: {direct.Text}"
                                 + (direct.Replayed ? "（离线补投）" : ""));
                    break;

                case "dm-sync":
                    int batch = root.TryGetProperty("count", out JsonElement batchElement) &&
                                batchElement.TryGetInt32(out int parsedBatch)
                        ? parsedBatch
                        : 0;
                    _state.AddSyncBatch(batch);
                    _logger.Info($"[SENTINEL Link] 离线私聊补投 {batch} 条");
                    break;

                case "ack":
                case "pong":
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"[SENTINEL Link] 帧解析失败：{ex.Message}");
        }
    }

    private void ApplyPeer(JsonElement peer)
    {
        if (!peer.TryGetProperty("serverId", out JsonElement idElement))
        {
            return;
        }

        string serverId = idElement.GetString() ?? "";
        if (serverId.Length == 0)
        {
            return;
        }

        bool online = !peer.TryGetProperty("online", out JsonElement onlineElement) ||
                      onlineElement.ValueKind != JsonValueKind.False;

        _state.SetPeer(new LinkPeerInfo
        {
            ServerId = serverId,
            Nickname = Str(peer, "nickname"),
            Version = Str(peer, "version"),
            SptVersion = Str(peer, "sptVersion"),
            Online = online
        });
    }

    /// <summary>发一条世界聊天。Hub 没连上时立即返回 false，不排队（P0 不做离线补发）。</summary>
    public async Task<bool> SendChatAsync(string text, CancellationToken token)
    {
        ClientWebSocket? socket = _socket;
        if (socket is not { State: WebSocketState.Open })
        {
            return false;
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { type = "chat", text }, Json);
        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _state.LastError = ex.Message;
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>发一条私聊。对方离线时 Hub 会落盘排队，下次连上补投。</summary>
    public async Task<bool> SendDirectAsync(string to, string text, CancellationToken token)
    {
        ClientWebSocket? socket = _socket;
        if (socket is not { State: WebSocketState.Open })
        {
            return false;
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { type = "dm", to, text }, Json);
        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _state.LastError = ex.Message;
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _socket?.Dispose();
            _http.Dispose();
            _sendLock.Dispose();
        }
        catch
        {
            // 关闭期异常忽略
        }
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static long Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;
}

/// <summary>Hub 连接配置（SPT_Runtime\user\mods\&lt;guid&gt;\config.json）。</summary>
public sealed class HubConfig
{
    public bool Enabled { get; set; }

    public string HubUrl { get; set; } = "";

    public string InviteKey { get; set; } = "dev-local";

    public string Realm { get; set; } = "default";

    public string Nickname { get; set; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static HubConfig LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                HubConfig? loaded = JsonSerializer.Deserialize<HubConfig>(
                    File.ReadAllText(path, Encoding.UTF8), Options);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // 坏档回落默认
        }

        HubConfig fallback = new()
        {
            Enabled = false,
            HubUrl = "http://127.0.0.1:5180",
            InviteKey = "dev-local",
            Realm = "default",
            Nickname = ""
        };

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(fallback, Options), Encoding.UTF8);
        }
        catch
        {
            // 写不了就算了，用内存里的默认值跑
        }

        return fallback;
    }
}
