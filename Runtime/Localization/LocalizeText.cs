using R3;
using TMPro;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 直接挂 UI 组件的本地化文本。指定 key，语言切换自动刷新。
    /// <para>适用于静态文本（按钮标签/标题）；动态带参文本用 <see cref="ReactiveString"/>。</para>
    /// </summary>
    [AddComponentMenu("PumpGF/UI/LocalizeText")]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizeText : MonoBehaviour
    {
        [SerializeField] private string _key;
        [SerializeField] private TMP_Text _target;
        [SerializeField] private bool _autoFindTarget = true;

        private IDisposable _subscription;

        private void OnEnable()
        {
            if (_target == null && _autoFindTarget)
                _target = GetComponent<TMP_Text>();
            Refresh();

            _subscription?.Dispose();
            var loc = GameGlobal.Localization;
            if (loc != null)
                _subscription = loc.OnLanguageChanged.Subscribe(_ => Refresh());
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <summary>设置 key 并刷新</summary>
        public void SetKey(string key)
        {
            _key = key;
            Refresh();
        }

        /// <summary>设置目标文本组件</summary>
        public void SetTarget(TMP_Text target)
        {
            _target = target;
            Refresh();
        }

        private void Refresh()
        {
            if (string.IsNullOrEmpty(_key) || _target == null) return;
            var loc = GameGlobal.Localization;
            _target.text = loc != null ? loc.GetText(_key) : _key;
        }
    }
}
