﻿using linker.server;
using System.Text.Json.Serialization;

namespace linker.client
{
    /// <summary>
    /// 登入对象
    /// </summary>
    public sealed class ClientSignInState
    {
        /// <summary>
        /// 登入服务器的连接对象
        /// </summary>
        [JsonIgnore]
        public IConnection Connection { get; set; }

        [JsonIgnore]
        public bool connecting = false;
        public bool Connecting => connecting;
        public bool Connected => Connection != null && Connection.Connected;

        public string Version { get; set; }

        private int networdkEnabledTimes = 0;
        /// <summary>
        /// 上线事件
        /// </summary>
        [JsonIgnore]
        public Action<int> NetworkEnabledHandle { get; set; }
        /// <summary>
        /// 第一次上线
        /// </summary>
        [JsonIgnore]
        public Action NetworkFirstEnabledHandle { get; set; }
        /// <summary>
        /// 发布上线事件
        /// </summary>
        public void PushNetworkEnabled()
        {
            if (networdkEnabledTimes == 0)
            {
                NetworkFirstEnabledHandle?.Invoke();
            }
            NetworkEnabledHandle?.Invoke(networdkEnabledTimes);
            networdkEnabledTimes++;
        }
    }
}
