using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 内部组件，挂载在池化对象上以追踪其所属的池 key。
    /// 外部代码不应直接操作此组件。
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class PooledObjectTracker : MonoBehaviour
    {
        [SerializeField] internal string poolKey;
    }
}
