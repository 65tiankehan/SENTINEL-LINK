using System;

namespace SentinelLink.Shared
{
    /// <summary>
    /// Link 版唯一版本真相源。对外名 SENTINEL Link，技术身份 SentinelLink。
    /// 其它文件不允许出现版本字面量（csproj / 安装器用正则读这里）。
    /// 本文件不引用 UnityEngine / BepInEx / EFT，服务端与 Hub 共用。
    /// </summary>
    public static class BuildInfo
    {
        /// <summary>对外产品名（只出现在玩家可见处：面板标题、安装器、帖子）。</summary>
        public const string ProductName = "SENTINEL Link";

        /// <summary>产品版本。Link 走独立 0.x 线。</summary>
        public const string Version = "0.1.0";

        /// <summary>当前适配的 SPT 服务端版本。</summary>
        public const string SptVersion = "4.1.6";

        /// <summary>服务端 mod 技术身份（不随品牌改动）。</summary>
        public const string ServerGuid = "cn.passionyang.sentinellink.server";

        /// <summary>客户端 BepInEx 插件 GUID（P1 起启用）。</summary>
        public const string ClientGuid = "cn.passionyang.sentinellink.client";

        /// <summary>面板副标题。</summary>
        public const string DisplayLine = "SPT " + SptVersion + " · 联机社交";

        /// <summary>一行式诊断描述。</summary>
        public static string Describe()
        {
            return ProductName + " " + Version + " / SPT " + SptVersion;
        }

        /// <summary>时间戳，失败返回空串（永不抛异常）。</summary>
        public static string Timestamp()
        {
            try { return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { return string.Empty; }
        }
    }
}
