# nekoNx 研究笔记（截至 2026-11-21）

> 对象：`D:\sptEdior\_ref\upstream\nekoNx` —— **反编译产物**（JetBrains/ILSpy 导出，`// Assembly location: D:\sptEdior\NekoNET\…`），不是原仓库，不能直接编译。
> 用途：SENTINEL Link 的竞品对标。本文只记录**能验证的事实**与**可复用的手法**，不猜。

## 1. 一句话画像

neko 是「在 Fika 之上再包一层」的**联机社交 + 跨服交易**方案：客户端 7 个 BepInEx 插件，服务端 11 个 SPT mod，中间挂一个中心 **Hub**（HTTP + WebSocket）。
玩家不在同一个 Fika 会话里也能聊天、看在线、买卖东西、收空投 —— 因为数据都过 Hub。

## 2. 规模

| 项 | 数 |
|---|---|
| 文件 | 588（537 个 .cs，1.60 MB 源码） |
| 客户端 | `Airdrop` 26 文件 / 1,501 行 · `Core` 22 / 1,218 · `Friends` 6 / 245 · `GameState` 12 / 951 · `GroupEX` 134 / 7,874 · `MarketGlow` 15 / 898 · `WorldChat` 26 / 2,303 |
| 服务端 | 296 文件 / 21,435 行（11 个 mod：`NekoNet.Server.{Core,Airdrop,Friends,GameState,Market,PrivateChat,Profiles,WorldChat}` + `NekoParty.Server.{Core,GroupEX}`） |
| Harmony 打点 | 客户端 **41 处**（`HarmonyPatch(typeof…)`）、服务端 **33 个补丁类**（`AbstractPatch` + `AccessTools`） |
| HTTP 路由 | **24 条**（`/nekoparty/*` 14 条、`/nekonet/*` 10 条） |
| 目标框架 | 服务端 `v9.0`（**给 SPT 4.0.x 写的**，我们 4.1.6 是 net10.0） |

## 3. 架构

```
┌── 客户端（BepInEx 7 插件）──┐        ┌── 本机 SPT 服务端（11 mod）──┐        ┌── Hub（闭源/不在仓库）──┐
│ WorldChat / Friends /      │ HTTP   │ 路由 /nekoparty/* /nekonet/* │  HTTP  │  /hello 发 token        │
│ PrivateChat / MarketGlow / │───────►│ 服务端 patch 造数据          │───────►│  /ws 长连、广播          │
│ Airdrop / GameState / Core │◄───────│ （存档只读，虚拟数据只放内存）│◄───────│  realm + offline spool   │
└────────────────────────────┘        └──────────────────────────────┘        └─────────────────────────┘
                                            ▲                                    ▲
                                            └────────── 其他玩家的 SPT ──────────┘
```

- 客户端只跟**自己本机的 SPT 服务端**说话（`SPT.Common.Http.RequestHandler`，自带 sessionId），没有直连 Hub 的代码 —— Hub 只被 SPT 服务端连。
- 服务端 `HubClient`（HTTP + Bearer token）+ `HubWsClient`（WebSocket 长连）+ `HubConnection.EnsureAuthenticated()`。

## 4. 七个模块逐条

### 4.1 Core（骨架）

- 客户端 `NekoParty.Core`：`NekoPartyCorePlugin : BaseUnityPlugin`，`[BepInPlugin("com.nekoparty.core")]`、`Harmony.PatchAll`。
- 局内生命周期：`GameWorld.OpenGameStarted` / `GameWorld.Dispose`（Postfix）→ `NekoRaidLifecycle.OnGameStarted/OnGameEnded` → 再广播给其它模块。
- 服务端 `NekoState`：`AccountId/Token/RealmHash/Authenticated/LastSyncServerTime` + 市场同步的一堆映射表。
- `NekoHooks`：四个 `Action` 事件（`PlayerOfferCreated`、`PlayerProfilePublishRequested`、`HubRawMessage`、`HubConnected`）—— **服务端 mod 之间靠这个解耦**，值得抄。
- 配置：`NekoConfig` 一个文件管全部模块开关（`enableFriends/enableWorldChat/enableGameState/enablePrivateChat/enableAirdrop/enableParty/enableCrossRaid` + 各自心跳/轮询间隔），`config.dat` 用硬编码 AES key 加密，`config.json` 明文。

### 4.2 Friends（好友）

- 服务端：`DialogueController.GetFriendList` **Postfix** —— 把 Hub 上的远端好友拼成 `UserDialogInfo` 按 id 去重塞进 `__result.Friends`。
- 配套 5 个补丁：`SendFriendRequest`（Prefix）、`DeleteFriend`（Prefix）、`AcceptFriendRequest`/`CancelFriendRequest`/`DeclineFriendRequest`/`ListInbox`/`ListOutbox`（Prefix，`DialogueCallbacks`）、`ProfileController.SearchProfiles`（Postfix，跨服搜人）。
- **关键**：只改**返回值**，`FriendProfileIds` 一个字不写 → 存档干净。

### 4.3 PrivateChat（私聊）

- 服务端：`DialogueCallbacks.SendMessage` **Prefix** —— 目标是远端好友时拦下来转 Hub；`SptWebSocketConnectionHandler.OnConnection` **Postfix** —— 玩家上线的瞬间把离线队列补投。
- `PrivateChatService`：`Hello` / 离线队列 / 心跳。
- 与我们 P2a/P2b 几乎同构（我们甚至已经跑通 UI）。

### 4.4 WorldChat（世界频道）★ 最有借鉴价值

- 客户端 10 处补丁，核心是 `ChatScreen.Show` Postfix：记住 `ChatScreen` 实例、把 `_chatInputField` 的 `regex_1 → AllowAll("[\\s\\S]*")`、`regex_2 → StripNone("(?!)")`、`characterValidation → None`（**这是中文能输入的关键**）。
- `EnsureChannels()` 造 3 个 pinned 会话：

```csharp
new GClass1059 { Id = "nekoworld", type = 9 /* GlobalChat */, Name = "世界", pinned = true, Users = new UpdatableChatMember[0] }
social.CreateDialogue(info, false);                       // 进原生会话列表
social.GDelegate33_0 = new GDelegate33(null, __methodptr(OnGlobalSend));  // 劫原生发送框
social.DisplayMessage(new ChatMessageClass{...}, dialogue);               // 塞消息进原生列表
```

- 三个频道：`世界` / `交易` / `求组`。
- 局内：`InventoryScreen.Show` Postfix 复制原生 `_backButton` 造按钮（`InventoryScreenRaidChatPatch`），点开把 `ChatScreen` 当浮层 `Show(social, info, itemController, NoopTransfer, NoopOffer)`。
- 服务端：`WorldChatService{Hello, HistoryForClient, RosterForClient, SendFromClient}` + 3 条路由 `history/roster/send`。
- 表情：自带 `EmojiRenderer`（13.6 KB）+ `WorldChatEmojiViewPatch`。

### 4.5 MarketGlow（跳蚤市场）

- 客户端 6 处补丁（`AddOfferWindow.Show`、`FiltersPanel.Show`、`OfferView.Awake`、`OfferViewList.method_8`、`RagfairCategoriesPanel.Show`、`RagFairClass.method_7`）—— 给真实玩家报价**高亮**、加筛选、加买卖模式开关。
- 服务端 5 处补丁 + 2 条路由 `/nekonet/market/{onlyrealplayers,sellmode}`：`GetOffers` Postfix 注入远端报价、`AddPlayerOffer` Postfix 把本地挂单推到 Hub、`BuyPmcItemFromRagfair` Prefix 拦跨服购买、`ProcessOffersOnProfile`/`FlagOfferForRemoval` 维护幂等。
- 这是我们 **P3 的正面对手**：它做在 Ragfair 里，我们也做在 Ragfair 里，谁做得稳谁赢。

### 4.6 Airdrop（局内空投）

- 客户端 5 处补丁：`MessageView.Show/Close`（把空投画成一条带物品的消息）、`ItemUiContext.GetItemContextInteractions`（右键取货）、`NotificationManagerClass.method_7`（通知栏）、`LightScroller.Update`（修滚动）。
- 服务端 `AirdropService{CreateFromClient, CreateFromRaidClient, ClaimFromClient, RaidFailed, RaidReceived, ProcessPollCycle}` + 4 条路由。
- 思路：**空投不是凭空进背包，而是先变成一封带 Items 的 `Message`**，玩家在原生邮件界面点「领取」才进档 —— 存档写入路径完全走 SPT 原生的。

### 4.7 GameState（局内状态）

- 客户端 3 处补丁 + 服务端 `GameStateService{ReportFromClient, SyncSnapshot}` + `/nekonet/gamestate/report`。
- 用途：把「我在哪张图、什么装备、打到哪」推给好友列表显示。

### 4.8 GroupEX（组队 / 跨局，134 文件、7,874 行，最大的模块）

- 服务端 8 处补丁：`MatchService.{CreateMatch, AddPlayerToMatch, RemovePlayerFromMatch, DeleteMatch, SetMatchHost}`、`RaidController.HandleRaidCreate`、`UpdateController.HandleSetStatus`、`SaveServer.GetProfile`（Scav 档兜底）。
- 14 条路由：组队创建/邀请/接受/拒绝/踢人/转移点/阵营同步/局设置同步。
- 客户端：`NekoPartyP2PProbe`（NAT 打洞，用 Fika 的 LiteNetLib）、`NekoNatTypeDetector`、`NekoFikaCompat`、`MixedSide`、`NekoRewindSnapshot*`（局内回滚：AI/尸体/地面物品快照）、`NekoRecoverySnapshot*`（断线恢复）、Arena（竞技场）模型。
- **它 patch 掉 Fika 自带的 party 面板**（`NekoPartySuppressFikaMatchmakerPartyPanelPatch`），换成自己的 MatchMaker UI（59 KB 的 `NekoPartyMatchMakerController`）。
- 这是它最重、最容易随 EFT/Fika 版本崩的部分。

## 5. 关键工程手法（值得抄的 8 条）

1. **服务端 Postfix 造数据，不写存档** —— 好友、对话、市场报价全是返回值注入，`FriendProfileIds`/`DialogueRecords` 一个字节不动。
2. **客户端复用原生 UI** —— 世界频道/私聊/空投都用原生 `ChatScreen`、`MessageView`，不重画。
3. **输入校验放宽**（`regex_1/regex_2/characterValidation`）—— 中文输入法能用的前提。
4. **离线队列落盘 + 上线即补投**（`SptWebSocketConnectionHandler.OnConnection` Postfix）。
5. **`serverId` 落盘稳定**（`serverid.txt`），重启不换身份。
6. **模块间用 `Action` 事件解耦**（`NekoHooks`），一个模块挂了不影响其它。
7. **所有 patch 都 try/catch 且开关可关**（`NekoConfig.enable*`），单模块失效不崩游戏。
8. **配置集中一个文件**，文档里能一眼看出每个模块的节流参数。

## 6. 我们不该学的 5 条

1. **改 Fika 的 MatchMaker 面板** —— 它用 134 个文件去替换 Fika UI，版本一变全崩；我们 P5 只做「轻同步」或直接复用 Fika 界面。
2. **依赖反编译类名**（`GClass1059`、`GClass1628<T>`、`GClass2266`）—— EFT 每次更新重编号，必须用类型特征（接口/字段）定位。
3. **把跨服状态塞 profile** —— 它靠补丁绕开，但一旦漏路径就脏档（我们 P2a 已经踩过 `GetDialogByIdFromProfile` 写档）。
4. **无保活的长连** —— 我们踩过：不 ping 45 秒就被清。
5. **服务端直接 patch `SaveServer.GetProfile`** 这类核心路径 —— 一旦签名变，整个 mod 起不来。

## 7. 旧版 → 我们 4.1.6 的 API 对照

| neko 用的名字（旧版 EFT / SPT 4.0） | 我们 4.1.6 的等价物 | 备注 |
|---|---|---|
| `SocialNetworkClass` | `EFT.SocialNetwork`（93 方法） | `CreateDialogue / DisplayMessage / SendMessage / ChatSendDelegate` 全在 |
| `GClass1059`（会话信息） | `ChatShared.ChatRoomInformation` | `Id / type / Name / pinned / Users / message / new` |
| `ChatMessageClass` | `ChatShared.DialogueChatMessage` | `_id / Type / Text / LocalDateTime / UtcDateTime / Member / ReplyTo / Items` |
| `gclass.ChatMessages.Add(...)` | `SocialNetwork.DisplayMessage(msg, dialogue)` | **public**，比 neko 反编译不出的 `SafeDisplay` 干净 |
| `social.GDelegate33_0` | `SocialNetwork.ChatSendDelegate` | public get/set |
| `GClass1628<T>` | `UpdatableBindableList<T>` | 强类型列表 |
| `type = 9` | `EMessageType.GlobalChat = 9` | 枚举 15 项 |
| `DialogueController.GetFriendList` | 同名同签名（`MongoId → GetFriendListDataResponse`） | 我们 P2a 已用 |
| `SPTarkov.Reflection.Patching.AbstractPatch` + `[PatchPostfix]` | 同样是 `AbstractPatch` + `AccessTools` | 我们用的是 `Harmony.CreateClassProcessor`，等价 |

## 8. SENTINEL Link 的取舍（基于以上）

**做**（有把握、复用我们已有成果）：
- 世界频道 → 服务端虚拟 `GlobalChat` 会话 + 原生 `ChatScreen`（P2b，方案 B2）
- 私聊 → 已交付（P2a），补客户端页签/原生入口
- 跳蚤远程报价 → 对标 MarketGlow，但我们走「服务端注入 + 幂等表」，不做客户端高亮补丁
- 空投 → 走原生「带 Items 的 Message」路线

**不做**：
- 跨局 P2P 打洞（Fika 已经在做，我们接它）
- 竞技场 / 回滚快照
- 替换 MatchMaker UI

**已知缺口**：neko 的 Hub 本体不在仓库里（`hubUrl` 指向外部服务），中心端的限流、鉴权、跨 realm 隔离只能靠我们自己的设计补。

## 9. 复现命令

```powershell
# 客户端补丁目标清单
Get-ChildItem D:\sptEdior\_ref\upstream\nekoNx -Recurse -File -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\server\\' } |
  Select-String -Pattern 'HarmonyPatch\(typeof\s*\(([\w\.]+)\)(?:\s*,\s*"([\w<>\.]+)")?' -AllMatches

# 服务端补丁目标
Get-ChildItem ...\nekoNx\server -Recurse -File -Filter *Patch*.cs |
  Select-String -Pattern 'AccessTools\.(Method|Property|Constructor)\s*\(\s*typeof\s*\(\s*([\w\.]+)\s*\)'

# 路由
Get-ChildItem ...\nekoNx\server -Recurse -File -Filter *.cs |
  Select-String -Pattern '"(/(?:neko|link)[a-zA-Z0-9/_-]*)"'
```
