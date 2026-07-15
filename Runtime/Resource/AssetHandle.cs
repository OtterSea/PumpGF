using System;
using Object = UnityEngine.Object;

namespace PumpGF
{
    /// <summary>
    /// 资源引用票据。调用方持有的引用，类型安全，IDisposable。
    /// Dispose 减引用计数，归零才真正释放底层 handle（幂等）。
    /// **不可被复制传递**——每个需要资源的模块应自行 LoadAssetAsync 获取独立 handle。
    /// </summary>
    public sealed class AssetHandle<T> : IDisposable where T : Object
    {
        private AssetEntry _entry;
        private bool _disposed;

        /// <summary>获取加载的资源（已 Dispose 返回 null）</summary>
        public T Asset => !_disposed && _entry != null ? _entry.Result as T : null;

        /// <summary>引用是否仍有效（未 Dispose 且底层未释放）</summary>
        public bool IsValid => !_disposed && _entry != null && _entry.RefCount > 0;

        /// <summary>对应的 Addressables key</summary>
        public string Key => _entry?.Key;

        internal AssetHandle(AssetEntry entry)
        {
            _entry = entry;
        }

        /// <summary>减引用计数（幂等，多次调用安全）</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _entry?.Release();
            _entry = null;
        }
    }
}
