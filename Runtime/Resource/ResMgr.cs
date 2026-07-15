using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PumpGF
{
    /// <summary>
    /// 资源加载模块。Addressables 为主，引用计数管理，实例自动托管。
    /// 所有异步方法返回 UniTask，支持 CancellationToken 与进度回调。
    /// </summary>
    public sealed class ResMgr : IModule
    {
        // key → AssetEntry（引用计数条目）
        private readonly Dictionary<string, AssetEntry> _entries = new(64);
        // 实例 → 实例追踪条目
        private readonly Dictionary<GameObject, InstanceEntry> _instances = new(128);
        // Addressables 初始化任务（缓存，防并发竞态）
        private UniTask _initTask;

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Init()
        {
            _initTask = default;
        }

        // ──────────────────────────────────────────────
        //  资产加载（引用计数）
        // ──────────────────────────────────────────────

        /// <summary>
        /// 异步加载资产，返回引用票据。同一 key 共享底层 handle，各自持有独立引用计数。
        /// 调用方在使用完毕后必须 Dispose 返回的 AssetHandle。
        /// </summary>
        public async UniTask<AssetHandle<T>> LoadAssetAsync<T>(
            string key, IProgress<float> progress = null, CancellationToken ct = default)
            where T : Object
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty.", nameof(key));

            await EnsureAddressablesInitialized();

            // 已加载且引用计数 > 0：复用
            if (_entries.TryGetValue(key, out var existing) && existing.RefCount > 0)
            {
                existing.AddRef();
                progress?.Report(1f);
                return new AssetHandle<T>(existing);
            }

            // 新加载
            var handle = Addressables.LoadAssetAsync<T>(key);
            var entry = new AssetEntry
            {
                Key = key,
                Handle = handle,
                RefCount = 1
            };
            entry.OnReleased = OnEntryReleased;
            _entries[key] = entry;

            await handle.ToUniTask(cancellationToken: ct);

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Log.Error("ResMgr", $"加载失败: '{key}': {handle.OperationException?.Message}");
                entry.Release();
                throw new InvalidOperationException($"[ResMgr] Failed to load asset '{key}'");
            }

            progress?.Report(1f);
            return new AssetHandle<T>(entry);
        }

        // ──────────────────────────────────────────────
        //  GameObject 实例化
        // ──────────────────────────────────────────────

        /// <summary>
        /// 异步实例化 GameObject。若 PoolMgr 已注册对应 key 的池则走池，否则直接实例化。
        /// 非池化实例绑定 destroyCancellationToken，销毁时自动清理引用。
        /// </summary>
        public async UniTask<GameObject> InstantiateAsync(
            string key, Vector3 position, Quaternion rotation, CancellationToken ct = default)
        {
            var handle = await LoadAssetAsync<GameObject>(key, ct: ct);

            // 池化路径
            if (GameGlobal.PoolMgr != null && GameGlobal.PoolMgr.HasPool(key))
            {
                var pooledInstance = GameGlobal.PoolMgr.Get(key, position, rotation);
                _instances[pooledInstance] = new InstanceEntry
                {
                    Source = InstanceSource.Pool,
                    AssetHandle = null
                };
                return pooledInstance;
            }

            // 非池化路径
            var prefab = handle.Asset;
            if (prefab == null)
            {
                handle.Dispose();
                throw new InvalidOperationException($"[ResMgr] Prefab is null for key '{key}'");
            }

            var instance = Object.Instantiate(prefab, position, rotation);
            _instances[instance] = new InstanceEntry
            {
                Source = InstanceSource.Direct,
                AssetHandle = handle
            };

            // 绑定 destroyCancellationToken：GameObject 销毁时自动清理
            instance.destroyCancellationToken.Register(() =>
            {
                if (_instances.Remove(instance, out var entry))
                {
                    if (entry.Source == InstanceSource.Direct)
                    {
                        entry.AssetHandle?.Dispose();
                    }
                }
            });

            return instance;
        }

        /// <summary>异步实例化 GameObject（默认位置和旋转）。</summary>
        public UniTask<GameObject> InstantiateAsync(string key, CancellationToken ct = default)
        {
            return InstantiateAsync(key, Vector3.zero, Quaternion.identity, ct);
        }

        /// <summary>
        /// 释放实例化对象。池化对象回池，非池化对象销毁 + 释放 AssetHandle。
        /// 不影响资源 handle 的引用计数（如需释放资源本身，Dispose AssetHandle）。
        /// </summary>
        public void Release(GameObject instance)
        {
            if (instance == null)
            {
                Log.Warning("ResMgr", "Release called with null.");
                return;
            }

            if (_instances.TryGetValue(instance, out var entry))
            {
                if (entry.Source == InstanceSource.Pool)
                {
                    GameGlobal.PoolMgr?.Release(instance);
                }
                else // Direct
                {
                    Object.Destroy(instance);
                    entry.AssetHandle?.Dispose();
                }
                _instances.Remove(instance);
            }
            else
            {
                Log.Warning("ResMgr", $"Object '{instance.name}' is not managed by ResMgr, destroying.");
                Object.Destroy(instance);
            }
        }

        // ──────────────────────────────────────────────
        //  场景原语
        // ──────────────────────────────────────────────

        /// <summary>
        /// 异步加载场景，返回 SceneHandle。支持 Single/Additive 模式与 activateOnLoad。
        /// </summary>
        public async UniTask<SceneHandle> LoadSceneAsync(
            string key,
            LoadSceneMode mode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Scene key cannot be null or empty.", nameof(key));

            await EnsureAddressablesInitialized();

            var handle = Addressables.LoadSceneAsync(key, mode, activateOnLoad);
            await handle.ToUniTask(cancellationToken: ct);

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Log.Error("ResMgr", $"场景加载失败: '{key}': {handle.OperationException?.Message}");
                throw new InvalidOperationException($"[ResMgr] Failed to load scene '{key}'");
            }

            progress?.Report(1f);
            return new SceneHandle(handle, activateOnLoad);
        }

        // ──────────────────────────────────────────────
        //  预加载
        // ──────────────────────────────────────────────

        /// <summary>批量预加载（按 key 列表）。</summary>
        public async UniTask PreloadAsync(
            IReadOnlyList<string> keys, IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (keys == null || keys.Count == 0) return;
            await EnsureAddressablesInitialized();

            int completed = 0;
            await UniTask.WhenAll(keys.Select(async key =>
            {
                if (_entries.TryGetValue(key, out var existing) && existing.RefCount > 0) return;

                var handle = Addressables.LoadAssetAsync<Object>(key);
                var entry = new AssetEntry
                {
                    Key = key,
                    Handle = handle,
                    RefCount = 1
                };
                entry.OnReleased = OnEntryReleased;
                _entries[key] = entry;
                await handle.ToUniTask(cancellationToken: ct);

                completed++;
                progress?.Report((float)completed / keys.Count);
            }));
        }

        /// <summary>按 Addressables Label 批量预加载。</summary>
        public async UniTask PreloadByLabelAsync(
            string label, IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(label)) return;
            await EnsureAddressablesInitialized();

            var locations = Addressables.LoadResourceLocationsAsync(label);
            await locations.ToUniTask(cancellationToken: ct);

            if (locations.Status != AsyncOperationStatus.Succeeded || locations.Result == null) return;

            var keys = new List<string>(locations.Result.Count);
            foreach (var loc in locations.Result)
            {
                keys.Add(loc.PrimaryKey);
            }

            await PreloadAsync(keys, progress, ct);
        }

        // ──────────────────────────────────────────────
        //  查询 / 统计
        // ──────────────────────────────────────────────

        /// <summary>指定 key 的资源是否已加载（RefCount > 0）。</summary>
        public bool IsAssetLoaded(string key)
        {
            return _entries.TryGetValue(key, out var entry) && entry.RefCount > 0;
        }

        /// <summary>已加载资产数量。</summary>
        public int LoadedAssetCount => _entries.Count;

        /// <summary>池化实例数量。</summary>
        public int PooledInstanceCount => _instances.Count(e => e.Value.Source == InstanceSource.Pool);

        /// <summary>非池化实例数量。</summary>
        public int DirectInstanceCount => _instances.Count(e => e.Value.Source == InstanceSource.Direct);

        /// <summary>获取所有已加载资产信息（Debug 用）。</summary>
        public IReadOnlyList<AssetInfo> GetLoadedAssetsInfo()
        {
            var list = new List<AssetInfo>(_entries.Count);
            foreach (var kvp in _entries)
            {
                list.Add(new AssetInfo
                {
                    Key = kvp.Key,
                    Type = kvp.Value.Result?.GetType(),
                    RefCount = kvp.Value.RefCount,
                });
            }
            return list;
        }

        // ──────────────────────────────────────────────
        //  内部
        // ──────────────────────────────────────────────

        private UniTask EnsureAddressablesInitialized()
        {
            return _initTask ??= InitCoreAsync();
        }

        private async UniTask InitCoreAsync()
        {
            var init = Addressables.InitializeAsync();
            await init.ToUniTask();

            if (init.Status != AsyncOperationStatus.Succeeded)
            {
                _initTask = default;
                Log.Error("ResMgr", "Addressables 初始化失败。");
                throw new InvalidOperationException("[ResMgr] Addressables initialization failed.");
            }
        }

        private void OnEntryReleased(AssetEntry entry)
        {
            _entries.Remove(entry.Key);
        }

        // ──────────────────────────────────────────────
        //  IModule.Dispose
        // ──────────────────────────────────────────────

        public void Dispose()
        {
            // 清理非池化实例
            foreach (var kvp in _instances)
            {
                if (kvp.Key == null) continue;
                if (kvp.Value.Source == InstanceSource.Direct)
                {
                    kvp.Value.AssetHandle?.Dispose();
                    Object.Destroy(kvp.Key);
                }
            }
            _instances.Clear();

            // 释放所有 AssetEntry
            foreach (var entry in _entries.Values)
            {
                if (entry.RefCount > 0 && entry.Handle.IsValid())
                {
                    Addressables.Release(entry.Handle);
                }
            }
            _entries.Clear();
            _initTask = default;
        }

        // ── 内部类型 ──

        private enum InstanceSource { Pool, Direct }

        private struct InstanceEntry
        {
            public InstanceSource Source;
            public AssetHandle<GameObject> AssetHandle; // 仅 Direct 模式持有
        }
    }

    /// <summary>
    /// 已加载资产信息（Debug 用）。
    /// </summary>
    public struct AssetInfo
    {
        /// <summary>Addressables key</summary>
        public string Key;
        /// <summary>资源类型</summary>
        public Type Type;
        /// <summary>当前引用计数</summary>
        public int RefCount;
    }
}
