using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using SentinelLink.Server.Callbacks;
using SentinelLink.Server.Models;

namespace SentinelLink.Server.Routers;

/// <summary>
/// Link 的全部静态路由都挂在这里（P0 只有 ping）。
/// 客户端调用方式：POST /sentinelink/client/ping，body 为 LinkPingRequest。
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
        ])
{
}
