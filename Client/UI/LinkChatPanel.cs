using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx.Configuration;
using SentinelLink.Client.Core;
using SentinelLink.Client.Models;
using SentinelLink.Shared;
using SentinelMin.UI;
using UnityEngine;

namespace SentinelLink.Client.UI
{
    /// <summary>
    /// 世界聊天面板（P1）。
    ///
    /// 数据流：本机 SPT 服务端轮询（2 s 一次）→ 面板只读快照；发送走一次 POST。
    /// 线程模型：网络在后台线程，结果先落在锁里，绘制时再合并 —— OnGUI 里绝不 await。
    /// </summary>
    internal sealed class LinkChatPanel
    {
        private const float Width = 436f;
        private const float Margin = 24f;
        private const float HeaderHeight = 68f;
        private const float InputHeight = 48f;
        private const float FooterHeight = 30f;
        private const float RowHeight = 42f;
        private const float PollInterval = 2f;
        private const int HistoryLimit = 200;

        private readonly object _sync = new object();
        private readonly List<LinkMessage> _messages = new List<LinkMessage>();
        private readonly ConfigEntry<KeyCode> _toggleKey;

        private bool _visible;
        private string _input = "";
        private Vector2 _scroll;
        private long _sinceMs;
        private float _nextPoll;
        private bool _polling;
        private bool _sending;
        private string _hubState = "unlinked";
        private string _realm = "default";
        private string _ownServerId = "";
        private int _peerCount;
        private string _pendingStatus;
        private bool _pendingBad;
        private string _status = "";
        private bool _statusBad;
        private float _statusUntil;
        private GUIStyle _inputStyle;

        // 打开面板前的输入状态，关闭时原样还回去
        private CursorLockMode _prevLockState;
        private bool _prevCursorVisible;
        private IMECompositionMode _prevImeMode;

        public bool Visible => _visible;

        public LinkChatPanel(ConfigEntry<KeyCode> toggleKey)
        {
            _toggleKey = toggleKey;
        }

        public void Toggle()
        {
            if (_visible)
            {
                Close();
                return;
            }

            _visible = true;
            _nextPoll = 0f;
            _status = "";
            EnterInputMode();
        }

        public void Close()
        {
            if (!_visible)
            {
                return;
            }

            _visible = false;
            ExitInputMode();
        }

        /// <summary>
        /// 接管输入：解锁光标（不然鼠标被锁在屏幕中心点不到输入框）、
        /// 打开输入法（不然中文候选词出不来）、掐住角色输入（不然边打字边跑边开枪）。
        /// </summary>
        private void EnterInputMode()
        {
            _prevLockState = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _prevImeMode = Input.imeCompositionMode;
            Input.imeCompositionMode = IMECompositionMode.On;

            Patches.LinkInputLock.Locked = true;
        }

        private void ExitInputMode()
        {
            Patches.LinkInputLock.Locked = false;
            Cursor.lockState = _prevLockState;
            Cursor.visible = _prevCursorVisible;
            Input.imeCompositionMode = _prevImeMode;
        }

        public void Dispose()
        {
            _inputStyle = null;
        }

        // ── 后台轮询（每 2 s）──────────────────────────────────────────────
        public void Tick()
        {
            // 键位：配置键 / F11 / Ctrl+L 三个入口（F 键在 EFT 菜单里可能被吞，Ctrl+L 兜底）
            bool toggle = false;
            if (_toggleKey != null && Input.GetKeyDown(_toggleKey.Value))
            {
                toggle = true;
            }
            else if (Input.GetKeyDown(KeyCode.F11))
            {
                toggle = true;
            }
            else if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.L))
            {
                toggle = true;
            }

            if (toggle)
            {
                Toggle();
                Plugin.Log?.LogInfo("[Link] 面板 " + (_visible ? "打开" : "关闭"));
            }

            // EFT 自己每帧会去锁光标，我们放开后要持续压回去，否则鼠标一闪就又被吃走
            if (_visible)
            {
                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                }

                if (!Cursor.visible)
                {
                    Cursor.visible = true;
                }
            }

            lock (_sync)
            {
                if (!string.IsNullOrEmpty(_pendingStatus))
                {
                    _status = _pendingStatus;
                    _statusBad = _pendingBad;
                    _statusUntil = Time.realtimeSinceStartup + 4f;
                    _pendingStatus = null;
                }
            }

            if (!_visible || _polling || Time.realtimeSinceStartup < _nextPoll)
            {
                return;
            }

            _polling = true;
            _nextPoll = Time.realtimeSinceStartup + PollInterval;
            long since = _sinceMs;

            Task.Run(async () =>
            {
                LinkChatSnapshot snapshot = await LinkApi.FetchStateAsync(since);
                lock (_sync)
                {
                    if (snapshot != null && !snapshot.Messages.IsEmpty())
                    {
                        Merge(snapshot);
                    }

                    _polling = false;
                }
            });
        }

        private void Merge(LinkChatSnapshot snapshot)
        {
            _hubState = string.IsNullOrEmpty(snapshot.HubState) ? "unlinked" : snapshot.HubState;
            _realm = string.IsNullOrEmpty(snapshot.Realm) ? "default" : snapshot.Realm;

            // Hub 的 peers 列表**包含自己**，面板上的「对端在线」必须只算别人且只算 online，
            // 否则一个对端都没有时会显示「1 个」（自己）——P2a 真机截图踩过。
            int onlinePeers = 0;
            if (snapshot.Peers != null)
            {
                foreach (LinkPeer peer in snapshot.Peers)
                {
                    if (peer == null || !peer.Online)
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(snapshot.ServerId) && peer.ServerId == snapshot.ServerId)
                    {
                        continue;
                    }
                    onlinePeers++;
                }
            }
            _peerCount = onlinePeers;

            // 服务端把自己的 serverId 一起报上来，才能把「我发的」和「别人发的」分开画。
            if (!string.IsNullOrEmpty(snapshot.ServerId) && snapshot.ServerId != _ownServerId)
            {
                _ownServerId = snapshot.ServerId;
                foreach (LinkMessage existing in _messages)
                {
                    existing.Local = existing.From == _ownServerId;
                }
            }

            foreach (LinkMessage message in snapshot.Messages)
            {
                bool duplicate = false;
                for (int i = 0; i < _messages.Count; i++)
                {
                    LinkMessage existing = _messages[i];
                    if (existing.At == message.At &&
                        existing.From == message.From &&
                        existing.Text == message.Text)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(_ownServerId) == false)
                {
                    message.Local = message.From == _ownServerId;
                }

                _messages.Add(message);
                if (message.At >= _sinceMs)
                {
                    _sinceMs = message.At + 1;
                }
            }

            if (_messages.Count > HistoryLimit)
            {
                _messages.RemoveRange(0, _messages.Count - HistoryLimit);
            }

            _messages.Sort((a, b) => a.At.CompareTo(b.At));
        }

        /// <summary>往页脚状态位塞一句话（4 秒后自动退回提示文案）。</summary>
        private void Flash(string text, bool bad)
        {
            lock (_sync)
            {
                _pendingStatus = text;
                _pendingBad = bad;
            }
        }

        /// <summary>
        /// 从系统剪贴板贴一段文字进输入框。
        /// IMGUI 的 TextField 吃不到 Windows 输入法的候选词，所以中文用「别处打好再贴」这条路。
        /// </summary>
        private void PasteClipboard()
        {
            string clip;
            try
            {
                clip = GUIUtility.systemCopyBuffer;
            }
            catch (Exception ex)
            {
                Flash("读剪贴板失败：" + ex.GetType().Name, true);
                return;
            }

            if (string.IsNullOrEmpty(clip))
            {
                Flash("剪贴板是空的", true);
                return;
            }

            clip = clip.Replace("\r", " ").Replace("\n", " ").Trim();
            if (clip.Length == 0)
            {
                Flash("剪贴板里没有文字", true);
                return;
            }

            string merged = (_input ?? "") + clip;
            _input = merged.Length > 200 ? merged.Substring(0, 200) : merged;
            Flash("已粘贴 " + clip.Length + " 字", false);
        }

        private void Send()
        {
            string text = (_input ?? "").Trim();
            if (text.Length == 0 || _sending)
            {
                return;
            }

            _input = "";
            _sending = true;

            Task.Run(async () =>
            {
                LinkSendResult result = await LinkApi.SendAsync(text);
                lock (_sync)
                {
                    _pendingStatus = result != null && result.Ok
                        ? "已发送"
                        : "发送失败：" + (result?.Error ?? "未知");
                    _pendingBad = result == null || !result.Ok;
                    if (result != null && !string.IsNullOrEmpty(result.HubState))
                    {
                        _hubState = result.HubState;
                    }

                    _sending = false;
                }
            });
        }

        // ── 绘制 ───────────────────────────────────────────────────────────
        public void Draw()
        {
            MinDraw.Init();

            float scale = Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.75f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            try
            {
                float viewWidth = Screen.width / scale;
                float viewHeight = Screen.height / scale;
                float height = Mathf.Min(560f, viewHeight - Margin * 2f);
                Rect panel = new Rect(viewWidth - Width - Margin, Margin, Width, height);

                // 投影 + 主体（与 Min 面板同一套圆角/描边，视觉上像一家人）
                MinDraw.Round(new Rect(panel.x - 4f, panel.y + 10f, panel.width + 8f, panel.height), new Color(0f, 0f, 0f, 0.35f), MinTheme.PanelRadius);
                MinDraw.Outlined(panel, MinTheme.PanelBg, MinTheme.PanelBorder, MinTheme.PanelRadius);

                float y = panel.y;
                DrawHeader(panel, ref y);
                DrawMessages(panel, ref y);
                DrawInput(panel, ref y);
                DrawFooter(panel);
            }
            finally
            {
                GUI.matrix = previous;
            }
        }

        private void DrawHeader(Rect panel, ref float y)
        {
            float x = panel.x + MinTheme.PadPanel;
            float innerWidth = panel.width - MinTheme.PadPanel * 2f;

            MinDraw.Chip(new Rect(x, y + 14f, 34f, 34f), MinTheme.Blue, "链");
            MinDraw.Text(new Rect(x + 44f, y + 10f, innerWidth - 44f, 22f), "SENTINEL LINK", 15, MinTheme.LabelPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            MinDraw.Text(new Rect(x + 44f, y + 30f, innerWidth - 44f, 16f), SentinelLink.Shared.BuildInfo.DisplayLine + "  ·  v" + SentinelLink.Shared.BuildInfo.Version, 10, MinTheme.LabelTertiary);

            Color stateColor = StateColor(_hubState);
            string stateText = LocalizeState(_hubState);
            float pillWidth = Mathf.Max(76f, MinDraw.Measure(stateText, 10).x + 24f);
            MinDraw.Round(new Rect(panel.x + panel.width - MinTheme.PadPanel - pillWidth, y + 18f, pillWidth, 22f), MinTheme.Chip(stateColor), 11f);
            MinDraw.Text(new Rect(panel.x + panel.width - MinTheme.PadPanel - pillWidth, y + 18f, pillWidth, 22f), stateText, 10, stateColor, TextAnchor.MiddleCenter);

            y += 52f;
            MinDraw.Text(new Rect(x, y, innerWidth, 16f),
                "realm " + _realm + "   ·   " + _peerCount + " 个对端在线（不含自己）",
                10, MinTheme.LabelTertiary);
            y += 20f;
            MinDraw.Line(new Rect(x, y, innerWidth, 1f), MinTheme.Separator);
            y += 8f;
        }

        private void DrawMessages(Rect panel, ref float y)
        {
            float x = panel.x + MinTheme.PadPanel;
            float innerWidth = panel.width - MinTheme.PadPanel * 2f;
            float available = panel.y + panel.height - InputHeight - FooterHeight - y - 8f;
            Rect view = new Rect(x, y, innerWidth, available);

            List<LinkMessage> snapshot;
            lock (_sync)
            {
                snapshot = new List<LinkMessage>(_messages);
            }

            float contentHeight = Mathf.Max(available, snapshot.Count * RowHeight + 4f);

            // 滚轮：向上滚看历史，向下滚回底部
            Event current = Event.current;
            if (current != null && current.type == EventType.ScrollWheel && view.Contains(current.mousePosition))
            {
                _scroll.y = Mathf.Clamp(_scroll.y + current.delta.y * 26f, 0f, Mathf.Max(0f, contentHeight - available));
                current.Use();
            }

            if (snapshot.Count == 0)
            {
                MinDraw.Text(new Rect(x, y + 8f, innerWidth, 20f), "还没有人说话。发第一条吧。", 11, MinTheme.LabelTertiary);
                y += available;
                return;
            }

            if (contentHeight > available)
            {
                _scroll = GUI.BeginScrollView(view, _scroll,
                    new Rect(0f, 0f, innerWidth - 16f, contentHeight), false, true,
                    GUIStyle.none, GUIStyle.none);
            }

            float rowY = 0f;
            float offsetX = contentHeight > available ? 0f : x;
            float offsetY = contentHeight > available ? 0f : y;

            foreach (LinkMessage message in snapshot)
            {
                Rect row = new Rect(offsetX, offsetY + rowY, innerWidth - (contentHeight > available ? 16f : 0f), RowHeight - 4f);
                Color accent = message.Local ? MinTheme.Green : MinTheme.Blue;
                MinDraw.Text(new Rect(row.x, row.y, row.width, 16f),
                    TimeSpan(message.At) + "  " + (message.Local ? "我" : Short(message.Nickname, 18)),
                    10, message.Local ? MinTheme.Green : MinTheme.LabelTertiary);

                string text = Fit(message.Text, 12, row.width - 8f);
                MinDraw.Text(new Rect(row.x, row.y + 17f, row.width, 18f), text, 12, MinTheme.LabelPrimary);

                rowY += RowHeight;
            }

            if (contentHeight > available)
            {
                GUI.EndScrollView();
            }

            y += available;
        }

        private void DrawInput(Rect panel, ref float y)
        {
            float x = panel.x + MinTheme.PadPanel;
            float innerWidth = panel.width - MinTheme.PadPanel * 2f;
            float buttonWidth = 74f;

            Rect field = new Rect(x, y + 4f, innerWidth - buttonWidth - 8f, 34f);
            Rect button = new Rect(x + innerWidth - buttonWidth, y + 4f, buttonWidth, 34f);

            if (_inputStyle == null)
            {
                // 背景交给 MinDraw 画（圆角 + 描边），这里只留透明底的文字层，
                // 否则 GUI.color 会把文字一起染色，字会看不见。
                _inputStyle = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(12, 10, 4, 4),
                    border = new RectOffset(0, 0, 0, 0),
                    normal = { textColor = MinTheme.LabelPrimary, background = null },
                    focused = { textColor = MinTheme.LabelPrimary, background = null },
                    hover = { textColor = MinTheme.LabelPrimary, background = null },
                    active = { textColor = MinTheme.LabelPrimary, background = null }
                };
            }

            bool focused = GUI.GetNameOfFocusedControl() == "link-input";
            MinDraw.Outlined(field, MinTheme.Alpha(MinTheme.CardBg, 0.9f),
                focused ? MinTheme.Alpha(MinTheme.Blue, 0.55f) : MinTheme.CardBorder, 12f);

            GUI.SetNextControlName("link-input");
            _input = GUI.TextField(field, _input ?? "", 200, _inputStyle);

            if (GUI.GetNameOfFocusedControl() == "link-input")
            {
                Event current = Event.current;
                if (current != null && current.type == EventType.KeyDown)
                {
                    if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                    {
                        Send();
                        current.Use();
                    }
                    else if (current.keyCode == KeyCode.V && (current.control || current.command))
                    {
                        // 输入法在 IMGUI 里不稳，给一条确定能用的中文入口：别的软件里打好，Ctrl+V 贴进来
                        PasteClipboard();
                        current.Use();
                    }
                }
            }

            string label = _sending ? "发送中…" : "发送";
            if (MinDraw.Primary(button, label, ""))
            {
                Send();
            }

            y += InputHeight;
        }

        private void DrawFooter(Rect panel)
        {
            float x = panel.x + MinTheme.PadPanel;
            float width = panel.width - MinTheme.PadPanel * 2f;
            float y = panel.y + panel.height - FooterHeight;

            string left = Time.realtimeSinceStartup < _statusUntil && !string.IsNullOrEmpty(_status)
                ? _status
                : "回车发送 · Ctrl+V 粘贴中文 · Esc 关闭";
            Color color = Time.realtimeSinceStartup < _statusUntil && _statusBad
                ? MinTheme.Red
                : MinTheme.LabelTertiary;

            MinDraw.Text(new Rect(x, y, width - 90f, 20f), left, 10, color);
            MinDraw.Text(new Rect(x + width - 90f, y, 90f, 20f), "F11 / Ctrl+L", 10, MinTheme.LabelTertiary, TextAnchor.MiddleRight);
        }

        // ── 小工具 ─────────────────────────────────────────────────────────
        private static Color StateColor(string state)
        {
            switch (state)
            {
                case "online":
                    return MinTheme.Green;
                case "handshaking":
                    return MinTheme.Orange;
                case "auth-failed":
                    return MinTheme.Red;
                default:
                    return MinTheme.LabelSecondary;
            }
        }

        private static string LocalizeState(string state)
        {
            switch (state)
            {
                case "online":
                    return "已连接 Hub";
                case "handshaking":
                    return "握手中…";
                case "auth-failed":
                    return "邀请码错误";
                default:
                    return "未连接";
            }
        }

        private static string TimeSpan(long at)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("HH:mm");
            }
            catch
            {
                return "--:--";
            }
        }

        private static string Short(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "匿名";
            }

            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        /// <summary>按像素宽度裁剪：MinDraw.Text 是单行绘制，超宽会被硬切，先自己加省略号。</summary>
        private static string Fit(string text, int size, float width)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            if (MinDraw.Measure(text, size).x <= width)
            {
                return text;
            }

            string result = text;
            while (result.Length > 1 && MinDraw.Measure(result + "…", size).x > width)
            {
                result = result.Substring(0, result.Length - 1);
            }

            return result + "…";
        }
    }

    internal static class LinkMessageListExtensions
    {
        public static bool IsEmpty(this List<LinkMessage> list)
        {
            return list == null || list.Count == 0;
        }
    }
}
