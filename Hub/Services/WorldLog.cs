using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentinelLink.Hub.Services;

/// <summary>
/// 世界频道（world / trade / lfg）的环形历史。
///
/// 为什么放在 Hub：三个频道的语义是「同一 realm 内所有人可见」，
/// 只有 Hub 同时看到所有实例，服务端各自留一份就会互相看不到，
/// 晚进来的实例也拿不到历史。
///
/// 上限按「每 realm 每频道」算，超了就丢最旧的 —— 这是聊天，不是账本。
/// </summary>
public sealed class WorldLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _sync = new();
    private readonly Dictionary<string, List<WorldRecord>> _byRealm = new(StringComparer.Ordinal);

    public int PerChannelLimit { get; }

    public WorldLog(int perChannelLimit)
    {
        PerChannelLimit = perChannelLimit < 1 ? 100 : perChannelLimit;
    }

    public WorldRecord NewRecord(string realm, string channel, string from, string fromNick, string text)
    {
        return new WorldRecord
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 16),
            Realm = realm,
            Channel = channel,
            From = from,
            FromNick = fromNick,
            Text = text,
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public void Append(WorldRecord record)
    {
        lock (_sync)
        {
            string key = KeyOf(record.Realm, record.Channel);
            if (!_byRealm.TryGetValue(key, out List<WorldRecord>? list))
            {
                list = new List<WorldRecord>();
                _byRealm[key] = list;
            }

            list.Add(record);
            if (list.Count > PerChannelLimit)
            {
                list.RemoveRange(0, list.Count - PerChannelLimit);
            }
        }
    }

    /// <summary>取某频道最近 limit 条（按时间正序返回，客户端直接追加即可）。</summary>
    public List<WorldRecord> Recent(string realm, string channel, int limit)
    {
        lock (_sync)
        {
            string key = KeyOf(realm, channel);
            if (!_byRealm.TryGetValue(key, out List<WorldRecord>? list) || list.Count == 0)
            {
                return new List<WorldRecord>();
            }

            if (limit <= 0 || limit >= list.Count)
            {
                return new List<WorldRecord>(list);
            }

            return list.GetRange(list.Count - limit, limit);
        }
    }

    public int Count(string realm, string channel)
    {
        lock (_sync)
        {
            return _byRealm.TryGetValue(KeyOf(realm, channel), out List<WorldRecord>? list) ? list.Count : 0;
        }
    }

    private static string KeyOf(string realm, string channel)
    {
        return (string.IsNullOrEmpty(realm) ? "default" : realm) + "|" + (string.IsNullOrEmpty(channel) ? "world" : channel);
    }

    public static string ToJson(WorldRecord record)
    {
        return JsonSerializer.Serialize(record, JsonOptions);
    }
}

public sealed class WorldRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>不做序列化：Hub 内部按 realm 分桶，下发给客户端时由调用方决定是否带。</summary>
    [JsonIgnore] public string Realm { get; set; } = "default";

    [JsonPropertyName("channel")] public string Channel { get; set; } = "world";
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("fromNick")] public string FromNick { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("at")] public long At { get; set; }
}
