using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 暂停档案（ScriptableObject）。定义"某个暂停上下文激活时，冻结哪些通道"。
    /// 纯数据，设计师可在 Inspector 编辑。运行时通过 LifecycleMgr.PushPause 激活。
    /// </summary>
    [CreateAssetMenu(menuName = "PumpGF/Lifecycle/PauseProfile")]
    public class PauseProfile : ScriptableObject
    {
        /// <summary>唯一标识，如 "TimeStop"、"MenuPause"</summary>
        [Tooltip("唯一标识，如 TimeStop、MenuPause")]
        public string ProfileName;

        /// <summary>要冻结的通道（标志位组合）</summary>
        [Tooltip("要冻结的通道（可多选）")]
        public UpdateChannel PausedChannels;

        /// <summary>设计师可读说明</summary>
        [Tooltip("说明（可选）")]
        [TextArea] public string Description;
    }
}
