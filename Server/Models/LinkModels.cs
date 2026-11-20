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

    [JsonPropertyName("serverId")]
    public string ServerId { get; init; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; init; } = "";

    [JsonPropertyName("realm")]
    public string Realm { get; init; } = "";

    [JsonPropertyName("peers")]
    public IReadOnlyList<LinkPeerInfo> Peers { get; init; } = [];

    [JsonPropertyName("messages")]
    public IReadOnlyList<LinkChatMessage> Messages { get; init; } = [];

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; init; }
}

/// <summary>一条私聊（P2）。Replayed=true 表示这是离线补投，不是实时送达。</summary>
public sealed record LinkDirectMessage
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("from")]
    public string From { get; init; } = "";

    [JsonPropertyName("fromNick")]
    public string FromNick { get; init; } = "";

    [JsonPropertyName("to")]
    public string To { get; init; } = "";

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("at")]
    public long At { get; init; }

    [JsonPropertyName("replayed")]
    public bool Replayed { get; init; }
}

/// <summary>客户端 → 服务端：发一条私聊。</summary>
public sealed record LinkDirectSendRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

/// <summary>服务端 → 客户端：私聊发送结果（delivered=false 时 queued=true 表示已排队等对方上线）。</summary>
public sealed record LinkDirectSendResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("delivered")]
    public bool Delivered { get; init; }

    [JsonPropertyName("queued")]
    public bool Queued { get; init; }

    [JsonPropertyName("pending")]
    public int Pending { get; init; }

    [JsonPropertyName("hubState")]
    public string HubState { get; init; } = "";

    [JsonPropertyName("error")]
    public string Error { get; init; } = "";
}

/// <summary>客户端 → 服务端：拉私聊增量（sinceMs 之后）。</summary>
public sealed record LinkDirectStateRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    [JsonPropertyName("sinceMs")]
    public long SinceMs { get; init; }

    /// <summary>true 时顺手把未读清零（面板打开即已读）。</summary>
    [JsonPropertyName("markRead")]
    public bool MarkRead { get; init; }
}

/// <summary>服务端 → 客户端：私聊消息 + 未读计数。</summary>
public sealed record LinkDirectStateResponse
{
    [JsonPropertyName("hubState")]
    public string HubState { get; init; } = "";

    [JsonPropertyName("serverId")]
    public string ServerId { get; init; } = "";

    [JsonPropertyName("messages")]
    public List<LinkDirectMessage> Messages { get; init; } = new();

    [JsonPropertyName("unread")]
    public int Unread { get; init; }

    /// <summary>离线补投过的批次数（Hub 连上时告诉我们的），诊断用。</summary>
    [JsonPropertyName("syncBatches")]
    public int SyncBatches { get; init; }

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; init; }
}
/// <summary>P2a 自检：传自己的 profile id（sessionId），服务端直接问一遍原生控制器。</summary>
public sealed record LinkP2aStateRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = "";
}

/// <summary>P2a 自检回包。</summary>
public sealed record LinkP2aStateResponse
{
    [JsonPropertyName("patchActive")]
    public bool PatchActive { get; init; }

    [JsonPropertyName("patchVersion")]
    public int PatchVersion { get; init; }

    [JsonPropertyName("peers")]
    public int Peers { get; init; }

    [JsonPropertyName("virtualFriends")]
    public int VirtualFriends { get; init; }

    [JsonPropertyName("friendsTotal")]
    public int FriendsTotal { get; init; }

    [JsonPropertyName("injectedFriends")]
    public int InjectedFriends { get; init; }

    [JsonPropertyName("dialoguesTotal")]
    public int DialoguesTotal { get; init; }

    [JsonPropertyName("injectedDialogues")]
    public int InjectedDialogues { get; init; }

    [JsonPropertyName("sampleDialogId")]
    public string SampleDialogId { get; init; } = "";

    [JsonPropertyName("viewMessages")]
    public int ViewMessages { get; init; }

    [JsonPropertyName("viewProfiles")]
    public int ViewProfiles { get; init; }

    /// <summary>P2b：三个世界频道里，有几个真的出现在 GenerateDialogueList 的返回值里。</summary>
    [JsonPropertyName("worldInjected")]
    public int WorldInjected { get; init; }

    [JsonPropertyName("worldViewMessages")]
    public int WorldViewMessages { get; init; }

    [JsonPropertyName("worldViewProfiles")]
    public int WorldViewProfiles { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = "";
}

/// <summary>一条世界频道消息（world / trade / lfg）。</summary>
public sealed class LinkWorldMessage
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("channel")] public string Channel { get; init; } = "world";
    [JsonPropertyName("from")] public string From { get; init; } = "";
    [JsonPropertyName("fromNick")] public string FromNick { get; init; } = "";
    [JsonPropertyName("text")] public string Text { get; init; } = "";
    [JsonPropertyName("at")] public long At { get; init; }
    [JsonPropertyName("replayed")] public bool Replayed { get; init; }
}

public sealed class LinkWorldSendRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    [JsonPropertyName("channel")] public string Channel { get; set; } = "world";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public sealed class LinkWorldSendResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("delivered")] public bool Delivered { get; init; }
    [JsonPropertyName("hubState")] public string HubState { get; init; } = "";
    [JsonPropertyName("channel")] public string Channel { get; init; } = "";
    [JsonPropertyName("error")] public string Error { get; init; } = "";
}

public sealed class LinkWorldStateRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    [JsonPropertyName("channel")] public string Channel { get; set; } = "";
    [JsonPropertyName("sinceMs")] public long SinceMs { get; set; }
}

public sealed class LinkWorldStateResponse
{
    [JsonPropertyName("hubState")] public string HubState { get; init; } = "";
    [JsonPropertyName("realm")] public string Realm { get; init; } = "";
    [JsonPropertyName("channels")] public IReadOnlyList<string> Channels { get; init; } = new List<string>();
    [JsonPropertyName("messages")] public IReadOnlyList<LinkWorldMessage> Messages { get; init; } = new List<LinkWorldMessage>();
    [JsonPropertyName("serverTime")] public long ServerTime { get; init; }
}
