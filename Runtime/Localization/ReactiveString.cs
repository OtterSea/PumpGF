using System;
using R3;

namespace PumpGF
{
    /// <summary>
    /// 响应式本地化字符串。持有 key 与格式化参数，语言切换时自动更新文本。
    /// 内部包装一个 <see cref="ReactiveProperty{T}"/>，暴露 R3 <see cref="Observable{T}"/> 用于绑定。
    /// </summary>
    /// <example>
    /// <code>
    /// var rs = new ReactiveString("KillCount", 0);
    /// rs.AsObservable().Subscribe(text => label.text = text).AddTo(ref Bag);
    /// rs.UpdateArgs(42); // 击杀数变化时更新参数
    /// </code>
    /// </example>
    public sealed class ReactiveString : IDisposable
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

        /// <summary>作为 R3 Observable 暴露（用于 View 绑定）</summary>
        public Observable<string> AsObservable() => _reactive;

        /// <summary>只读 ReactiveProperty 视图（用于 View 绑定辅助方法）</summary>
        public ReadOnlyReactiveProperty<string> ToReadOnly() => _reactive.ToReadOnlyReactiveProperty();

        /// <summary>订阅文本变化（快捷方法）</summary>
        public IDisposable Subscribe(Action<string> onNext) => _reactive.Subscribe(onNext);

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
