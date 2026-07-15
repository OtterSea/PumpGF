using System;
using R3;

namespace PumpGF
{
    /// <summary>
    /// 响应式本地化字符串。持有 key 与格式化参数，语言切换时自动更新文本。
    /// 实现 <see cref="IReadOnlyReactiveProperty{T}"/>，可绑定到 ViewModel / View。
    /// </summary>
    /// <example>
    /// <code>
    /// var rs = new ReactiveString("KillCount", 0);
    /// rs.Subscribe(text => label.text = text).AddTo(ref Bag);
    /// rs.UpdateArgs(42); // 击杀数变化时更新参数
    /// </code>
    /// </example>
    public sealed class ReactiveString : IReadOnlyReactiveProperty<string>, IDisposable
    {
        private readonly string _key;
        private object[] _args;
        private readonly ReactiveProperty<string> _reactive;
        private readonly IDisposable _subscription;
        private bool _disposed;

        /// <param name="key">本地化 key</param>
        /// <param name="args">格式化参数（如 <c>new ReactiveString("KillCount", 42)</c> 对应 "击杀了 {0} 个敌人"）</param>
        public ReactiveString(string key, params object[] args)
        {
            _key = key;
            _args = args;
            _reactive = new ReactiveProperty<string>(GetFormattedText());
            var loc = GameGlobal.Localization;
            if (loc != null)
                _subscription = loc.OnLanguageChanged.Subscribe(_ => _reactive.Value = GetFormattedText());
        }

        /// <summary>当前文本值</summary>
        public string CurrentValue => _reactive.CurrentValue;

        /// <summary>订阅文本变化</summary>
        public IDisposable Subscribe(IObserver<string> observer)
        {
            return _reactive.Subscribe(observer);
        }

        /// <summary>更新格式化参数（如玩家名/数量变化时），立即刷新文本</summary>
        public void UpdateArgs(params object[] args)
        {
            _args = args;
            _reactive.Value = GetFormattedText();
        }

        private string GetFormattedText()
        {
            var loc = GameGlobal.Localization;
            if (loc == null) return _key;
            var text = loc.GetText(_key);
            return _args != null && _args.Length > 0 ? string.Format(text, _args) : text;
        }

        /// <summary>释放（取消语言切换订阅）</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();
            _reactive.Dispose();
        }
    }
}
