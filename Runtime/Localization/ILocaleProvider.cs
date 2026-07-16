using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace PumpGF
{
    /// <summary>
    /// 语言数据源抽象。隔离 SO/JSON 等不同数据源。
    /// <para>默认实现 <see cref="SOLocaleProvider"/>（从 LocaleSO 加载）。</para>
    /// <para>未来可接入 <c>JSONLocaleProvider</c>（翻译团队 Excel/CSV 导出 JSON）。</para>
    /// </summary>
    public interface ILocaleProvider
    {
        /// <summary>加载指定语言（首次加载后内部缓存）</summary>
        UniTask<LocaleData> LoadLocaleAsync(Language lang, CancellationToken ct);

        /// <summary>预加载语言（不切换当前语言）</summary>
        UniTask PreloadAsync(Language lang, CancellationToken ct);

        /// <summary>指定语言是否已加载</summary>
        bool IsLocaleLoaded(Language lang);

        /// <summary>卸载指定语言缓存</summary>
        void Unload(Language lang);
    }

    /// <summary>
    /// 基于 <see cref="LocaleSO"/> 的默认 Provider。
    /// 通过 <see cref="ResMgr"/> 加载 Addressables key 为 <c>"{AddressPrefix}{Language}"</c> 的 LocaleSO，
    /// 构建运行时 <see cref="LocaleData"/>（文本复制为字典副本，SO 资产用完即释放）。
    /// </summary>
    public sealed class SOLocaleProvider : ILocaleProvider
    {
        private readonly Dictionary<Language, LocaleData> _cache = new();
        private readonly string _addressPrefix;

        /// <param name="addressPrefix">Addressables key 前缀（默认 "Locale/"）</param>
        public SOLocaleProvider(string addressPrefix = "Locale/")
        {
            _addressPrefix = addressPrefix;
        }

        public async UniTask<LocaleData> LoadLocaleAsync(Language lang, CancellationToken ct)
        {
            if (_cache.TryGetValue(lang, out var cached)) return cached;

            var key = $"{_addressPrefix}{lang}";
            // LocaleData 为文本副本，SO 资产用完即释放引用
            using (var handle = await GameGlobal.ResMgr.LoadAssetAsync<LocaleSO>(key, ct: ct))
            {
                var data = new LocaleData { Language = lang };
                if (handle.Asset != null)
                {
                    data.Font = handle.Asset.Font;
                    foreach (var e in handle.Asset.Entries)
                    {
                        if (!string.IsNullOrEmpty(e.Key))
                            data.Texts[e.Key] = e.Text;
                    }
                }
                _cache[lang] = data;
                return data;
            }
        }

        public UniTask PreloadAsync(Language lang, CancellationToken ct) => LoadLocaleAsync(lang, ct).AsUniTask();

        public bool IsLocaleLoaded(Language lang) => _cache.ContainsKey(lang);

        public void Unload(Language lang) => _cache.Remove(lang);
    }
}
