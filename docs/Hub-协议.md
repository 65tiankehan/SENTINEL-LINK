# SENTINEL Link · Hub 协议 v1（冻结）

> 冻结日期：2026-11-21 · 适用 `SENTINEL.Link.Hub` 0.1.0 / `SENTINEL.Link.Server` 0.1.0 / `SENTINEL.Link.Client` 0.1.0
> 这份文档是 P3（跳蚤远程报价）、P4（局内空投）、P5（组队）唯一的帧依据。**代码与文档不一致时，先改代码再改文档，两边同一次提交。**

## 0. 冻结规则（违反即算破坏协议）

1. **只增字段，不删不改**：已有字段的名字、类型、单位永久不变；新需求一律加新字段。
2. **未知帧忽略**：收到不认识的 `type` 不回错、不断连、不落日志刷屏（Hub 与 SPT 服务端两侧都必须这样）。
3. **未知字段忽略**：反序列化一律宽松，旧端给新端发帧、新端给旧端发帧都不能崩。
4. **可选字段必须能缺**：文档里标「可选」的字段，缺省时按文档给的默认值处理，不许抛异常。
5. **时间统一毫秒**：协议层所有 `at` 是 Unix 毫秒（`long`）。⚠️ 注意：注入到 **EFT 原生对话**的 `Message.DateTime/UtcDateTime` 是**秒**，换算只发生在 `LinkFriends` 里，协议层不受影响。
6. **id 统一规则**：`serverId` = 32 位小写 hex；私聊 `id` = 16 位 hex；派生给 EFT 的 24 位 MongoId 必须尾 4 位是域标记 `1e0f`（见 §6）。

## 1. 传输

| 项 | 值 |
|---|---|
| 基址 | `http://<hub-host>:5180`（`SENTINEL_HUB_PORT` 可改） |
| 编码 | UTF-8，`application/json`，JSON 字段小写驼峰 |
| 长连 | `GET /v1/ws?token=<token>`（WebSocket，文本帧，每帧一个 JSON 对象） |
| 保活 | 任一端每 **15 秒**发 `ping`；Hub 侧 **45 秒**收不到任何帧即判掉线（`Sweep` 每 10 秒一次） |
| 单帧上限 | 聊天 512 字符、私聊 1024 字符（超出**截断**，不报错） |

### 1.1 `GET /v1/health`（无需鉴权）

```json
{ "ok": true, "product": "SENTINEL Link", "version": "0.1.0", "sptVersion": "4.1.6",
  "peers": 2, "realms": ["default"], "dmQueued": 0, "serverTime": 1795190743209 }
```

`peers` = 当前连上 WS 的对端数；`dmQueued` = 落盘队列里的私聊总条数（跨所有人）。

### 1.2 `POST /v1/hello`（邀请码准入）

请求体（字段全部可选，缺省用默认值）：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `inviteKey` | string | ✔ | 与 Hub 的 `SENTINEL_HUB_KEY` 逐字符比对，错则 401 |
| `serverId` | string | ✔ | 32 位 hex；服务端首次启动生成后落 `serverid.txt`，**必须跨重启稳定**，否则 Hub 上留幽灵对端 |
| `realm` | string | 可选 | 默认 `default`；不同 realm 之间互不可见（聊天/私聊/在线表都按 realm 隔离） |
| `nickname` | string | 可选 | 显示名，默认 `unknown` |
| `version` | string | 可选 | 客户端 mod 版本，仅展示 |
| `sptVersion` | string | 可选 | SPT 版本，仅展示 |

响应 200：

```json
{ "ok": true, "product": "SENTINEL Link", "hubVersion": "0.1.0",
  "token": "f616d2b0bb598c38c000c5dd0da2a711",
  "serverId": "bc44f77d62f04c908ad73837b9d893c4", "realm": "default",
  "peers": [ …同 §2.1 welcome.peers… ],
  "serverTime": 1795190743209 }
```

| 失败 | 状态码 | 体 |
|---|---|---|
| 邀请码不对 | 401 | `{"ok":false,"error":"邀请码错误"}` |
| `serverId` 为空 | 400 | `{"ok":false,"error":"serverId 不能为空"}` |
| 同 realm 超过 64 个对端 | 400 | `{"ok":false,"error":"realm default 已满（上限 64）"}`（实际文案带域名字与上限值） |

> hello 成功时会向同域其他人广播一帧 `presence{online:true}`（不回给自己）。

### 1.3 WS 鉴权失败

`/v1/ws?token=` 无效 → **401** + 纯文本 `invalid token`（不是 JSON）。客户端应把它当作「token 失效，重新 hello」。

## 2. 帧目录

一帧一个 JSON 对象，必须有 `type`（string）。下表是 v1 的**全部**帧。

### 2.1 Hub → 客户端

#### `welcome`（连上即发，第一帧）

| 字段 | 类型 | 说明 |
|---|---|---|
| `serverId` | string | 你自己的 id |
| `realm` | string | 你的域 |
| `hubVersion` | string | Hub 版本 |
| `peers` | array | 同域在线表，元素见下 |
| `at` | long | 毫秒 |

`peers[]` 元素：`{ serverId, nickname, version, sptVersion, online, lastSeenMs }`。

```json
{"type":"welcome","serverId":"peer-x5","realm":"default","hubVersion":"0.1.0",
 "peers":[{"serverId":"peer-x5","nickname":"路人X5","version":"synthetic-0.1.0","sptVersion":"4.1.6","online":true,"lastSeenMs":1795190749232}],"at":1795190749232}
```

#### `presence`（上下线）

| 字段 | 类型 | 说明 |
|---|---|---|
| `serverId` | string | 变化的对端 |
| `nickname` | string | 显示名 |
| `version` | string | 可选，仅 `online=true` 时附带 |
| `online` | bool | `true` 上线 / `false` 下线 |
| `reason` | string | 可选，`timeout` 表示心跳超时被清扫 |
| `at` | long | 毫秒 |

#### `chat`（世界频道，广播给同域其他人，**不回给发送方**）

| 字段 | 类型 | 说明 |
|---|---|---|
| `from` | string | 发送方 serverId |
| `nickname` | string | 发送方显示名 |
| `realm` | string | 域 |
| `text` | string | 正文（≤512） |
| `at` | long | 毫秒 |

#### `dm`（私聊，点对点）

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | string | 消息 id（16 hex），与 ack 的 `id` 对应 |
| `from` | string | 发送方 serverId |
| `fromNick` | string | 发送方显示名 |
| `to` | string | 收件人 serverId |
| `text` | string | 正文（≤1024） |
| `at` | long | 毫秒 |
| `replayed` | bool | `true` = 离线补投（队列读出），`false` = 实时投递 |

#### `dm-sync`（补投前导帧，可选）

| 字段 | 类型 | 说明 |
|---|---|---|
| `count` | int | 紧接着会发多少条 `dm`（`replayed=true`） |
| `at` | long | 毫秒 |

顺序保证：`welcome` → `dm-sync`（仅当有积压）→ N 条 `dm`。

#### `ack`（对 chat / dm 的回执，只回发送方）

| 字段 | 类型 | 说明 |
|---|---|---|
| `forType` | string | `chat` 或 `dm` |
| `delivered` | bool | 是否已投递给在线的收件人 |
| `queued` | bool | 可选，`true` = 收件人不在线，已落盘排队 |
| `pending` | int | 可选，排队后该收件人队列里的总条数 |
| `id` | string | 可选，`dm` 的消息 id |
| `error` | string | 可选，失败原因（`缺少收件人或正文`） |
| `at` | long | 毫秒 |

#### `pong`

`{ "type": "pong", "at": <long> }`

### 2.2 客户端 → Hub

#### `chat`

`{ "type": "chat", "text": "..." }` → 广播 `chat` + 回 `ack{forType:"chat"}`。

#### `dm`

`{ "type": "dm", "to": "<serverId>", "text": "..." }`
- 收件人在线且同 realm → 直投 + `ack{delivered:true,queued:false,id}`
- 否则 → 落盘 `spool/<realm>/<serverId>.jsonl` + `ack{delivered:false,queued:true,pending}`
- `to` 或 `text` 为空 → `ack{delivered:false,error:"缺少收件人或正文"}`

#### `ping`

`{ "type": "ping" }` → `pong`。**每 15 秒必须发**，否则 45 秒后被清扫。

#### 未知 `type`

忽略。Hub 不回包、不断连。

## 3. 边界与资源上限

| 项 | 值 | 位置 |
|---|---|---|
| 聊天正文上限 | 512 字符（超出截断） | `HubOptions.MaxChatLength` |
| 私聊正文上限 | 1024 字符（超出截断） | `HubOptions.MaxDirectLength` |
| 每人离线队列 | 500 条（超出丢最旧） | `SENTINEL_HUB_DM_LIMIT` |
| 离线保留 | 168 小时（过期不投） | `HubOptions.DirectTtl` |
| 掉线判定 | 45 秒无帧 | `HubOptions.PeerTimeout` |
| 清扫周期 | 10 秒 | `HubOptions.SweepInterval` |
| 单 realm 上限 | 64 个对端 | `SENTINEL_HUB_MAX_PEERS` |
| 队列落盘目录 | `SENTINEL_HUB_SPOOL`，默认 `<hub>/spool` | — |

## 4. 状态机

```
        hello(HTTP)                welcome
  [无] ────────────→ [已注册] ────────────→ [在线]
                        │   ws(token)          │
                        │                      ├─ dm-sync + dm(replayed)  ← 有积压
                        │                      ├─ presence / chat / dm / ack / pong
                        │                      ├─ 每 15s 发 ping
                        └──────────────────────┴─ 45s 无帧 → presence offline(timeout) → [已注册]
                                                  token 失效 → 401 → 重新 hello
```

- `[已注册]` 没连 WS 时：**不进别人的在线表**，但仍可收离线私聊（落盘）。
- 断线重连：`LinkHubClient` 退避 2→4→8→16→30 秒，重连成功后 Hub 会重新发 `welcome` + 补投。

## 5. 兼容性矩阵（v1）

| 帧 | Hub 发 | Hub 收 | SPT 服务端收 | 合成对端收 |
|---|---|---|---|---|
| `welcome` / `presence` / `chat` / `dm` / `dm-sync` / `ack` / `pong` | ✔ | — | ✔ | ✔ |
| `chat` / `dm` / `ping` | — | ✔ | ✔（发） | ✔（发） |

## 6. 派生 id（写给 EFT 原生面板的那一层）

不改协议，但 P3/P4 会复用同一套规则，所以写在这里：

| 用途 | 规则 | 例子 |
|---|---|---|
| 虚拟对话 id | `sha256("sentinel.link.friend:" + serverId)` 前 20 hex + `1e0f` | `cb98d304c62cd9195` + `1e0f` |
| 虚拟消息 id | `sha256("sentinel.link.message:" + 私聊 id)` 前 20 hex + `1e0f` | — |
| 虚拟 aid | `sha256(serverId)` 后 4 字节 & `0x7FFFFFFF`，取模到 `1_000_000..9_999_999` | — |
| 域标记 | 永远尾 4 位 `1e0f`（判「是不是我们的」只认这 4 位） | — |

**铁律**：`DialogueController.GetDialogByIdFromProfile` 对未知 dialogId 会**新建对话并写进 profile**。任何把虚拟 id 放给原生的路径都必须先判域标记并短路，否则脏档（P2a 踩过：多出一条空对话 `23041e039769da209d161e0f`）。

## 7. 扩展流程（P3/P4/P5 照做）

1. 在本文档 §2 加一节，写清字段表 + 一个示例帧；
2. 代码里**新增** `case`，不修改已有分支；
3. 收端 `default` 保持「忽略未知帧」；
4. 旧端不发新字段时，新端按默认值处理；
5. 同一次提交里更新 `docs\Link-进度.md`；
6. 用 §8 的三条命令回归。

示例（P3 预演，**尚未实现**）：

```json
{"type":"market.offer","id":"…","seller":"<serverId>","tpl":"5d2f0d5a…","count":1,"price":120000,"at":1795190743209}
```

## 8. 回归清单（每次改协议都跑）

```powershell
# 1. Hub 起来
dotnet D:\sptEdior\SENTINELLink\Hub\bin\Release\net10.0\SENTINEL.Link.Hub.dll

# 2. 假对端：在线直投 3 条 + 对账
dotnet D:\sptEdior\SENTINELLink\Tools\SyntheticPeer\bin\Release\net10.0\SyntheticPeer.dll `
  --hub http://127.0.0.1:5180 --server-id peer-x5 --nickname 路人X5 --key dev-local `
  --to <目标serverId> --count 3 --expect 3

# 3. 离线补投 100 条（必须 0 丢失）
#    先让目标下线，再连发 100 条；目标上线应收到 dm-sync(1) + dm(100, replayed=true)
```

验收口径：`health.dmQueued` 投递后归 0；`--expect` 打印「对账 OK」；profile 字节数在**不玩**的情况下不变（玩了会因正常存档变化，改用「虚拟 id 命中 0」判定）。
