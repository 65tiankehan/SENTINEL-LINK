using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using SentinelLink.Server.Callbacks;
using SentinelLink.Server.Models;

namespace SentinelLink.Server.Routers;

/// <summary>
/// Link 的全部静态路由都挂在这里。
/// 客户端调用方式：POST /sentinelink/client/&lt;action&gt;，body 为对应 Request 模型。
/// P0：ping（链路自检）/ chat/send（发一条）/ chat/state（拉增量）。
/// </summary>
[Injectable]
public sealed class LinkRouter(
    JsonUtil jsonUtil,
    LinkCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<LinkPingRequest>(
                "/sentinelink/client/ping",
                async (url, request, sessionId, output, cancellationToken) =>
                    await callbacks.Ping(url, request, sessionId)),

            new RouteAction<LinkChatSendRequest>(
                "/sentinelink/client/chat/send",
                async (url, request, sessionId, output, cancellationToken) =>
                    await callbacks.SendChat(url, request, sessionId)),

            new RouteAction<LinkChatStateRequest>(
                "/sentinelink/client/chat/state",
                async (url, request, sessionId, output, cancellationToken) =>
                    await callbacks.ChatState(url, request, sessionId)),

            new RouteAction<LinkDirectSendRequest>(
                "/sentinelink/client/dm/send",
                async (url, request, sessionId, output, cancellationToken) =>
                    await callbacks.SendDirect(url, request, sessionId)),

            new RouteAction<LinkDirectStateRequest>(
                "/sentinelink/client/dm/state",
                async (url, request, sessionId, output, cancellationToken) =>
                    await callbacks.DirectState(url, request, sessionId)),
        ])
{
}
