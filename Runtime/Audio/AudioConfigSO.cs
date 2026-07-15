using System;
using System.Collections.Generic;
using UnityEngine;

namespace PumpGF
{
    /// <summary>音频类型（决定路由通道）</summary>
    public enum AudioType
    {
        /// <summary>背景音乐（单曲、淡入淡出、循环）</summary>
        BGM,
        /// <summary>音效（池化并发、Fire-and-Forget）</summary>
        SFX,
        /// <summary>UI 交互音效</summary>
        UI,
        /// <summary>语音/配音</summary>
        Voice,
    }

    /// <summary>
    /// 音频条目元数据。业务通过 <c>AudioMgr.Play(key)</c> 播放，AudioMgr 据此路由到对应通道。
    /// </summary>
    [Serializable]
    public class AudioEntry
    {
        /// <summary>业务引用名（如 "AttackHit"、"BGM_Battle"）</summary>
        public string Key;
        /// <summary>Addressables 中 AudioClip 的 key</summary>
        public string AddressablesKey;
        /// <summary>音频类型（决定路由）</summary>
        public AudioType Type = AudioType.SFX;
        /// <summary>默认音量（0~1，与音量组相乘）</summary>
        [Range(0f, 1f)] public float Volume = 1f;
        /// <summary>默认音调</summary>
        [Range(-3f, 3f)] public float Pitch = 1f;
        /// <summary>是否 3D 空间音效（SFX/UI/Voice 有效，BGM 始终 2D）</summary>
        public bool Is3D;
        /// <summary>3D 混合度（0=2D, 1=3D）</summary>
        [Range(0f, 1f)] public float SpatialBlend = 1f;
        /// <summary>预加载标签（可选，分组预加载用）</summary>
        public string PreloadLabel;
        /// <summary>同 key 最大并发数（-1 不限，防叠加轰炸）</summary>
        [Tooltip("同 key 最大并发数，-1 不限")]
        public int MaxConcurrent = -1;
    }

    /// <summary>
    /// 音频配置资产。定义 key → 元数据映射。Addressables key 通常为 <c>"AudioConfig"</c>。
    /// </summary>
    [CreateAssetMenu(menuName = "PumpGF/Audio/AudioConfig")]
    public class AudioConfigSO : ScriptableObject
    {
        /// <summary>音频条目列表</summary>
        public List<AudioEntry> Entries = new();
    }
}
