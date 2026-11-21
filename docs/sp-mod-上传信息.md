# SPT Forge 填写内容（SENTINEL LINK 0.1.0）

## Thumbnail

上传 `docs/design/link-thumbnail-1280x720.png`（1280×720，**271,597 B**，16:9，小于 2 MB）。

## Name（≤ 75）

```
SENTINEL LINK
```

## Mod GUID（可选，但 4.0.0+ 建议填）

```
cn.passionyang.sentinellink.server
```

> 这是我们服务端模组的 GUID（`Shared/BuildInfo.cs` 的 `ServerGuid`）。客户端插件是 `cn.passionyang.sentinellink.client`，Forge 这个字段只收一个，填服务端那个。

## Teaser（≤ 255）

```
把跨服好友、私聊和三个公共频道接进游戏原本的好友列表与聊天窗；一条 Hub 中转，单人也用得上的社交层，不改存档。
```

## Description（Markdown，直接粘下面整段）

```markdown
## 这不是联机开黑

SENTINEL LINK 给 SPT 4.1.6 加一层「社交」：好友列表、私聊、世界频道，全部长在游戏**原本**的消息界面里——没有第二套 UI，不用记新按键。

你可以一个人玩，也可以和别的服的人聊天、加好友、挂报价（P3 起）。不需要 Fika。

## 装完多出什么

- **好友列表**里多出「世界 / 交易 / 求组」三个频道，和别的服的真人
- **私聊**走原生对话框，对方离线也能发——Hub 每收件人排队 500 条、保留 168 小时，上线按序补投
- **世界频道**长在原生聊天窗，翻历史、发中文都正常
- **F11 面板**：局内外都能开，打开时自动锁人物输入（不会边打字边走路），`Ctrl+V` 粘中文

## 怎么接进去的（给别人看原理，省得担心）

| 游戏行为 | 我们的做法 |
| --- | --- |
| 打开好友列表 | 在原生 `GetFriendList` 返回值尾部追加，不写 `FriendProfileIds` |
| 打开对话列表 | 在 `GenerateDialogueList` 尾部追加虚拟会话 |
| 打开某个对话 | `GenerateDialogueView` 前缀拦截虚拟 id，自己造返回值 |
| 发一条消息 | `SendMessage` 前缀把虚拟 id 的消息转给 Hub |
| 虚拟 id | `sha256(...)` 前 20 位 + 域标记 `1e0f`，与真实 id 不可能撞 |

**存档安全**：所有注入都走返回值，不写 `DialogueRecords`；每次保存前比对对话条数，一旦变少立刻记 `profile-drop.log`。仓库里有 `文档/P2a-注入清单.md` 写了完整判定。

## 安装

1. 下载 `SENTINEL-LINK-Setup.exe`，选游戏根目录（含 `EscapeFromTarkov.exe` 那层）
2. 装完重启游戏，主菜单底栏「消息」里就有好友和频道
3. 要联机时先跑 `SENTINEL-Link-Hub\启动 Hub.cmd`（**需要 .NET 10 运行时**）

手动装也可以，用 `SENTINEL-LINK-0.1.0.zip`：客户端插件丢 `BepInEx\plugins\`，服务端模组丢 `SPT_Runtime\user\mods\`，Hub 丢游戏根。

## 已知限制

- Hub 是**单点**：自己开服的人要自己跑 Hub，别人连你得能访问到
- 频道成员按 realm 划分，跨 realm 不互通（配置里 `realm` 一致才看得见）
- 跨服跳蚤报价（P3）、局内空投（P4）、组队（P5）还没做
- 原生好友列表的「计数」会把三个频道也算进去（显示 5 实际 6 行）

## 许可

MIT。协议文档在仓库 `docs/Hub-协议.md`，二次开发请保持帧契约只增字段、虚拟 id 派生规则不变。
```

## License

选 **MIT**。

## Category

选最接近的（一般是 `Utility` / `Tools` / `Other`，看 Forge 的选项）——我们不属于 gameplay 改动。

## Additional Authors

留空。

## Source Code Links

```
https://github.com/65tiankehan/SENTINEL-LINK
```

Label 填 `GitHub`。

## Publish Date

留空（先建好，检查页面无误再点发布）；想立刻上架就点 `Now`。

## General Disclosure

- **Contains Ads**：不勾
- **Show Cheat Warning**：**不勾**（本模组不含作弊功能：无 ESP、无透视、无改属性；好友/聊天/UI）

## Comments

勾上 **Enable Comments**（要收反馈）。

## Issues

勾上 **Enable Issues**（Forge 提示「in beta」，但用户报 bug 好用）。

## Comment Version Tags

保持默认（Red / Amber / Amber）。

## Add-ons

**不勾** `Disable Add-ons`（留默认，允许别人做附属）。

## Mod Lists

**不勾** `Disable Mod Lists`。

## Notifications

勾上 `Subscribe to Comment Notifications`。

---

## 下载资产（两处 sha256 一致）

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `SENTINEL-LINK-Setup.exe` | 3,121,664 | `3F86D78CD07968DEEAA322E6DE81EC1488EB9A73643579D5BA513AB2A77D15CF` |
| `SENTINEL-LINK-0.1.0.zip` | 189,892 | `252FC8AA0C87A7F467D892E296BC54DF558166CB98980C76BE5DD670C5D13634` |

## 提示

Forge 会从 `SPT_Runtime/user/mods/<guid>/package.json` 读版本与 GUID 做兼容表与更新检查。我们的 `package.json` 已把 `license` 从 `Proprietary` 改成 `MIT` 与仓库一致——如果你已经上传过旧包，记得换成新包再传。
