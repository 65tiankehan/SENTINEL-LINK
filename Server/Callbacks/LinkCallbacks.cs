using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;
using SentinelLink.Server.Models;
using SentinelLink.Server.Services;

namespace SentinelLink.Server.Callbacks;

[Injectable]
public sealed class LinkCallbacks(
    HttpResponseUtil httpResponse,
    LinkState state)
{
    /// <summary>P0 唯一入口：证明「客户端 → 本地服务端」这条链路通了，并回报 Hub 现状。</summary>
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
            HubState = state.HubState,
            HubUrl = state.HubUrl,
            Realm = state.Realm,
            Peers = state.Peers,
            LastHelloMs = state.LastHelloMs,
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Echo = request.Echo,
        };

        return new ValueTask<string>(httpResponse.NoBody(response));
    }
}
