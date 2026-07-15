namespace PumpGF
{
    /// <summary>
    /// 播放句柄。<see cref="AudioMgr.Play"/> 返回，业务可忽略（Fire-and-Forget）或用于控制。
    /// <para>失效后（播放完自动回收）操作为 Noop + Warning。</para>
    /// </summary>
    public interface IAudioHandle
    {
        /// <summary>是否仍在播放</summary>
        bool IsValid { get; }

        /// <summary>实时音量（0~1，叠加在组音量上）</summary>
        float Volume { get; set; }

        /// <summary>停止（可选淡出秒数）</summary>
        void Stop(float fadeOut = 0f);

        /// <summary>暂停</summary>
        void Pause();

        /// <summary>恢复</summary>
        void Resume();
    }
}
