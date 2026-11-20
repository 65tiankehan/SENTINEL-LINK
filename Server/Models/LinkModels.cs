using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace SentinelLink.Server.Models;

/// <summary>客户端 → 服务端：探活与握手。字段全部可选，为 P1 的链路自检留位。</summary>
public sealed record LinkPingRequest : IRequestData
{
    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; init; }

    [JsonPropertyName("echo")]
    public string? Echo { get; init; }
}

/// <summary>服务端 → 客户端：Link 链路现状。Hub 未接时 HubState = "unlinked"。</summary>
public sealed record LinkPingResponse
{
    [JsonPropertyName("product")]
    public string Product { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("spt")]
    public string Spt { get; init; } = "";

    [JsonPropertyName("serverId")]
    public string ServerId { get; init; } = "";

    [JsonPropertyName("hubState")]
    public string HubState { get; init; } = "unlinked";

    [JsonPropertyName("hubUrl")]
    public string HubUrl { get; init; } = "";

    [JsonPropertyName("realm")]
    public string Realm { get; init; } = "";

    [JsonPropertyName("peers")]
    public int Peers { get; init; }

    [JsonPropertyName("lastHelloMs")]
    public long LastHelloMs { get; init; }

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; init; }

    [JsonPropertyName("echo")]
    public string? Echo { get; init; }
}
