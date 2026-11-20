# SENTINEL Link

SPT 4.1.6 的**联机 + 社交**套件。对外名 **SENTINEL Link**，技术身份 `SentinelLink.*`。

对手是 NekoParty / NekoNET（参考快照在 `D:\sptEdior\_ref\upstream\nekoNx`，ILSpy 反编译件）。
我们的差异点：**单人也能用的社交层，联机是加法不是前提** —— P0–P4 不需要 Fika、不需要玩家开端口、不改渲染。

路线与判据见 [docs/SENTINEL-Link-计划书.md](docs/SENTINEL-Link-计划书.md)，改动记录见 [docs/Link-进度.md](docs/Link-进度.md)。

## 架构

```
客户端插件 SENTINEL.Link.Client   BepInEx net48 · IMGUI 面板（复用 MinDraw/MinTheme）
        │ 只打本地 SPT 服务端（SPT.Common.Http，带 Bearer）
        ▼
服务端 mod SENTINEL.Link.Server   net10 · StaticRouter + [Injectable] + IOnLoad
        │ 出站一条长连
        ▼
中心 Hub   SENTINEL.Link.Hub      ASP.NET Core net10 · Kestrel · WS 广播 · SQLite
```

## 目录

| 路径 | 内容 |
|---|---|
| `Shared\BuildInfo.cs` | 唯一版本真相源（产品名 / 版本 / SPT 版本 / GUID） |
| `Server\SENTINEL.Link.Server.csproj` | 服务端 mod（net10.0，引用 `E:\EFT\SPT_Runtime` 的 SPT 程序集） |
| `Server\ModMetadata.cs` | SPT 4.1.6 硬性要求的 `IModMetadata` |
| `Server\Routers\LinkRouter.cs` | 全部静态路由（当前：`/sentinelink/client/ping`） |
| `Server\Services\LinkState.cs` | 服务端内存态（Hub 状态 / serverId / realm） |
| `Server\Startup\LinkServerLoad.cs` | 开机钩子（`OnLoadAsync`） |

## 构建

```powershell
cd D:\sptEdior\SENTINELLink\Server
dotnet build SENTINEL.Link.Server.csproj -c Release -p:SptPath=E:\EFT\SPT_Runtime --nologo
```

## 部署

```powershell
# 目标：E:\EFT\SPT_Runtime\user\mods\cn.passionyang.sentinellink.server\
```

## 当前状态

**P0 第 1 步完成**：服务端骨架可编译（0 警告 0 错误）、`/sentinelink/client/ping` 路由就绪、开机横幅。
下一步：Hub 最小服务（`/v1/hello` + `/v1/ws`）+ 服务端握手。
