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
double listenSeconds = double.TryParse(Arg("listen", "6"), out double parsed) ? parsed : 6;
bool quiet = opts.ContainsKey("quiet");

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
