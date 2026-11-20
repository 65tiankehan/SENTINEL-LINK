using SentinelLink.Shared;
using SPTarkov.DI.Annotations;

namespace SentinelLink.Server.Services;

/// <summary>
/// Link 的服务端内存态。P0 只放「Hub 接上了没有」这一组事实；
/// 后续 chat/presence/market 的状态都往这里加，禁止散落在 Service 里。
/// </summary>
[Injectable]
public sealed class LinkState
{
    public string ServerId { get; set; } = "";

    public string HubUrl { get; set; } = "";

    public string Realm { get; set; } = "default";

    /// <summary>unlinked / handshaking / online / auth-failed</summary>
    public string HubState { get; set; } = "unlinked";

    public int Peers { get; set; }

    public long LastHelloMs { get; set; }

    public string Product => BuildInfo.ProductName;

    public string Version => BuildInfo.Version;

    public string SptVersion => BuildInfo.SptVersion;
}
