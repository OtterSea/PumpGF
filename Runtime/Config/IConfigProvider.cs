using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UObject = UnityEngine.Object;

namespace PumpGF
{
    /// <summary>
    /// 配置数据源抽象。SO 为默认实现，未来 Luban 只需新增 Provider。
    /// 泛型约束 T : class，兼容 SO（ScriptableObject）和未来 Luban（普通 C# 类）。
    /// </summary>
    public interface IConfigProvider
    {
        /// <summary>加载配置</summary>
        UniTask<T> LoadConfigAsync<T>(string key, CancellationToken ct) where T : class;

        /// <summary>预加载所有配置</summary>
        UniTask PreloadAsync(IProgress<float> progress, CancellationToken ct);

        /// <summary>指定 key 是否已加载</summary>
        bool IsConfigLoaded(string key);

        /// <summary>卸载配置</summary>
        void Unload(string key);
    }

    /// <summary>
    /// SO 配置 Provider（默认实现）。通过 ResMgr 加载 ScriptableObject。
    /// 内部持有 AssetHandle，Unload 时 Dispose。
    /// <para>由于 ResMgr.LoadAssetAsync 约束 T:UnityEngine.Object，此处以 UObject 加载再向下转型；
    /// 若 T 不是 UnityEngine.Object 派生（如未来 Luban 纯 C# 类），请改用专属 Provider。</para>
    /// </summary>
    public sealed class SOConfigProvider : IConfigProvider
    {
        private readonly Dictionary<string, IDisposable> _handles = new();

        public async UniTask<T> LoadConfigAsync<T>(string key, CancellationToken ct) where T : class
        {
            if (GameGlobal.ResMgr == null)
                throw new InvalidOperationException("[ConfigProvider] ResMgr not initialized.");

            var handle = await GameGlobal.ResMgr.LoadAssetAsync<UObject>(key, ct: ct);
            _handles[key] = handle;
            return handle.Asset as T;
        }

        public async UniTask PreloadAsync(IProgress<float> progress, CancellationToken ct)
        {
            // 由 ConfigMgr.PreloadAsync 调用具体 key 列表
            await UniTask.CompletedTask;
        }

        public bool IsConfigLoaded(string key) => _handles.ContainsKey(key);

        public void Unload(string key)
        {
            if (_handles.TryGetValue(key, out var handle))
            {
                handle.Dispose();
                _handles.Remove(key);
            }
        }
    }
}
