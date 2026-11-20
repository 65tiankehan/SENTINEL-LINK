using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using SentinelLink.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace SentinelLink.Server.Services;

/// <summary>
/// 把 Hub 上的对端映射成「原生好友」要的数据结构（P2a）。
/// 纯只读：这里产生的一切对象都只进 Harmony 的返回值，绝不写 SptProfile。
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class LinkFriends(LinkState state)
{
    /// <summary>派生 MongoId 的域标记（追加在 20 位哈希之后，共 24 hex）。</summary>
    public const string DomainTag = "1e0f";

    /// <summary>对端 → 我们会给原生面板看的身份。</summary>
    public sealed record PeerRef(string ServerId, string Nickname, bool Online, int Aid, MongoId DialogId);

    private readonly object _sync = new();
    private readonly Dictionary<MongoId, PeerRef> _byDialog = new();
    private readonly Dictionary<string, MongoId> _byServer = new();
    private long _builtAtMs;
    private int _version;

    /// <summary>每次重建 +1，面板用它确认「注入表换过一批」。</summary>
    public int Version
    {
        get
        {
            lock (_sync)
            {
                return _version;
            }
        }
    }

    public bool Enabled { get; set; } = true;

    /// <summary>refresh 的最短间隔，避免每个请求都重算哈希。</summary>
    private const int RebuildIntervalMs = 1000;

    /// <summary>serverId(32 hex) → 24 hex MongoId；确定性、跨重启稳定。</summary>
    public static MongoId DeriveDialogId(string serverId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("sentinel.link.friend:" + serverId));
        StringBuilder sb = new(24);
        for (int i = 0; i < 10; i++)
        {
            sb.Append(hash[i].ToString("x2"));
        }

        sb.Append(DomainTag);
        return new MongoId(sb.ToString());
    }

    /// <summary>Aid（int）同源派生，用于面板里显示数字账号。</summary>
    public static int DeriveAid(string serverId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("sentinel.link.aid:" + serverId));
        int value = (hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3];
        return (value & 0x7FFFFFFF) % 9_000_000 + 1_000_000;
    }

    /// <summary>按当前 peers 重建映射表（内部有 1 s 节流）。</summary>
    public IReadOnlyList<PeerRef> Refresh(bool force = false)
    {
        long now = Environment.TickCount64;
        lock (_sync)
        {
            if (!force && now - _builtAtMs < RebuildIntervalMs && _byDialog.Count > 0)
            {
                return SnapshotUnlocked();
            }

            _builtAtMs = now;
            _byDialog.Clear();
            _byServer.Clear();

            AddPeer(state.SnapshotPeers().Select(p => (p.ServerId, p.Nickname, p.Online)));

            // 历史私聊的发件人也要变成「好友」，否则对方下线后消息在原生面板里就消失了。
            List<(string, string, bool)> history = new();
            foreach (LinkDirectMessage item in state.SnapshotDirect(0))
            {
                if (!string.IsNullOrEmpty(item.From) && item.From != state.ServerId)
                {
                    history.Add((item.From, item.FromNick, false));
                }
            }

            AddPeer(history);

            _version++;
            return SnapshotUnlocked();
        }
    }

    /// <summary>把一批对端并进映射表；同 serverId 只留一次，在线信息取并集中更可信的那份。</summary>
    private void AddPeer(IEnumerable<(string ServerId, string Nickname, bool Online)> peers)
    {
        foreach ((string serverId, string nickname, bool online) in peers)
        {
            if (string.IsNullOrEmpty(serverId) || serverId == state.ServerId)
            {
                continue;
            }

            MongoId id = DeriveDialogId(serverId);
            if (_byDialog.TryGetValue(id, out PeerRef? existing) && existing != null)
            {
                if (online && !existing.Online)
                {
                    _byDialog[id] = existing with { Online = true };
                }

                continue;
            }

            if (_byDialog.ContainsKey(id))
            {
                // 24 hex 撞了：不静默覆盖，记一次就跳过。
                PluginLog.Warn("[P2a] 派生 MongoId 冲突：" + serverId);
                continue;
            }

            PeerRef reference = new(
                serverId,
                string.IsNullOrEmpty(nickname) ? "路人" : nickname,
                online,
                DeriveAid(serverId),
                id);
            _byDialog[id] = reference;
            _byServer[serverId] = id;
        }
    }

    private List<PeerRef> SnapshotUnlocked()
    {
        List<PeerRef> list = new(_byDialog.Count);
        foreach (PeerRef item in _byDialog.Values)
        {
            list.Add(item);
        }

        return list;
    }

    public bool TryResolve(MongoId dialogId, out PeerRef reference)
    {
        Refresh();
        lock (_sync)
        {
            return _byDialog.TryGetValue(dialogId, out reference!);
        }
    }

    public bool TryResolve(string dialogId, out PeerRef reference)
    {
        reference = null!;
        if (string.IsNullOrWhiteSpace(dialogId) || !MongoId.IsValidMongoId(dialogId))
        {
            return false;
        }

        return TryResolve(new MongoId(dialogId), out reference);
    }

    /// <summary>自己这侧的身份（对话里的另一半）。</summary>
    public UserDialogInfo BuildSelfUser()
    {
        return new UserDialogInfo
        {
            Id = string.IsNullOrEmpty(state.ServerId) ? MongoId.Empty() : DeriveDialogId(state.ServerId),
            Aid = null,
            Info = new UserDialogDetails
            {
                Nickname = string.IsNullOrEmpty(state.Nickname) ? "本机" : state.Nickname,
                Side = "Usec",
                Level = 1,
            },
        };
    }

    public UserDialogInfo BuildPeerUser(PeerRef reference)
    {
        return new UserDialogInfo
        {
            Id = reference.DialogId,
            Aid = reference.Aid,
            Info = new UserDialogDetails
            {
                Nickname = reference.Nickname,
                Side = "Usec",
                Level = 1,
            },
        };
    }

    /// <summary>某个对端的私聊历史（我们自己的 LinkState，不是 profile）。</summary>
    public List<LinkDirectMessage> MessagesOf(PeerRef reference)
    {
        string self = state.ServerId;
        List<LinkDirectMessage> result = new();
        foreach (LinkDirectMessage item in state.SnapshotDirect(0))
        {
            bool fromPeer = item.From == reference.ServerId && (string.IsNullOrEmpty(item.To) || item.To == self);
            bool toPeer = item.From == self && item.To == reference.ServerId;
            if (fromPeer || toPeer)
            {
                result.Add(item);
            }
        }

        result.Sort((a, b) => a.At.CompareTo(b.At));
        return result;
    }

    /// <summary>把一条 Link 私聊转成原生 Message（只进返回值）。</summary>
    public Message BuildNativeMessage(PeerRef reference, LinkDirectMessage item, MongoId selfId)
    {
        bool mine = item.From == state.ServerId;
        return new Message
        {
            Id = DeriveMessageId(item.Id),
            UserId = mine ? selfId : reference.DialogId,
            MessageType = MessageType.UserMessage,
            DateTime = item.At,
            UtcDateTime = item.At,
            Member = new UpdatableChatMember
            {
                Nickname = mine ? (string.IsNullOrEmpty(state.Nickname) ? "本机" : state.Nickname) : reference.Nickname,
                Side = "Usec",
                Level = 1,
            },
            Text = item.Text,
            HasRewards = false,
            RewardCollected = false,
        };
    }

    private static MongoId DeriveMessageId(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("sentinel.link.message:" + seed));
        StringBuilder sb = new(24);
        for (int i = 0; i < 10; i++)
        {
            sb.Append(hash[i].ToString("x2"));
        }

        sb.Append("1e0f");
        return new MongoId(sb.ToString());
    }

    /// <summary>对话列表条目（GenerateDialogueList 的 Postfix 追加项）。</summary>
    public DialogueInfo BuildDialogueInfo(PeerRef reference)
    {
        MongoId selfId = BuildSelfUser().Id;
        List<LinkDirectMessage> messages = MessagesOf(reference);
        DialogueInfo info = new()
        {
            Id = reference.DialogId,
            Type = MessageType.UserMessage,
            Users = new List<UserDialogInfo> { BuildSelfUser(), BuildPeerUser(reference) },
            Messages = new List<Message>(),
            New = 0,
            AttachmentsNew = 0,
            Pinned = false,
        };

        // 对话列表只需要最近若干条，避免把 200 条全塞进列表接口。
        int from = Math.Max(0, messages.Count - 10);
        for (int i = from; i < messages.Count; i++)
        {
            info.Messages.Add(BuildNativeMessage(reference, messages[i], selfId));
        }

        if (messages.Count > 0)
        {
            LinkDirectMessage last = messages[^1];
            info.Message = new MessagePreview
            {
                UserId = last.From == state.ServerId ? selfId.ToString() : reference.DialogId.ToString(),
                MessageType = MessageType.UserMessage,
                DateTime = last.At,
                Text = last.Text,
            };
        }

        return info;
    }

    /// <summary>打开对话窗口时的完整响应（GenerateDialogueView 的 Prefix 返回值）。</summary>
    public GetMailDialogViewResponseData BuildView(PeerRef reference, int limit)
    {
        MongoId selfId = BuildSelfUser().Id;
        List<LinkDirectMessage> messages = MessagesOf(reference);
        int take = limit > 0 ? limit : 100;
        int from = Math.Max(0, messages.Count - take);

        GetMailDialogViewResponseData response = new()
        {
            Messages = new List<Message>(),
            Profiles = new List<UserDialogInfo> { BuildSelfUser(), BuildPeerUser(reference) },
            HasMessagesWithRewards = false,
        };

        for (int i = from; i < messages.Count; i++)
        {
            response.Messages.Add(BuildNativeMessage(reference, messages[i], selfId));
        }

        return response;
    }
}
