using System.Collections.Generic;
using SentinelLink.Server.Models;
using SentinelLink.Shared;
using SPTarkov.DI.Annotations;

namespace SentinelLink.Server.Services;

/// <summary>
/// Link 的服务端内存态。P0 只放「Hub 接上了没有」这一组事实；
/// 后续 chat/presence/market 的状态都往这里加，禁止散落在 Service 里。
/// 注意：全部是内存态，服务端重启即清空 —— Hub 也一样，两边都不落盘。
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class LinkState
{
    private readonly object _sync = new();
    private readonly List<LinkChatMessage> _chat = new();
    private readonly Dictionary<string, LinkPeerInfo> _peers = new();

    /// <summary>聊天历史保留条数（P1 做界面时按这个上限滚）。</summary>
    public const int ChatHistoryLimit = 100;

    public string ServerId { get; set; } = "";

    public string HubUrl { get; set; } = "";

    public string Realm { get; set; } = "default";

    public string Nickname { get; set; } = "";

    /// <summary>unlinked / handshaking / online / auth-failed</summary>
    public string HubState { get; set; } = "unlinked";

    /// <summary>最近一次错误（给面板显示原因用，成功时清空）。</summary>
    public string LastError { get; set; } = "";

    public int Peers
    {
        get
        {
            lock (_sync)
            {
                return _peers.Count;
            }
        }
    }

    public long LastHelloMs { get; set; }

    public long LastFrameMs { get; set; }

    public string Product => BuildInfo.ProductName;

    public string Version => BuildInfo.Version;

    public string SptVersion => BuildInfo.SptVersion;

    public void AddChat(LinkChatMessage message)
    {
        lock (_sync)
        {
            _chat.Add(message);
            if (_chat.Count > ChatHistoryLimit)
            {
                _chat.RemoveRange(0, _chat.Count - ChatHistoryLimit);
            }
        }
    }

    public List<LinkChatMessage> SnapshotChat(long sinceMs)
    {
        lock (_sync)
        {
            List<LinkChatMessage> result = new();
            foreach (LinkChatMessage item in _chat)
            {
                if (item.At >= sinceMs)
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }

    public void SetPeer(LinkPeerInfo peer)
    {
        lock (_sync)
        {
            if (peer.Online)
            {
                _peers[peer.ServerId] = peer;
            }
            else
            {
                _peers.Remove(peer.ServerId);
            }
        }
    }

    public List<LinkPeerInfo> SnapshotPeers()
    {
        lock (_sync)
        {
            return new List<LinkPeerInfo>(_peers.Values);
        }
    }

    public void ClearPeers()
    {
        lock (_sync)
        {
            _peers.Clear();
        }
    }
}
