using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SentinelLink.Server.Patches;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace SentinelLink.Server.Services;

/// <summary>
/// 存档哨兵：记录每个 profile 的对话条目数基线，保存前后各记一次。
///
/// 背景（2026-11-21）：真机测试期间 `6ae7b434…json` 曾在一次会话里从 24 条对话掉到 1 条
/// （526,317 B → 513,440 B），用 SPT 自带 backups 恢复。我们查过 SPT 核心里所有成批销毁对话的路径
/// （`RemoveAllDialoguesFromProfile` 全核心零调用、`RemoveDialogue` 只由客户端删除按钮触发），
/// 没能复现，于是改成"只要再掉就当场留证"：基线 + 每次保存前后计数 + 掉落即 warn 并写盘。
/// 正常构建下这里应该一条 warn 都不出。
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class ProfileGuard(SaveServer saveServer, ISptLogger<ProfileGuard> logger)
{
    private const string BaselineFileName = "profile-baseline.txt";
    private const string DropLogFileName = "profile-drop.log";

    private readonly object _sync = new();
    private readonly Dictionary<string, int> _baseline = new(StringComparer.OrdinalIgnoreCase);

    private static string ModDirectory()
    {
        try
        {
            return Path.GetDirectoryName(typeof(ProfileGuard).Assembly.Location) ?? AppContext.BaseDirectory;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    /// <summary>启动时给所有已加载 profile 记一次基线，并落盘（便于重启后比对）。</summary>
    public void CaptureBaseline()
    {
        try
        {
            Dictionary<string, int> snapshot = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<MongoId, SptProfile> pair in saveServer.GetProfiles())
            {
                int count = pair.Value?.DialogueRecords?.Count ?? 0;
                snapshot[pair.Key.ToString()] = count;
            }

            lock (_sync)
            {
                _baseline.Clear();
                foreach (KeyValuePair<string, int> pair in snapshot)
                {
                    _baseline[pair.Key] = pair.Value;
                }
            }

            StringBuilder sb = new();
            foreach (KeyValuePair<string, int> pair in snapshot)
            {
                sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            }

            File.WriteAllText(Path.Combine(ModDirectory(), BaselineFileName), sb.ToString(), new UTF8Encoding(false));
            logger.Info($"[profile] 基线已记录：{snapshot.Count} 个 profile（{Describe(snapshot)}）");
        }
        catch (Exception ex)
        {
            logger.Warning($"[profile] 记录基线失败：{ex.Message}");
        }
    }

    /// <summary>打一行计数；比基线少就 warn + 落盘（这就是我们要抓的现场）。</summary>
    public void Report(string tag, MongoId sessionId)
    {
        try
        {
            SptProfile profile = saveServer.GetProfile(sessionId);
            int count = profile?.DialogueRecords?.Count ?? -1;
            string key = sessionId.ToString();
            int baseline;
            lock (_sync)
            {
                _baseline.TryGetValue(key, out baseline);
            }

            if (count < 0)
            {
                return;
            }

            if (baseline <= 0)
            {
                lock (_sync)
                {
                    _baseline[key] = count;
                }

                logger.Info($"[profile] {tag} dialogues={count} baseline=（新建）");
                return;
            }

            if (count < baseline)
            {
                int virtualCount = CountVirtual(profile);
                string line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {tag} session={key} dialogues={count} baseline={baseline} 少 {baseline - count} 条 虚拟={virtualCount}";
                logger.Warning("[profile] 对话条目比基线少：" + line);
                try
                {
                    File.AppendAllText(Path.Combine(ModDirectory(), DropLogFileName), line + "\n", new UTF8Encoding(false));
                }
                catch
                {
                    // 写盘失败不影响主流程
                }

                lock (_sync)
                {
                    _baseline[key] = count;
                }

                return;
            }

            logger.Info($"[profile] {tag} dialogues={count} baseline={baseline}");
        }
        catch (Exception ex)
        {
            logger.Warning($"[profile] {tag} 检查失败：{ex.Message}");
        }
    }

    private static int CountVirtual(SptProfile? profile)
    {
        int virtualCount = 0;
        Dictionary<MongoId, Dialogue>? records = profile?.DialogueRecords;
        if (records == null)
        {
            return 0;
        }

        foreach (KeyValuePair<MongoId, Dialogue> pair in records)
        {
            if (LinkFriends.IsOurs(pair.Key))
            {
                virtualCount++;
            }
        }

        return virtualCount;
    }

    private static string Describe(Dictionary<string, int> snapshot)
    {
        StringBuilder sb = new();
        foreach (KeyValuePair<string, int> pair in snapshot)
        {
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(pair.Key[..Math.Min(8, pair.Key.Length)]).Append('=').Append(pair.Value);
        }

        return sb.ToString();
    }
}
