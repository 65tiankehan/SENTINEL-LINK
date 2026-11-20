using SentinelLink.Server.Services;
using SentinelLink.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelLink.Server.Startup;

/// <summary>
/// 服务端开机钩子。P0 只做三件事：生成 serverId、初始化内存态、打一条横幅。
/// Hub 握手在 P0 第二步接进来（LinkHubClient）。
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class LinkServerLoad(
    ISptLogger<LinkServerLoad> logger,
    LinkState state) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        state.ServerId = Guid.NewGuid().ToString("N");
        state.HubState = "unlinked";

        logger.Success("========================================");
        logger.Success(" " + BuildInfo.ProductName + " Server 已加载");
        logger.Success("  serverId : " + state.ServerId);
        logger.Success("  realm    : " + state.Realm);
        logger.Success("  hub      : (未配置，P0 第二步接入)");
        logger.Success("========================================");

        return Task.CompletedTask;
    }
}
