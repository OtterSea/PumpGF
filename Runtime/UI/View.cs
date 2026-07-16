using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PumpGF
{
    /// <summary>
    /// View 非泛型基类。UIManager 持有此基类引用，通过 internal 方法驱动生命周期与动画。
    /// </summary>
    public abstract class View : MonoBehaviour
    {
        /// <summary>订阅托管网（子类 OnBind 中 .AddTo(ref Bag) 注册绑定）</summary>
        protected DisposableBag Bag;

        /// <summary>创建对应 ViewModel 实例</summary>
        internal abstract ViewModel CreateViewModel();

        /// <summary>初始化（绑定 ViewModel）。UIManager 在实例化后调用。</summary>
        internal abstract void InitializeInternal(ViewModel vm);

        /// <summary>清理（解绑 + Dispose ViewModel）。UIManager 关闭时调用。</summary>
        internal abstract void CleanupInternal();

        /// <summary>入场动画（子类可重写，默认立即完成）</summary>
        internal virtual UniTask PlayEnterAnimationInternal(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>出场动画（子类可重写，默认立即完成）</summary>
        internal virtual UniTask PlayExitAnimationInternal(CancellationToken ct) => UniTask.CompletedTask;
    }

    /// <summary>
    /// View 泛型基类。挂在 UI prefab 根节点，绑定 <typeparamref name="TViewModel"/>。
    /// <para><b>不写业务逻辑</b>，只做"显示"和"转发用户操作到 ViewModel"。</para>
    /// <para>绑定逻辑在 <see cref="OnBind"/> 中（手写或配合 <see cref="UIBinder"/>）。</para>
    /// </summary>
    /// <typeparam name="TViewModel">关联的 ViewModel 类型（需有无参构造）</typeparam>
    public abstract class View<TViewModel> : View where TViewModel : ViewModel, new()
    {
        /// <summary>当前绑定的 ViewModel</summary>
        public TViewModel ViewModel { get; private set; }

        internal override ViewModel CreateViewModel() => new TViewModel();

        internal override void InitializeInternal(ViewModel vm)
        {
            ViewModel = vm as TViewModel;
            OnBind(ViewModel);
        }

        internal override void CleanupInternal()
        {
            OnUnbind();
            Bag.Dispose();
            ViewModel?.Dispose();
            ViewModel = null;
        }

        internal override UniTask PlayEnterAnimationInternal(CancellationToken ct) => PlayEnterAnimation(ct);
        internal override UniTask PlayExitAnimationInternal(CancellationToken ct) => PlayExitAnimation(ct);

        /// <summary>子类实现绑定逻辑（数据→UI、UI→命令）</summary>
        protected abstract void OnBind(TViewModel vm);

        /// <summary>解绑钩子（默认空，子类可重写做额外清理）</summary>
        protected virtual void OnUnbind() { }

        /// <summary>入场动画（默认无动画，子类可重写，如 DOTween 淡入）</summary>
        protected virtual UniTask PlayEnterAnimation(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>出场动画（默认无动画，子类可重写）</summary>
        protected virtual UniTask PlayExitAnimation(CancellationToken ct) => UniTask.CompletedTask;

        // ──────────────────────────────────────────────
        //  绑定辅助方法（减少样板，统一托管到 Bag）
        // ──────────────────────────────────────────────

        /// <summary>绑定文本属性到 TMP_Text（自动刷新，可选格式化）</summary>
        protected IDisposable BindText<T>(
            ReadOnlyReactiveProperty<T> prop, TMP_Text text, Func<T, string> formatter = null)
        {
            return prop.Subscribe(v =>
            {
                text.text = formatter != null ? formatter(v) : (v?.ToString() ?? string.Empty);
            }).AddTo(ref Bag);
        }

        /// <summary>绑定 float 属性到 Slider.value</summary>
        protected IDisposable BindSlider(ReadOnlyReactiveProperty<float> prop, Slider slider)
        {
            return prop.Subscribe(v => slider.value = v).AddTo(ref Bag);
        }

        /// <summary>绑定 bool 属性到 GameObject 激活状态</summary>
        protected IDisposable BindActive(ReadOnlyReactiveProperty<bool> prop, GameObject target)
        {
            return prop.Subscribe(active => target.SetActive(active)).AddTo(ref Bag);
        }

        /// <summary>绑定按钮点击到回调</summary>
        protected IDisposable BindClick(Button button, Action onClick)
        {
            return button.OnClickAsObservable()
                .Subscribe(_ => onClick())
                .AddTo(ref Bag);
        }

        /// <summary>绑定按钮点击到命令（自动检查 CanExecute）</summary>
        protected IDisposable BindClick(Button button, ICommand command)
        {
            return button.OnClickAsObservable()
                .Subscribe(_ =>
                {
                    if (command.CanExecute(null)) command.Execute(null);
                })
                .AddTo(ref Bag);
        }

        /// <summary>绑定 Slider 值变化到回调（UI→数据）</summary>
        protected IDisposable BindSliderValue(Slider slider, Action<float> onChange)
        {
            return slider.OnValueChangedAsObservable()
                .Subscribe(v => onChange(v))
                .AddTo(ref Bag);
        }

        /// <summary>绑定 Toggle 值变化到回调（UI→数据）</summary>
        protected IDisposable BindToggleValue(Toggle toggle, Action<bool> onChange)
        {
            return toggle.OnValueChangedAsObservable()
                .Subscribe(v => onChange(v))
                .AddTo(ref Bag);
        }

        /// <summary>
        /// 通用绑定：将 <typeparamref name="T"/> 属性变化通过 <paramref name="setter"/> 应用到 UI 目标。
        /// 可用于 Image.color、Image.fillAmount、Dropdown.value、InputField.text 等任意场景。
        /// </summary>
        /// <example>
        /// <code>
        /// Bind(vm.HpColor, hpImage, (img, c) => img.color = c);
        /// Bind(vm.Fill, hpImage, (img, f) => img.fillAmount = f);
        /// </code>
        /// </example>
        protected IDisposable Bind<T, TTarget>(
            ReadOnlyReactiveProperty<T> prop, TTarget target, Action<TTarget, T> setter)
            where TTarget : class
        {
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            return prop.Subscribe(v => setter(target, v)).AddTo(ref Bag);
        }

        /// <summary>绑定颜色到 Graphic（Image / RawImage / Text 等的 color）</summary>
        protected IDisposable BindColor(ReadOnlyReactiveProperty<Color> prop, Graphic target)
        {
            return prop.Subscribe(c => target.color = c).AddTo(ref Bag);
        }

        /// <summary>绑定 float 到 Image.fillAmount（进度条常用）</summary>
        protected IDisposable BindFillAmount(ReadOnlyReactiveProperty<float> prop, Image image)
        {
            return prop.Subscribe(v => image.fillAmount = v).AddTo(ref Bag);
        }

        // ──────────────────────────────────────────────
        //  Auto_ 命名查找（基础能力，无需 Inspector 拖拽）
        // ──────────────────────────────────────────────

        /// <summary>按名查找子物体组件（配合 Auto_ 命名约定，避免 Inspector 引用丢失）</summary>
        protected T FindChild<T>(string name) where T : Component
        {
            var tr = transform.Find(name);
            return tr != null ? tr.GetComponent<T>() : null;
        }

        /// <summary>按路径查找子物体组件</summary>
        protected T FindChild<T>(params string[] path) where T : Component
        {
            var tr = transform.Find(string.Join("/", path));
            return tr != null ? tr.GetComponent<T>() : null;
        }
    }
}
