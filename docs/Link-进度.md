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

## 2026-11-20 · P0 第 2 步：Hub 上线 + 服务端接入 + 四跳跑通

**做了什么**

1. Hub 落地 `Hub\`：`SENTINEL.Link.Hub.csproj`（`Microsoft.NET.Sdk.Web`、net10.0）+ `Program.cs`（281 行）+ `Services\HubOptions.cs`（40 行）+ `Services\PeerRegistry.cs`（279 行）。
   - 三个端点：`GET /v1/health`、`POST /v1/hello`（邀请码准入 → 发 token）、`GET /v1/ws?token=`（长连）。
   - 帧协议：`welcome` / `presence` / `chat` / `ack` / `ping`→`pong`。聊天**不回声给发送方**，改回 `ack`（发送方的界面自己已经画过这条）。
   - 心跳：45 s 无帧判掉线，每 10 s 清扫一次并广播 `presence online=false`；单 realm 上限 64。
2. 服务端接入 `Server\Services\LinkHubClient.cs`（395 行）：读 mod 目录下 `config.json` → `/v1/hello` → `/v1/ws` → 收帧写进 `LinkState`；断线退避 2→4→8…→30 s 重连。
   - `serverid.txt` 存盘，跨重启保持同一个 serverId（否则 Hub 上会堆幽灵对端）。
3. 新路由两条：`/sentinelink/client/chat/send`（发一条）、`/sentinelink/client/chat/state`（拉 `sinceMs` 之后的增量 + 在线列表）；`LinkState` 存 100 条聊天环形缓冲。
4. `Tools\SyntheticPeer\`（179 行）：假对端，没有第二台机器时冒充「另一个玩家的 SPT 服务端」——hello / ws / 发一条 / 收帧打印。

**踩到的坑（4.1.6 的 DI 默认生命周期不是单例）**

- `[Injectable]` 默认是 **Transient**（Cecil 读 `SPTarkov.DI.dll` 确认 `InjectionType` 有 HostedService/Singleton/Transient/Scoped，特性上还有 `TypePriority`）。`LinkState` 与 `LinkHubClient` 各拿到一份状态，结果 hello 发出去 `serverId` 是空串 → Hub 400「serverId 不能为空」，而开机横幅明明打印了 serverId。
- 修法：`LinkState`、`LinkHubClient` 都显式写 `[Injectable(InjectionType.Singleton)]`。
- 另外 `lastSeenMs` 一开始算成 `TimeSpan.FromTicks(ticks).TotalMilliseconds`（约 6.4e13），改成 `(ticks - DateTime.UnixEpoch.Ticks) / TicksPerMillisecond` 才是 Unix 毫秒。

**实测（全部本机）**

| 项 | 值 |
|---|---|
| Hub 构建 | `dotnet build -c Release` → **0 个警告 0 个错误**；`SENTINEL.Link.Hub.dll` **45,568 B**，sha8 **F0455F1A** |
| 服务端构建 | `-p:SptPath=E:\EFT\SPT_Runtime` → **0 个警告 0 个错误**；`SENTINEL.Link.Server.dll` **54,272 B**，sha8 **574CABDA**（已部署到 `E:\EFT\SPT_Runtime\user\mods\cn.passionyang.sentinellink.server\`） |
| 假对端构建 | **0 个警告 0 个错误**；`SyntheticPeer.dll` **16,896 B**，sha8 **C76DF1E1** |
| Hub 健康 | `GET /v1/health` → `{"ok":true,"product":"SENTINEL Link","version":"0.1.0","sptVersion":"4.1.6","peers":1,"realms":["default"]}` |
| 假对端互转 | peer-a 收到 peer-b 的 `presence online=true` → `chat`（中文原样，不转义）→ peer-b 收到 `ack`；peer-a 退出后 peer-b 收到 `presence online=false` |
| 真服务端接入 | 开机日志 `[SENTINEL Link] 已连上 Hub http://127.0.0.1:5180（realm=default）` |
| 四跳 | 假对端 peer-c 发「四跳测试：我从假对端发一句话」→ Hub → 真 SPT 服务端日志打出 `[SENTINEL Link] 世界聊天 路人C: 四跳测试：我从假对端发一句话` |
| 路由注册 | `POST /sentinelink/client/chat/send` 与 `/chat/state` 返回 **200**，未注册路径返回 **404** |

**没做 / 下一步**

- `chat/send`、`chat/state` 目前**没有带 session 的完整回包验证**：SPT 4.1.6 开了 Web 认证，未登录调用返回 200 但空体（同样的空体在已上线的 SkyShieldTrainer 路由上也能复现，属认证层而不是我们的实现）。P1 客户端面板上线后走登录态一起验。
- P1：客户端 BepInEx 插件 + 世界聊天面板（复用 Min 的 `MinDraw` / `MinTheme`）。
- Hub 部署形态（单文件自托管 + 反向代理）与邀请码生成器，放到 P2 开头。

**怎么跑（本机）**

```powershell
# 1) Hub
cd D:\sptEdior\SENTINELLink\Hub
$env:SENTINEL_HUB_PORT="5180"; $env:SENTINEL_HUB_KEY="dev-local"
dotnet run -c Release --no-build

# 2) 假对端（另开一个窗口）
cd D:\sptEdior\SENTINELLink\Tools\SyntheticPeer
dotnet bin\Release\net10.0\SyntheticPeer.dll --hub http://127.0.0.1:5180 --server-id peer-b --nickname 路人B --chat "hello" --listen 5

# 3) 真 SPT 服务端：把 config.json 的 enabled 改 true、hubUrl 指向 Hub，正常启动 SPT.Server.exe
```

---

## P1 第 1 步 · 客户端世界聊天面板（2026-11-20）

**做了什么**

| 项 | 内容 |
|---|---|
| 新工程 | `Client\SENTINEL.Link.Client.csproj`（net48 BepInEx 插件，`AssemblyName=SENTINEL.Link.Client`） |
| 入口 | `Client\Plugin.cs`：`[BepInPlugin("cn.passionyang.sentinellink.client","SENTINEL Link","0.1.0")]` + `[BepInProcess("EscapeFromTarkov.exe")]`，热键 **F10**（F7 是 Min、F9 是完整版菜单） |
| 宿主 | `Client\UI\LinkChatBehaviour.cs`：DontDestroyOnLoad 的 GameObject，Update 处理键位、OnGUI 交给面板 |
| 面板 | `Client\UI\LinkChatPanel.cs`：右侧 436 宽卡片，头部状态胶囊 + realm/对端数，中部消息流（滚轮翻历史），底部输入框 + 发送，页脚提示 |
| 数据 | `Client\Core\LinkApi.cs`：`SPT.Common.Http.RequestHandler.PostJsonAsync` → `/sentinelink/client/chat/state`、`/chat/send`；后台线程轮询 2 s，结果加锁合并，OnGUI 里绝不 await |
| 模型 | `Client\Models\LinkClientModels.cs`：`LinkChatSnapshot / LinkPeer / LinkMessage / LinkSendResult`（Newtonsoft） |
| 复用 | `..\..\SENTINEL-Min\UI\MinDraw.cs`、`MinTheme.cs` 以 `<Compile Include>` 链接进本工程 —— 圆角/字号/配色与 Min 面板同一套，不复制副本 |

**构建与部署**

- `dotnet build .\Client\SENTINEL.Link.Client.csproj -c Release -p:SptClientDir=E:\EFT --no-incremental` → **0 警告 0 错误**
- `SENTINEL.Link.Client.dll` **29,184 B**，sha8 **4BF883FB**，FileVersion `0.1.0.0`（版本号从 `Shared\BuildInfo.cs` 读，不在 csproj 写第二份）
- 已部署两个实例：`E:\EFT\BepInEx\plugins\SENTINEL.Link\` 与 `E:\EFT-Fika\BepInEx\plugins\SENTINEL.Link\`（sha8 一致）

**属性自检（Cecil 直读成品 dll）**

- `[BepInPlugin("cn.passionyang.sentinellink.client", "SENTINEL Link", "0.1.0")]` ✔
- `[BepInProcess("EscapeFromTarkov.exe")]` ✔
- 类型：`Plugin / LinkChatBehaviour / LinkChatPanel / LinkApi / MinDraw / MinTheme / BuildInfo / 4 个 DTO`

**怎么试**

1. 起 Hub：`cd Hub; $env:SENTINEL_HUB_PORT="5180"; dotnet run -c Release --no-build`
2. 起 SPT 服务端 + 启动器，进主菜单
3. 游戏里按 **F10**：面板应从右侧滑出，头部胶囊显示「已连接 Hub」；若显示「未连接」，先看 `BepInEx\LogOutput.log` 有无 `[Link] state 拉取失败`
4. 用假对端发一条：`SyntheticPeer --hub http://127.0.0.1:5180 --server-id peer-b --nickname 路人B --chat "面板收到吗"` → 面板 2 s 内出现「路人B：面板收到吗」
5. 面板里输入文字回车 → 假对端（带 `--listen 8`）应打印 `chat` 帧；发送方自己收到的是 `ack`，不重复显示

**已知限制（P1 有意不做）**

- 输入框是 IMGUI `TextField`：中文在游戏内需要输入法接管，可能吞字；P2 换 TMP/系统输入框或快捷键预设消息。
- 面板打开时游戏仍响应鼠标/键盘（没有拦输入），会误触开火 —— P2 加「打开时锁输入」。
- 消息只有 `at/from/nickname/text`，没有频道概念（世界/好友/队伍），频道在 P2。
- 未读提示、@提及、表情、本地历史落盘都没有。

## P1 第 2 步（2026-11-20 23:18）：打开面板锁游戏输入 + 中文入口

前一步只解决了「画得出来」，这一步解决「能用」：面板打开时角色不再乱跑、点发送不再顺带开枪、中文有确定的输入路径。

**改了什么**

| 项 | 文件 | 做法 |
|---|---|---|
| 移动/转视角 | `Client\Patches\InputLockPatches.cs` | Harmony 前缀拦 `Player.Move(Vector2)`、`Player.Look(float,float,bool)`、`Player.Rotate(Vector2,bool)`，只在 `IsYourPlayer` 且面板打开时返回 false |
| 开火 | 同上 | `ClientFirearmController.CanPressTrigger()` 前缀在面板打开时 `__result = false` 并 return false |
| 光标 | `Client\UI\LinkChatPanel.cs` | 打开时记下 `Cursor.lockState/visible`，改成 `None/true`；关闭时原样还回去。EFT 自己每帧会重新锁光标，所以 `Tick()` 里每帧压回去 |
| 输入法 | 同上 | 打开时 `Input.imeCompositionMode = On`，关闭时还原 |
| 中文兜底 | 同上 | `Ctrl+V` 读 `GUIUtility.systemCopyBuffer` 插进输入框（IMGUI 吃不到候选词时的确定路径），页脚提示改成「回车发送 · Ctrl+V 粘贴中文 · Esc 关闭」 |
| 补丁挂载 | `Client\Plugin.cs` | `Awake` 里 `new Harmony(BuildInfo.ClientGuid).PatchAll(assembly)`，日志打「输入锁补丁已应用 · N 个方法」 |

**为什么用补丁而不是设标志**：EFT 的输入是每帧从 `GamePlayerOwner` 直读的，没有公开的「暂停输入」开关；唯一稳的做法是在真正吃输入的方法上拦一刀。只掐 `IsYourPlayer` —— 联机时别人走同一条基类方法，全局拦截会把队友冻住。

**两个编译器坑（都实测过）**

1. `Assembly-CSharp.dll` 里有一个**全局命名空间**的 `BuildInfo` 类型，它会静默压掉 `using SentinelLink.Shared;` 引入的 `BuildInfo` —— 报错形式是「BuildInfo 未包含 DisplayLine 的定义」，看起来像自己的类坏了。修法：调用点写全 `SentinelLink.Shared.BuildInfo`。
2. `using HarmonyLib;` 会带进 `HarmonyLib.BuildInfo`，与上面的全局类型叠加后连别名 `using BuildInfo = ...` 都报 CS0576。结论：这个仓库里一律全限定，不用别名。

**构建与部署**

- `dotnet build .\Client\SENTINEL.Link.Client.csproj -c Release -p:SptClientDir=E:\EFT --no-incremental` → **0 警告 0 错误**
- `SENTINEL.Link.Client.dll` **32,768 B**，sha8 **1049A161**
- 已覆盖 `E:\EFT` 与 `E:\EFT-Fika` 的 `BepInEx\plugins\SENTINEL.Link\`（两处 sha8 一致）

**Cecil 自检（成品 dll 里的补丁目标）**

```
Player_Move_InputLockPatch                       → EFT.Player | Move
Player_Look_InputLockPatch                       → EFT.Player | Look
Player_Rotate_InputLockPatch                     → EFT.Player | Rotate
ClientFirearmController_CanPressTrigger_...      → EFT.ClientFirearmController | CanPressTrigger
```

**怎么验**

1. 重启游戏（客户端 dll 换了），`BepInEx\LogOutput.log` 里应有 `[Link] 输入锁补丁已应用 · 4 个方法`
2. 进战局按 F11：**角色应立刻停住、视角锁死、鼠标能点到输入框**；在输入框里点一下发送按钮，不应该开枪
3. 别处打好中文 → 面板里 `Ctrl+V` → 输入框出现文字、页脚闪「已粘贴 N 字」→ 回车发送
4. 按 Esc 或 F11 关闭：角色恢复移动、视角恢复、光标重新被游戏锁住、`imeCompositionMode` 还原

**仍然没做**（留给 P2）

- 输入法候选词本身：IMGUI `TextField` 在 EFT 里候选窗不一定出，所以中文的**确定路径**是粘贴；要做「游戏内直接打中文」得换 TMP 输入或自己画候选条。
- 面板打开时聊天消息不滚动暂停、没有未读计数。
- `Player.Move`/`Look` 拦截是全局前缀：将来若别的 mod 也拦同一个方法，顺序由 Harmony 决定，需要时加 `[HarmonyPriority]` 明确。
---

## P2 第 1 步：私聊通道 + 离线补投（2026-11-20）

**做了什么**

| 层 | 文件 | 改动 |
|---|---|---|
| Hub | `Hub\Services\DirectSpool.cs`（新增） | 每收件人一个 JSONL：`spool/{realm}/{serverId}.jsonl`；入队超 500 条丢最旧、保留 7 天、投递即删（读后清空）。**落盘的原因**：P2 验收是「离线消息补投 0 丢失」，只放内存的话 Hub 一重启承诺就破了。 |
| Hub | `Hub\Services\HubOptions.cs` | 新增 `MaxDirectLength=1024`、`DirectQueueLimit`（env `SENTINEL_HUB_DM_LIMIT`，默认 500）、`DirectTtl=168h`、`SpoolDirectory`（env `SENTINEL_HUB_SPOOL`，默认 `bin/…/spool`）。 |
| Hub | `Hub\Program.cs` | `dm` 帧：收件人在本域且在线 → 直投并回 `delivered=true`；否则入队并回 `queued=true, pending=N`。`/v1/ws` 连上后先发 `dm-sync{count}`，再把队列逐条以 `replayed=true` 补投。`/v1/health` 增 `dmQueued`。 |
| 服务端 | `Server\Models\LinkModels.cs` | 新增 `LinkDirectMessage`、`LinkDirectSendRequest/Response`、`LinkDirectStateRequest/Response`。 |
| 服务端 | `Server\Services\LinkState.cs` | 私聊环形缓冲 `DirectHistoryLimit=200`、`AddDirect/SnapshotDirect/MarkDirectRead`、`Unread`、`SyncBatches/LastSyncCount`。 |
| 服务端 | `Server\Services\LinkHubClient.cs` | 处理 `dm` / `dm-sync` 帧（写 state + 落一行日志）；新增 `SendDirectAsync(to, text)`。 |
| 服务端 | `Server\Callbacks\LinkCallbacks.cs` | `SendDirect`（空收件人/发给自己直接拒；发出后本地也留一份，界面才画得出「我发的」）、`DirectState`（`markRead` 顺手清未读）。 |
| 服务端 | `Server\Routers\LinkRouter.cs` | 新路由 `/sentinelink/client/dm/send`、`/sentinelink/client/dm/state`。 |
| 工具 | `Tools\SyntheticPeer\Program.cs` | 新增 `--to/--count/--delay/--expect`：连发私聊、统计收到的 `dm` 帧、按期望条数对账（不符退出码 4）。 |

**实测（本机，Hub 5180）**

| 场景 | 结果 |
|---|---|
| 离线 100 条 | 甲给没上线的 `peer-offline` 连发 100 条 → `dmQueued=100`、队列文件 `peer-offline.jsonl` 17,492 B / 100 行；`peer-offline` 上线 → 收到 `dm` **100** 条 + `dm-sync` 1 次，对账 OK；队列文件投递后消失。 |
| 在线直投 | 甲 → 在线的乙 5 条 → 乙收到 5 条、全部 `replayed=false`、`dm-sync` 0 次、`dmQueued` 全程 0。 |
| Hub 重启不丢 | 给离线的丙发 3 条（队列 3 行）→ **杀掉 Hub 重编重起** → `dmQueued` 仍是 3 → 丙上线收到 3 条 `replayed=true`，对账 OK。 |
| 真服务端四跳 | 部署新 `SENTINEL.Link.Server.dll`（71,168 B / sha8 `842ED26E`）到 `E:\EFT\SPT_Runtime\user\mods\cn.passionyang.sentinellink.server\`，起 SPT 服务端 → 假对端甲发 3 条私聊给真服务端 `bc44f77d…` → 服务端日志逐条打出 `[SENTINEL Link] 私聊 甲: 私聊 #1/3 …`。 |
| 路由注册 | `POST /sentinelink/client/dm/state` → 200（未登录回包体为空，属 SPT Web 认证层），`/sentinelink/nope` → 404。**注意 SPT 后端是 https**，探针要 `-SkipCertificateCheck`。 |

**怎么跑**

```powershell
dotnet .\Hub\bin\Release\net10.0\SENTINEL.Link.Hub.dll
dotnet .\Tools\SyntheticPeer\bin\Release\net10.0\SyntheticPeer.dll --server-id peer-a --to peer-offline --count 100
dotnet .\Tools\SyntheticPeer\bin\Release\net10.0\SyntheticPeer.dll --server-id peer-offline --listen 8 --expect 100
```

**还没做**

- 客户端面板还没有私聊页（下一个 commit）：`LinkClientModels` 要加 `dm` 字段、面板加「世界 / 私聊」两个页签、未读红点。
- 好友名单（`friends.json`）还没落地：目前私聊靠手填对端 serverId，好友化之后才谈得上「登录即见朋友」。
- Hub 侧的 `dm` 没有限流（一个连接可以刷爆别人的队列），P2 收尾时加每连接每秒上限。