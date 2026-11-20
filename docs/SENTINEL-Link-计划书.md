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
            ├ UI（复用 MinDraw/MinTheme）             Harmony · IMGUI 面板 F10）
            └ LinkHttp（SPT.Common.Http，带 Bearer）
                        │  只打本地，不开端口
                        ▼
本地服务端  SENTINEL.Link.Server          (SPT mod · net10 ·
            ├ Routers   StaticRouter 路由 /sentinelink/*
            ├ Services  业务（chat/presence/market/airdrop）
            ├ LinkHubClient  HTTP + WS 出站
            └ LinkState 内存态 + config.dat 加密
                        │  出站 443/80，一条长连
                        ▼
中心服务    SENTINEL.Link.Hub             (ASP.NET Core net10 · Kestrel ·
            ├ 准入  inviteKey / realmId / serverId
            ├ 广播  WS 房间（realm 内所有 SPT 服务端）
            └ 存储  SQLite（账号、离线消息、报价索引）
```

**为什么客户端不直连 Hub**：玩家只需要能上网；服务端侧一条长连即可，NAT 友好，也不会把 `inviteKey` 漏到客户端。

---

## 四、分阶段路线

| 阶段 | 内容 | 依赖 | 验收（必须给数字） | 状态 |
|---|---|---|---|---|
| **P0 骨架** | Hub（`/v1/hello`、`/v1/ws`、presence 广播、心跳清扫）+ 服务端出站握手 + 假对端 | — | build 0 警告 0 错误；四跳（客户端→SPT→Hub→假对端）通；`/v1/health` 能看到 peer | ✅ 已交付 `3bbfff2` |
| **P1 世界聊天** | 服务端转发 + 客户端聊天面板（复用 Min 绘制层）+ 输入锁 | P0 | 面板实收 10 条对端消息；`chat` 帧不丢；重连退避 2→30 s | ✅ 已交付 `25ca19c` / `93058de` / `d36eb00` |
| **P1.5 协议冻结** | 把 `hello / welcome / presence / chat / ack / dm / dm-sync / ping` 写成 `docs\Hub-协议.md`：字段表、语义、向后兼容规则（未知帧必须忽略） | P1 | 文档含 8 类帧；旧客户端连新 Hub 不崩；Hub 侧只增字段不删字段 | ⬜ |
| **P2a 原生好友 + 私聊** | 服务端 patch `DialogueController`：`GetFriendList` / `GenerateDialogueList` / `GenerateDialogueView` / `SendMessage`，把跨服好友与消息「造」进原生好友面板与私聊窗口；**跨服消息只走 `LinkState` + Hub，不写 profile** | P2b 的 Hub 私聊通道（已交付 `de4a7a3`）+ P1.5 | 原生好友面板出现跨服好友 ≤ 3 s；连发 100 条离线补投 0 丢失（Hub 侧已验 100/100）；**存档不脏写**：注入前后 profile `dialogues` 条目数差 0、profile 文件字节差 0 | ⬜ **下一步** |
| **P2b 世界频道 + 局内入口** | 自建面板只做原生做不到的部分：局内私聊、世界频道、诊断；后续照 neko 的做法复制 `DefaultUIButton`，给背包界面注入聊天入口 | P1 | 局内 F11 收 10 条 / 发 10 条；世界频道与私聊互不串台；面板关掉后角色输入恢复 | 🟡 面板已交付，缺私聊页签 |
| **P3 跳蚤远程报价** | `RagfairOfferHolder` 注入 + 幂等表（Remote/Pushed/OfferOwners）+ 模板校验 + 结算 | **P1.5**（不再依赖 P2 的 UI） | 报价同步 ≤ 300 条上限不超；`requireLocalItemTemplates` 命中 0 崩；同一报价重复推送 0 次 | ⬜ |
| **P4 局内空投** | WS 下发 + 客户端落点/拾取 + TTL 600 s | P3 + P2b 通道 | 投递成功率 ≥ 95%；TTL 到期回收 0 残骸 | ⬜ |
| **P5 组队 / 跨局** | 二选一：① 接 Fika（省力，受制于人）② 自研轻量同步（共享标记/观战，不搬网络栈） | P4 | 决策点，届时再定 | ⬜ |

**2026-11-20 修订**：P2 拆成 P2a / P2b —— 好友与私聊可以「服务端造数据」进原生 UI（neko 的 `GetFriendListPatch` 就是这么干的），客户端一行 UI 都不用写；自建面板退为「局内 + 世界频道 + 诊断」。新增 P1.5 协议冻结与「存档不脏写」验收，P3 的依赖从 P2 改为 P1.5。

**P3 之前不碰 Ragfair**：跨服物品错配会把存档搞坏，模板校验和结算必须先有。

---

## 五、起点 —— P0 第 1 步（✅ 已完成：`3bbfff2`，详见 `docs\Link-进度.md`）

**先打通「两个本地 SPT 实例 ↔ 一个 Hub」的最小闭环**，只做三件事：

1. `SENTINEL.Link.Server` 起一个路由 `/sentinelink/client/ping`，返回 `{ version, serverTime, hubState }`；
2. `SENTINEL.Link.Hub` 起 `/v1/hello`（准入）与 `/v1/ws`（长连），realm 内广播 `presence`；
3. 服务端开机 `OnLoad` 时向 Hub 握手 + 每 20 s 心跳，把 Hub 状态存进 `LinkState`。

**验收**：两个 SPT 服务端（一个真机 4.1.6，一个本地第二实例）在 Hub 的 `/v1/roster` 里互相看见，`ping` 路由回得出对方 serverId。

为什么先做这个：

- 它是**所有功能的地基**（聊天/好友/报价/空投全走这条链），错在这里，后面全返工。
- 它**完全不碰游戏代码**（不用 Harmony、不用 IMGUI），一天内能验完。
- 它能立刻回答三个关键问题：鉴权怎么做、断线怎么恢复、realm 怎么隔离。
- 客户端插件到 P1 才进厂，届时能直接对着一个已经稳定的服务端写 UI。

---

## 六、工程约定

- 版本真相源：`Shared\BuildInfo.cs`（`ProductName` / `Version` / `SptVersion` / `DisplayLine`），别处不许写版本字面量。
- 服务端 mod 部署路径：`E:\EFT\SPT_Runtime\user\mods\<guid>\`，需 `package.json` + `ModMetadata : IModMetadata`（SPT 4.1.6 硬性要求）。
- 服务端编译引用：`SPTarkov.Common` / `SPTarkov.DI` / `SPTarkov.Server.Core` / `SemanticVersioning`，全部 `<Private>false</Private>`。
- 路由一律继承 `StaticRouter` + `RouteAction<TRequest>`，回调走 `HttpResponseUtil.NoBody(...)`。
- 配置：明文 `config.json` 起步，稳定后加 `config.dat`（AES-GCM，密钥不进源码，放 Hub 侧配置）。
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
