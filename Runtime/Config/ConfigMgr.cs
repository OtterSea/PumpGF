using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Interlocked = System.Threading.Interlocked;

namespace PumpGF
{
    /// <summary>
    /// 配置管理模块。通过 IConfigProvider 抽象加载配置，内部缓存。
    /// 支持 SO（默认）与未来 Luban 切换。提供强类型 Get 与异步 GetAsync。
    /// </summary>
    public sealed class ConfigMgr : IModule
    {
        private IConfigProvider _provider;
        private readonly Dictionary<string, object> _cache = new();

        public void Init()
        {
            _provider = new SOConfigProvider();
        }

        // ──────────────────────────────────────────────
        //  配置访问
        // ──────────────────────────────────────────────

        /// <summary>
        /// 同步获取已加载的配置。未加载返回 null + Warning。
        /// </summary>
        public T Get<T>(string key) where T : class
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached as T;
            Log.Warning("ConfigMgr", $"Config '{key}' not loaded. Call GetAsync first or Preload.");
            return null;
        }

        /// <summary>
        /// 异步加载并获取配置。首次加载后缓存，后续 Get 直接命中。
        /// </summary>
        public async UniTask<T> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty.", nameof(key));

            if (_cache.TryGetValue(key, out var cached))
                return cached as T;

            var config = await _provider.LoadConfigAsync<T>(key, ct);
            if (config == null)
            {
                Log.Error("ConfigMgr", $"Failed to load config '{key}': null result.");
                return null;
            }

            _cache[key] = config;

            // 配置表自动构建索引
            if (config is IConfigTable table)
                table.BuildIndex();

            Log.Info("ConfigMgr", $"Config loaded: '{key}' ({config.GetType().Name})");
            return config;
        }

        // ──────────────────────────────────────────────
        //  预加载
        // ──────────────────────────────────────────────

        /// <summary>
        /// 批量预加载配置（按 key 列表）。
        /// </summary>
        public async UniTask PreloadAsync(
            IReadOnlyList<string> keys, IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (keys == null || keys.Count == 0) return;

            int completed = 0;
            int total = keys.Count;
            // 并发加载：多个 IO/Addressables 请求同时进行，进度用 Interlocked 保序
            await UniTask.WhenAll(keys.Select(async key =>
            {
                if (!_cache.ContainsKey(key))
                {
                    await GetAsync<object>(key, ct);
                }
                var done = Interlocked.Increment(ref completed);
                progress?.Report((float)done / total);
            }));
        }

        // ──────────────────────────────────────────────
        //  查询 / 管理
        // ──────────────────────────────────────────────

        /// <summary>指定 key 是否已加载</summary>
        public bool IsLoaded(string key) => _cache.ContainsKey(key);

        /// <summary>已加载配置数量</summary>
        public int LoadedCount => _cache.Count;

        /// <summary>切换 Provider（清空缓存）</summary>
        public void SetProvider(IConfigProvider provider)
        {
            ClearCache();
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>清空缓存（不释放 Provider 内部 handle）</summary>
        private void ClearCache()
        {
            _cache.Clear();
        }

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Dispose()
        {
            _cache.Clear();
            _provider = null;
        }
    }
}
