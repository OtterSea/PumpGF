using System;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 生命周期配置（ScriptableObject）。定义各固定步长通道的频率、防追帧上限等。
    /// 通过 Addressables 加载（key = "LifecycleConfig"），加载失败使用代码默认值。
    /// </summary>
    [CreateAssetMenu(menuName = "PumpGF/Lifecycle/LifecycleConfig")]
    public class LifecycleConfig : ScriptableObject
    {
        /// <summary>固定步长通道配置列表</summary>
        [Tooltip("固定步长通道配置")]
        public List<ChannelConfig> FixedStepChannels = new()
        {
            new() { Channel = UpdateChannel.Logic, TickRate = 60, DefaultTimeScale = 1f },
        };

        /// <summary>单帧最大追帧次数（防死亡螺旋）</summary>
        [Tooltip("单帧最大追帧次数，超出丢弃")]
        public int MaxCatchUpPerFrame = 5;

        /// <summary>CTS 池容量</summary>
        [Tooltip("CancellationTokenSource 池容量")]
        public int CtsPoolCapacity = 32;

        /// <summary>PauseProfile 的 Addressables key 前缀</summary>
        [Tooltip("PauseProfile 的 Addressables key 前缀")]
        public string PauseProfileAddressPrefix = "PauseProfiles/";

        /// <summary>单通道配置</summary>
        [Serializable]
        public class ChannelConfig
        {
            /// <summary>通道</summary>
            public UpdateChannel Channel;
            /// <summary>频率（Hz），如 60</summary>
            public int TickRate = 60;
            /// <summary>默认时间缩放</summary>
            public float DefaultTimeScale = 1f;
        }
    }
}
