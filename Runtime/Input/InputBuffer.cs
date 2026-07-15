using System.Collections.Generic;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 输入缓冲。动作游戏预输入用：缓冲按下动作，动画可取消帧消费。
    /// <para>缓冲时间窗口内有效（默认 0.2 秒），超时自动丢弃。</para>
    /// </summary>
    public sealed class InputBuffer
    {
        private readonly Dictionary<string, float> _timestamps = new(8);
        private float _window;

        /// <param name="windowSeconds">缓冲时间窗口（秒）</param>
        public InputBuffer(float windowSeconds)
        {
            _window = Mathf.Max(0f, windowSeconds);
        }

        /// <summary>设置缓冲时间窗口</summary>
        public void SetWindow(float seconds) => _window = Mathf.Max(0f, seconds);

        /// <summary>缓冲一个输入（记录当前时间）</summary>
        public void Buffer(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return;
            _timestamps[actionName] = Time.time;
        }

        /// <summary>
        /// 消费缓冲的输入。返回 true 表示有有效缓冲输入并已消费，false 表示无。
        /// </summary>
        public bool Consume(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return false;
            if (_timestamps.TryGetValue(actionName, out var t))
            {
                if (Time.time - t <= _window)
                {
                    _timestamps.Remove(actionName);
                    return true;
                }
                _timestamps.Remove(actionName);
            }
            return false;
        }

        /// <summary>清空所有缓冲</summary>
        public void Clear() => _timestamps.Clear();
    }
}
