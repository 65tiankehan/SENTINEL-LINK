using UnityEngine;

namespace SentinelLink.Client.UI
{
    /// <summary>
    /// 面板的宿主：只管生命周期与键位，绘制全部交给 <see cref="LinkChatPanel"/>。
    /// 挂在 DontDestroyOnLoad 的 GameObject 上，跨场景（菜单 → 战局 → 菜单）不重建。
    /// </summary>
    internal sealed class LinkChatBehaviour : MonoBehaviour
    {
        private LinkChatPanel _panel;

        private void Awake()
        {
            _panel = new LinkChatPanel();
        }

        private void Update()
        {
            if (Plugin.ToggleKey != null && Input.GetKeyDown(Plugin.ToggleKey.Value))
            {
                _panel.Toggle();
            }

            if (_panel.Visible && Input.GetKeyDown(KeyCode.Escape))
            {
                _panel.Close();
            }

            _panel.Tick();
        }

        private void OnGUI()
        {
            if (!_panel.Visible)
            {
                return;
            }

            _panel.Draw();
        }

        private void OnDestroy()
        {
            _panel?.Dispose();
        }
    }
}
