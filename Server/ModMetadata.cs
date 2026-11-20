using SentinelLink.Shared;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SemanticRange = SemanticVersioning.Range;
using SemanticVersion = SemanticVersioning.Version;

namespace SentinelLink.Server;

/// <summary>
/// SPT 4.1.6 服务端模组必须提供 IModMetadata，否则加载器报
/// “Failed to load mod metadata … did you implement IModMetadata ?”。
/// GUID 是技术身份，永不随品牌改名。
/// </summary>
public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = BuildInfo.ServerGuid;

    public string Name { get; init; } = BuildInfo.ProductName + " Server";

    public string Author { get; init; } = "passionYang";

    public SemanticVersion Version { get; init; } = new(BuildInfo.Version);

    public SemanticRange SptVersion { get; init; } = new("~" + BuildInfo.SptVersion);

    public bool HasPrepatcher { get; init; } = false;

    public List<string>? Contributors { get; init; }

    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, SemanticRange>? ModDependencies { get; init; }

    public string? Url { get; init; }

    public string License { get; init; } = "Proprietary";
}
