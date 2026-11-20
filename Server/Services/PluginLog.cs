using System;

namespace SentinelLink.Server.Services;

/// <summary>
/// 极薄的日志转发：Harmony 补丁是静态方法，拿不到 DI 的 ISptLogger，
/// 启动时把 ServerLoad 的 logger 塞进来即可，避免到处传参。
/// </summary>
public static class PluginLog
{
    public static Action<string>? InfoSink { get; set; }

    public static Action<string>? WarnSink { get; set; }

    public static Action<string>? ErrorSink { get; set; }

    public static void Info(string message) => InfoSink?.Invoke(message);

    public static void Warn(string message) => WarnSink?.Invoke(message);

    public static void Error(string message) => ErrorSink?.Invoke(message);
}
