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

| 阶段 | 内容 | 依赖 | 验收（必须给数字） |
|---|---|---|---|
| **P0 骨架** | 仓库 + `SENTINEL.Link.Server` 最小可编译（`/sentinelink/ping`）+ `SENTINEL.Link.Hub` 最小服务（`/v1/hello`、`/v1/ws`）+ 服务端出站握手 | 无 | build 0 警告 0 错误；两个 SPT 实例在 Hub 的 `/v1/roster` 里互相看见；设备日志有握手往返时间 |
| **P1 世界聊天** | 服务端转发 + 客户端聊天面板（复用 Min UI）+ 历史 50 条 | P0 | 双实例互发消息端到端 < 300 ms；断线 5 s 内自动重连；历史拉取一次 ≤ 50 条 |
| **P2 好友在线 + 私聊** | presence 心跳、上下线推送、未读队列 | P1 | 上线可见延迟 ≤ 3 s；离线消息补投 0 丢失（连发 100 条对账） |
| **P3 跳蚤远程报价** | `RagfairOfferHolder` 注入 + 幂等表（Remote/Pushed/OfferOwners）+ 模板校验 + 结算 | P2 | 报价同步 300 上限不超；`requireLocalItemTemplates` 命中 0 崩；同一报价重复推送 0 次 |
| **P4 局内空投** | WS 下发 + 客户端落点/拾取 + TTL 600 s | P3 | 投递成功率 ≥ 95%；TTL 到期回收 0 残骸 |
| **P5 组队 / 跨局** | 二选一：① 接 Fika（省力，受制于人）② 自研轻量同步（共享标记/观战，不搬网络栈） | P4 | 决策点，届时再定 |

**P3 之前不碰 Ragfair**：跨服物品错配会把存档搞坏，模板校验和结算必须先有。

---

## 五、从哪开始 —— P0 第 1 步（本次动工）

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
- 改动记录写 `docs\链接-进度.md`，一条一次，带数字。

## 七、风险

| 风险 | 止损 |
|---|---|
| 玩家没有公网 Hub → 功能形同虚设 | P0 就把 Hub 做成单文件可自托管（`dotnet SENTINEL.Link.Hub.dll --port 8443`），并支持局域网模式 |
| 跨服物品错配毁档 | P3 强制模板校验；默认只同步"可堆叠 + 白名单"品类 |
| Fika 生态占位 | P5 先做"不搬网络栈"的轻同步，绝不跟 Fika 版本绑死 |
| 与 Pro 的跳蚤功能重叠 | P3 之前先和 Pro 的 SVM 对表，避免两套并存互相打架 |
| 反编译参考的版权 | 只借鉴协议与流程，代码 100% 自写；不反编译发布二进制 |
