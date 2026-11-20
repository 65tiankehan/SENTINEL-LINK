using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using SentinelLink.Server.Models;
using SentinelLink.Server.Services;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace SentinelLink.Server.Patches;

/// <summary>
/// P2a：把 Hub 对端注入原生好友/私聊。
/// 四条补丁全部只改**返回值**或**短路**，不碰 SptProfile.DialogueRecords / FriendProfileIds。
/// 每个补丁单独 try/catch：任何一条挂掉只降级，绝不把服务端带崩。
/// </summary>
internal static class DialoguePatchRuntime
{
    public static LinkFriends? Friends { get; set; }

    public static LinkState? State { get; set; }

    public static LinkHubClient? Hub { get; set; }

    /// <summary>四条补丁是否都打上了；false 时对端只出现在我们自己的面板里。</summary>
    public static bool Active { get; set; }

    public static bool Ready => Active && Friends is { Enabled: true } && State != null;
}

[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.GetFriendList))]
internal static class GetFriendListInjectPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref GetFriendListDataResponse __result)
    {
        if (!DialoguePatchRuntime.Ready)
        {
            return;
        }

        try
        {
            if (__result?.Friends == null)
            {
                return;
            }

            HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
            foreach (UserDialogInfo friend in __result.Friends)
            {
                existing.Add(friend.Id.ToString());
            }

            foreach (LinkFriends.PeerRef reference in DialoguePatchRuntime.Friends!.Refresh())
            {
                if (existing.Contains(reference.DialogId.ToString()))
                {
                    continue;
                }

                __result.Friends.Add(DialoguePatchRuntime.Friends.BuildPeerUser(reference));
            }
        }
        catch (Exception ex)
        {
            DialoguePatchRuntime.Active = false;
            PluginLog.Error("[P2a] GetFriendList 注入失败，已降级：" + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.GenerateDialogueList))]
internal static class GenerateDialogueListInjectPatch
{
    [HarmonyPostfix]
    private static void Postfix(MongoId sessionId, ref List<DialogueInfo> __result)
    {
        if (!DialoguePatchRuntime.Ready)
        {
            return;
        }

        try
        {
            __result ??= new List<DialogueInfo>();

            HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
            foreach (DialogueInfo info in __result)
            {
                existing.Add(info.Id.ToString());
            }

            foreach (LinkFriends.PeerRef reference in DialoguePatchRuntime.Friends!.Refresh())
            {
                if (existing.Contains(reference.DialogId.ToString()))
                {
                    continue;
                }

                __result.Add(DialoguePatchRuntime.Friends.BuildDialogueInfo(sessionId, reference));
            }

            // 世界频道（P2b）：三个固定频道，永远在列表里（原生没有就造一个，服务端各自造、id 一致）。
            foreach (string channel in LinkFriends.WorldChannels)
            {
                string worldId = DialoguePatchRuntime.Friends.WorldDialogId(channel).ToString();
                if (existing.Contains(worldId))
                {
                    continue;
                }

                __result.Add(DialoguePatchRuntime.Friends.BuildWorldDialogueInfo(sessionId, channel));
            }
        }
        catch (Exception ex)
        {
            DialoguePatchRuntime.Active = false;
            PluginLog.Error("[P2a] GenerateDialogueList 注入失败，已降级：" + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.GenerateDialogueView))]
internal static class GenerateDialogueViewInjectPatch
{
    /// <summary>
    /// 必须 Prefix 短路：虚拟 dialogId 不在 DialogueRecords 里，走原生会在 Dialogue.get_Messages 上 NRE。
    /// </summary>
    [HarmonyPrefix]
    private static bool Prefix(GetMailDialogViewRequestData request, MongoId sessionId, ref GetMailDialogViewResponseData __result)
    {
        if (!DialoguePatchRuntime.Ready || request == null)
        {
            return true;
        }

        try
        {
            // 世界频道先判：同样是虚拟 id，但不在好友表里，走 BuildWorldView。
            if (DialoguePatchRuntime.Friends!.TryResolveWorld(request.DialogId, out string worldChannel))
            {
                __result = DialoguePatchRuntime.Friends.BuildWorldView(
                    sessionId, worldChannel, request.Limit ?? 100);
                return false;
            }

            if (!DialoguePatchRuntime.Friends!.TryResolve(request.DialogId, out LinkFriends.PeerRef reference))
            {
                // 解析不到但对端是我们派生的 id（例如刚被清扫）：绝不能放给原生 ——
                // GetDialogByIdFromProfile 会为未知 id 新建对话并写进 profile。
                if (LinkFriends.IsOurs(request.DialogId))
                {
                    __result = DialoguePatchRuntime.Friends.BuildEmptyView(sessionId);
                    return false;
                }

                return true;
            }

            __result = DialoguePatchRuntime.Friends.BuildView(sessionId, reference, request.Limit ?? 100);
            return false;
        }
        catch (Exception ex)
        {
            PluginLog.Error("[P2a] GenerateDialogueView 短路失败，交回原生：" + ex.Message);
            return true;
        }
    }
}

[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.SendMessage))]
internal static class SendMessageToHubPatch
{
    /// <summary>
    /// 发给虚拟好友时直接转 Hub（对方离线则由 Hub 落盘补投），不进 SendPlayerMessageToNpc，所以不写存档。
    /// </summary>
    [HarmonyPrefix]
    private static bool Prefix(SendMessageRequest request, ref ValueTask<string> __result)
    {
        if (!DialoguePatchRuntime.Ready || request == null || string.IsNullOrWhiteSpace(request.DialogId))
        {
            return true;
        }

        try
        {
            // 世界频道：转 Hub 的 world 帧（Hub 记历史并广播），本地先记一份让原生窗立刻可见。
            if (DialoguePatchRuntime.Friends!.TryResolveWorld(request.DialogId, out string worldChannel))
            {
                string worldText = request.Text ?? "";
                if (worldText.Length == 0)
                {
                    __result = ValueTask.FromResult(string.Empty);
                    return false;
                }

                LinkHubClient? worldHub = DialoguePatchRuntime.Hub;
                LinkState? worldState = DialoguePatchRuntime.State;
                if (worldHub == null || worldState == null)
                {
                    __result = ValueTask.FromResult(string.Empty);
                    return false;
                }

                worldState.AddWorld(new LinkWorldMessage
                {
                    Id = Guid.NewGuid().ToString("N")[..16],
                    Channel = worldChannel,
                    From = worldState.ServerId,
                    FromNick = worldState.Nickname,
                    Text = worldText,
                    At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Replayed = false,
                });

                _ = worldHub.SendWorldAsync(worldChannel, worldText, default);
                __result = ValueTask.FromResult(string.Empty);
                return false;
            }

            if (!DialoguePatchRuntime.Friends!.TryResolve(request.DialogId, out LinkFriends.PeerRef reference))
            {
                // 解析不到但对端是我们派生的 id（例如刚被清扫）：绝不能放给原生 ——
                // GetDialogByIdFromProfile 会为未知 id 新建对话并写进 profile。
                if (LinkFriends.IsOurs(request.DialogId))
                {
                    __result = ValueTask.FromResult(string.Empty);
                    return false;
                }

                return true;
            }

            string text = request.Text ?? "";
            if (text.Length == 0)
            {
                __result = ValueTask.FromResult(string.Empty);
                return false;
            }

            LinkHubClient? hub = DialoguePatchRuntime.Hub;
            LinkState? state = DialoguePatchRuntime.State;
            if (hub == null || state == null)
            {
                return true;
            }

            // 本地先记一份，面板读的是 LinkState，不依赖 Hub 回执。
            state.AddDirect(new LinkDirectMessage
            {
                Id = Guid.NewGuid().ToString("N")[..16],
                From = state.ServerId,
                FromNick = state.Nickname,
                To = reference.ServerId,
                Text = text,
                At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Replayed = false,
            });

            _ = hub.SendDirectAsync(reference.ServerId, text, default);
            __result = ValueTask.FromResult(string.Empty);
            return false;
        }
        catch (Exception ex)
        {
            PluginLog.Error("[P2a] SendMessage 转 Hub 失败，交回原生：" + ex.Message);
            return true;
        }
    }
}
