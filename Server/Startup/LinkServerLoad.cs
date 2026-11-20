using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using SentinelLink.Server.Patches;
using SentinelLink.Server.Services;
using SentinelLink.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace SentinelLink.Server.Startup;

/// <summary>
/// 服务端开机钩子：生成 serverId、读 config.json、把 Hub 连接拉起来。
/// 连接本身是后台任务，这里只负责点火，绝不阻塞服务端启动。
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class LinkServerLoad(
    ISptLogger<LinkServerLoad> logger,
    LinkState state,
    LinkHubClient hub,
    LinkFriends friends) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        state.ServerId = ResolveServerId();
        state.HubState = "unlinked";

        PluginLog.InfoSink = message => logger.Info(message);
        PluginLog.WarnSink = message => logger.Warning(message);
        PluginLog.ErrorSink = message => logger.Error(message);

        string modDirectory = ResolveModDirectory();
        logger.Success("========================================");
        logger.Success(" " + BuildInfo.ProductName + " Server 已加载");
        logger.Success("  serverId : " + state.ServerId);
        logger.Success("  realm    : " + state.Realm);
        logger.Success("  hub      : " + state.HubUrl);
        logger.Success("========================================");

        try
        {
            hub.Start(modDirectory, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.Warning("[SENTINEL Link] Hub 连接启动失败：" + ex.Message);
        }

        ApplyDialoguePatches(friends);

        return Task.CompletedTask;
    }

    /// <summary>
    /// P2a：把对端注入原生好友/私聊。失败只降级（对端仍在我们自己的面板里），绝不崩服务端。
    /// </summary>
    private void ApplyDialoguePatches(LinkFriends friends)
    {
        DialoguePatchRuntime.Friends = friends;
        DialoguePatchRuntime.State = state;
        DialoguePatchRuntime.Hub = hub;

        try
        {
            Harmony harmony = new(BuildInfo.ServerGuid + ".p2a");
            int count = 0;
            foreach (Type type in new[]
                     {
                         typeof(GetFriendListInjectPatch),
                         typeof(GenerateDialogueListInjectPatch),
                         typeof(GenerateDialogueViewInjectPatch),
                         typeof(SendMessageToHubPatch),
                     })
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    count++;
                }
                catch (Exception ex)
                {
                    logger.Error("[P2a] 补丁 " + type.Name + " 失败：" + ex.Message);
                }
            }

            DialoguePatchRuntime.Active = count == 4;
            if (DialoguePatchRuntime.Active)
            {
                logger.Success("[P2a] 原生好友/私聊注入已启用（4/4）；虚拟 id 域标记 = " + LinkFriends.DomainTag);
            }
            else
            {
                logger.Warning("[P2a] 注入不完整（" + count + "/4），已降级：对端只出现在 SENTINEL Link 面板里");
            }
        }
        catch (Exception ex)
        {
            DialoguePatchRuntime.Active = false;
            logger.Error("[P2a] Harmony 初始化失败，已降级：" + ex.Message);
        }
    }

    /// <summary>
    /// serverId 要跨重启稳定，否则每重启一次 Hub 上就多一个幽灵对端。
    /// 存在 mod 目录下的 serverid.txt；写不了就退回进程内 GUID。
    /// </summary>
    private string ResolveServerId()
    {
        try
        {
            string path = Path.Combine(ResolveModDirectory(), "serverid.txt");
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (existing.Length >= 8)
                {
                    return existing;
                }
            }

            string created = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, created);
            return created;
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>mod 自己的目录（dll 所在处），不是 SPT_Runtime 根。</summary>
    private static string ResolveModDirectory()
    {
        try
        {
            string? location = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(location))
            {
                return Path.GetDirectoryName(location) ?? AppContext.BaseDirectory;
            }
        }
        catch
        {
            // 忽略，走兜底
        }

        return AppContext.BaseDirectory;
    }
}
