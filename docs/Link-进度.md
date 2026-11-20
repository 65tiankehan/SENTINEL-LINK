# SENTINEL Link 进度

> 一条一次，带数字。发版前把关键条目挪进计划书。

## 2026-11-20 · P0 第 1 步：仓库 + 服务端骨架

**做了什么**

1. 新建仓库 `D:\sptEdior\SENTINELLink`，目录：`Shared\`、`Server\{Routers,Callbacks,Models,Services,Startup}\`、`docs\`。
2. `Shared\BuildInfo.cs` 立版本真相源：`ProductName="SENTINEL Link"`、`Version="0.1.0"`、`SptVersion="4.1.6"`、`ServerGuid="cn.passionyang.sentinellink.server"`、`ClientGuid="cn.passionyang.sentinellink.client"`。
3. 服务端 mod 骨架：`SENTINEL.Link.Server.csproj`（net10.0，`<SptPath>` 默认 `E:\EFT\SPT_Runtime`，`Private=false` 引用 SPTarkov.Common/DI/Server.Core + SemanticVersioning）。
4. `ModMetadata : IModMetadata`（GUID 用 `BuildInfo.ServerGuid`，`SptVersion = ~4.1.6`）。
5. 路由 `/sentinelink/client/ping`（`StaticRouter` + `RouteAction<LinkPingRequest>`），返回 `LinkPingResponse`：product / version / spt / serverId / hubState / hubUrl / realm / peers / lastHelloMs / serverTime / echo。
6. `LinkState`（`[Injectable]` 内存态）+ `LinkServerLoad`（`[Injectable(TypePriority=OnLoadOrder.PostLoad)]`，`OnLoadAsync`）打开机横幅。

**踩到的两个坑（4.1.6 与老写法不同）**

- `IOnLoad` 的实现是 **`Task OnLoadAsync(CancellationToken)`**，不是 `OnLoad()`；写 `OnLoad()` 报 CS0535。
- `ISptLogger<T>` 在 **`SPTarkov.Common.Models.Logging`**（SPTarkov.Common.dll），不在 `SPTarkov.Server.Core.Models.Utils`。
- `logger.Success(msg, (Exception)null)` 的 null 实参会触发 CS8600（`Nullable enable`）→ 直接单参调用即可，4.1.6 的签名有默认值。

**实测**

| 项 | 值 |
|---|---|
| 构建 | `dotnet build -c Release -p:SptPath=E:\EFT\SPT_Runtime --no-incremental` → **0 个警告 0 个错误** |
| 产物 | `Server\bin\Release\net10.0\SENTINEL.Link.Server.dll` **24,576 B**，sha8 **CF8BEB4A** |
| 源码 | 服务端 6 个 .cs 共 171 行（含 ModMetadata / LinkState / LinkServerLoad / LinkRouter / LinkCallbacks / LinkModels） |

**没做（下一步）**

- Hub 侧一行没写：`/v1/hello` 准入、`/v1/ws` 长连、realm 广播。
- 服务端 `LinkHubClient` 出站握手与 20 s 心跳。
- 客户端插件（P1 才进厂）。
- 未部署到 `E:\EFT`（P0 第 2 步和 Hub 一起上）。
