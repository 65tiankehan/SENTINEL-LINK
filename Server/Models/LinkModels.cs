using System.Text.Json.Serialization;

namespace SentinelLink.Server.Models;

/// <summary>客户端 → 服务端：探活与握手。字段全部可选，为 P1 的链路自检留位。</summary>
public sealed record LinkPingRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
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

    [JsonPropertyName("nickname")]
    public string Nickname { get; init; } = "";

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

    [JsonPropertyName("lastFrameMs")]
    public long LastFrameMs { get; init; }

    [JsonPropertyName("lastError")]
    public string LastError { get; init; } = "";

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; init; }

    [JsonPropertyName("echo")]
    public string? Echo { get; init; }
}

/// <summary>一句话聊天记录（Hub 转发的帧和服务端本地发出的都长这样）。</summary>
public sealed record LinkChatMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "chat";

    [JsonPropertyName("from")]
    public string From { get; init; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; init; } = "";

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("at")]
    public long At { get; init; }
}

/// <summary>realm 内的一个对端（另一个 SPT 服务端）。</summary>
public sealed record LinkPeerInfo
{
    [JsonPropertyName("serverId")]
    public string ServerId { get; init; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("sptVersion")]
    public string SptVersion { get; init; } = "";

    [JsonPropertyName("online")]
    public bool Online { get; init; }
}

/// <summary>客户端 → 服务端：发一条世界聊天。</summary>
public sealed record LinkChatSendRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

/// <summary>服务端 → 客户端：发送结果。</summary>
public sealed record LinkChatSendResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("hubState")]
    public string HubState { get; init; } = "";

    [JsonPropertyName("error")]
    public string Error { get; init; } = "";
}

/// <summary>客户端 → 服务端：拉聊天与在线列表（sinceMs 之后的消息）。</summary>
public sealed record LinkChatStateRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    [JsonPropertyName("sinceMs")]
    public long SinceMs { get; init; }
}

/// <summary>服务端 → 客户端：聊天 + 在线列表快照。</summary>
public sealed record LinkChatStateResponse
{
    [JsonPropertyName("hubState")]
    public string HubState { get; init; } = "";

    [JsonPropertyName("realm")]
    public string Realm { get; init; } = "";

    [JsonPropertyName("peers")]
    public IReadOnlyList<LinkPeerInfo> Peers { get; init; } = [];

    [JsonPropertyName("messages")]
    public IReadOnlyList<LinkChatMessage> Messages { get; init; } = [];

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; init; }
}
