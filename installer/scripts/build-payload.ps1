# 生成 src-tauri/payload/payload.zip —— 安装器内嵌的那份载荷。
#
# 用法：
#   pwsh -File scripts\build-payload.ps1
#   pwsh -File scripts\build-payload.ps1 -SptClientDir "E:\EFT" -SkipBuild
#
# 载荷结构（固定，不许漂）：
#   BepInEx/plugins/SENTINEL.Link/{SENTINEL.Link.Client.dll,SENTINEL.Link.Client.pdb}
#   SPT_Runtime/user/mods/cn.passionyang.sentinellink.server/{SENTINEL.Link.Server.dll,package.json,config.json}
#   SENTINEL-Link-Hub/{SENTINEL.Link.Hub.exe,SENTINEL.Link.Hub.dll,...,启动 Hub.cmd}

[CmdletBinding()]
param(
    [string]$SptClientDir = "E:\EFT",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$installer = Split-Path -Parent $here
$repo = Split-Path -Parent $installer
$stage = Join-Path $installer ".stage"
$payload = Join-Path $installer "src-tauri\payload\payload.zip"

if (-not $SkipBuild) {
    Write-Host "[1/5] 编译客户端插件"
    & dotnet build (Join-Path $repo "Client\SENTINEL.Link.Client.csproj") -c Release "-p:SptClientDir=$SptClientDir" --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "客户端构建失败" }

    Write-Host "[2/5] 编译服务端模组"
    & dotnet build (Join-Path $repo "Server\SENTINEL.Link.Server.csproj") -c Release "-p:SptPath=$SptClientDir\SPT_Runtime" --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "服务端构建失败" }
}

Write-Host "[3/5] 组装载荷目录"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
$clientDir = Join-Path $stage "BepInEx\plugins\SENTINEL.Link"
$serverDir = Join-Path $stage "SPT_Runtime\user\mods\cn.passionyang.sentinellink.server"
$hubDir = Join-Path $stage "SENTINEL-Link-Hub"
New-Item -ItemType Directory -Force -Path $clientDir, $serverDir, $hubDir | Out-Null

Copy-Item (Join-Path $repo "Client\bin\Release\SENTINEL.Link.Client.dll") $clientDir -Force
$pdb = Join-Path $repo "Client\bin\Release\SENTINEL.Link.Client.pdb"
if (Test-Path $pdb) { Copy-Item $pdb $clientDir -Force }

Copy-Item (Join-Path $repo "Server\bin\Release\net10.0\SENTINEL.Link.Server.dll") $serverDir -Force

Write-Host "[4/5] 发布 Hub（框架依赖，需 .NET 10 运行时）"
& dotnet publish (Join-Path $repo "Hub\SENTINEL.Link.Hub.csproj") -c Release -o $hubDir --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Hub 发布失败" }
Get-ChildItem $hubDir -Filter *.pdb -File | Remove-Item -Force

# 版本只认 Shared\BuildInfo.cs 一个真相源
$buildInfo = Get-Content (Join-Path $repo "Shared\BuildInfo.cs") -Raw
$modVersion = [regex]::Match($buildInfo, 'Version\s*=\s*"([0-9.]+)"').Groups[1].Value
if (-not $modVersion) { throw "读不到 Shared\BuildInfo.cs 的 Version" }
Write-Host "载荷版本 = $modVersion"

$serverJson = @(
    '{'
    '  "name": "sentinel-link-server",'
    ('  "version": "' + $modVersion + '",')
    '  "author": "SENTINEL",'
    '  "license": "MIT",'
    '  "description": "SENTINEL Link server-side: 接 Hub、注入原生好友与聊天。"'
    '}'
) -join "`r`n"
Set-Content (Join-Path $serverDir "package.json") -Value $serverJson -Encoding UTF8

# 服务端模组首次启动会自己写 config.json；这里给一份带注释的默认档，改 hubUrl 就能连别人。
$configJson = @(
    '{'
    '  "enabled": true,'
    '  "hubUrl": "http://127.0.0.1:5180",'
    '  "inviteKey": "dev-local",'
    '  "realm": "default",'
    '  "nickname": "SENTINEL Link",'
    '  "hubState": "idle"'
    '}'
) -join "`r`n"
Set-Content (Join-Path $serverDir "config.json") -Value $configJson -Encoding UTF8

# Hub 启动脚本：双击就能跑，缺 dotnet 时给出人话提示
$launcher = @(
    '@echo off'
    'chcp 65001 >nul'
    'title SENTINEL Link Hub'
    'where dotnet >nul 2>nul'
    'if errorlevel 1 ('
    '  echo 需要 .NET 10 运行时：https://dotnet.microsoft.com/download/dotnet/10.0'
    '  pause'
    '  exit /b 1'
    ')'
    'echo SENTINEL Link Hub 监听 http://127.0.0.1:5180'
    'echo 关掉这个窗口 = Hub 停止。'
    'dotnet "%~dp0SENTINEL.Link.Hub.dll"'
    'pause'
) -join "`r`n"
Set-Content (Join-Path $hubDir "启动 Hub.cmd") -Value $launcher -Encoding OEM

Write-Host "[5/5] 打包 payload.zip"
Remove-Item $payload -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $payload,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force

$size = (Get-Item $payload).Length
Write-Host ("payload.zip = {0:N0} B" -f $size)
Write-Host "下一步：cd src-tauri; cargo build --release --offline"