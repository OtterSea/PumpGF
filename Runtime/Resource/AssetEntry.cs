using System;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PumpGF
{
    /// <summary>
    /// 资源引用计数条目。每个 key 对应一个 entry，维护引用计数与底层 Addressables handle。
    /// RefCount 归零时释放 Addressables handle 并触发 OnReleased 回调。
    /// </summary>
    public sealed class AssetEntry
    {
        /// <summary>Addressables key</summary>
        public string Key;
        /// <summary>底层 Addressables handle</summary>
        public AsyncOperationHandle Handle;
        /// <summary>引用计数</summary>
        public int RefCount;
        /// <summary>归零时的回调（ResMgr 用于从 _entries 移除）</summary>
        public Action<AssetEntry> OnReleased;

        /// <summary>资源结果（handle.Result）</summary>
        public UnityEngine.Object Result => Handle.IsValid() ? Handle.Result : null;

        /// <summary>增加引用计数</summary>
        public void AddRef() => RefCount++;

        /// <summary>
        /// 减少引用计数。归零时释放 Addressables handle 并触发回调。
        /// 幂等：RefCount 已为 0 时不再操作。
        /// </summary>
        public void Release()
        {
            if (RefCount <= 0) return;
            RefCount--;
            if (RefCount == 0)
            {
                if (Handle.IsValid())
                {
                    Addressables.Release(Handle);
                }
                OnReleased?.Invoke(this);
            }
        }
    }
}
