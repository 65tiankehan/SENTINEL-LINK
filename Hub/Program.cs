using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SentinelLink.Hub.Services;
using SentinelLink.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// SENTINEL Link Hub（P0）
//
// 职责只有三件：认人（/v1/hello + 邀请码）、保活（/v1/ws 长连 + 心跳清扫）、
// 转发（realm 内广播）。业务语义（聊天记录、好友关系、报价同步）一律在设计上
// 留在各 SPT 服务端，Hub 只做「谁在线、帮我把这条帧送到同域的其他人」。
//
// 这一层刻意不落盘：Hub 重启 = 全体掉线重连，由每个 SPT 服务端在加载时自动补连，
// 因此不需要任何持久化设施。
// ─────────────────────────────────────────────────────────────────────────────

HubOptions options = new();
PeerRegistry registry = new(options);

WebApplicationBuilder builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://0.0.0.0:{options.Port}");
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

WebApplication app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

WebApplication app2 = app;

// ── 健康检查：部署后第一件要能敲通的东西 ────────────────────────────────────
app.MapGet("/v1/health", () => Results.Json(new
{
    ok = true,
    product = BuildInfo.ProductName,
    version = BuildInfo.Version,
    sptVersion = BuildInfo.SptVersion,
    peers = registry.Count,
    realms = registry.Realms,
    serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
}));

// ── 注册：SPT 服务端加载时调一次，拿到 token 后连 /v1/ws ────────────────────
app.MapPost("/v1/hello", (HelloRequest request) =>
{
    if (!string.Equals(request.InviteKey, options.InviteKey, StringComparison.Ordinal))
    {
        return Results.Json(new { ok = false, error = "邀请码错误" }, statusCode: 401);
    }

    HubPeer? peer = registry.Register(
        request.ServerId ?? string.Empty,
        request.Realm ?? string.Empty,
        request.Nickname ?? string.Empty,
        request.Version ?? string.Empty,
        request.SptVersion ?? string.Empty,
        out string error);

    if (peer is null)
    {
        return Results.Json(new { ok = false, error }, statusCode: 400);
    }

    _ = registry.BroadcastAsync(peer.Realm, new
    {
        type = "presence",
        serverId = peer.ServerId,
        nickname = peer.Nickname,
        version = peer.Version,
        online = true,
        at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    }, excludeServerId: peer.ServerId);

    return Results.Json(new
    {
        ok = true,
        product = BuildInfo.ProductName,
        hubVersion = BuildInfo.Version,
        token = peer.Token,
        serverId = peer.ServerId,
        realm = peer.Realm,
        peers = registry.Snapshot(peer.Realm),
        serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    });
});

// ── 长连：所有实时帧都走这里（chat / presence / ping / 后续的 party 事件）──
app.Map("/v1/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("websocket required");
        return;
    }

    string token = context.Request.Query["token"].ToString();
    HubPeer? peer = registry.FindByToken(token);
    if (peer is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("invalid token");
        return;
    }

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    peer.Socket = socket;
    registry.Touch(peer);

    await registry.SendAsync(peer, new
    {
        type = "welcome",
        serverId = peer.ServerId,
        realm = peer.Realm,
        hubVersion = BuildInfo.Version,
        peers = registry.Snapshot(peer.Realm),
        at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    });

    byte[] buffer = new byte[8192];
    try
    {
        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(
                new ArraySegment<byte>(buffer), context.RequestAborted);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            registry.Touch(peer);
            string text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await HandleFrameAsync(peer, text);
        }
    }
    catch (OperationCanceledException)
    {
        // 客户端断开
    }
    catch (WebSocketException)
    {
        // 连接异常断开，下面统一处理
    }
    finally
    {
        peer.Socket = null;
        registry.Remove(peer.ServerId);
        await registry.BroadcastAsync(peer.Realm, new
        {
            type = "presence",
            serverId = peer.ServerId,
            nickname = peer.Nickname,
            online = false,
            at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    return;

    async Task HandleFrameAsync(HubPeer sender, string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        string type = root.TryGetProperty("type", out JsonElement typeElement)
            ? typeElement.GetString() ?? string.Empty
            : string.Empty;

        switch (type)
        {
            case "ping":
                await registry.SendAsync(sender, new
                {
                    type = "pong",
                    at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                break;

            case "nick":
                if (root.TryGetProperty("nickname", out JsonElement nick))
                {
                    sender.Nickname = PeerRegistry.Sanitize(nick.GetString(), sender.Nickname);
                    await registry.BroadcastAsync(sender.Realm, new
                    {
                        type = "presence",
                        serverId = sender.ServerId,
                        nickname = sender.Nickname,
                        online = true,
                        at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                }

                break;

            case "chat":
                string body = root.TryGetProperty("text", out JsonElement textElement)
                    ? textElement.GetString() ?? string.Empty
                    : string.Empty;
                body = body.Trim();
                if (body.Length == 0)
                {
                    break;
                }

                if (body.Length > options.MaxChatLength)
                {
                    body = body.Substring(0, options.MaxChatLength);
                }

                await registry.BroadcastAsync(sender.Realm, new
                {
                    type = "chat",
                    from = sender.ServerId,
                    nickname = sender.Nickname,
                    realm = sender.Realm,
                    text = body,
                    at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }, excludeServerId: sender.ServerId);

                // 回声只给发送方一个 ack：它自己的界面已经画过这条消息，不需要再收一遍正文。
                await registry.SendAsync(sender, new
                {
                    type = "ack",
                    forType = "chat",
                    delivered = true,
                    at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                break;

            default:
                // 未知帧不报错也不回包：Hub 要能被旧客户端连着不崩。
                break;
        }
    }
});

// ── 心跳清扫：45 秒没有任何帧就判掉线，并广播 presence offline ──────────────
_ = Task.Run(async () =>
{
    using PeriodicTimer timer = new(options.SweepInterval);
    while (await timer.WaitForNextTickAsync())
    {
        foreach (HubPeer gone in registry.Sweep())
        {
            await registry.BroadcastAsync(gone.Realm, new
            {
                type = "presence",
                serverId = gone.ServerId,
                nickname = gone.Nickname,
                online = false,
                reason = "timeout",
                at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
    }
});

Console.WriteLine();
Console.WriteLine($"  {BuildInfo.ProductName} Hub {BuildInfo.Version}  ·  SPT {BuildInfo.SptVersion}");
Console.WriteLine($"  监听   http://0.0.0.0:{options.Port}   (health: /v1/health)");
Console.WriteLine($"  邀请码 {(options.InviteKey.Length == 0 ? "<空>" : options.InviteKey)}");
Console.WriteLine($"  超时   {options.PeerTimeout.TotalSeconds:0}s   清扫 {options.SweepInterval.TotalSeconds:0}s   单域上限 {options.MaxPeersPerRealm}");
Console.WriteLine();

await app2.RunAsync();

/// <summary>/v1/hello 的请求体。字段全部可空，缺什么补什么，不让客户端因为少字段 400。</summary>
public sealed class HelloRequest
{
    public string? ServerId { get; set; }
    public string? InviteKey { get; set; }
    public string? Realm { get; set; }
    public string? Nickname { get; set; }
    public string? Version { get; set; }
    public string? SptVersion { get; set; }
}
