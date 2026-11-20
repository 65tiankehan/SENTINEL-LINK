# SENTINEL Link 计划书 v0.1

> 目标：给 SPT 4.1.6 做一套**联机 + 社交**套件，和 NekoParty / NekoNET 正面竞争。
> 对外名 **SENTINEL Link**，技术身份 `SentinelLink.*`，GUID 前缀 `cn.passionyang.sentinellink`。
> 本文件是唯一路线口径；改路线先改这里。

---

## 一、先看清楚对手（参考物：`_ref\upstream\nekoNx`，ILSpy 反编译快照）

它的结构是三层：**游戏客户端插件 → 本地 SPT 服务端 mod → 中心 Hub**。

| 层 | 实现 | 关键点 |
|---|---|---|
| 客户端 | 7 个 BepInEx 程序集（`NekoParty.Core` / `.GroupEX` / `NekoNet.Client.{MarketGlow,WorldChat,Friends,GameState,Airdrop}`） | 不直连公网；HTTP 打本机 SPT 服务端（`SPT.Common.Http.RequestHandler`，第三参=true 带鉴权） |
| 服务端 | 11 个 net9 项目，`[Injectable]` DI + `IOnLoad` | `HubClient`(HTTP+Bearer) / `HubWsClient`(WS 长连) / `NekoState` 内存态 / `NekoCrypto` 加密 `config.dat` |
| Hub | 外部服务中心（未包含在快照里） | `realmId` 分区、`inviteKey` 准入、服务端→Hub→其它服务端广播 |

**它的三个软肋，就是我们的机会：**

1. **整套深度绑定 Fika**：跨局 P2P 直接用 Fika 自带的 LiteNetLib，还 patch 掉 Fika 原生的组队面板。Fika 一升级它就跟着崩。
2. **没有 Hub 就什么都不是**：社交、市场、空投全部要求玩家先接一个中心服务。
3. **目标框架停在 net9**（SPT 4.0 时代），在 4.1.6（`net10.0`）上属于"能跑但不是为我们这版写的"。

> 反编译件只做**接口与流程参考**，不复制代码。我们照自己的骨架重写。

---

## 二、我们的定位（差异化）

**一句话：单人也能用的社交层，联机是加法不是前提。**

- **零依赖起步**：P0–P4 全部只依赖「SPT 服务端 + 我们自己的 Hub」，不需要 Fika、不需要改游戏渲染、不需要玩家开端口。
- **面板复用**：客户端 UI 直接吃 `SENTINEL-Min` 那套 `MinDraw` / `MinTheme`（四语、暗色、iOS 26 质感），省掉两周的界面工作。
- **流水线复用**：`deploy.ps1` / `selfcheck` / 安装器 / 四件套验收，全部照搬现有工程的做法。
- **和现有产品咬合**：Pro 的跳蚤增强、Min 的保镖面板，都能把 Link 的社交能力接进去（例如保镖在场时显示队友位置）。

---

## 三、架构（我们自己的版本）

```
游戏客户端  SENTINEL.Link.Client         (BepInEx net48 ·
            ├ UI（原生聊天窗注入 + 自建面板 F11）      Harmony · IMGUI）
            └ LinkHttp（SPT.Common.Http，带 Bearer）
                        │  只打本地，不开端口
                        ▼
本地服务端  SENTINEL.Link.Server          (SPT mod · net10 ·
            ├ Routers   StaticRouter 路由 /sentinelink/*
            ├ Services  业务（chat/presence/market/airdrop）
            ├ LinkHubClient  HTTP + WS 出站
            └ LinkState 内存态 + config.json（明文，加密留 P3）
                        │  出站 443/80，一条长连
                        ▼
中心服务    SENTINEL.Link.Hub             (ASP.NET Core net10 · Kestrel ·
            ├ 准入  inviteKey / realmId / serverId
            ├ 广播  WS 房间（realm 内所有 SPT 服务端）
            └ 存储  内存 peer 表 + spool/*.jsonl（P3 再上 SQLite 报价索引）
```

**为什么客户端不直连 Hub**：玩家只需要能上网；服务端侧一条长连即可，NAT 友好，也不会把 `inviteKey` 漏到客户端。

---

## 四、分阶段路线

| 阶段 | 内容 | 依赖 | 验收（必须给数字） | 状态 |
|---|---|---|---|---|
| **P0 骨架** | Hub（`/v1/hello`、`/v1/ws`、presence 广播、心跳清扫）+ 服务端出站握手 + 假对端 | — | build 0 警告 0 错误；四跳（客户端→SPT→Hub→假对端）通；`/v1/health` 能看到 peer | ✅ 已交付 `3bbfff2` |
| **P1 世界聊天** | 服务端转发 + 客户端聊天面板（复用 Min 绘制层）+ 输入锁 | P0 | 面板实收 10 条对端消息；`chat` 帧不丢；重连退避 2→30 s | ✅ 已交付 `25ca19c` / `93058de` / `d36eb00` |
| **P1.5 协议冻结** | 把 `hello / welcome / presence / chat / ack / dm / dm-sync / ping` 写成 `docs\Hub-协议.md`：字段表、语义、向后兼容规则（未知帧必须忽略） | P1 | 文档含 8 类帧；旧客户端连新 Hub 不崩；Hub 侧只增字段不删字段 | ✅ 已交付 `55abcd5`（250 行 / 11,170 B） |
| **P2a 原生好友 + 私聊** | 服务端 patch `DialogueController`：`GetFriendList` / `GenerateDialogueList` / `GenerateDialogueView` / `SendMessage`，把跨服好友与消息「造」进原生好友面板与私聊窗口；**跨服消息只走 `LinkState` + Hub，不写 profile** | P2b-1 Hub 私聊通道（已交付 `de4a7a3`）+ P1.5 | 原生好友 +1、对话 +1、点开取到 3~4 条消息 + 2 个 profiles（实测）；离线 100 条补投 0 丢失；**存档不脏写**：profile 字节差 0（525,509 B 前后一致）、虚拟 id 命中 0 | ✅ 已交付 `158344d` / `32a5c61` |
| **P2b 世界频道 + 局内入口** | ①**世界频道走原生聊天窗**（照 neko 的 B2 方案：服务端造 `GlobalChat` 虚拟会话，客户端零 UI 或极小补丁）；②局内入口（`InventoryScreen.Show` Postfix 注入按钮）；③自建面板降级为诊断 + 备用 | P1 + P2a 的注入面 | 世界频道与私聊互不串台；原生窗收 10 条 / 发 10 条；`ChatSendDelegate` 单槽被抢时降级不崩；局内按钮点开可发 | 🟡 Hub 私聊通道已交付（`de4a7a3`）；世界频道 + 局内入口待做（**下一步**） |
| **P3 跳蚤远程报价** | `RagfairOfferHolder` 注入 + 幂等表（Remote/Pushed/OfferOwners）+ 模板校验 + 结算 | **P1.5**（不再依赖 P2 的 UI） | 报价同步 ≤ 300 条上限不超；`requireLocalItemTemplates` 命中 0 崩；同一报价重复推送 0 次 | ⬜ |
| **P4 局内空投** | WS 下发 + 客户端落点/拾取 + TTL 600 s | P3 + P2b 通道 | 投递成功率 ≥ 95%；TTL 600 s 归**服务端定时器**（Hub 只转帧不存，重启不背锅）；TTL 到期回收 0 残骸 | ⬜ |
| **P5 组队 / 跨局** | 二选一：① 接 Fika（省力，受制于人）② 自研轻量同步（共享标记/观战，不搬网络栈） | P4 | 决策点，届时再定 | ⬜ |

**2026-11-20 修订**：P2 拆成 P2a / P2b —— 好友与私聊可以「服务端造数据」进原生 UI（neko 的 `GetFriendListPatch` 就是这么干的），客户端一行 UI 都不用写；自建面板退为「局内 + 世界频道 + 诊断」。新增 P1.5 协议冻结与「存档不脏写」验收，P3 的依赖从 P2 改为 P1.5。

**P3 之前不碰 Ragfair**：跨服物品错配会把存档搞坏，模板校验和结算必须先有。

---

## 五、起点 —— P0 第 1 步（✅ 已完成：`3bbfff2`，详见 `docs\Link-进度.md`）

**归档要点**（原始任务：打通「两个本地 SPT 实例 ↔ 一个 Hub」的最小闭环）：

1. 服务端 `/sentinelink/client/ping` 返回 `{ version, serverTime, hubState }`；
2. Hub `/v1/hello`（准入）+ `/v1/ws`（长连），realm 内广播 `presence`；
3. 服务端 `OnLoad` 握手 + 心跳，Hub 状态存 `LinkState`。

**实际验收口径**：`/v1/health` 能看到 peer + WS `presence` 广播（**不是** `/v1/roster` —— 该路由从未实现），四跳「假对端 → Hub → 真服务端 → 客户端面板」全通。

为什么它仍然是地基：聊天/好友/报价/空投全走这条链，鉴权、断线恢复、realm 隔离都在这一步定死。

## 六、工程约定

- 版本真相源：`Shared\BuildInfo.cs`（`ProductName` / `Version` / `SptVersion` / `DisplayLine`），别处不许写版本字面量。
- 服务端 mod 部署路径：`E:\EFT\SPT_Runtime\user\mods\<guid>\`；带 `package.json` + `ModMetadata : IModMetadata` 是推荐做法（实测 4.1.6 只有 dll 也能加载，别依赖这一点）。
- 服务端编译引用：`SPTarkov.Common` / `SPTarkov.DI` / `SPTarkov.Server.Core` / `SemanticVersioning`，全部 `<Private>false</Private>`。
- 路由一律继承 `StaticRouter` + `RouteAction<TRequest>`，回调走 `HttpResponseUtil.NoBody(...)`。
- 配置：服务端 `config.json` **明文**（现状）；加密（`config.dat` AES-GCM，密钥放 Hub 侧）推迟到 P3，未实现前文档不许写"已加密"。
- 改动记录写 `docs\Link-进度.md`，一条一次，带数字。

## 七、风险

| 风险 | 止损 |
|---|---|
| 玩家没有公网 Hub → 功能形同虚设 | P0 就把 Hub 做成单文件可自托管（`dotnet SENTINEL.Link.Hub.dll --port 8443`），并支持局域网模式 |
| 跨服物品错配毁档 | P3 强制模板校验；默认只同步"可堆叠 + 白名单"品类 |
| Fika 生态占位 | P5 先做"不搬网络栈"的轻同步，绝不跟 Fika 版本绑死 |
| 与 Pro 的跳蚤功能重叠 | P3 之前先和 Pro 的 SVM 对表，避免两套并存互相打架 |
| 反编译参考的版权 | 只借鉴协议与流程，代码 100% 自写；不反编译发布二进制 |
| 原生 dialogue patch 被 EFT 版本变动打穿 | `PatchAll` 失败只记日志并降级回自建面板（P2b），**不允许崩游戏**；每次 SPT 小版本升级先跑 P2a 自验 |
| 跨服好友/消息写脏 profile `dialogues` | 跨服消息只落 `LinkState` + Hub spool，注入走 Postfix 返回值不碰写入；验收要求 profile 文件字节差 0 |
| serverId(32 hex) → MongoId(24 hex) 派生冲突 | 取前 20 位 + 4 位域标记；启动时查重，冲突则换标记位并记 warning |
| `SocialNetwork.ChatSendDelegate` 是**单槽** | 别的 mod（含 Fika/neko）抢走就静默失效：安装时检测已有委托，非空则记 warning 并降级成自建面板发送，不覆盖别人的委托 |
| 原生会话列表的未读红点被虚拟会话污染 | 虚拟会话 `new=0`、不发 `ReadDialogues`；发现红点异常先摘掉 P2a 的 `GenerateDialogueList` 注入验证隔离 |
| Hub 单点：内存态一重启就没 | `spool/*.jsonl` 只保私聊，在线表与聊天历史不保；文档明写「Hub 重启 = 掉线重连 + 丢历史」（已写进 README 已知限制），P3 起再考虑落库 |
