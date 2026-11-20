using EFT;
using HarmonyLib;
using UnityEngine;

namespace SentinelLink.Client.Patches
{
    /// <summary>
    /// 面板打开时把「本地玩家」的移动/转视角/开火掐住。
    ///
    /// 为什么要打补丁而不是设个标志：EFT 的输入是每帧从 GamePlayerOwner 直读的，
    /// 没有公开的「暂停输入」开关；唯一稳的做法是在真正吃输入的方法上拦一刀。
    ///
    /// 只掐 IsYourPlayer —— 联机时别人的 ObservedPlayer 走同一条基类方法，
    /// 全局拦截会把队友也冻住。
    /// </summary>
    internal static class LinkInputLock
    {
        /// <summary>面板是否占据输入。由 LinkChatPanel 在开/关时翻转。</summary>
        public static bool Locked;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Move))]
    internal static class Player_Move_InputLockPatch
    {
        // ReSharper disable once InconsistentNaming
        private static bool Prefix(Player __instance)
        {
            return !(LinkInputLock.Locked && __instance != null && __instance.IsYourPlayer);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Look))]
    internal static class Player_Look_InputLockPatch
    {
        // ReSharper disable once InconsistentNaming
        private static bool Prefix(Player __instance)
        {
            return !(LinkInputLock.Locked && __instance != null && __instance.IsYourPlayer);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Rotate))]
    internal static class Player_Rotate_InputLockPatch
    {
        // ReSharper disable once InconsistentNaming
        private static bool Prefix(Player __instance)
        {
            return !(LinkInputLock.Locked && __instance != null && __instance.IsYourPlayer);
        }
    }

    /// <summary>
    /// 开火闸门：ClientFirearmController.CanPressTrigger() 返回 false 时扳机不响应。
    /// 鼠标点在输入框上时，鼠标左键同时也会被游戏读到 —— 没有这一刀就会边打字边开枪。
    /// </summary>
    [HarmonyPatch(typeof(ClientFirearmController), nameof(ClientFirearmController.CanPressTrigger))]
    internal static class ClientFirearmController_CanPressTrigger_InputLockPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!LinkInputLock.Locked)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
