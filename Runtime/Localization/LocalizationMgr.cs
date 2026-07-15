using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;

namespace PumpGF
{
    /// <summary>
    /// 多语言文本与资源的统一管理中枢。通过 <see cref="ILocaleProvider"/> 抽象隔离数据源，
    /// 提供 <see cref="ReactiveString"/> / <see cref="LocalizeText"/> 自动刷新 UI，
    /// 缺失 key 自动回退（当前语言 → 默认语言 → key 名 + Warning）。
    /// </summary>
    /// <remarks>
    /// <b>初始化注意</b>：<see cref="Init"/> 为同步无法 await 加载默认语言，故采用 fire-and-forget
    /// 预加载（<see cref="PreloadDefaultAsync"/>）。若预加载未完成时调用 <see cref="GetText"/>，
    /// 会回退到 key 名 + Warning。业务也可显式 <see cref="SetLanguageAsync"/>。
    /// </remarks>
    public sealed class LocalizationMgr : IModule
    {
        private ILocaleProvider _provider;
        private readonly ReactiveProperty<Language> _currentLanguage = new(Language.ChineseSimplified);
        private readonly Subject<Language> _onLanguageChanged = new();
        private readonly Subject<TMP_FontAsset> _onFontChanged = new();

        private LocaleData _currentData;
        private LocaleData _defaultData;
        private Language _defaultLanguage = Language.ChineseSimplified;
        private string _addressPrefix = "Locale/";

        private static readonly Language[] _supported =
        {
            Language.ChineseSimplified, Language.English, Language.Japanese
        };

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Init()
        {
            _provider = new SOLocaleProvider(_addressPrefix);
            // 默认语言异步预加载（fire-and-forget，不阻塞同步 Init）
            PreloadDefaultAsync().Forget();
        }

        public void Dispose()
        {
            _onLanguageChanged.Dispose();
            _onFontChanged.Dispose();
            _currentLanguage.Dispose();
            _provider = null;
            _currentData = null;
            _defaultData = null;
        }

        // ──────────────────────────────────────────────
        //  语言管理
        // ──────────────────────────────────────────────

        /// <summary>当前语言（ReactiveProperty，UI 可绑定）</summary>
        public ReactiveProperty<Language> CurrentLanguage => _currentLanguage;

        /// <summary>语言切换事件流</summary>
        public IObservable<Language> OnLanguageChanged => _onLanguageChanged;

        /// <summary>字体切换事件流</summary>
        public IObservable<TMP_FontAsset> OnFontChanged => _onFontChanged;

        /// <summary>默认语言（缺失回退目标）</summary>
        public Language DefaultLanguage => _defaultLanguage;

        /// <summary>支持的语言列表</summary>
        public Language[] SupportedLanguages => _supported;

        /// <summary>设置默认语言</summary>
        public void SetDefaultLanguage(Language lang) => _defaultLanguage = lang;

        /// <summary>Addressables key 前缀（默认 "Locale/"）</summary>
        public string AddressPrefix
        {
            get => _addressPrefix;
            set
            {
                _addressPrefix = value;
                if (_provider is SOLocaleProvider)
                    _provider = new SOLocaleProvider(_addressPrefix);
            }
        }

        /// <summary>
        /// 异步切换语言。加载新语言数据，切换后通知所有 ReactiveString / LocalizeText 自动刷新。
        /// </summary>
        public async UniTask SetLanguageAsync(Language lang, CancellationToken ct = default)
        {
            if (_currentData == null || _currentData.Language != lang)
                _currentData = await _provider.LoadLocaleAsync(lang, ct);

            // 确保默认语言数据可用（回退用）
            if (_defaultData == null && lang != _defaultLanguage)
                _defaultData = await _provider.LoadLocaleAsync(_defaultLanguage, ct);
            else if (lang == _defaultLanguage)
                _defaultData = _currentData;

            _currentLanguage.Value = lang;
            _onLanguageChanged.OnNext(lang);

            // 字体切换
            if (_currentData.Font != null)
            {
                if (TMP_Settings.defaultFontAsset != _currentData.Font)
                    TMP_Settings.defaultFontAsset = _currentData.Font;
                _onFontChanged.OnNext(_currentData.Font);
            }

            Log.Info("Localization", $"语言切换: {lang}");
        }

        // ──────────────────────────────────────────────
        //  文本访问
        // ──────────────────────────────────────────────

        /// <summary>获取 key 对应文本（缺失回退：当前 → 默认 → key 名）</summary>
        public string GetText(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // 当前语言
            if (_currentData != null && _currentData.Texts.TryGetValue(key, out var text))
                return text;

            // 默认语言回退
            if (_currentLanguage.Value != _defaultLanguage
                && _defaultData != null
                && _defaultData.Texts.TryGetValue(key, out var defText))
            {
                Log.Warning("Localization", $"当前语言 '{_currentLanguage.Value}' 缺失 key '{key}'，回退默认语言。");
                return defText;
            }

            Log.Warning("Localization", $"缺失 key '{key}'，返回 key 名。");
            return key;
        }

        /// <summary>获取格式化文本</summary>
        public string GetText(string key, params object[] args)
        {
            return string.Format(GetText(key), args);
        }

        /// <summary>创建响应式本地化字符串</summary>
        public ReactiveString GetReactiveString(string key, params object[] args)
        {
            return new ReactiveString(key, args);
        }

        // ──────────────────────────────────────────────
        //  多语言资源
        // ──────────────────────────────────────────────

        /// <summary>
        /// 拼接当前语言的多语言资源 key。
        /// <para>如当前中文，<c>GetLocalizedResourceKey("UI_Logo")</c> → <c>"Locale/ChineseSimplified/UI_Logo"</c></para>
        /// </summary>
        public string GetLocalizedResourceKey(string resourceKey)
        {
            return $"{_addressPrefix}{_currentLanguage.Value}/{resourceKey}";
        }

        // ──────────────────────────────────────────────
        //  Provider
        // ──────────────────────────────────────────────

        /// <summary>切换数据源 Provider（清空缓存）</summary>
        public void SetProvider(ILocaleProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _currentData = null;
            _defaultData = null;
        }

        // ──────────────────────────────────────────────
        //  查询
        // ──────────────────────────────────────────────

        /// <summary>指定语言是否已加载</summary>
        public bool IsLocaleLoaded(Language lang) => _provider?.IsLocaleLoaded(lang) ?? false;

        /// <summary>当前语言是否包含指定 key</summary>
        public bool HasKey(string key)
        {
            return _currentData != null && _currentData.Texts.ContainsKey(key);
        }

        // ──────────────────────────────────────────────
        //  内部
        // ──────────────────────────────────────────────

        private async UniTaskVoid PreloadDefaultAsync()
        {
            try
            {
                _defaultData = await _provider.LoadLocaleAsync(_defaultLanguage);
                _currentData = _defaultData;
                _currentLanguage.Value = _defaultLanguage;
                Log.Info("Localization", $"默认语言预加载完成: {_defaultLanguage}");
            }
            catch (Exception e)
            {
                Log.Warning("Localization", $"默认语言预加载失败（业务应显式调用 SetLanguageAsync）: {e.Message}");
            }
        }
    }
}
