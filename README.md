# SENTINEL Link

SPT 4.1.6 的**联机 + 社交**套件。对外名 **SENTINEL Link**，技术身份 `SentinelLink.*`，GUID 前缀 `cn.passionyang.sentinellink`。

对手是 NekoParty / NekoNET（研究笔记：[docs/neko-研究.md](docs/neko-研究.md)）。
我们的差异点：**单人也能用的社交层，联机是加法不是前提** —— 不需要 Fika、不需要玩家开端口、不改渲染。

仓库：<https://github.com/65tiankehan/SENTINEL-LINK> · 协议：**MIT**（见 [LICENSE](LICENSE)）
路线与判据见 [docs/SENTINEL-Link-计划书.md](docs/SENTINEL-Link-计划书.md)，帧规范见 [docs/Hub-协议.md](docs/Hub-协议.md)，改动记录见 [docs/Link-进度.md](docs/Link-进度.md)。

## 当前状态（2026-11-21）

| 阶段 | 状态 | 证据 |
|---|---|---|
| P0 骨架（Hub hello/ws/presence + 服务端握手 + 假对端） | ✅ | `3bbfff2` |
| P1 世界聊天（IMGUI 面板，F11） | ✅ | `25ca19c` `93058de` `d36eb00` |
| P1.5 协议冻结（`docs/Hub-协议.md`） | ✅ | `55abcd5` |
| P2b-1 Hub 私聊通道 + 落盘队列 | ✅ | `de4a7a3`（离线 100/100 对账） |
| P2a 原生好友 + 私聊（4/4 补丁，存档 0 字节变化） | ✅ | `158344d` `32a5c61` |
| P2b-2 世界频道走原生聊天窗 + 局内入口 | ✅ | `426c6b3` `dce6031`（真机：24 条历史 + 自己发 1 条；局内 F11 可发） |
| 0.1.0 安装器（Tauri 2：客户端插件 + 服务端模组 + 本地 Hub） | ✅ | `fcffaa7`（exe 3,121,152 B / 载荷 178,250 B / 内嵌 zip 逐字节一致） |
| 安装器界面多语言 + 三套主题（中/英/俄 × 暖金/暗黑/亮白） | ✅ | `a5f0c33`（exe 3,126,272 B / 界面与 F9 菜单同一套色） |
| 0.1.0 发布稿（论坛帖 + 已知限制） | ✅ | `docs/发布稿-Link-0.1.0.md` 138 行 / 6,924 B |
| P3 跳蚤远程报价 / P4 局内空投 / P5 组队 | ⬜ | — |

**未验**：①对端在线时的即时投递（只验过离线排队 → 上线补投）；②两个真实 SPT 实例跨服（只用假对端验过）；③好友栏计数把三个频道算进去了（显示 5，实际 6 行）。

## 架构

```
客户端插件 SENTINEL.Link.Client   BepInEx net48 · 原生聊天窗（P2a）+ IMGUI 面板 F11 · Ctrl+L
        │ 只打本地 SPT 服务端（SPT.Common.Http，带 sessionId）
        ▼
服务端 mod SENTINEL.Link.Server   net10 · StaticRouter + [Injectable Singleton] + IOnLoad + Harmony
        │ 出站一条 WebSocket 长连（15 s ping，断线退避 2→30 s）
        ▼
中心 Hub   SENTINEL.Link.Hub      ASP.NET Core net10 · Kestrel · 内存 peer 表 + spool/*.jsonl
```

- Hub **没有数据库**（P0–P2 都是内存 + JSONL 落盘），SQLite 留到 P3 报价索引再上。
- 服务端配置是**明文** `config.json`（`SENTINEL_HUB_KEY` 等密钥放 Hub 侧环境变量），尚未做加密。

## 目录

| 路径 | 内容 |
|---|---|
| `Shared\BuildInfo.cs` | 唯一版本真相源（产品名 / 版本 / SPT 版本 / GUID） |
| `Hub\` | Hub 服务：`Program.cs`（`/v1/health` `/v1/hello` `/v1/ws`）、`Services\PeerRegistry.cs`、`Services\DirectSpool.cs`、`Services\HubOptions.cs` |
| `Server\` | SPT mod：`Routers\LinkRouter.cs`（`/sentinelink/client/{ping,chat/send,chat/state,dm/send,dm/state,p2a/state}`）、`Services\{LinkState,LinkHubClient,LinkFriends,PluginLog}.cs`、`Patches\DialoguePatches.cs`（P2a 四个补丁）、`Startup\LinkServerLoad.cs` |
| `Client\` | BepInEx 插件：`Plugin.cs`（宿主 + 热键）、`UI\LinkChatPanel.cs`、`Core\LinkApi.cs`、`Patches\InputLockPatches.cs`（锁移动/视角/开火） |
| `Tools\SyntheticPeer\` | 假对端（`--to/--count/--expect` 可对账） |
| `Tools\FikaProbe\` | 静态扫 Harmony 补丁目标，算与 Fika 的交集 |
| `docs\` | 计划书 / Hub 协议 / P2a 注入清单 / neko 研究 / 进度 |

## 构建

```powershell
cd D:\sptEdior\SENTINELLink
dotnet build .\Hub\SENTINEL.Link.Hub.csproj         -c Release --nologo
dotnet build .\Server\SENTINEL.Link.Server.csproj  -c Release -p:SptPath=E:\EFT\SPT_Runtime --nologo
dotnet build .\Client\SENTINEL.Link.Client.csproj  -c Release -p:SptClientDir=E:\EFT --nologo
dotnet build .\Tools\SyntheticPeer\SyntheticPeer.csproj -c Release --nologo
```

## 部署

```powershell
# 服务端：两实例都刷（主实例 + Fika 测试实例）
copy Server\bin\Release\net10.0\SENTINEL.Link.Server.dll `
     E:\EFT\SPT_Runtime\user\mods\cn.passionyang.sentinellink.server\
copy Server\bin\Release\net10.0\SENTINEL.Link.Server.dll `
     E:\EFT-Fika\SPT_Runtime\user\mods\cn.passionyang.sentinellink.server\

# 客户端插件：两个实例
copy Client\bin\Release\SENTINEL.Link.Client.dll E:\EFT\BepInEx\plugins\SENTINEL.Link\
copy Client\bin\Release\SENTINEL.Link.Client.dll E:\EFT-Fika\BepInEx\plugins\SENTINEL.Link\
```

`mods\...\config.json`（服务端，首次启动自动生成）：

```json
{ "enabled": true, "hubUrl": "http://127.0.0.1:5180", "inviteKey": "dev-local",
  "realm": "default", "nickname": "本机实验服" }
```

## 怎么用

| 动作 | 键 |
|---|---|
| 打开/关闭自建面板 | **F11** / `Ctrl+L` |
| 关闭 | `Esc` |
| 发送 | `Enter`（中文用 `Ctrl+V` 粘贴，IMGUI 吃不到输入法） |
| 原生聊天 | 走游戏内聊天窗（P2a 注入的好友与私聊） |

## 已知限制

- 面板打开时锁角色移动/视角/开火（只锁本地玩家，不影响联机队友）。
- 原生私聊消息只存 `LinkState` + Hub spool，**不写 profile**；profile 里出现 `…1e0f` 结尾的空对话即视为脏写，启动时会自动清理。
- Hub 重启会丢在线表与聊天历史（私聊 spool 不丢）。
## 安装（给玩家）

`installer/dist/SENTINEL-LINK-Setup.exe` 一次装三样：客户端插件、服务端模组、本地 Hub。

1. 双击安装器，选游戏根目录（含 `EscapeFromTarkov.exe` 的那层）
2. 装完重启游戏；主菜单底栏「消息」里就有好友和世界频道
3. 要联机时先跑游戏目录里的 `SENTINEL-Link-Hub\启动 Hub.cmd`（需要 .NET 10 运行时）

联别人的服：改 `SPT_Runtime\user\mods\cn.passionyang.sentinellink.server\config.json` 的 `hubUrl` / `inviteKey` / `realm`。

安装器源码在 `installer/`（Tauri 2 + WebView2，520×448 无边框）；自己出包：

```powershell
pwsh -File installer\scripts\build-payload.ps1 -SptClientDir "E:\EFT"
cd installer\src-tauri; cargo build --release --offline
```

## 开源与发布

最新版本：**[link-0.1.0](https://github.com/65tiankehan/SENTINEL-LINK/releases/tag/link-0.1.0)**

| 资产 | 字节 | sha256 |
| --- | --- | --- |
| `SENTINEL-LINK-Setup.exe` | 3,126,272 | `D5239C9F5BDD83B545DD7110B03B881727CC1AF49A25E4ECD333263B91FC9F80` |
| `SENTINEL-LINK-0.1.0.zip` | 189,893 | `3A2C84024DCFE5B1A41559D3ED58AEDFE65B859EE195689493B6976112559DAF` |

- 仓库：<https://github.com/65tiankehan/SENTINEL-LINK>（MIT，欢迎 issue / PR）
- 安装包：本仓库 release 的 `SENTINEL-LINK-Setup.exe`（界面自带 中/英/俄 与 暖金/暗黑/亮白 三套主题，右上角两个小胶囊切换）；sp-mod 页面同步上传同一份 zip
- 三个组件可单独取用：只想连别人的 Hub 就只装客户端插件；自己做 Hub 起 `Hub/`；只写服务端逻辑看 `Server/Services/LinkFriends.cs` 与 `Patches/DialoguePatches.cs`
- 二次开发请注意：`docs/Hub-协议.md` 是帧契约（只增字段不删、未知帧忽略），改协议要同步升版本；虚拟 id 必须保持 `sha256 前 20 位 + 1e0f` 的派生规则，否则会和别人的实现撞车

## 许可证

MIT。用、改、商用都行，保留版权声明即可。游戏资产（EFT / SPT 本体）不在本仓库内，遵循各自的许可。
