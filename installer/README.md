# SENTINEL LINK 安装器

Tauri 2 + WebView2 的小安装器：把 SENTINEL Link 的**客户端插件**、**服务端模组**和**本地 Hub** 一次装进 SPT 目录。

窗口 520×448、无边框、Win10 走窗口区域裁圆角（Win11 走 DWM），卸载保留服务端 `config.json`。

## 装什么

载荷（`src-tauri/payload/payload.zip`）的固定结构：

```
BepInEx/plugins/SENTINEL.Link/                      客户端插件 SENTINEL.Link.Client.dll(.pdb)
SPT_Runtime/user/mods/cn.passionyang.sentinellink.server/   服务端模组 dll + package.json + config.json
SENTINEL-Link-Hub/                                  框架依赖的 Hub（双击「启动 Hub.cmd」）
```

服务端模组两种目录都兼容：有 `SPT_Runtime/user/mods` 就装那里，老版 SPT 自动落到 `user/mods`。

## 界面

内置 **中 / 英 / 俄** 三种语言与 **暖金 / 暗黑 / 亮白** 三套主题（与游戏内 F9 菜单同一套 token），右上角两个小胶囊切换，选择记在 WebView 的 localStorage。

调试时可以绕过记忆、直接用 URL 片段强制预览：`ui/index.html#dark`、`ui/index.html#light-ru`（`#主题-语言`）。页面在没有 Tauri 的环境里也能打开（`window.__TAURI__` 为空时用空实现兜底），方便截图与调色。


## 构建

```powershell
pwsh -File scripts\build-payload.ps1 -SptClientDir "E:\EFT"
Set-Location src-tauri
cargo build --release --offline
```

产物：`src-tauri\target\release\sentinel-link-installer.exe`，拷成 `dist\SENTINEL-LINK-Setup.exe` 即可分发。

只改了文档/UI、想跳过 dotnet 编译：

```powershell
pwsh -File scripts\build-payload.ps1 -SptClientDir "E:\EFT" -SkipBuild
```

版本号只有一个真相源：`..\Shared\BuildInfo.cs` 的 `Version`。`package.json`、`tauri.conf.json`、`Cargo.toml` 都跟着它走，别再手写。

## Hub

装完 Hub 在游戏根目录的 `SENTINEL-Link-Hub\`，双击 `启动 Hub.cmd` 起在 `http://127.0.0.1:5180`。要联别人的服，改服务端模组目录里的 `config.json`（`hubUrl` / `inviteKey` / `realm`）。
