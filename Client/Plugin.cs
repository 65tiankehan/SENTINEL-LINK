using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using SentinelLink.Client.UI;
using SentinelLink.Shared;
using UnityEngine;

namespace SentinelLink.Client
{
    /// <summary>
    /// SENTINEL Link 客户端插件（P1）：只做一件事 —— 在游戏里开一个世界聊天面板，
    /// 内容来自本机 SPT 服务端的 /sentinelink/client/*，服务端再转发到 Hub。
    /// 决策/战斗相关的东西一行都不碰，出问题最多是面板不显示。
    /// </summary>
    [BepInPlugin(BuildInfo.ClientGuid, BuildInfo.ProductName, BuildInfo.Version)]
    [BepInProcess("EscapeFromTarkov.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyCode> ToggleKey;

        private void Awake()
        {
            Log = Logger;
            ToggleKey = Config.Bind(
                "General",
                "ToggleKey",
                KeyCode.F10,
                "世界聊天面板开关（默认 F10；F7 是 Min 面板、F9 是完整版菜单）");

            GameObject host = new GameObject("SENTINEL.Link.UI");
            DontDestroyOnLoad(host);
            host.AddComponent<LinkChatBehaviour>();

            Log.LogInfo($"{BuildInfo.ProductName} {BuildInfo.Version} 已加载 · {ToggleKey.Value} 打开世界聊天");
        }
    }
}
