using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelLink.Hub.Services
{
    /// <summary>Hub 眼里的一个 SPT 服务端。</summary>
    public sealed class HubPeer
    {
        public required string ServerId { get; init; }
        public required string Realm { get; init; }
        public string Nickname { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string SptVersion { get; set; } = string.Empty;
        public required string Token { get; init; }

        /// <summary>最后一次收到帧的时间（UtcNow.Ticks），清扫用。</summary>
        public long LastSeenTicks;

        /// <summary>WebSocket 可能还没连上（只调了 /v1/hello 的客户端）。</summary>
        public WebSocket? Socket;

        /// <summary>同一个 peer 的发送串行化，避免多线程写同一个 socket。</summary>
        public readonly SemaphoreSlim SendLock = new(1, 1);

        /// <summary>最后活跃时间的 Unix 毫秒（不是「从 0 起算的时长」，别写成 TimeSpan.TotalMilliseconds）。</summary>
        public long LastSeenMs =>
            (LastSeenTicks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
    }

    /// <summary>
    /// P0 的内存注册表：realm 内的 peer 列表 + 广播。不落盘，
    /// Hub 重启即清空 —— 真实部署时由各 SPT 服务端在加载时自动重连。
    /// </summary>
    public sealed class PeerRegistry
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // 中文昵称原样出线，方便抓包和日志直读；转义版虽然等价但没法肉眼排错。
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly ConcurrentDictionary<string, HubPeer> _peers =
            new(StringComparer.Ordinal);

        private readonly HubOptions _options;

        public PeerRegistry(HubOptions options)
        {
            _options = options;
        }

        public int Count => _peers.Count;

        public IReadOnlyList<string> Realms =>
            _peers.Values.Select(p => p.Realm).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>注册（或重新注册）一个 SPT 服务端，返回 peer 与其 token。</summary>
        public HubPeer Register(
            string serverId,
            string realm,
            string nickname,
            string version,
            string sptVersion,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(serverId))
            {
                error = "serverId 不能为空";
                return null!;
            }

            realm = string.IsNullOrWhiteSpace(realm) ? _options.DefaultRealm : realm;

            int inRealm = _peers.Values.Count(p =>
                string.Equals(p.Realm, realm, StringComparison.Ordinal));
            bool known = _peers.TryGetValue(serverId, out HubPeer? existing);
            if (!known && inRealm >= _options.MaxPeersPerRealm)
            {
                error = $"realm {realm} 已满（上限 {_options.MaxPeersPerRealm}）";
                return null!;
            }

            var peer = new HubPeer
            {
                ServerId = serverId,
                Realm = realm,
                Nickname = Sanitize(nickname, serverId),
                Version = version ?? string.Empty,
                SptVersion = sptVersion ?? string.Empty,
                Token = NewToken()
            };
            peer.LastSeenTicks = DateTime.UtcNow.Ticks;

            // 重连沿用旧 socket 对象会在旧连接上乱发，直接丢弃旧 peer。
            _peers[serverId] = peer;
            if (existing?.Socket is { State: WebSocketState.Open })
            {
                _ = CloseQuietly(existing);
            }

            return peer;
        }

        public HubPeer? Find(string serverId) =>
            _peers.TryGetValue(serverId, out HubPeer? peer) ? peer : null;

        public HubPeer? FindByToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            return _peers.Values.FirstOrDefault(p =>
                string.Equals(p.Token, token, StringComparison.Ordinal));
        }

        public void Remove(string serverId)
        {
            _peers.TryRemove(serverId, out _);
        }

        /// <summary>某个 realm 的在线列表（用于 hello 回包 / presence 广播）。</summary>
        public List<object> Snapshot(string realm)
        {
            return _peers.Values
                .Where(p => string.Equals(p.Realm, realm, StringComparison.Ordinal))
                .OrderBy(p => p.ServerId, StringComparer.Ordinal)
                .Select(p => (object)new
                {
                    serverId = p.ServerId,
                    nickname = p.Nickname,
                    version = p.Version,
                    sptVersion = p.SptVersion,
                    online = p.Socket is { State: WebSocketState.Open },
                    lastSeenMs = p.LastSeenMs
                })
                .ToList();
        }

        /// <summary>向 realm 内所有 peer 广播一帧；可选排除某个 serverId（不回声给发送者）。</summary>
        public async Task BroadcastAsync(
            string realm,
            object frame,
            string? excludeServerId = null)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
            List<HubPeer> targets = _peers.Values
                .Where(p => string.Equals(p.Realm, realm, StringComparison.Ordinal))
                .Where(p => p.Socket is { State: WebSocketState.Open })
                .Where(p => excludeServerId is null ||
                            !string.Equals(p.ServerId, excludeServerId, StringComparison.Ordinal))
                .ToList();

            foreach (HubPeer peer in targets)
            {
                await SendAsync(peer, bytes).ConfigureAwait(false);
            }
        }

        public async Task SendAsync(HubPeer peer, object frame)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
            await SendAsync(peer, bytes).ConfigureAwait(false);
        }

        private async Task SendAsync(HubPeer peer, byte[] bytes)
        {
            WebSocket? socket = peer.Socket;
            if (socket is not { State: WebSocketState.Open })
            {
                return;
            }

            await peer.SendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // 发送失败说明连接已断，清扫器会把它收走。
            }
            finally
            {
                peer.SendLock.Release();
            }
        }

        /// <summary>清扫超时 peer，返回被移除的列表（供广播 offline）。</summary>
        public List<HubPeer> Sweep()
        {
            long now = DateTime.UtcNow.Ticks;
            TimeSpan timeout = _options.PeerTimeout;
            var removed = new List<HubPeer>();

            foreach (KeyValuePair<string, HubPeer> pair in _peers)
            {
                HubPeer peer = pair.Value;
                bool timedOut = now - Interlocked.Read(ref peer.LastSeenTicks) >
                                timeout.Ticks;
                bool dead = peer.Socket is { State: WebSocketState.Open or WebSocketState.CloseReceived } == false &&
                            peer.Socket != null;
                if (!timedOut && !dead)
                {
                    continue;
                }

                if (_peers.TryRemove(pair.Key, out HubPeer? gone) && gone != null)
                {
                    removed.Add(gone);
                    _ = CloseQuietly(gone);
                }
            }

            return removed;
        }

        public void Touch(HubPeer peer)
        {
            Interlocked.Exchange(ref peer.LastSeenTicks, DateTime.UtcNow.Ticks);
        }

        public static string Sanitize(string? text, string fallback)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return fallback;
            }

            return value.Length > 32 ? value.Substring(0, 32) : value;
        }

        private static async Task CloseQuietly(HubPeer peer)
        {
            try
            {
                if (peer.Socket is { } socket &&
                    socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "replaced",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static string NewToken()
        {
            Span<byte> buffer = stackalloc byte[16];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToHexString(buffer).ToLowerInvariant();
        }

        public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    }
}
