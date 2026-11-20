using System;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;
using SentinelLink.Server.Models;
using SentinelLink.Server.Services;

namespace SentinelLink.Server.Callbacks;

[Injectable]
public sealed class LinkCallbacks(
    HttpResponseUtil httpResponse,
    LinkState state,
    LinkHubClient hub,
    LinkFriends friends,
    SPTarkov.Server.Core.Controllers.DialogueController dialogue)
{
    /// <summary>P0 入口：证明「客户端 → 本地服务端」这条链路通了，并回报 Hub 现状。</summary>
    public ValueTask<string> Ping(
        string url,
        LinkPingRequest request,
        MongoId sessionId)
    {
        LinkPingResponse response = new()
        {
            Product = state.Product,
            Version = state.Version,
            Spt = state.SptVersion,
            ServerId = state.ServerId,
            Nickname = state.Nickname,
            HubState = state.HubState,
            HubUrl = state.HubUrl,
            Realm = state.Realm,
            Peers = state.Peers,
            LastHelloMs = state.LastHelloMs,
            LastFrameMs = state.LastFrameMs,
            LastError = state.LastError,
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Echo = request.Echo,
        };

        return new ValueTask<string>(httpResponse.NoBody(response));
    }

    /// <summary>发一条世界聊天：交给 Hub 转发给同 realm 的其它 SPT 服务端。</summary>
    public async ValueTask<string> SendChat(
        string url,
        LinkChatSendRequest request,
        MongoId sessionId)
    {
        string text = (request.Text ?? "").Trim();
        LinkChatSendResponse response;

        if (text.Length == 0)
        {
            response = new LinkChatSendResponse
            {
                Ok = false,
                HubState = state.HubState,
                Error = "文本为空"
            };
        }
        else
        {
            bool sent = await hub.SendChatAsync(text, System.Threading.CancellationToken.None);
            response = new LinkChatSendResponse
            {
                Ok = sent,
                HubState = state.HubState,
                Error = sent ? "" : "Hub 未连接"
            };

            if (sent)
            {
                // 本地也留一份：发送方自己的界面靠它显示，Hub 不会再回声给本人。
                state.AddChat(new LinkChatMessage
                {
                    From = state.ServerId,
                    Nickname = state.Nickname,
                    Text = text,
                    At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        }

        return httpResponse.NoBody(response);
    }

    /// <summary>拉聊天增量与在线列表（P1 的界面按 sinceMs 轮询）。</summary>
    public ValueTask<string> ChatState(
        string url,
        LinkChatStateRequest request,
        MongoId sessionId)
    {
        LinkChatStateResponse response = new()
        {
            HubState = state.HubState,
            ServerId = state.ServerId,
            Nickname = state.Nickname,
            Realm = state.Realm,
            Peers = state.SnapshotPeers(),
            Messages = state.SnapshotChat(request.SinceMs),
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return new ValueTask<string>(httpResponse.NoBody(response));
    }

    // ── 私聊（P2）──────────────────────────────────────────────────────────

    /// <summary>发一条私聊：走 Hub 定向投递，对方离线则由 Hub 落盘排队。</summary>
    public async ValueTask<string> SendDirect(
        string url,
        LinkDirectSendRequest request,
        MongoId sessionId)
    {
        string to = (request.To ?? "").Trim();
        string text = (request.Text ?? "").Trim();
        LinkDirectSendResponse response;

        if (to.Length == 0 || text.Length == 0)
        {
            response = new LinkDirectSendResponse
            {
                Ok = false,
                HubState = state.HubState,
                Error = to.Length == 0 ? "缺少收件人" : "文本为空"
            };
        }
        else if (string.Equals(to, state.ServerId, StringComparison.Ordinal))
        {
            response = new LinkDirectSendResponse
            {
                Ok = false,
                HubState = state.HubState,
                Error = "不能发给自己"
            };
        }
        else
        {
            bool sent = await hub.SendDirectAsync(to, text, CancellationToken.None);
            bool targetOnline = state.SnapshotPeers()
                .Exists(p => string.Equals(p.ServerId, to, StringComparison.Ordinal));

            response = new LinkDirectSendResponse
            {
                Ok = sent,
                Delivered = sent && targetOnline,
                Queued = sent && !targetOnline,
                HubState = state.HubState,
                Error = sent ? "" : "Hub 未连接"
            };

            if (sent)
            {
                // 本地留一份：对方是别人，Hub 不会回显给我，界面靠这条显示「我发出去的」
                state.AddDirect(new LinkDirectMessage
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                    From = state.ServerId,
                    FromNick = state.Nickname,
                    To = to,
                    Text = text,
                    At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Replayed = false
                });
                state.MarkDirectRead();
            }
        }

        return httpResponse.NoBody(response);
    }

    /// <summary>拉私聊增量；markRead=true 时顺手把未读清零。</summary>
    public ValueTask<string> DirectState(
        string url,
        LinkDirectStateRequest request,
        MongoId sessionId)
    {
        if (request.MarkRead)
        {
            state.MarkDirectRead();
        }

        LinkDirectStateResponse response = new()
        {
            HubState = state.HubState,
            ServerId = state.ServerId,
            Messages = state.SnapshotDirect(request.SinceMs),
            Unread = state.Unread,
            SyncBatches = state.SyncBatches,
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return new ValueTask<string>(httpResponse.NoBody(response));
    }

    /// <summary>
    /// P2a 自检：直接调原生 DialogueController（会被我们四条补丁拦到），
    /// 回报「注入了几条好友 / 几个对话 / 打开对话能取到几条消息」。
    /// 只读：GetFriendList / GenerateDialogueList / GenerateDialogueView 都不写存档。
    /// </summary>
    public ValueTask<string> P2aState(
        string url,
        LinkP2aStateRequest request,
        MongoId sessionId)
    {
        LinkP2aStateResponse response = new()
        {
            PatchActive = SentinelLink.Server.Patches.DialoguePatchRuntime.Active,
            PatchVersion = friends.Version,
            Peers = state.Peers,
        };

        try
        {
            MongoId target = sessionId;
            if (MongoId.IsValidMongoId(request.SessionId))
            {
                target = new MongoId(request.SessionId);
            }

            if (target.IsEmpty)
            {
                response = response with { Error = "缺少 sessionId（传 profile id 或先登录）" };
                return new ValueTask<string>(httpResponse.NoBody(response));
            }

            List<LinkFriends.PeerRef> peers = new(friends.Refresh(true));
            response = response with { VirtualFriends = peers.Count };

            var friendList = dialogue.GetFriendList(target);
            int injectedFriends = 0;
            if (friendList?.Friends != null)
            {
                foreach (var friend in friendList.Friends)
                {
                    if (peers.Exists(p => p.DialogId.ToString() == friend.Id.ToString()))
                    {
                        injectedFriends++;
                    }
                }

                response = response with
                {
                    FriendsTotal = friendList.Friends.Count,
                    InjectedFriends = injectedFriends
                };
            }

            var dialogues = dialogue.GenerateDialogueList(target);
            int injectedDialogues = 0;
            string sample = "";
            if (dialogues != null)
            {
                foreach (var item in dialogues)
                {
                    if (peers.Exists(p => p.DialogId.ToString() == item.Id.ToString()))
                    {
                        injectedDialogues++;
                        if (sample.Length == 0)
                        {
                            sample = item.Id.ToString();
                        }
                    }
                }

                response = response with
                {
                    DialoguesTotal = dialogues.Count,
                    InjectedDialogues = injectedDialogues,
                    SampleDialogId = sample
                };
            }

            if (sample.Length > 0 && peers.Exists(p => p.DialogId.ToString() == sample))
            {
                var view = dialogue.GenerateDialogueView(
                    new SPTarkov.Server.Core.Models.Eft.Dialog.GetMailDialogViewRequestData
                    {
                        DialogId = new MongoId(sample),
                        Limit = 100,
                        Type = SPTarkov.Server.Core.Models.Enums.MessageType.UserMessage
                    },
                    target);

                response = response with
                {
                    ViewMessages = view?.Messages?.Count ?? 0,
                    ViewProfiles = view?.Profiles?.Count ?? 0
                };
            }
        }
        catch (Exception ex)
        {
            response = response with { Error = ex.GetType().Name + ": " + ex.Message };
        }

        PluginLog.Info("[P2a] 自检 active=" + response.PatchActive + " peers=" + response.Peers + " 虚拟好友=" + response.VirtualFriends + " 好友总数=" + response.FriendsTotal + " 注入好友=" + response.InjectedFriends + " 对话总数=" + response.DialoguesTotal + " 注入对话=" + response.InjectedDialogues + " 样本=" + response.SampleDialogId + " 消息=" + response.ViewMessages + " profiles=" + response.ViewProfiles + " err=" + response.Error);
        return new ValueTask<string>(httpResponse.NoBody(response));
    }
}
