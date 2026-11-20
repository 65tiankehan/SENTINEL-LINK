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

        /// <summary>私聊单条文本上限（字符），和聊天的 512 分开——私聊更长一点。</summary>
        public int MaxDirectLength { get; init; } = 1024;

        /// <summary>世界频道单条文本上限（字符），和 chat 一致。</summary>
        public int MaxWorldLength { get; init; } = 512;

        /// <summary>
        /// 每个 realm 每个频道（world / trade / lfg）保留多少条历史。
        /// 晚进来的实例如要看到最近讨论，靠的就是这份环形缓冲。
        /// </summary>
        public int WorldHistoryLimit { get; init; } = ReadInt("SENTINEL_HUB_WORLD_HISTORY", 100);

        /// <summary>每个收件人的离线私聊队列上限，超了丢最旧的。</summary>
        public int DirectQueueLimit { get; init; } = ReadInt("SENTINEL_HUB_DM_LIMIT", 500);

        /// <summary>离线私聊保留时长，过期的在投递时丢掉。</summary>
        public TimeSpan DirectTtl { get; init; } = TimeSpan.FromHours(168);

        /// <summary>
        /// 离线私聊的落盘目录。Hub 自己不存业务数据，但「离线消息不丢」这条承诺
        /// 必须活过 Hub 重启，所以队列单独写文件，重连时读走即删。
        /// </summary>
        public string SpoolDirectory { get; init; } =
            Environment.GetEnvironmentVariable("SENTINEL_HUB_SPOOL")
            ?? System.IO.Path.Combine(AppContext.BaseDirectory, "spool");

        private static int ReadInt(string name, int fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out int value) && value > 0 ? value : fallback;
        }
    }
}
