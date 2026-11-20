using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// ─────────────────────────────────────────────────────────────────────────────
// SyntheticPeer —— 一台「假玩家」的 SPT 服务端。
//
// 用途：没有第二台机器时验证 Hub 的四跳链路
//       游戏客户端 → 本机 SPT 服务端 → Hub ← 这台假对端
// 它只做真对端会做的最小集合：hello 拿 token、连 ws、收帧、发一条聊天、待一会儿。
//
//   dotnet run --project Tools\SyntheticPeer -c Release -- --hub http://127.0.0.1:5180 --server-id peer-b --nickname 小明 --chat "hi"
// ─────────────────────────────────────────────────────────────────────────────

Dictionary<string, string> opts = ParseArgs(args);

string hub = Arg("hub", "http://127.0.0.1:5180");
string serverId = Arg("server-id", "peer-" + Guid.NewGuid().ToString("N").Substring(0, 6));
string nickname = Arg("nickname", serverId);
string inviteKey = Arg("key", "dev-local");
string realm = Arg("realm", "default");
string chat = Arg("chat", string.Empty);
string to = Arg("to", string.Empty);
string world = Arg("world", string.Empty);
int count = int.TryParse(Arg("count", "1"), out int parsedCount) && parsedCount > 0 ? parsedCount : 1;
int delayMs = int.TryParse(Arg("delay", "20"), out int parsedDelay) && parsedDelay >= 0 ? parsedDelay : 20;
int expect = int.TryParse(Arg("expect", "0"), out int parsedExpect) ? parsedExpect : 0;
int worldExpect = int.TryParse(Arg("world-expect", "0"), out int parsedWorldExpect) ? parsedWorldExpect : 0;
double listenSeconds = double.TryParse(Arg("listen", "6"), out double parsed) ? parsed : 6;
bool quiet = opts.ContainsKey("quiet");

int directReceived = 0;
int worldReceived = 0;
int directSync = 0;

using HttpClient http = new() { BaseAddress = new Uri(hub) };

Console.WriteLine($"[{serverId}] hello → {hub}");

HttpResponseMessage hello;
try
{
    hello = await http.PostAsJsonAsync("/v1/hello", new
    {
        serverId,
        inviteKey,
        realm,
        nickname,
        version = "synthetic-0.1.0",
        sptVersion = "4.1.6"
    });
}
catch (Exception ex)
{
    Console.WriteLine($"[{serverId}] hello 失败：{ex.Message}");
    return 2;
}

string helloBody = await hello.Content.ReadAsStringAsync();
if (!hello.IsSuccessStatusCode)
{
    Console.WriteLine($"[{serverId}] hello 被拒 {(int)hello.StatusCode}：{helloBody}");
    return 2;
}

using JsonDocument helloJson = JsonDocument.Parse(helloBody);
JsonElement root = helloJson.RootElement;
string token = root.GetProperty("token").GetString() ?? string.Empty;
string realmOut = root.GetProperty("realm").GetString() ?? realm;
Console.WriteLine($"[{serverId}] 已注册 realm={realmOut} token={token.Substring(0, Math.Min(8, token.Length))}…");

if (root.TryGetProperty("peers", out JsonElement peers) && peers.ValueKind == JsonValueKind.Array)
{
    foreach (JsonElement peer in peers.EnumerateArray())
    {
        Console.WriteLine(
            $"[{serverId}]   对端 {peer.GetProperty("serverId").GetString()} " +
            $"({peer.GetProperty("nickname").GetString()}, online={peer.GetProperty("online").GetBoolean()})");
    }
}

using ClientWebSocket socket = new();
string wsBase = hub.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
                    .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                    .TrimEnd('/');
try
{
    await socket.ConnectAsync(new Uri($"{wsBase}/v1/ws?token={token}"), CancellationToken.None);
}
catch (Exception ex)
{
    Console.WriteLine($"[{serverId}] ws 连接失败：{ex.Message}");
    return 3;
}

Console.WriteLine($"[{serverId}] ws 已连接");

CancellationTokenSource cts = new(TimeSpan.FromSeconds(listenSeconds));

Task receiver = Task.Run(async () =>
{
    byte[] buffer = new byte[8192];
    try
    {
        while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            string text = Encoding.UTF8.GetString(buffer, 0, result.Count);

            // 私聊帧单独数：验收「离线补投 0 丢失」靠这个计数对账
            if (text.Contains("\"type\":\"dm\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref directReceived);
            }
            else if (text.Contains("\"type\":\"dm-sync\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref directSync);
            }

                if (text.Contains("\"type\":\"world\"", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref worldReceived);
                }

            if (!quiet)
            {
                Console.WriteLine($"[{serverId}] ← {Shorten(text)}");
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        if (!quiet)
        {
            Console.WriteLine($"[{serverId}] 接收结束：{ex.Message}");
        }
    }
});

await Task.Delay(400);

if (!string.IsNullOrWhiteSpace(chat))
{
    string frame = JsonSerializer.Serialize(new { type = "chat", text = chat });
    await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);
    Console.WriteLine($"[{serverId}] → chat: {chat}");
}

if (!string.IsNullOrWhiteSpace(to))
{
    for (int i = 1; i <= count; i++)
    {
        string text = count == 1 ? (chat.Length > 0 ? chat : $"私聊 #{i}") : $"私聊 #{i}/{count}";
        string frame = JsonSerializer.Serialize(new { type = "dm", to, text });
        await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);

        if (!quiet)
        {
            Console.WriteLine($"[{serverId}] → dm[{i}/{count}] → {to}: {text}");
        }

        if (delayMs > 0 && i < count)
        {
            await Task.Delay(delayMs);
        }
    }
}

if (!string.IsNullOrWhiteSpace(world))
{
    for (int i = 1; i <= count; i++)
    {
        string text = count == 1 ? (chat.Length > 0 ? chat : $"世界 #{i}") : $"世界 #{i}/{count}";
        string frame = JsonSerializer.Serialize(new { type = "world", channel = world, text });
        await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);

        if (!quiet)
        {
            Console.WriteLine($"[{serverId}] → world[{i}/{count}] {world}: {text}");
        }

        if (delayMs > 0 && i < count)
        {
            await Task.Delay(delayMs);
        }
    }
}

// Hub 的 PeerTimeout 是 45 秒，空闲不发 ping 会被清扫；每 15 秒补一次心跳。
Task keepAlive = Task.Run(async () =>
{
    byte[] ping = JsonSerializer.SerializeToUtf8Bytes(new { type = "ping" });
    while (!cts.IsCancellationRequested && socket.State == WebSocketState.Open)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cts.Token);
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(ping, WebSocketMessageType.Text, true, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }
    }
});

await Task.WhenAny(receiver, Task.Delay(TimeSpan.FromSeconds(listenSeconds)));

try
{
    if (socket.State == WebSocketState.Open)
    {
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }
}
catch
{
    // 关闭失败不影响结论
}

cts.Cancel();
Console.WriteLine($"[{serverId}] 收到 dm {directReceived} 条 · dm-sync {directSync} 次");
Console.WriteLine($"[{serverId}] 收到 world {worldReceived} 条");

if (worldExpect > 0)
{
    bool worldOk = worldReceived == worldExpect;
    Console.WriteLine(worldOk
        ? $"[{serverId}] 世界对账 OK：期望 {worldExpect} 条，实收 {worldReceived} 条"
        : $"[{serverId}] 世界对账 FAIL：期望 {worldExpect} 条，实收 {worldReceived} 条");
}


if (expect > 0)
{
    bool ok = directReceived == expect;
    Console.WriteLine(ok
        ? $"[{serverId}] 对账 OK：期望 {expect} 条，实收 {directReceived} 条"
        : $"[{serverId}] 对账 FAIL：期望 {expect} 条，实收 {directReceived} 条");
    Console.WriteLine($"[{serverId}] 退出");
    return ok ? 0 : 4;
}

Console.WriteLine($"[{serverId}] 退出");
return 0;

string Arg(string name, string fallback) =>
    opts.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

static string Shorten(string text) => text.Length <= 240 ? text : text.Substring(0, 240) + "…";

static Dictionary<string, string> ParseArgs(string[] raw)
{
    Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < raw.Length; i++)
    {
        string a = raw[i];
        if (!a.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        string key = a.Substring(2);
        if (i + 1 < raw.Length && !raw[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            map[key] = raw[i + 1];
            i++;
        }
        else
        {
            map[key] = "1";
        }
    }

    return map;
}
