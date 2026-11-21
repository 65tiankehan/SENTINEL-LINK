#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::fs;
use std::io::{Cursor, Write};
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use tauri::Manager;

const PAYLOAD: &[u8] = include_bytes!("../payload/payload.zip");
const PRODUCT: &str = "SENTINEL LINK";
const VERSION: &str = env!("CARGO_PKG_VERSION");
const CLIENT_DIR: &str = "BepInEx/plugins/SENTINEL.Link";
const CLIENT_DLL: &str = "SENTINEL.Link.Client.dll";
const SERVER_DIR: &str = "SPT_Runtime/user/mods/cn.passionyang.sentinellink.server";
const HUB_DIR: &str = "SENTINEL-Link-Hub";
const CLIENT_CFG: &str = "BepInEx/config/cn.passionyang.sentinellink.client.cfg";

#[derive(Serialize, Clone, Default)]
pub struct Detection {
    pub root: String,
    pub has_game: bool,
    pub has_bepinex: bool,
    pub has_server_root: bool,
    pub has_hub: bool,
    pub installed: bool,
    pub message: String,
}

#[derive(Serialize, Clone, Default)]
pub struct Report {
    pub ok: bool,
    pub files: Vec<String>,
    pub bytes: u64,
    pub message: String,
}

/// 跨次启动要记住的两件事：引导看过没有、上次装到哪个目录。
#[derive(Serialize, Deserialize, Clone, Default)]
pub struct AppState {
    #[serde(default)]
    pub intro_seen: bool,
    #[serde(default)]
    pub last_root: String,
}

fn state_file() -> PathBuf {
    let base = std::env::var("LOCALAPPDATA")
        .ok()
        .filter(|value| !value.trim().is_empty())
        .map(PathBuf::from)
        .unwrap_or_else(|| std::env::temp_dir());
    base.join("SENTINEL-LINK").join("state.json")
}

fn read_state() -> AppState {
    match fs::read_to_string(state_file()) {
        Ok(text) => serde_json::from_str::<AppState>(&text).unwrap_or_default(),
        Err(_) => AppState::default(),
    }
}

fn persist_state(state: &AppState) {
    let path = state_file();
    if let Some(parent) = path.parent() {
        let _ = fs::create_dir_all(parent);
    }
    if let Ok(text) = serde_json::to_string_pretty(state) {
        let _ = fs::write(path, text);
    }
}

fn normalize_root(input: &str) -> PathBuf {
    let trimmed = input.trim().trim_matches('"');
    PathBuf::from(trimmed)
}

fn check(root: &Path) -> Detection {
    let has_game = root.join("EscapeFromTarkov.exe").is_file();
    let has_bepinex = root.join("BepInEx").is_dir();
    let has_server_root =
        root.join("SPT_Runtime/user/mods").is_dir() || root.join("user/mods").is_dir();
    let has_hub = root.join(HUB_DIR).join("启动 Hub.cmd").is_file();
    let installed = root.join(CLIENT_DIR).join(CLIENT_DLL).is_file();

    let mut missing = Vec::new();
    if !has_game {
        missing.push("找不到 EscapeFromTarkov.exe");
    }
    if !has_bepinex {
        missing.push("找不到 BepInEx");
    }
    if !has_server_root {
        missing.push("找不到 SPT_Runtime/user/mods");
    }

    Detection {
        root: root.to_string_lossy().to_string(),
        has_game,
        has_bepinex,
        has_server_root,
        has_hub,
        installed,
        message: if missing.is_empty() {
            "环境正常".to_string()
        } else {
            missing.join("；")
        },
    }
}

fn extract_to(root: &Path) -> Result<(Vec<String>, u64), String> {
    let mut archive =
        zip::ZipArchive::new(Cursor::new(PAYLOAD)).map_err(|e| format!("载荷损坏：{e}"))?;
    let mut files = Vec::new();
    let mut bytes = 0u64;

    for index in 0..archive.len() {
        let mut entry = archive
            .by_index(index)
            .map_err(|e| format!("读取载荷失败：{e}"))?;
        let name = entry.name().replace('\\', "/");
        if name.contains("..") {
            continue;
        }
        if name.ends_with('/') {
            continue;
        }

        // 老版 SPT 把模组放在 user/mods，新版放在 SPT_Runtime/user/mods，按实际存在的那条落盘。
        let relative = if name.starts_with("SPT_Runtime/")
            && !root.join("SPT_Runtime/user/mods").is_dir()
        {
            name.trim_start_matches("SPT_Runtime/").to_string()
        } else {
            name.clone()
        };

        let target = root.join(relative.replace('/', std::path::MAIN_SEPARATOR_STR));
        if let Some(parent) = target.parent() {
            fs::create_dir_all(parent)
                .map_err(|e| format!("建目录失败 {}：{e}", parent.display()))?;
        }

        let mut buffer = Vec::new();
        std::io::copy(&mut entry, &mut buffer).map_err(|e| format!("解压失败 {name}：{e}"))?;
        let mut file =
            fs::File::create(&target).map_err(|e| format!("写入失败 {}：{e}", target.display()))?;
        file.write_all(&buffer)
            .map_err(|e| format!("写入失败 {}：{e}", target.display()))?;

        bytes += buffer.len() as u64;
        files.push(relative);
    }

    Ok((files, bytes))
}

#[tauri::command]
fn detect_path(root: String) -> Detection {
    check(&normalize_root(&root))
}

#[tauri::command]
fn load_state() -> AppState {
    read_state()
}

#[tauri::command]
fn save_state(intro_seen: bool, last_root: String) -> bool {
    let mut state = read_state();
    state.intro_seen = intro_seen;
    if !last_root.trim().is_empty() {
        state.last_root = last_root;
    }
    persist_state(&state);
    true
}

#[tauri::command]
fn install(root: String) -> Report {
    let base = normalize_root(&root);
    if !base.join("BepInEx").is_dir() {
        return Report {
            ok: false,
            message: "这个目录里没有 BepInEx，先确认选的是游戏根目录。".into(),
            ..Default::default()
        };
    }

    match extract_to(&base) {
        Ok((files, bytes)) => {
            write_uninstall_entry(&base);
            let mut state = read_state();
            state.intro_seen = true;
            state.last_root = base.to_string_lossy().to_string();
            persist_state(&state);
            Report {
                ok: true,
                message: format!("已安装 {} 个文件（{} KB）", files.len(), bytes / 1024),
                files,
                bytes,
            }
        }
        Err(error) => Report {
            ok: false,
            message: error,
            ..Default::default()
        },
    }
}

#[tauri::command]
fn uninstall(root: String, keep_config: bool) -> Report {
    let base = normalize_root(&root);
    let mut removed = Vec::new();

    for dir in [CLIENT_DIR, SERVER_DIR, HUB_DIR] {
        let path = base.join(dir.replace('/', std::path::MAIN_SEPARATOR_STR));
        if path.exists() {
            let _ = fs::remove_dir_all(&path);
            removed.push(dir.to_string());
        }
    }
    let legacy = base.join("user/mods/cn.passionyang.sentinellink.server");
    if legacy.exists() {
        let _ = fs::remove_dir_all(&legacy);
        removed.push("user/mods/cn.passionyang.sentinellink.server".to_string());
    }

    if !keep_config {
        let config = base.join(CLIENT_CFG.replace('/', std::path::MAIN_SEPARATOR_STR));
        if config.is_file() {
            let _ = fs::remove_file(&config);
            removed.push(CLIENT_CFG.to_string());
        }
    }

    remove_uninstall_entry();

    Report {
        ok: true,
        files: removed,
        bytes: 0,
        message: if keep_config {
            "已卸载，服务端的 config.json 保留（重装后直连原来的 Hub）".into()
        } else {
            "已卸载，客户端配置一并删除".into()
        },
    }
}

#[tauri::command]
fn open_path(path: String) {
    let target = normalize_root(&path);
    if target.exists() {
        let _ = std::process::Command::new("explorer").arg(target).spawn();
    }
}

#[tauri::command]
fn close_window(window: tauri::Window) {
    let _ = window.close();
}

#[tauri::command]
fn product_info() -> serde_json::Value {
    serde_json::json!({
        "name": PRODUCT,
        "version": VERSION,
        "client": CLIENT_DIR,
        "server": SERVER_DIR,
        "hub": HUB_DIR,
        "payloadBytes": PAYLOAD.len(),
    })
}

#[cfg(windows)]
fn write_uninstall_entry(root: &Path) {
    use winreg::enums::HKEY_CURRENT_USER;
    use winreg::RegKey;

    let exe = std::env::current_exe().unwrap_or_default();
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    if let Ok(key) =
        hkcu.create_subkey(r"Software\Microsoft\Windows\CurrentVersion\Uninstall\SENTINEL LINK")
    {
        let key = key.0;
        let _ = key.set_value("DisplayName", &PRODUCT);
        let _ = key.set_value("DisplayVersion", &VERSION);
        let _ = key.set_value("Publisher", &"passionYang");
        let _ = key.set_value("InstallLocation", &root.to_string_lossy().to_string());
        let _ = key.set_value(
            "UninstallString",
            &format!("\"{}\" --uninstall", exe.to_string_lossy()),
        );
        let _ = key.set_value("NoModify", &1u32);
        let _ = key.set_value("NoRepair", &1u32);
    }
}

#[cfg(not(windows))]
fn write_uninstall_entry(_root: &Path) {}

#[cfg(windows)]
fn remove_uninstall_entry() {
    use winreg::enums::HKEY_CURRENT_USER;
    use winreg::RegKey;

    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let _ = hkcu.delete_subkey_all(r"Software\Microsoft\Windows\CurrentVersion\Uninstall\SENTINEL LINK");
}

#[cfg(not(windows))]
fn remove_uninstall_entry() {}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![
            detect_path,
            load_state,
            save_state,
            install,
            uninstall,
            open_path,
            close_window,
            product_info
        ])
        .setup(|app| {
            // WebView2 会跟随系统「放大文字/缩放」把页面按 1.25 渲染，窗口却是 1:1 物理像素，
            // 结果内容比窗口大、右下角按钮被裁掉。强制 1.0 让设计稿的像素等于窗口像素。
            apply_window_rounding(app.handle());
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.set_zoom(1.0);
                let _ = window.set_size(tauri::LogicalSize::new(520.0, 448.0));
            }
            if std::env::args().any(|arg| arg == "--uninstall") {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.eval("window.__START_UNINSTALL__ = true;");
                }
            }
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("SENTINEL LINK 安装器启动失败");
}

/// 无边框透明窗请求 DWM 圆角（Win11 一致）：WebView2 在本机不合成透明，
/// 靠窗口区域的圆角裁切才能得到真正的圆角，而不是四角方形。
#[cfg(windows)]
fn apply_window_rounding(app: &tauri::AppHandle) {
    use tauri::Manager;
    if let Some(w) = app.get_webview_window("main") {
        if let Ok(hwnd) = w.hwnd() {
            use windows_sys::Win32::Graphics::Dwm::{
                DwmSetWindowAttribute, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND,
            };
            let pref = DWMWCP_ROUND as u32;
            unsafe {
                // Win11 才认；Win10 返回错误码，下面用窗口区域兜底
                DwmSetWindowAttribute(
                    hwnd.0,
                    DWMWA_WINDOW_CORNER_PREFERENCE as u32,
                    &pref as *const u32 as *const core::ffi::c_void,
                    4,
                );

                use windows_sys::Win32::Foundation::RECT;
                use windows_sys::Win32::Graphics::Gdi::{CreateRoundRectRgn, SetWindowRgn};
                use windows_sys::Win32::UI::WindowsAndMessaging::GetWindowRect;

                let mut rect: RECT = core::mem::zeroed();
                if GetWindowRect(hwnd.0, &mut rect) != 0 {
                    let width = rect.right - rect.left;
                    let height = rect.bottom - rect.top;
                    let radius = (18.0f32 * (width as f32 / 520.0)).round() as i32;
                    let rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
                    if !rgn.is_null() {
                        SetWindowRgn(hwnd.0, rgn, 1);
                    }
                }
            }
        }
    }
}

#[cfg(not(windows))]
fn apply_window_rounding(_app: &tauri::AppHandle) {}