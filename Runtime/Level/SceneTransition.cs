using Cysharp.Threading.Tasks;
using System.Threading;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace PumpGF
{
    /// <summary>
    /// 场景过渡效果抽象基类。
    /// <para>PlayFadeOut：加载前遮罩当前画面；PlayFadeIn：加载后揭开新画面。</para>
    /// </summary>
    public abstract class SceneTransition
    {
        /// <summary>加载前：遮罩当前画面（如淡入黑屏/显示 Loading）</summary>
        public abstract UniTask PlayFadeOut(IProgress<float> progress, CancellationToken ct);

        /// <summary>加载后：揭开新画面（如淡出黑屏/隐藏 Loading）</summary>
        public abstract UniTask PlayFadeIn(CancellationToken ct);
    }

    /// <summary>
    /// 淡入淡出过渡。全屏 Image 用 DOTween DOFade。内部创建临时 Canvas。
    /// </summary>
    public class FadeTransition : SceneTransition
    {
        /// <summary>淡入淡出时长（秒）</summary>
        public float Duration = 0.5f;
        /// <summary>遮罩颜色</summary>
        public Color FadeColor = Color.black;

        private GameObject _canvasGo;
        private Image _image;

        public override async UniTask PlayFadeOut(IProgress<float> progress, CancellationToken ct)
        {
            CreateCanvas();
            _image.color = new Color(FadeColor.r, FadeColor.g, FadeColor.b, 0f);
            await _image.DOFade(1f, Duration).ToUniTask(cancellationToken: ct);
            progress?.Report(1f);
        }

        public override async UniTask PlayFadeIn(CancellationToken ct)
        {
            if (_image == null) return;
            _image.color = new Color(FadeColor.r, FadeColor.g, FadeColor.b, 1f);
            await _image.DOFade(0f, Duration).ToUniTask(cancellationToken: ct);
            DestroyCanvas();
        }

        private void CreateCanvas()
        {
            _canvasGo = new GameObject("PumpGF_FadeTransition");
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            _canvasGo.AddComponent<GraphicRaycaster>();

            _image = _canvasGo.AddComponent<Image>();
            _image.raycastTarget = true;
        }

        private void DestroyCanvas()
        {
            if (_canvasGo != null)
            {
                Object.Destroy(_canvasGo);
                _canvasGo = null;
                _image = null;
            }
        }
    }

    /// <summary>
    /// Loading 界面过渡。通过 UIManager Push/Pop Loading 页面。
    /// <para>进度更新需业务在 Loading 页面 ViewModel 中自行绑定（本阶段 transition 仅负责 Push/Pop）。</para>
    /// </summary>
    public class LoadingScreenTransition : SceneTransition
    {
        /// <summary>Loading 页面 viewId（Addressables key）</summary>
        public string LoadingPageId = "UI/LoadingPage";

        public override async UniTask PlayFadeOut(IProgress<float> progress, CancellationToken ct)
        {
            if (GameGlobal.UIManager != null)
                await GameGlobal.UIManager.Push(LoadingPageId, ct: ct);
            progress?.Report(0f);
        }

        public override async UniTask PlayFadeIn(CancellationToken ct)
        {
            if (GameGlobal.UIManager != null)
                await GameGlobal.UIManager.Pop(ct: ct);
        }
    }
}
