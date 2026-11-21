# sp-mod / 论坛上传信息（SENTINEL LINK 0.1.0）

## 标题

`【SPT4.1.6】SENTINEL LINK —— 好友、私聊、世界频道，长在游戏原本的聊天窗里`

标签：`#4.1.6` `#联机` `#社交` `#单人可用` `#MIT`

## 一句话简介（列表页用，≤ 100 字）

把跨服好友、私聊和世界频道接进游戏原本的好友列表与聊天窗；一条 Hub 中转，单人也用得上的社交层，不改存档。MIT 开源。

## 分类 / 兼容

| 字段 | 值 |
| --- | --- |
| SPT 版本 | 4.1.6（EFT 0.16.9.40743） |
| 类型 | 客户端插件（BepInEx 5）+ 服务端模组 + 本地 Hub |
| 需要 | .NET 10 运行时（只为 Hub；不装 Hub 时客户端/服务端仍可加载） |
| 依赖 | 无（不需要 Fika、不需要 BigBrain） |
| 协议 | MIT |
| 仓库 | https://github.com/65tiankehan/SENTINEL-LINK |

## 上传文件

优先用安装器（推荐给玩家）：

- `SENTINEL-LINK-Setup.exe` — 3,121,152 B
  sha256 `93852192F97CCDE92FA100E603D6D5AA61DFA274BAE9857ABD8F1A5C9556670D`

给要手动装的人用的压缩包：

- `SENTINEL-LINK-0.1.0.zip` — 189,187 B
  sha256 `C06CD24A928902A21D764E6430B92C805EEE28DBDC9667718A89E390FBA87AFA`
  内含 `BepInEx/plugins/SENTINEL.Link/`、`SPT_Runtime/user/mods/cn.passionyang.sentinellink.server/`、`SENTINEL-Link-Hub/`，以及 `LICENSE` / `README.md` / `Hub-协议.md`

> 安装器与 zip 的载荷完全一致（同一份 `payload.zip`，178,250 B / 12 个条目，内嵌校验逐字节相同）。

## 正文

直接用 `docs/发布稿-Link-0.1.0.md` 的全文（145 行 / 7,435 B）。要点顺序：不是什么 → 装完多出什么 → 怎么接进去的 → 安装三步 → 不做什么 → 已知限制 → 下载与 sha256 → 更新日志 → 开源仓库。

## 变更日志（0.1.0）

- 跨服好友注入原生好友列表，点开即私聊
- 私聊落盘队列：离线 500 条 / 168 小时，上线按序补投（100 条对账 0 丢失）
- 世界 / 交易 / 求组 三个公共频道，长在原生聊天窗（Hub 每频道留最近 100 条）
- F11 面板：局内外可用，打开时锁人物输入，`Ctrl+V` 粘中文
- Hub 协议冻结 v1（`docs/Hub-协议.md`：帧目录、边界、id 派生规则、扩展流程）
- 存档哨兵：每次保存前比对对话条数，变少立刻记 `profile-drop.log`

## 上传前检查清单

- [ ] 游戏关掉再测安装器（BepInEx 只在启动加载）
- [ ] 装完进主菜单，底栏「消息」能看到 `世界 / 交易 / 求组` 与在线对端
- [ ] 起 `SENTINEL-Link-Hub\启动 Hub.cmd`，Hub 控制台打印监听 `127.0.0.1:5180`
- [ ] 发一条世界消息，原生窗里能看到自己和别人的回显
- [ ] 卸载一次，确认 `BepInEx\config\cn.passionyang.sentinellink.client.cfg` 与 profile 都还在（服务端 `config.json` 保留）
