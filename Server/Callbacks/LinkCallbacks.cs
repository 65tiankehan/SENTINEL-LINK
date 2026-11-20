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
    LinkHubClient hub)
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
}
