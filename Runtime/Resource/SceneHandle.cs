using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace PumpGF
{
    /// <summary>
    /// 场景句柄。加载场景返回，持有 Addressables scene handle。
    /// 支持 activateOnLoad=false 的延迟激活，以及 UnloadAsync 卸载。
    /// </summary>
    public sealed class SceneHandle : IDisposable
    {
        private AsyncOperationHandle _handle;
        private bool _disposed;

        /// <summary>加载的场景</summary>
        public Scene Scene
        {
            get
            {
                if (_disposed || !_handle.IsValid()) return default;
                if (_handle.Result is SceneInstance si) return si.Scene;
                return default;
            }
        }

        /// <summary>加载进度 0~1</summary>
        public float Progress => !_disposed && _handle.IsValid() ? _handle.PercentComplete : 0f;

        /// <summary>是否已激活</summary>
        public bool IsActivated { get; private set; }

        internal SceneHandle(AsyncOperationHandle handle, bool activateOnLoad)
        {
            _handle = handle;
            IsActivated = activateOnLoad;
        }

        /// <summary>激活场景（仅 activateOnLoad=false 时需要）</summary>
        public void Activate()
        {
            if (_disposed || IsActivated) return;
            if (_handle.IsValid() && _handle.Result is SceneInstance si)
            {
                si.Activate();
                IsActivated = true;
            }
        }

        /// <summary>异步卸载场景</summary>
        public async UniTask UnloadAsync(CancellationToken ct = default)
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle.IsValid())
            {
                await Addressables.UnloadSceneAsync(_handle).ToUniTask(cancellationToken: ct);
            }
        }

        /// <summary>等同 UnloadAsync（若未激活则直接释放）</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle.IsValid())
            {
                Addressables.UnloadSceneAsync(_handle);
            }
        }
    }
}
