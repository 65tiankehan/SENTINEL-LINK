using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SentinelLink.Client.Models
{
    /// <summary>服务端 /sentinelink/client/chat/state 的回包。</summary>
    internal sealed class LinkChatSnapshot
    {
        [JsonProperty("hubState")]
        public string HubState { get; set; } = "unlinked";

        [JsonProperty("serverId")]
        public string ServerId { get; set; } = "";

        [JsonProperty("nickname")]
        public string Nickname { get; set; } = "";

        [JsonProperty("realm")]
        public string Realm { get; set; } = "";

        [JsonProperty("peers")]
        public List<LinkPeer> Peers { get; set; } = new List<LinkPeer>();

        [JsonProperty("messages")]
        public List<LinkMessage> Messages { get; set; } = new List<LinkMessage>();

        [JsonProperty("serverTime")]
        public long ServerTime { get; set; }
    }

    internal sealed class LinkPeer
    {
        [JsonProperty("serverId")]
        public string ServerId { get; set; } = "";

        [JsonProperty("nickname")]
        public string Nickname { get; set; } = "";

        [JsonProperty("version")]
        public string Version { get; set; } = "";

        [JsonProperty("online")]
        public bool Online { get; set; }
    }

    internal sealed class LinkMessage
    {
        [JsonProperty("from")]
        public string From { get; set; } = "";

        [JsonProperty("nickname")]
        public string Nickname { get; set; } = "";

        [JsonProperty("text")]
        public string Text { get; set; } = "";

        [JsonProperty("at")]
        public long At { get; set; }

        /// <summary>是不是本机发的（界面区分颜色用）。本地不持久化，靠 serverId 比对。</summary>
        [JsonIgnore]
        public bool Local { get; set; }
    }

    internal sealed class LinkSendResult
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("hubState")]
        public string HubState { get; set; } = "";

        [JsonProperty("error")]
        public string Error { get; set; } = "";
    }
}
