using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PumpGF
{
    /// <summary>
    /// 资源加载模块。Addressables 为主，Resources 为辅。
    /// 所有异步方法返回 UniTask，支持 CancellationToken。
    /// </summary>
    public sealed class ResMgr : IModule
    {
        // key → 已加载的 Addressables handle（非泛型，Result 为 object）
        Dictionary<string, AsyncOperationHandle> _handles;
        // 非池化实例 → 对应的资源 key（用于 Release 时查找）
        Dictionary<GameObject, string> _nonPooledInstances;
        bool _addressablesReady;

        public void Init()
        {
            _handles = new Dictionary<string, AsyncOperationHandle>(64);
            _nonPooledInstances = new Dictionary<GameObject, string>(128);
            _addressablesReady = false;
        }

        // ──────────────────────────────────────────────
        //  Addressables 主接口
        // ──────────────────────────────────────────────

        /// <summary>
        /// 异步加载资源。同一 key 共享同一个 handle，多次调用不会重复加载。
        /// 调用方在使用完毕后应调用 <see cref="UnloadAsset"/> 释放。
        /// </summary>
        public async UniTask<T> LoadAssetAsync<T>(string key, CancellationToken ct = default) where T : Object
        {
            await EnsureAddressablesInitialized(ct);

            if (_handles.TryGetValue(key, out var existing))
            {
                return (T)existing.Result;
            }

            var handle = Addressables.LoadAssetAsync<T>(key);
            _handles[key] = handle;

            await handle.ToUniTask(cancellationToken: ct);

            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException($"[ResMgr] Failed to load asset '{key}': {handle.OperationException?.Message}");

            return handle.Result;
        }

        /// <summary>
        /// 异步实例化 GameObject。如果 PoolMgr 已注册对应 key 的池，则从池中取用；否则直接实例化。
        /// </summary>
        public async UniTask<GameObject> InstantiateAsync(
            string key, Vector3 position, Quaternion rotation, CancellationToken ct = default)
        {
            // 确保资源已加载
            await LoadAssetAsync<GameObject>(key, ct);

            // 如果池已注册，走池
            if (GameGlobal.PoolMgr.HasPool(key))
            {
                return GameGlobal.PoolMgr.Get(key, position, rotation);
            }

            // 无池，直接实例化
            var prefab = (GameObject)_handles[key].Result;
            var instance = Object.Instantiate(prefab, position, rotation);
            _nonPooledInstances[instance] = key;
            return instance;
        }

        /// <summary>
        /// 异步实例化 GameObject（默认位置和旋转）。
        /// </summary>
        public UniTask<GameObject> InstantiateAsync(string key, CancellationToken ct = default)
        {
            return InstantiateAsync(key, Vector3.zero, Quaternion.identity, ct);
        }

        /// <summary>
        /// 释放实例化对象。池化对象回池，非池化对象销毁。
        /// 不影响资源 handle 的引用计数——如需释放资源本身，调用 <see cref="UnloadAsset"/>。
        /// </summary>
        public void Release(GameObject instance)
        {
            if (instance == null)
            {
                Debug.LogWarning("[ResMgr] Release called with null.");
                return;
            }

            // 非池化实例 → 销毁
            if (_nonPooledInstances.Remove(instance, out var key))
            {
                Object.Destroy(instance);
                return;
            }

            // 尝试走池（池化实例有 PooledObjectTracker）
            if (instance.GetComponent<PooledObjectTracker>() != null)
            {
                GameGlobal.PoolMgr.Release(instance);
                return;
            }

            // 既不在非池化字典中，也没有 Tracker
            Debug.LogWarning($"[ResMgr] Object '{instance.name}' is not managed by ResMgr, destroying.");
            Object.Destroy(instance);
        }

        /// <summary>
        /// 卸载已加载的资源 handle，释放内存。
        /// 注意：确保所有基于该资源的实例已被 Release，否则可能产生空引用。
        /// </summary>
        public void UnloadAsset(string key)
        {
            if (_handles.TryGetValue(key, out var handle))
            {
                Addressables.Release(handle);
                _handles.Remove(key);
            }
        }

        /// <summary>
        /// 批量预加载资源。
        /// </summary>
        /// <param name="keys">要预加载的资源 key 列表</param>
        /// <param name="progress">可选的进度报告（0~1）</param>
        public async UniTask PreloadAsync(
            IReadOnlyList<string> keys, IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (keys == null || keys.Count == 0) return;

            await EnsureAddressablesInitialized(ct);

            int completed = 0;

            await UniTask.WhenAll(keys.Select(async key =>
            {
                if (_handles.ContainsKey(key)) return;

                var handle = Addressables.LoadAssetAsync<Object>(key);
                _handles[key] = handle;
                await handle.ToUniTask(cancellationToken: ct);

                completed++;
                progress?.Report((float)completed / keys.Count);
            }));
        }

        /// <summary>
        /// 异步加载场景（Addressables）。
        /// </summary>
        public async UniTask LoadSceneAsync(string key, CancellationToken ct = default)
        {
            await EnsureAddressablesInitialized(ct);

            var handle = Addressables.LoadSceneAsync(key, LoadSceneMode.Single);
            await handle.ToUniTask(cancellationToken: ct);
        }

        // ──────────────────────────────────────────────
        //  Resources 备用接口
        // ──────────────────────────────────────────────

        /// <summary>
        /// 通过 Resources.LoadAsync 加载资源（备用接口，不走 Addressables）。
        /// 加载的资源不受 <see cref="UnloadAsset"/> 管理；如需卸载请调用 <see cref="Resources.UnloadAsset"/>。
        /// </summary>
        public async UniTask<T> LoadResourceAsync<T>(string path, CancellationToken ct = default) where T : Object
        {
            var req = await Resources.LoadAsync<T>(path).ToUniTask(cancellationToken: ct);
            if (req == null)
                throw new InvalidOperationException($"[ResMgr] Resources.Load failed: '{path}'");
            return (T)req;
        }

        // ──────────────────────────────────────────────
        //  查询
        // ──────────────────────────────────────────────

        /// <summary>
        /// 指定 key 的资源是否已加载。
        /// </summary>
        public bool IsAssetLoaded(string key) => _handles.ContainsKey(key);

        /// <summary>
        /// 当前已加载但未卸载的资源数量。
        /// </summary>
        public int LoadedAssetCount => _handles.Count;

        // ──────────────────────────────────────────────
        //  生命周期
        // ──────────────────────────────────────────────

        public void Dispose()
        {
            // 清理非池化实例
            foreach (var kvp in _nonPooledInstances)
            {
                if (kvp.Key != null) Object.Destroy(kvp.Key);
            }
            _nonPooledInstances.Clear();

            // 释放所有 Addressables handle
            foreach (var handle in _handles.Values)
            {
                Addressables.Release(handle);
            }
            _handles.Clear();
        }

        async UniTask EnsureAddressablesInitialized(CancellationToken ct)
        {
            if (_addressablesReady) return;
            _addressablesReady = true;

            var init = Addressables.InitializeAsync();
            await init.ToUniTask(cancellationToken: ct);

            if (init.Status != AsyncOperationStatus.Succeeded)
            {
                _addressablesReady = false;
                throw new InvalidOperationException("[ResMgr] Addressables initialization failed.");
            }
        }
    }
}
