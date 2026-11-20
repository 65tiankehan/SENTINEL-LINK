using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SPT.Common.Http;
using SentinelLink.Client.Models;

namespace SentinelLink.Client.Core
{
    /// <summary>
    /// 客户端 → 本机 SPT 服务端的薄封装。
    ///
    /// 走 SPT 自己的 <see cref="RequestHandler"/>：它已经处理好端口、自签证书和 sessionId，
    /// 我们不多带一份 HttpClient，也不碰证书校验。所有方法都把异常吞成 false/null ——
    /// 面板层只需要知道「这次没拿到」，不该让异常冒到 OnGUI 里。
    /// </summary>
    internal static class LinkApi
    {
        private const string StatePath = "/sentinelink/client/chat/state";
        private const string SendPath = "/sentinelink/client/chat/send";

        public static async Task<LinkChatSnapshot> FetchStateAsync(long sinceMs)
        {
            try
            {
                string body = "{\"sinceMs\":" + sinceMs.ToString() + "}";
                string json = await RequestHandler.PostJsonAsync(StatePath, body).ConfigureAwait(false);
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<LinkChatSnapshot>(json);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[Link] state 拉取失败：" + ex.Message);
                return null;
            }
        }

        public static async Task<LinkSendResult> SendAsync(string text)
        {
            try
            {
                string body = JsonConvert.SerializeObject(new { text });
                string json = await RequestHandler.PostJsonAsync(SendPath, body).ConfigureAwait(false);
                if (string.IsNullOrEmpty(json))
                {
                    return new LinkSendResult { Ok = false, Error = "空回包" };
                }

                return JsonConvert.DeserializeObject<LinkSendResult>(json);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[Link] 发送失败：" + ex.Message);
                return new LinkSendResult { Ok = false, Error = ex.Message };
            }
        }
    }
}
