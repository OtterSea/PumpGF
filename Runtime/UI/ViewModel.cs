using R3;
using System;

namespace PumpGF
{
    /// <summary>
    /// ViewModel 抽象基类。纯 C#，不引用任何 UnityEngine.UI 组件。
    /// <para>状态用 <see cref="ReactiveProperty{T}"/> / <see cref="ReadOnlyReactiveProperty{T}"/>；</para>
    /// <para>订阅 GameDataStore 用 <see cref="Bag"/> 托管生命周期；</para>
    /// <para>命令用方法或 <see cref="ICommand"/>。可单元测试（不依赖 Unity）。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// public class PlayerHUDViewModel : ViewModel
    /// {
    ///     public ReadOnlyReactiveProperty&lt;float&gt; Hp { get; }
    ///
    ///     public PlayerHUDViewModel()
    ///     {
    ///         Hp = GameGlobal.GameData.Player.Hp.ToReadOnlyReactiveProperty();
    ///     }
    ///
    ///     public void OnAttackButton() => GameGlobal.GameData.Execute(new PlayerAttackCommand());
    /// }
    /// </code>
    /// </example>
    public abstract class ViewModel : IDisposable
    {
        /// <summary>
        /// 订阅托管网。子类在构造时 <c>.AddTo(ref Bag)</c> 注册订阅，
        /// <see cref="Dispose"/> 时统一释放。
        /// </summary>
        protected DisposableBag Bag;

        private bool _disposed;

        /// <summary>是否已释放</summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// 释放 ViewModel 资源（由 UIManager 在 View 销毁时调用）。
        /// 幂等，多次调用安全。
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Bag.Dispose();
        }
    }
}
