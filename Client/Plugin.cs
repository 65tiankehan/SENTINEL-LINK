using System;
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
    ///
    /// 注意：Update/OnGUI 直接写在 BaseUnityPlugin 子类上（和 SENTINEL Min 一样），
    /// 不要另建 GameObject 挂 Behaviour —— 那条路在 EFT 里实测收不到 Update。
    /// </summary>
    [BepInPlugin(BuildInfo.ClientGuid, BuildInfo.ProductName, BuildInfo.Version)]
    [BepInProcess("EscapeFromTarkov.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private LinkChatPanel _panel;

        private bool _updateLogged;

        private bool _drawLogged;

        private void Awake()
        {
            Log = Logger;

            ConfigEntry<KeyCode> toggleKey = Config.Bind(
                "General",
                "ToggleKey",
                KeyCode.F11,
                "世界聊天面板开关（F11 与 Ctrl+L 也始终可用；F7 是 Min 面板、F9 是完整版菜单）");

            _panel = new LinkChatPanel(toggleKey);

            Log.LogInfo($"{BuildInfo.ProductName} {BuildInfo.Version} 已加载 · {toggleKey.Value} / Ctrl+L 打开世界聊天");
        }

        private void Update()
        {
            if (!_updateLogged)
            {
                _updateLogged = true;
                Log.LogInfo("[Link] Update 已启动");
            }

            try
            {
                _panel.Tick();

                if (_panel.Visible && Input.GetKeyDown(KeyCode.Escape))
                {
                    _panel.Close();
                    Log.LogInfo("[Link] 面板 关闭（Esc）");
                }
            }
            catch (Exception ex)
            {
                Log.LogError("[Link] Update 异常：" + ex);
            }
        }

        private void OnGUI()
        {
            if (_panel == null || !_panel.Visible)
            {
                return;
            }

            try
            {
                _panel.Draw();

                if (!_drawLogged)
                {
                    _drawLogged = true;
                    Log.LogInfo($"[Link] 首次绘制 OK · 屏幕 {Screen.width}x{Screen.height} · scale {Screen.height / 1080f:0.00}");
                }
            }
            catch (Exception ex)
            {
                if (!_drawLogged)
                {
                    _drawLogged = true;
                    Log.LogError("[Link] 绘制异常：" + ex);
                }
            }
        }
    }
}
