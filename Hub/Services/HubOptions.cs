using System;

namespace SentinelLink.Hub.Services
{
    /// <summary>
    /// Hub 运行参数。P0 阶段只用环境变量，避免过早引入配置文件格式；
    /// 部署到 VPS 时用 <c>SENTINEL_HUB_PORT</c> / <c>SENTINEL_HUB_KEY</c> 覆盖即可。
    /// </summary>
    public sealed class HubOptions
    {
        /// <summary>监听端口，默认 5180（不占 6969，避免和 SPT 服务端撞）。</summary>
        public int Port { get; init; } = ReadInt("SENTINEL_HUB_PORT", 5180);

        /// <summary>邀请码：所有 SPT 服务端接入必须带同一个值。</summary>
        public string InviteKey { get; init; } =
            Environment.GetEnvironmentVariable("SENTINEL_HUB_KEY") ?? "dev-local";

        /// <summary>没给 realm 时落到哪个域。</summary>
        public string DefaultRealm { get; init; } =
            Environment.GetEnvironmentVariable("SENTINEL_HUB_REALM") ?? "default";

        /// <summary>心跳超时：超过这么久没收到任何帧就判定掉线。</summary>
        public TimeSpan PeerTimeout { get; init; } = TimeSpan.FromSeconds(45);

        /// <summary>清扫间隔。</summary>
        public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>单个 realm 的并发上限，防止一个邀请码被刷爆。</summary>
        public int MaxPeersPerRealm { get; init; } = ReadInt("SENTINEL_HUB_MAX_PEERS", 64);

        /// <summary>单条聊天文本上限（字符）。</summary>
        public int MaxChatLength { get; init; } = 512;

        private static int ReadInt(string name, int fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out int value) && value > 0 ? value : fallback;
        }
    }
}
