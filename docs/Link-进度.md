# SENTINEL Link 进度

> 一条一次，带数字。发版前把关键条目挪进计划书。
## 当前状态（2026-11-21 11:05，随提交更新）

| 阶段 | 状态 | 提交 | 关键数字 |
|---|---|---|---|
| P0 骨架 | ✅ | `3bbfff2` | Hub health/hello/ws 通，四跳对账 OK |
| P1 世界聊天面板 | ✅ | `25ca19c` `93058de` `d36eb00` | 面板实收 10 条；宿主挂 `BaseUnityPlugin`；F11/Ctrl+L |
| P1.5 协议冻结 | ✅ | `55abcd5` | `docs\Hub-协议.md` 282 行，12 类帧 |
| P2b-1 Hub 私聊 + 落盘队列 | ✅ | `de4a7a3` | 离线 100/100 对账，重启不丢 |
| P2a 原生好友 + 私聊 | ✅ | `158344d` `32a5c61` | 好友 +1 / 对话 +1 / 消息 3~4；profile 字节差 0 |
| P2b-2 世界频道 | ✅ 真机验通 | 见 2026-11-21 11:00 段 | 3 频道渲染 + 可发送；`worldViewMessages=24`；profile 24 条不变 |
| P2b-2 局内入口 | ✅ 真机验通 | `dce6031` | 局内按 F11 拉起面板、光标可用、输入框发「3文档」状态「已发送」（11:17 截图，LOCAL PvE 战局内） |

**未验清单**

1. 对端**在线时**的即时投递（只验过离线排队 → 上线补投）；
2. 两个真实 SPT 实例跨服（只用假对端验过）；
3. 面板在线数修正后的真机显示（`_peerCount` 排除自己，未截图复验）；
4. ~~局内按 F11 能否拉起面板~~ ✅ 2026-11-21 11:17 真机验通；
5. 好友列表表头数字（显示 5、实列 6）—— 客户端缓存计数，下一轮对齐。

---

## 2026-11-21 · P2b-2 真机验收：世界频道两处根因 + 存档哨兵

**现象（用户 6 张真机截图）**

1. 会话列表里世界频道标题变成「世界 #21/24」（正文当标题），点开**消息区全黑**；
2. 「交易」「求组」连标题都没有；
3. 在世界窗口发消息 → 红字「你不能向这个用户发送消息。」

**根因一：消息类型用错（服务端）**

- 世界频道原来用 `MessageType.GlobalChat`；客户端正文流只渲染 `UserMessage`，`GlobalChat` 被过滤，预览也生不出来（于是退化成取最后一条正文当标题）。
- 改法：`LinkFriends.cs` 三处（`BuildWorldNativeMessage` 的 `MessageType`、`BuildWorldDialogueInfo` 的 `Type`、`MessagePreview.MessageType`）统一成 `MessageType.UserMessage`，与私聊完全同一套契约。
- 复测：`worldViewMessages=24`，截图里 `世界 #16/20 … #20/20` 正常滚动。

**根因二：发送闸门在客户端**

- `ChatShared.UpdatableChatDialogue::CanSendMessage(SocialNetwork, out string error)` 只查 `socialNetwork.FriendsList.Contains(Profile)`，拿不到就返回「你不能向这个用户发送消息。」（服务端根本没被调用）。
- 私聊能发是因为对端本来就在好友表里；三个频道只在对话列表、不在好友表。
- 改法：`GetFriendListInjectPatch` 里把三个频道成员也注入 `__result.Friends`（按 id 去重），`friendsTotal=5`。
- 复测：截图里「那个」发出成功、预览变「你: 那个」、「世界/交易/求组」出现在好友列表。

**顺手加的存档哨兵（`Server\Services\ProfileGuard.cs`）**

- 背景：09:26 那次会话里 profile 从 526,317 B / 24 条对话掉到 513,440 B / 1 条（已用 SPT 自带 backups 恢复）。查过核心所有成批销毁对话的路径：`RemoveAllDialoguesFromProfile` **全核心零调用**，`RemoveDialogue` 只由客户端删除按钮触发 → 未能复现。
- 做法：启动时给每个 profile 记基线（`profile-baseline.txt`），每次 `SaveServer.SaveProfileAsync` 前缀采样；比基线少就 `logger.Warning` + 追加写 `profile-drop.log`（带时间、session、少了几条、虚拟条目数）。
- 实测：`存档哨兵 1/1`、`基线已记录：2 个 profile（6ae7b434=24, 6af696c6=17）`、启动那次保存打 `保存前 dialogues=24 baseline=24`。
- Harmony 坑：Postfix 里挂 `Task.ContinueWith` 续体报 `IL Compile Error (unknown location)`，改成只在 Prefix 采样（足够区分「内存里就少了」与「写盘写丢了」）。

**收尾数字**

| 项 | 值 |
|---|---|
| Server dll | 116,224 B / `83cb7e09` |
| 补丁 | 4 对话 + 1 存档哨兵，0 警告 0 错误 |
| profile | 526,317 B / 24 条 / 虚拟 id 0 |
| 部署 | `E:\EFT` 与 `E:\EFT-Fika` 两处一致 |

---

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
| 入口 | `Client\Plugin.cs`：`[BepInPlugin("cn.passionyang.sentinellink.client","SENTINEL Link","0.1.0")]` + `[BepInProcess("EscapeFromTarkov.exe")]`，热键 **F11（`d36eb00` 起从 F10 改绑）**（F7 是 Min、F9 是完整版菜单） |
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
3. 游戏里按 **F11（`d36eb00` 起从 F10 改绑）**：面板应从右侧滑出，头部胶囊显示「已连接 Hub」；若显示「未连接」，先看 `BepInEx\LogOutput.log` 有无 `[Link] state 拉取失败`
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
---

## 计划修订：P2 拆成 P2a / P2b，新增 P1.5（2026-11-20）

**为什么改**：读了 nekoNx 的源码才发现，好友与私聊这两块**不需要自建 UI**——它的 `NekoNet.Server.Friends\Patches\GetFriendListPatch.cs` 是服务端 Harmony Postfix `DialogueController.GetFriendList`，把跨服对端拼成 `UserDialogInfo` 塞进 `__result.Friends`，原生好友面板自己就把人画出来了。我实测 4.1.6 的 `DialogueController`（`SPTarkov.Server.Core.dll`，29 个方法）签名齐全：`GetFriendList` / `GenerateDialogueList` / `GenerateDialogueView` / `SendMessage(MongoId, SendMessageRequest)` / `GetDialogueInfo` / `SetRead` 都在，所以这条路我们能走。

**改了什么**（`docs\SENTINEL-Link-计划书.md` 116 → 123 行 / 9,656 B）

| 改动 | 内容 |
|---|---|
| 路线表加「状态」列 | P0 `3bbfff2`、P1 `25ca19c`/`93058de`/`d36eb00` 标 ✅；P2b 标「面板已交付，缺私聊页签」 |
| 新增 **P1.5 协议冻结** | 8 类帧（`hello/welcome/presence/chat/ack/dm/dm-sync/ping`）写成 `docs\Hub-协议.md`，规则「只增字段不删、未知帧忽略」 |
| P2 拆 **P2a** | 服务端 patch `DialogueController` 四个方法，把跨服好友与消息造进原生 UI；跨服消息只落 `LinkState` + Hub spool，**不写 profile** |
| P2 拆 **P2b** | 自建面板只做原生做不到的：局内私聊、世界频道、诊断；后续照 neko 复制 `DefaultUIButton` 给背包注入入口 |
| P2a 新验收 | 好友出现 ≤ 3 s；离线补投 0 丢失；**存档不脏写**：profile `dialogues` 条目差 0、文件字节差 0 |
| P3 依赖改 | 从「P2」改成「P1.5」——报价同步不需要好友 UI，早做早验幂等表 |
| 风险表 +3 行 | patch 被打穿→降级回自建面板不崩游戏；写脏 `dialogues`→只走 LinkState/spool；32 hex→24 hex MongoId 派生冲突→前 20 位 + 4 位域标记 + 启动查重 |

**为什么不直接砍掉自建面板**：世界频道原生没有这个概念；局内原生没有聊天入口（neko 也得自己往背包塞按钮）。面板退成「补原生做不到的两块」，不跟任何 UI 打架。

## P2a 第 1 步：原生好友 / 私聊注入（2026-11-21 00:00）

把 Hub 对端注进游戏**原生**好友面板与私聊窗口，客户端一行 UI 都不用写。
侦察依据见 `docs\P2a-注入清单.md`（Cecil 直读 4.1.6 的 2,635 个类型，未跑游戏、未改存档）。

| 位置 | 改动 |
|---|---|
| `Server\Services\LinkFriends.cs`（新，322 行） | serverId(32 hex) → MongoId(24 hex) 派生（sha256 前 20 hex + 域标记 `1e0f`）、Aid 派生、虚拟好友/对话/视图装配；对端来源于「在线 peers ∪ 私聊历史发件人」，所以对方下线后消息仍在 |
| `Server\Patches\DialoguePatches.cs`（新，4 条补丁） | `GetFriendList` Postfix 追加好友；`GenerateDialogueList` Postfix 追加对话；`GenerateDialogueView` **Prefix 短路**（虚拟 id 不在 `DialogueRecords` 里，走原生必 NRE）；`SendMessage` **Prefix 拦截** → 转 Hub，不进 `SendPlayerMessageToNpc` |
| `Server\Services\PluginLog.cs`（新） | 给静态补丁用的日志转发（Info/Warn/Error 三个 Action） |
| `Server\Startup\LinkServerLoad.cs` | 注入 `LinkFriends`、装配 `DialoguePatchRuntime`、逐类打补丁（失败只降级并记 error） |
| `Server\Callbacks\LinkCallbacks.cs` | 新增 `P2aState`（自检：直接调原生控制器，回报注入数量） |
| `Server\Routers\LinkRouter.cs` | 新增 `POST /sentinelink/client/p2a/state` |
| `Server\Models\LinkModels.cs` | `LinkP2aStateRequest` / `LinkP2aStateResponse` |
| `Server\SENTINEL.Link.Server.csproj` | 加 `0Harmony`（`$(SptPath)\0Harmony.dll`，`<Private>false</Private>`） |

### 实测（本机，Hub 5180 + SPT.Server 6969 + SyntheticPeer）

| 项 | 数字 |
|---|---|
| 补丁 | 启动日志 `[P2a] 原生好友/私聊注入已启用（4/4）；虚拟 id 域标记 = 1e0f` |
| 好友注入 | `好友总数 3 / 注入好友 1`（原生 2 个 SPT 假人 + 我们对端） |
| 对话注入 | `对话总数 24 / 注入对话 1`，样本 id `cb98d304c62cd9195f521e0f`（长度 24、尾 `1e0f`） |
| 打开对话 | `消息 3 / profiles 2` —— 三条来自 `路人X5` 的私聊原样出现在原生视图里 |
| 对端离线 | 关掉 SyntheticPeer 后仍 `虚拟好友 1 / 注入好友 1`（走历史发件人兜底） |
| **存档不脏写** | 两个 profile `sha256` 前后完全一致（`2EFF5A89` / `26AF0796`，526,031 B / 447,532 B）；文件里搜不到任何虚拟 id |
| 构建 | `SENTINEL.Link.Server.dll` **93,696 B**（0 警告 0 错误） |

### 两个坑（已踩过，记下来）

1. **SPT 4.1.6 的请求体是 zlib 压缩的**：直接 POST 明文 JSON 会在 `SptHttpListener.HandleAsync` 抛
   `The archive entry was compressed using an unsupported compression method`（Critical），路由回 200 空包。
   探针必须 `ZLibStream` 压一遍；响应也是压缩的，`Invoke-WebRequest` 拿到的 `Content` 是乱码，别看它、看服务端日志。
2. **MongoId 必须正好 24 hex**：派生消息 id 时写成 22+4=26 位 → `ObjectId must be a 24-character hex string`，
   `GenerateDialogueList` 的 Postfix 整体抛异常被降级。改成 20+4 后恢复正常。

### 怎么复现

```powershell
# 1) Hub + 服务端
dotnet D:\sptEdior\SENTINELLink\Hub\bin\Release\net10.0\SENTINEL.Link.Hub.dll
E:\EFT\SPT_Runtime\SPT.Server.exe

# 2) 让一个假对端连上并给我们发 3 条
dotnet D:\sptEdior\SENTINELLink\Tools\SyntheticPeer\bin\Release\net10.0\SyntheticPeer.dll `
  --hub http://127.0.0.1:5180 --server-id peer-x5 --nickname 路人X5 --key dev-local `
  --to bc44f77d62f04c908ad73837b9d893c4 --count 3 --delay 200

# 3) 自检（body 必须 zlib 压缩）
#    结果看 E:\EFT\SPT_Runtime\user\logs\spt\spt*.log 的「[P2a] 自检 …」
```

### 还没做

- 游戏内眼睛验收：主菜单好友面板点开虚拟好友、发一条、看对方（假对端）是否收到 —— 需要能登录的客户端。
- `SendMessage` 拦截的**游戏内**验证（本次只证明了前缀补丁已挂上，未从 UI 触发）。
- 未读数（`Dialogue.New`）目前恒 0，接 P2b 面板时再补。
## P2a 第 2 步：真机 UI 验收 + 四处修正（2026-11-21 00:45）

真机上把「好友 → 对话 → 发送」整条链走通，期间暴露四个只在客户端侧才看得见的问题，全部已修：

| # | 现象 | 根因 | 修法 |
|---|---|---|---|
| 1 | 对话标题显示成自己（`本机实验服`），红字「你不能向这个用户发送消息」 | `BuildSelfUser` 用了**派生 id**，客户端拿真实 sessionId 认「谁是我」，两边都被当成别人 | 三方注入全部改用原生传进来的 `sessionId` |
| 2 | 历史消息一条都不画（只有自己刚发的条） | `Message.DateTime/UtcDateTime` 写成**毫秒**，原生 `dt` 是**秒**（profile 实测 `dt=1793661128`） | `item.At / 1000`，`MessagePreview.DateTime` 同步 |
| 3 | 标题显示 `wwe`（自己），发不出去 | 原生约定 `users[0]=对方 / users[1]=自己`（profile 实测 `turbodestroyer | wwe`），我写反了 | `Users`/`Profiles` 改 `{ 对方, 自己 }` |
| 4 | profile 里多出一条 `23041e039769da209d161e0f` 空对话（+138 B） | `DialogueController.GetDialogByIdFromProfile` 对**未知 dialogId** 会 `new Dialogue` 并写进 `DialogueRecords`；虚拟对端刚被 Hub 清扫、缓存解析不到时漏给了原生 | ① `IsOurs(dialogId)`（尾 4 位域标记）短路，绝不放行；② 启动时清理「带域标记且 users/messages 都空」的记录 |

- 关键教训：**客户端的对话数据契约必须照 profile 里的原生样本抄**（users 顺序、dt 单位、id 语义），照模型字段名猜会连错三次。
- 实测收敛：好友列表 `路人X5` ✓ → 列表 3 条预览 ✓ → 打开能看到 3 条对方消息 ✓ → 输入「可以」发送成功，Hub `spool/peer-x5.jsonl` 收到 `{"text":"可以"}`（对方离线，排队等补投）✓ → profile `525,509 B / 23 条对话 / 虚拟 id 命中 0` ✓。
- 产物：`SENTINEL.Link.Server.dll` **97,792 B / sha8 `0309F17B`**（`E:\EFT` 与 `E:\EFT-Fika` 两实例一致）。

## P1.5 第 1 步：协议冻结（2026-11-21 01:05）

把 Hub 与两端之间已经跑起来的帧，全部落成一份可依据的规范：`docs\Hub-协议.md`（新建，约 210 行）。

| 内容 | 说明 |
|---|---|
| 冻结规则 6 条 | 只增字段不删不改 / 未知帧忽略 / 未知字段忽略 / 可选字段必须能缺 / 时间统一毫秒 / id 统一规则 |
| 传输与认证 | `/v1/health`、`/v1/hello`（请求响应字段表 + 三种失败码）、`/v1/ws?token=`（401 纯文本）、15 s ping / 45 s 清扫 |
| 帧目录 v1 | Hub→端 7 帧（`welcome`/`presence`/`chat`/`dm`/`dm-sync`/`ack`/`pong`）、端→Hub 3 帧（`chat`/`dm`/`ping`），逐字段列类型与含义 |
| 边界上限表 | 聊天 512 / 私聊 1024 / 每人队列 500 / 保留 168 h / 超时 45 s / 清扫 10 s / 单域 64 |
| 状态机 | 未注册 → 已注册 → 在线；断线退避 2→4→8→16→30 s；token 失效走 401 重新 hello |
| 派生 id 铁律 | 虚拟对话/消息 id = sha256 前 20 hex + `1e0f`；并写明「GetDialogByIdFromProfile 对未知 dialogId 会写档」这个 P2a 踩过的坑 |
| 扩展流程 6 步 | 先改文档 → 新增 case 不改老分支 → 收端 default 忽略 → 缺字段按默认值 → 同提交更进度 → 跑回归 |
| 回归清单 | Hub 起 / 假对端在线直投对账 / 离线 100 条补投，验收看 `dmQueued` 归零与「对账 OK」 |

- 文档里的每条错误文案都与 `Hub\Program.cs` / `PeerRegistry.cs` 的实际字符串逐字对过（`邀请码错误` / `serverId 不能为空` / `realm <域> 已满（上限 N）` / `缺少收件人或正文`）。
- 本次只写文档，**没有改代码**：v1 靠字段集识别，未加显式 `proto` 字段；真要协商版本时按扩展流程加，旧端会当未知字段忽略。

## 竞品研究：nekoNx 全量摸底（2026-11-21 01:20）

用户提了很对的一点：不该一问一答地摸，先把 neko 整体搞清楚。新建 `docs\neko-研究.md`（约 200 行）。

| 章节 | 内容 |
|---|---|
| 画像与规模 | 反编译产物 588 文件 / 客户端 7 插件 / 服务端 11 mod / 客户端打点 41 处 / 服务端 33 个补丁类 / 24 条路由 |
| 架构 | 客户端 → 本机 SPT 服务端 → Hub ← 别人 SPT；客户端从不直连 Hub |
| 七模块逐条 | Core / Friends / PrivateChat / WorldChat / MarketGlow / Airdrop / GameState / GroupEX，各自打点、数据流、可借鉴点 |
| 值得抄 8 条 | Postfix 造数据不写档 / 复用原生 UI / 放宽输入校验 / 离线队列补投 / serverId 落盘 / Action 事件解耦 / 全 patch try-catch / 配置集中 |
| 不该学 5 条 | 替换 Fika MatchMaker UI / 依赖 GClassNNN / 塞 profile / 无保活长连 / patch SaveServer.GetProfile |
| 4.1.6 对照表 | `SocialNetworkClass→EFT.SocialNetwork`、`ChatMessageClass→DialogueChatMessage`、`GClass1059→ChatRoomInformation`、`GDelegate33_0→ChatSendDelegate`、`GClass1628→UpdatableBindableList` |
| 取舍 | 做世界频道/私聊/跳蚤/空投；不做 P2P 打洞、竞技场、MatchMaker 替换；Hub 本体不在仓库，得自己补 |

- 世界频道结论：neko **复用客户端原生聊天列表**（造 `type=9 GlobalChat` 的 pinned 会话 + `DisplayMessage` 塞消息），不重画 UI；我们 4.1.6 的 public API 反而更干净（`DisplayMessage` 是公开方法，neko 那段 `SafeDisplay` 反编译不出来）。
- 下一步仍按 B2：服务端虚拟 `world` 会话，复用 P2a 的四个补丁；实现前先把每个模块的细节按需再深挖（这次只到「能判断可行性 + 能估工」的深度）。

## P2b-2 第 1 步 · 世界频道走原生聊天窗（B2，2026-11-21 01:20）

方案选择：**服务端造虚拟 `GlobalChat` 会话**（复用 P2a 的 id 派生与 Prefix 短路），客户端零 UI —— 不照 neko 在客户端伪造会话，少一半打点。

| 层 | 改动 |
|---|---|
| Hub | 新增 `Hub\Services\WorldLog.cs`（每 realm 每频道环形 100 条）；`world` / `world-sync` 两帧；`HubOptions.MaxWorldLength=512`、`SENTINEL_HUB_WORLD_HISTORY`；连上后先补历史再补私聊 |
| 服务端 | `LinkState.AddWorld/SnapshotWorld`（环形 200）；`LinkFriends` 增三频道 id 派生（`sha256("sentinel.link.world:"+realm+":"+channel)` 前 20 hex + `1e0f`）+ `BuildWorldUser/BuildWorldNativeMessage/BuildWorldDialogueInfo/BuildWorldView`；`DialoguePatches` 三处补丁各加世界分支（列表追加 / View 短路 / 发送转 Hub）；路由 `/world/send`、`/world/state` |
| 启动 | `LinkServerLoad` 打出三个频道 id 并做反解自检 |
| 工具 | `SyntheticPeer` 增 `--world <channel>`、`--world-expect <n>`，并统计收到的 `world` 帧数 |

实测（Hub 5180 + `SENTINEL.Hub` + `SPT.Server` 6969 + 假对端）：

| 场景 | 结果 |
|---|---|
| 在线广播 | `peer-w2` 向 `trade` 连发 3 条 → 3 条 `ack{delivered:true}`；旁听 `peer-w1` 收 3 条 `world` |
| 历史补投 | 晚连的 `peer-w4` 收到 `world-sync{world:2}` + 2 条 `replayed`、`world-sync{trade:3}` + 3 条 `replayed`，共 5 条 |
| 服务端加载 | `[P2b] 世界频道 world → 9e4a7a209dc0ae832cf41e0f 反解OK`、`trade → 3bb25dadb43573a623881e0f`、`lfg → eb4d5f229226f0d500f71e0f`（均 24 位、尾 `1e0f`） |
| 原生注入 | `/sentinelink/client/p2a/state`：`worldInjected=3`、`dialoguesTotal=26`（23 真实 + 3 频道）、`worldViewMessages=2`、`worldViewProfiles=2` |
| 历史进链路 | `/sentinelink/client/world/state` 返回 2 条（`from=peer-w3`，`replayed=true`） |
| 存档 | profile `6ae7b434…` 无任何 `*1e0f` 键（脏写 0）；新出现的 0 消息对话 `6723fd51c5924c57ce0ca01e`（Commando，aid 2999998）不带域标记，是 SPT 自己建的 |

构建：Hub / Server / SyntheticPeer **0 警告 0 错误**；`SENTINEL.Link.Server.dll` 111,616 B、sha8 `9270fb7b`，已刷 `E:\EFT` 与 `E:\EFT-Fika`。

两个坑：

1. 用“按行插入到某方法之前”改 `LinkCallbacks.cs` 时插在 `SendDirect` 的**签名行**上，把参数表劈开 → 后续插入要选「方法整体之后」的锚点（这里用 `DirectState`），或先 `git checkout` 复原再重插。
2. PowerShell 里多行字符串数组写 C# 片段，`'                },'` 这类行会因引号/括号组合被解析器判成语法错 → 改用 `@'…'@` here-string 再 `-split "`r?`n"`。

还没做：①局内入口（背包界面塞一个聊天按钮，原生局内没有）；②客户端面板加「世界」页签（现在只有原生窗）；③两个真实实例跨服验（只用假对端验过）。

## 2026-11-21 · 安装器（Tauri 2，照 Min 那套）

| 项 | 值 |
| --- | --- |
| 工程 | `installer/`（`scripts/build-payload.ps1` + `src-tauri` + `ui/index.html`） |
| 载荷 | `payload.zip` **178,250 B**，12 个条目（客户端 dll+pdb / 服务端 dll+package.json+config.json / Hub 6 个文件 + `启动 Hub.cmd`） |
| 安装器 | `dist/SENTINEL-LINK-Setup.exe` **3,121,152 B**，sha256 `93852192F97CCDE92FA100E603D6D5AA61DFA274BAE9857ABD8F1A5C9556670D` |
| 内嵌校验 | EOCD 反推起点 `0x22D744`、长度 178,250、`PK\x03\x04` 签名正确；内嵌 zip sha256 与 `payload.zip` **逐字节一致** |
| 窗口 | 520×448、无边框透明、Win10 走窗口区域裁圆角、WebView2 缩放强制 1.0 |
| 卸载 | 删客户端插件 + 服务端模组 + Hub 三个目录，保留服务端 `config.json`；写 `HKCU\...\Uninstall\SENTINEL LINK` |

- 版本号唯一真相源仍是 `Shared/BuildInfo.cs`：`build-payload.ps1` 正则读它，写进服务端 `package.json` 与 Hub 说明；`tauri.conf.json` / `Cargo.toml` 跟 0.1.0。
- Hub 以**框架依赖**方式发布（69 KB dll + 162 KB exe），装机需要 .NET 10 运行时；`启动 Hub.cmd` 会先 `where dotnet`，缺了给下载地址而不是闪退。
- 踩坑：Hub 的 `bin\Release\...\SENTINEL.Link.Hub.dll` 被正在跑的 Hub 进程锁定 → `MSB3027/MSB3021`，必须先停 Hub 再 publish（本次停 9868 → publish → 起 15544，游戏侧服务端 2 秒内自动重连）。
- 踩坑：`src-tauri/payload/` 目录在干净 clone 里不存在，`ZipFile.CreateFromDirectory` 报 `Could not find a part of the path ...payload.zip` → 脚本已改成先 `New-Item -Force` 建目录（本次是手工补建后通过）。
- UI 截图：`docs/design/link-installer-ui.png`（520×448，Edge headless 渲 `installer/ui/index.html`）。

## 2026-11-21 · 文档收尾（0.1.0 发布准备）

| 文档 | 改了什么 |
| --- | --- |
| `README.md` | 状态表补 P2b-2 ✅、安装器 ✅ `fcffaa7`、发布稿 ✅；未验清单加到三条（在线即时投递 / 真实双实例跨服 / 好友栏计数含频道） |
| `docs/SENTINEL-Link-计划书.md` | 路线表新增「**0.1.0 发布**」行（安装器 + 发布稿，tag/release 待发）+ 2026-11-21 修订说明 |
| `docs/发布稿-Link-0.1.0.md` | 新建，138 行 / 6,924 B：论坛帖正文（含标题格式 `【SPT4.1.6】`、标签、原理表、安装三步、已知限制、下载 sha256） |
| `docs/design/link-installer-ui.png` | 安装器第一屏截图 520×448（Edge headless 渲 `installer/ui/index.html`） |
| `docs/Link-进度.md` | 本节 |

发布稿里的数字全部现查核对：安装器 **3,121,152 B**、sha256 `93852192F97CCDE92FA100E603D6D5AA61DFA274BAE9857ABD8F1A5C9556670D`；载荷 **178,250 B / 12 条目**；内嵌 zip 与 `payload.zip` 逐字节一致（EOCD 反推起点 `0x22D744`）。

> 还没做的两件：`link-0.1.0` 的 git tag + release 附件；仓库推远端（本地仓库目前**无 remote**，发布稿里写的是「地址回帖里补」）。

## 2026-11-21 · 开源准备（MIT + sp-mod）

| 项 | 值 |
| --- | --- |
| 仓库 | `https://github.com/65tiankehan/SENTINEL-LINK`（local remote 已设为 origin） |
| 协议 | `LICENSE`（MIT，Copyright 2026 65tiankehan (passionYang)），1,081 B |
| README | 125 行 / 7,195 B：头部补「仓库 + MIT」，末尾补「开源与发布」「许可证」两节 |
| 发布稿 | 145 行 / 7,435 B：仓库段换成真实地址 + 三份契约文档指引 |
| sp-mod 素材 | `docs/sp-mod-上传信息.md`：标题/标签/兼容表/两个文件的 sha256/变更日志/上传前检查清单 |
| 发布包 | `installer/dist/SENTINEL-LINK-0.1.0.zip` **189,187 B**，sha256 `C06CD24A928902A21D764E6430B92C805EEE28DBDC9667718A89E390FBA87AFA`（载荷 + LICENSE + README + Hub-协议） |

- 二次开发的护栏写进 README 了：帧契约只增字段不删、虚拟 id 必须保持 `sha256 前 20 位 + 1e0f`，否则会和别人的实现撞车。
- 还没做：`git push -u origin master`（等用户发话）、`link-0.1.0` tag、GitHub release 附件上传（安装器 + zip）。

### 0.1.0 发版结果（2026-11-21 12:43）

| 项 | 值 |
| --- | --- |
| 分支 | `master` 已 push（19 提交 / 57 文件），跟踪 `origin/master` |
| Tag | `link-0.1.0`（annotated），已 push |
| Release | <https://github.com/65tiankehan/SENTINEL-LINK/releases/tag/link-0.1.0> |
| 资产 | `SENTINEL-LINK-Setup.exe` 3,121,152 B / `SENTINEL-LINK-0.1.0.zip` 189,187 B，digest 与本地 sha256 一致 |
| 仓库 | PUBLIC、MIT、默认分支 master；description 与 7 个 topics 已设 |
