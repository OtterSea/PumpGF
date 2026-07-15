using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace PumpGF
{
    /// <summary>
    /// 基于 MVVM 的 UI 管理框架。强制 View/ViewModel/Model 三层分离，
    /// 提供页面栈、弹窗队列、自由 HUD、异步打开关闭、Canvas 分层管理。
    /// <para>UI prefab 走 Addressables（ResMgr），key 即 UIManager 的 viewId。</para>
    /// <para>页面 View 实现 <see cref="IPage"/>，弹窗 <see cref="IPopup"/>，HUD <see cref="IHud"/>。</para>
    /// </summary>
    public sealed class UIManager : IModule
    {
        private ResMgr _resMgr;
        private GameObject _root;
        private Transform _hudLayer, _pageLayer, _popupLayer, _topLayer;
        private Canvas _hudCanvas, _pageCanvas, _popupCanvas, _topCanvas;
        private UILayerConfig _layerConfig = UILayerConfig.Default;

        // 页面栈（List 当栈用，便于索引访问）
        private readonly List<PageEntry> _pageStack = new(16);
        // 弹窗待显示队列（按优先级稳定排序）
        private readonly List<PendingPopup> _popupQueue = new(8);
        private View _currentPopup;
        private GameObject _currentPopupInstance;
        private string _currentPopupId;

        // HUD：key → (view, instance)
        private readonly Dictionary<string, (View view, GameObject instance)> _huds = new(16);
        // 正在异步打开中的 viewId（竞态保护）
        private readonly HashSet<string> _opening = new(8);

        // InputContext 联动钩子（由 InputMgr 注册，避免 UIManager 直接依赖 InputMgr 类型）
        private Action<InputContext> _onPushInputContext;
        private Action _onPopInputContext;

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Init()
        {
            _resMgr = GameGlobal.ResMgr;
            CreateLayers();
        }

        public void Dispose()
        {
            // 弹窗
            _popupQueue.Clear();
            if (_currentPopup != null)
            {
                SafeCleanup(_currentPopup);
                _resMgr?.Release(_currentPopupInstance);
                _currentPopup = null;
                _currentPopupInstance = null;
                _currentPopupId = null;
            }

            // 页面栈（逆序）
            for (int i = _pageStack.Count - 1; i >= 0; i--)
            {
                SafeCleanup(_pageStack[i].View);
                _resMgr?.Release(_pageStack[i].Instance);
            }
            _pageStack.Clear();

            // HUD
            foreach (var kvp in _huds)
            {
                SafeCleanup(kvp.Value.view);
                _resMgr?.Release(kvp.Value.instance);
            }
            _huds.Clear();

            _opening.Clear();
            _onPushInputContext = null;
            _onPopInputContext = null;

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        // ──────────────────────────────────────────────
        //  层级配置
        // ──────────────────────────────────────────────

        /// <summary>设置指定层的 SortingOrder（运行时修改）</summary>
        public void SetLayerSortingOrder(UILayer layer, int order)
        {
            switch (layer)
            {
                case UILayer.HUD: _layerConfig.HudOrder = order; _hudCanvas.sortingOrder = order; break;
                case UILayer.Page: _layerConfig.PageOrder = order; _pageCanvas.sortingOrder = order; break;
                case UILayer.Popup: _layerConfig.PopupOrder = order; _popupCanvas.sortingOrder = order; break;
                case UILayer.Top: _layerConfig.TopOrder = order; _topCanvas.sortingOrder = order; break;
            }
        }

        /// <summary>
        /// 注册 InputContext 联动钩子。由 <c>InputMgr</c> 在初始化时调用，
        /// 使 <see cref="PageConfig.InputContext"/> 自动联动输入上下文切换。
        /// </summary>
        public void SetInputContextHooks(Action<InputContext> onPush, Action onPop)
        {
            _onPushInputContext = onPush;
            _onPopInputContext = onPop;
        }

        // ──────────────────────────────────────────────
        //  页面栈
        // ──────────────────────────────────────────────

        /// <summary>打开新页面压栈。下层页面按 config.HideUnderlying 隐藏。</summary>
        public async UniTask Push(string pageId, PageConfig? config = null, CancellationToken ct = default)
        {
            var cfg = config ?? PageConfig.Default;
            if (!BeginOpen(pageId)) return;
            try
            {
                var opened = await InstantiateViewAsync(pageId, UILayer.Page, ct);
                if (cfg.HideUnderlying && _pageStack.Count > 0)
                    _pageStack[_pageStack.Count - 1].Instance.SetActive(false);
                await SafeEnterAsync(opened.View, ct);
                _pageStack.Add(new PageEntry { View = opened.View, Instance = opened.Instance, Id = pageId, Config = cfg });
                NotifyPushContext(cfg);
            }
            finally { EndOpen(pageId); }
        }

        /// <summary>关闭当前页面，回到上层</summary>
        public async UniTask Pop(CancellationToken ct = default)
        {
            if (_pageStack.Count == 0)
            {
                Log.Warning("UIManager", "页面栈为空，Pop 跳过。");
                return;
            }
            var entry = _pageStack[_pageStack.Count - 1];
            await CloseEntryAsync(entry, restoreUnderlying: true, ct);
            _pageStack.RemoveAt(_pageStack.Count - 1);
        }

        /// <summary>替换当前页面（不增加栈深度）</summary>
        public async UniTask Replace(string pageId, PageConfig? config = null, CancellationToken ct = default)
        {
            var cfg = config ?? PageConfig.Default;
            if (_pageStack.Count > 0)
            {
                var top = _pageStack[_pageStack.Count - 1];
                await CloseEntryAsync(top, restoreUnderlying: false, ct);
                _pageStack.RemoveAt(_pageStack.Count - 1);
            }
            if (!BeginOpen(pageId)) return;
            try
            {
                var opened = await InstantiateViewAsync(pageId, UILayer.Page, ct);
                if (cfg.HideUnderlying && _pageStack.Count > 0)
                    _pageStack[_pageStack.Count - 1].Instance.SetActive(false);
                await SafeEnterAsync(opened.View, ct);
                _pageStack.Add(new PageEntry { View = opened.View, Instance = opened.Instance, Id = pageId, Config = cfg });
                NotifyPushContext(cfg);
            }
            finally { EndOpen(pageId); }
        }

        /// <summary>弹出到指定页面（关闭其上的所有页面）</summary>
        public async UniTask PopTo(string pageId, CancellationToken ct = default)
        {
            int target = -1;
            for (int i = 0; i < _pageStack.Count; i++)
            {
                if (_pageStack[i].Id == pageId) { target = i; break; }
            }
            if (target < 0)
            {
                Log.Warning("UIManager", $"PopTo 未找到页面 '{pageId}'。");
                return;
            }
            // 从栈顶关到 target+1
            for (int i = _pageStack.Count - 1; i > target; i--)
            {
                await CloseEntryAsync(_pageStack[i], restoreUnderlying: false, ct);
                _pageStack.RemoveAt(i);
            }
            // 恢复目标显示
            _pageStack[target].Instance.SetActive(true);
        }

        /// <summary>清空页面栈</summary>
        public async UniTask PopAll(CancellationToken ct = default)
        {
            for (int i = _pageStack.Count - 1; i >= 0; i--)
            {
                await CloseEntryAsync(_pageStack[i], restoreUnderlying: false, ct);
            }
            _pageStack.Clear();
        }

        // ──────────────────────────────────────────────
        //  弹窗队列
        // ──────────────────────────────────────────────

        /// <summary>加入弹窗队列。当前无弹窗则立即显示，否则排队等待。</summary>
        public async UniTask ShowPopup(string popupId, int priority = 0, CancellationToken ct = default)
        {
            InsertPopup(popupId, priority);
            if (_currentPopup == null)
                await ShowNextPopupAsync(ct);
        }

        /// <summary>关闭当前弹窗，出列下一个</summary>
        public async UniTask ClosePopup(CancellationToken ct = default)
        {
            if (_currentPopup == null)
            {
                Log.Warning("UIManager", "当前无弹窗可关闭。");
                return;
            }
            await CloseViewAsync(_currentPopup, _currentPopupInstance, ct);
            _currentPopup = null;
            _currentPopupInstance = null;
            _currentPopupId = null;
            await ShowNextPopupAsync(ct);
        }

        /// <summary>清空弹窗队列并关闭当前弹窗</summary>
        public async UniTask CloseAllPopups(CancellationToken ct = default)
        {
            _popupQueue.Clear();
            if (_currentPopup != null)
            {
                await CloseViewAsync(_currentPopup, _currentPopupInstance, ct);
                _currentPopup = null;
                _currentPopupInstance = null;
                _currentPopupId = null;
            }
        }

        // ──────────────────────────────────────────────
        //  HUD
        // ──────────────────────────────────────────────

        /// <summary>显示 HUD（已显示则跳过）</summary>
        public async UniTask ShowHUD(string hudId, CancellationToken ct = default)
        {
            if (_huds.ContainsKey(hudId))
            {
                Log.Info("UIManager", $"HUD '{hudId}' 已显示。");
                return;
            }
            if (!BeginOpen(hudId)) return;
            try
            {
                var opened = await InstantiateViewAsync(hudId, UILayer.HUD, ct);
                await SafeEnterAsync(opened.View, ct);
                _huds[hudId] = (opened.View, opened.Instance);
            }
            finally { EndOpen(hudId); }
        }

        /// <summary>隐藏 HUD</summary>
        public async UniTask HideHUD(string hudId, CancellationToken ct = default)
        {
            if (!_huds.TryGetValue(hudId, out var hud))
            {
                Log.Warning("UIManager", $"HUD '{hudId}' 未显示。");
                return;
            }
            await CloseViewAsync(hud.view, hud.instance, ct);
            _huds.Remove(hudId);
        }

        /// <summary>HUD 是否已显示</summary>
        public bool IsHUDShown(string hudId) => _huds.ContainsKey(hudId);

        // ──────────────────────────────────────────────
        //  查询
        // ──────────────────────────────────────────────

        /// <summary>页面栈深度</summary>
        public int PageStackDepth => _pageStack.Count;

        /// <summary>指定页面是否在栈中</summary>
        public bool IsPageInStack(string pageId)
        {
            for (int i = 0; i < _pageStack.Count; i++)
                if (_pageStack[i].Id == pageId) return true;
            return false;
        }

        /// <summary>弹窗队列是否为空（含当前显示的弹窗）</summary>
        public bool IsPopupQueueEmpty => _currentPopup == null && _popupQueue.Count == 0;

        /// <summary>弹窗队列总数（含当前显示的弹窗）</summary>
        public int PopupQueueCount => _popupQueue.Count + (_currentPopup != null ? 1 : 0);

        // ──────────────────────────────────────────────
        //  预加载
        // ──────────────────────────────────────────────

        /// <summary>批量预加载 UI prefab（不实例化）</summary>
        public UniTask PreloadAsync(
            IReadOnlyList<string> viewIds, IProgress<float> progress = null, CancellationToken ct = default)
        {
            return _resMgr.PreloadAsync(viewIds, progress, ct);
        }

        // ──────────────────────────────────────────────
        //  内部：实例化/关闭
        // ──────────────────────────────────────────────

        private async UniTask<OpenedView> InstantiateViewAsync(string viewId, UILayer layer, CancellationToken ct)
        {
            var instance = await _resMgr.InstantiateAsync(viewId, ct);
            if (instance == null)
                throw new InvalidOperationException($"[UIManager] 实例化失败: '{viewId}'");

            var view = instance.GetComponent<View>();
            if (view == null)
            {
                _resMgr.Release(instance);
                throw new InvalidOperationException($"[UIManager] Prefab '{viewId}' 缺少 View 组件。");
            }

            var vm = view.CreateViewModel();
            view.InitializeInternal(vm);

            instance.transform.SetParent(GetLayer(layer), false);
            instance.transform.localScale = Vector3.one;
            SetStretch(instance.transform as RectTransform);

            return new OpenedView { View = view, Instance = instance };
        }

        private async UniTask CloseViewAsync(View view, GameObject instance, CancellationToken ct)
        {
            await SafeExitAsync(view, ct);
            SafeCleanup(view);
            _resMgr.Release(instance);
        }

        private async UniTask CloseEntryAsync(PageEntry entry, bool restoreUnderlying, CancellationToken ct)
        {
            await SafeExitAsync(entry.View, ct);
            SafeCleanup(entry.View);
            _resMgr.Release(entry.Instance);

            if (restoreUnderlying && _pageStack.Count > 1)
            {
                // entry 仍在栈中（调用方稍后移除），恢复其下层
                _pageStack[_pageStack.Count - 2].Instance.SetActive(true);
            }

            if (entry.Config.InputContext.HasValue && _onPopInputContext != null)
                _onPopInputContext();
        }

        private async UniTask ShowNextPopupAsync(CancellationToken ct)
        {
            if (_popupQueue.Count == 0) return;
            var next = _popupQueue[0];
            _popupQueue.RemoveAt(0);
            if (!BeginOpen(next.Id))
            {
                // 极少情况：跳过并尝试下一个
                await ShowNextPopupAsync(ct);
                return;
            }
            try
            {
                var opened = await InstantiateViewAsync(next.Id, UILayer.Popup, ct);
                await SafeEnterAsync(opened.View, ct);
                _currentPopup = opened.View;
                _currentPopupInstance = opened.Instance;
                _currentPopupId = next.Id;
            }
            finally { EndOpen(next.Id); }
        }

        // ──────────────────────────────────────────────
        //  内部：辅助
        // ──────────────────────────────────────────────

        private bool BeginOpen(string id)
        {
            if (!_opening.Add(id))
            {
                Log.Warning("UIManager", $"'{id}' 正在异步打开中，跳过重复请求。");
                return false;
            }
            return true;
        }

        private void EndOpen(string id) => _opening.Remove(id);

        private void NotifyPushContext(PageConfig cfg)
        {
            if (cfg.InputContext.HasValue && _onPushInputContext != null)
                _onPushInputContext(cfg.InputContext.Value);
        }

        private async UniTask SafeEnterAsync(View view, CancellationToken ct)
        {
            try { await view.PlayEnterAnimationInternal(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Log.Error("UIManager", $"入场动画异常: {e}"); }
        }

        private async UniTask SafeExitAsync(View view, CancellationToken ct)
        {
            try { await view.PlayExitAnimationInternal(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Log.Error("UIManager", $"出场动画异常: {e}"); }
        }

        private void SafeCleanup(View view)
        {
            try { view.CleanupInternal(); }
            catch (Exception e) { Log.Error("UIManager", $"Cleanup 异常: {e}"); }
        }

        private void InsertPopup(string id, int priority)
        {
            // 稳定插入：高优先在前，同优先 FIFO（从后向前找到第一个 priority >= 的位置，插其后）
            int idx = _popupQueue.Count;
            for (int i = 0; i < _popupQueue.Count; i++)
            {
                if (_popupQueue[i].Priority < priority)
                {
                    idx = i;
                    break;
                }
            }
            _popupQueue.Insert(idx, new PendingPopup { Id = id, Priority = priority });
        }

        private Transform GetLayer(UILayer layer)
        {
            return layer switch
            {
                UILayer.HUD => _hudLayer,
                UILayer.Popup => _popupLayer,
                UILayer.Top => _topLayer,
                _ => _pageLayer,
            };
        }

        private void CreateLayers()
        {
            _root = new GameObject("PumpGF_UIRoot");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            (_hudCanvas, _hudLayer) = CreateLayerCanvas("UI_HUD", _layerConfig.HudOrder, _root.transform);
            (_pageCanvas, _pageLayer) = CreateLayerCanvas("UI_Page", _layerConfig.PageOrder, _root.transform);
            (_popupCanvas, _popupLayer) = CreateLayerCanvas("UI_Popup", _layerConfig.PopupOrder, _root.transform);
            (_topCanvas, _topLayer) = CreateLayerCanvas("UI_Top", _layerConfig.TopOrder, _root.transform);
        }

        private static (Canvas, Transform) CreateLayerCanvas(string name, int sortingOrder, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return (canvas, go.transform);
        }

        private static void SetStretch(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // ──────────────────────────────────────────────
        //  内部类型
        // ──────────────────────────────────────────────

        private struct PageEntry
        {
            public View View;
            public GameObject Instance;
            public string Id;
            public PageConfig Config;
        }

        private struct PendingPopup
        {
            public string Id;
            public int Priority;
        }

        private struct OpenedView
        {
            public View View;
            public GameObject Instance;
        }
    }
}
