using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SentinelLink.Hub.Services
{
    /// <summary>一条投给离线对端的私聊。</summary>
    public sealed class DirectRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Realm { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string FromNick { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public long At { get; set; }
        public long ExpiresAt { get; set; }
    }

    /// <summary>
    /// 离线私聊暂存。每个收件人一个 JSONL 文件：`spool/{realm}/{serverId}.jsonl`。
    ///
    /// 为什么落盘而不是纯内存：P2 的验收是「离线消息补投 0 丢失」，如果只放内存，
    /// Hub 一重启这条承诺就破了。写入用追加，读取时整份读走并清空（投递即消费）。
    ///
    /// 目录/文件都在 Hub 进程自己的 `spool` 下，和 SPT 服务端的存档没有交集。
    /// </summary>
    public sealed class DirectSpool
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly object _sync = new();
        private readonly string _root;
        private readonly int _limit;
        private readonly TimeSpan _ttl;

        public DirectSpool(string root, int limit, TimeSpan ttl)
        {
            _root = root;
            _limit = Math.Max(1, limit);
            _ttl = ttl;
            Directory.CreateDirectory(_root);
        }

        public string Root => _root;

        /// <summary>入队一条；返回出队后该目标的待投条数（0 表示队列里只有这一条）。</summary>
        public int Enqueue(DirectRecord record)
        {
            lock (_sync)
            {
                string path = PathOf(record.Realm, record.To);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                List<DirectRecord> pending = ReadAll(path);
                pending.Add(record);

                // 超上限丢最旧的：宁可丢老消息，也不让一个离线号把磁盘吃穿
                if (pending.Count > _limit)
                {
                    pending.RemoveRange(0, pending.Count - _limit);
                }

                WriteAll(path, pending);
                return pending.Count;
            }
        }

        /// <summary>取走某个收件人的全部待投消息（读后清空，调用方负责真的送出去）。</summary>
        public List<DirectRecord> Drain(string realm, string to)
        {
            lock (_sync)
            {
                string path = PathOf(realm, to);
                List<DirectRecord> pending = ReadAll(path);

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                List<DirectRecord> alive = new List<DirectRecord>(pending.Count);
                foreach (DirectRecord record in pending)
                {
                    if (record.ExpiresAt <= 0 || record.ExpiresAt > now)
                    {
                        alive.Add(record);
                    }
                }

                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                    // 删不掉就下次再投一遍；重复投递比丢消息好
                }

                return alive;
            }
        }

        /// <summary>某个收件人当前待投条数（诊断用）。</summary>
        public int Count(string realm, string to)
        {
            lock (_sync)
            {
                return ReadAll(PathOf(realm, to)).Count;
            }
        }

        /// <summary>全局待投条数（/v1/health 里给一眼看得见的数）。</summary>
        public int TotalCount()
        {
            lock (_sync)
            {
                int total = 0;
                try
                {
                    foreach (string file in Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories))
                    {
                        total += ReadAll(file).Count;
                    }
                }
                catch (IOException)
                {
                    // 读不到就当 0，健康检查不该因为 spool 卡住
                }

                return total;
            }
        }

        public DirectRecord NewRecord(string realm, string from, string fromNick, string to, string text)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new DirectRecord
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                Realm = Sanitize(realm),
                From = from,
                FromNick = fromNick,
                To = to,
                Text = text,
                At = now,
                ExpiresAt = now + (long)_ttl.TotalMilliseconds
            };
        }

        private string PathOf(string realm, string to)
        {
            return Path.Combine(_root, Sanitize(realm), Sanitize(to) + ".jsonl");
        }

        private static List<DirectRecord> ReadAll(string path)
        {
            List<DirectRecord> list = new List<DirectRecord>();
            if (!File.Exists(path))
            {
                return list;
            }

            try
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        DirectRecord? record = JsonSerializer.Deserialize<DirectRecord>(line, Json);
                        if (record != null)
                        {
                            list.Add(record);
                        }
                    }
                    catch (JsonException)
                    {
                        // 半行（上次崩在写一半）——跳过，别让一条坏行堵住整个队列
                    }
                }
            }
            catch (IOException)
            {
                // 文件被占用：当作空队列，下次再来
            }

            return list;
        }

        private static void WriteAll(string path, List<DirectRecord> records)
        {
            StringBuilder builder = new StringBuilder();
            foreach (DirectRecord record in records)
            {
                builder.Append(JsonSerializer.Serialize(record, Json)).Append('\n');
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        /// <summary>目录名只能来自 serverId / realm，仍然清洗一遍防止路径穿越。</summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                bool ok = char.IsLetterOrDigit(c) || c == '-' || c == '_';
                builder.Append(ok ? c : '_');
            }

            return builder.ToString();
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "spool({0})", _root);
        }
    }
}
