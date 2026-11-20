# P2a 注入清单（原生好友 / 私聊）— 2026-11-21

> 只读侦察结果。全部结论来自 Cecil 直读 `E:\EFT\SPT_Runtime\SPTarkov.Server.Core.dll`（4.1.6.0，2,635 类型），
> 没有跑游戏、没有改任何 profile。**这是实现的依据，也是「存档不脏写」的判定书。**

## 0 一句话结论

四条注入全部走**返回值**（Postfix 追加 / Prefix 短路），一条写入路径都不碰：
`SptProfile.DialogueRecords` 与 `SptProfile.FriendProfileIds` 的读写者只有原生代码自己，
我们的虚拟好友与消息只活在 `LinkState` + Hub spool 里 → profile 字节差应为 0。

## 1 数据结构（实测字段）

| 类型 | 字段 |
|---|---|
| `SptProfile` | `<DialogueRecords> Dictionary<MongoId, Dialogue>`、`<FriendProfileIds> HashSet<MongoId>`（**这两处是脏写风险点**） |
| `Dialogue` | `Id: MongoId`、`Type: MessageType?`、`Users: List<UserDialogInfo>`、`Messages: List<Message>`、`New: int?`、`AttachmentsNew: int?`、`Pinned: bool?` |
| `DialogueInfo : Dialogue` | 追加 `Message: MessagePreview` |
| `MessagePreview` | `UserId: string`、`MessageType`、`DateTime`、`TemplateId: string`、`Text`、`SystemData` |
| `Message` | `Id`、`UserId: MongoId`、`MessageType`、`DateTime`、`UtcDateTime`、`Member: UpdatableChatMember`、`TemplateId`、`Text`、`ReplyTo`、`HasRewards`、`RewardCollected`、`Items`、`MaxStorageTime`、`SystemData` |
| `UserDialogInfo` | `Id: MongoId`、`Aid: int?`、`Info: UserDialogDetails` |
| `UserDialogDetails` | `Nickname`、`Side`、`Level: double?`、`MemberCategory`、`SelectedMemberCategory` |
| `GetFriendListDataResponse` | `Friends: List<UserDialogInfo>`、`Ignore: List<string>`、`InIgnoreList: List<string>` |
| `GetMailDialogViewRequestData` | `Type: MessageType?`、`DialogId: MongoId`、`Limit: int?`、`Time: decimal?` |
| `GetMailDialogViewResponseData` | `Messages: List<Message>`、`Profiles: List<UserDialogInfo>`、`HasMessagesWithRewards: bool?` |
| `SendMessageRequest` | `DialogId: string`、`Type: MessageType`、`Text: string`、`ReplyTo: string` |
| `MessageType` | `UserMessage=1`、`NpcTraderMessage=2`、`SystemMessage=7`、`GlobalChat=9` …（共 15 个） |

`MongoId` 是 `readonly struct`（`long + int`），**24 位十六进制**；提供 `op_Implicit(string)`、静态 `IsValidMongoId(string)`、`Empty()`。

## 2 四个注入点与原生行为

### 2.1 `DialogueController.GetFriendList(MongoId sessionId)` → `GetFriendListDataResponse`
原生调用链：遍历 `SptProfile.FriendProfileIds` → `ProfileHelper.GetFullProfile` → `UserDialogInfo{Id, Aid, Info=`GetChatRoomMemberFromSessionId`}` → `set_Friends`，另有 `GetActiveChatBots`（SPT 假人）。
**注入方式**：Postfix，把在线/离线对端转成 `UserDialogInfo` 追加进 `__result.Friends`（按 Id 去重）。
**不写**：`FriendProfileIds` 一个元素都不加。

### 2.2 `GenerateDialogueList(MongoId sessionId)` → `List<DialogueInfo>`
原生：`DialogueHelper.GetDialogsForProfile(sessionId)` 读 `DialogueRecords` → 每个 `GetDialogueInfo(dialogue, sessionId)` → `List.Add`。
**注入方式**：Postfix 追加虚拟 `DialogueInfo{Id=虚拟 dialogId, Type=UserMessage, Users=[自己, 对端], Messages=[最近 N 条], New=未读数, AttachmentsNew=0, Pinned=false, Message=最后一条预览}`。
**不写**：`DialogueRecords` 不加键。

### 2.3 `GenerateDialogueView(GetMailDialogViewRequestData, MongoId)` → `GetMailDialogViewResponseData`
原生：`GetDialogByIdFromProfile` 从 `DialogueRecords` 取 Dialogue → 读 `Dialogue.Messages/Users` → `GetLimitedMessages` + `GetProfilesForMail` + `set_New/set_AttachmentsNew`。
**风险**：虚拟 dialogId 在 `DialogueRecords` 里不存在 → `Dialogue` 为 null → `Dialogue.get_Messages` 直接 NRE。
**注入方式**：**Prefix 短路**（`__result = 自建响应; return false`），完全不进原生体。

### 2.4 `SendMessage(MongoId sessionId, SendMessageRequest)` → `ValueTask<string>`
原生调用链（状态机实测，仅三条业务调用）：`Enumerable.FirstOrDefault`（按 DialogId 找 chat bot）→ `IDialogueChatBot.HandleMessage`；否则 `MailSendService.SendPlayerMessageToNpc`（**只有这条会写 `DialogueRecords`**：`GetProfile` → `DialogueRecords` → `Dialogue.Messages.Add` → `set_*`）。
**注入方式**：Prefix，`request.DialogId` 是虚拟 id → 走 `LinkHubClient.SendDirectAsync(peerServerId, text)` + 本地 `LinkState.AddDirect`，`__result = string.Empty`，`return false`。
**不写**：不进 `SendPlayerMessageToNpc`，`DialogueRecords` 不变。

## 3 id 派生（serverId 32 hex → MongoId 24 hex）

```
derived = sha256("sentinel.link.friend:" + serverId) 的前 20 位 hex + "1e0f"
```
- 20 + 4 = 24 位，全 hex，`IsValidMongoId` 通过；`1e0f` 作域标记，便于肉眼与冲突排查。
- `Aid`（int?）：取 sha256 后 4 字节按 `& 0x7FFFFFFF` → 稳定正数。
- 反向映射：`LinkState` 里维护 `Dictionary<MongoId, PeerRef>`，启动时按当前 peers 重建；**不落盘**（重启后 peers 会重新 hello，自然重建）。
- 冲突：启动/每次刷新时检查派生表内是否有重复 MongoId → 重复则把标记位换成 `1e0{1..9}` 并记 warning。

## 4 「存档不脏写」怎么验（写进验收）

1. 进游戏前：`sha256` + 字节数记录 `SPT_Runtime/user/profiles/<aid>.json`。
2. 打开虚拟好友对话、发 5 条、收 5 条、关面板、退回主菜单（触发一次存档）。
3. 再取 `sha256` + 字节数 + `dialogues` 键数量。
4. 判定：**sha256 相同**（最严）→ 若因原生自身心跳存档导致变化，则退一步要求 `dialogues` 条目数差 0 且我们派生的 24 hex id 在文件里出现 0 次。

## 5 降级策略

`PatchAll` 逐类 try/catch：
- `GetFriendList` / `GenerateDialogueList` 失败 → 好友与对话列表不出现虚拟条目（原生照常）；
- `GenerateDialogueView` 失败 → 打开虚拟对话会走原生并可能 NRE → 因此该补丁失败时**同时禁用**对话列表注入（成对开关）；
- `SendMessage` 失败 → 虚拟对话里发消息会落进原生（对不存在的对话）→ 同上前提下也禁用注入。
任何失败只记 error 日志，**不抛、不崩游戏**，并把状态写进 `/sentinelink/client/chat/state` 的 `p2a` 字段供面板显示。

## 6 复现命令

```powershell
# 只读侦察（与本文件同源）
Add-Type -Path "E:\EFT\SPT_Runtime\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("E:\EFT\SPT_Runtime\SPTarkov.Server.Core.dll")
# 其余见本轮对话：按类型名 dump fields / 按 AsyncStateMachineAttribute 找 <SendMessage>d__32
```
