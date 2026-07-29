using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 场景有序初始化编排器。挂在场景根节点上，在场景加载后按优先级顺序 await 调用所有
    /// <see cref="ISceneInitializable"/>，把"跨对象初始化"从不可控的 Awake/Start 时序，
    /// 收敛为<strong>显式、有序、可 await</strong> 的流程，从机制上消除时序竞态。
    /// </summary>
    /// <remarks>
    /// <b>放置约定</b>：每个场景放一个（Single 场景）或每个 Additive 子场景各放一个。
    /// 使用 <c>[DefaultExecutionOrder(-900)]</c> 使其 Start 早于普通脚本，率先发起初始化。
    /// <para>
    /// <b>消费约定</b>：依赖跨对象引用的业务逻辑不得写在 Awake/Start；二选一：
    /// <list type="bullet">
    /// <item>实现 <see cref="ISceneInitializable"/>，在 <see cref="ISceneInitializable.InitializeAsync"/> 里完成依赖装配（依赖项的优先级应更高）；</item>
    /// <item>或在 Update 订阅前先 <c>await SceneBootstrapper.Current.WaitUntilInitializedAsync()</c>。</item>
    /// </list>
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-900)]
    public sealed class SceneBootstrapper : MonoBehaviour
    {
        [Tooltip("手动指定的初始化对象（必须实现 ISceneInitializable）。优先级高于自动扫描。")]
        [SerializeField] private List<MonoBehaviour> _manualRegistrations = new();

        [Tooltip("是否自动扫描本对象子层级（含自身）的 ISceneInitializable。")]
        [SerializeField] private bool _includeChildren = true;

        [Tooltip("扫描时是否包含未激活的 GameObject 上的组件。")]
        [SerializeField] private bool _includeInactive = false;

        [Tooltip("整个场景初始化的超时秒数。超时则取消并视为失败（防止卡死在 Loading）。")]
        [SerializeField] private float _maxInitSeconds = 30f;

        private readonly UniTaskCompletionSource _initTcs = new();
        private bool _initialized;
        private bool _initFailed;

        /// <summary>当前场景的引导器（最近开始初始化的一个）。Single 场景下即为当前场景引导器。</summary>
        public static SceneBootstrapper Current { get; private set; }

        /// <summary>场景是否已完成初始化（无论成功失败，完成后为 true）。</summary>
        public bool IsInitialized => _initialized;

        /// <summary>初始化是否因异常 / 超时失败。</summary>
        public bool InitFailed => _initFailed;

        /// <summary>
        /// 等待场景初始化完成。已完成则同步完成。
        /// <para>使用 <see cref="UniTaskExtensions.AttachExternalCancellation"/>，
        /// 单个调用者的取消不影响引导器本身或其他等待者。</para>
        /// </summary>
        public UniTask WaitUntilInitializedAsync(CancellationToken ct = default)
        {
            if (_initialized) return UniTask.CompletedTask;
            return _initTcs.Task.AttachExternalCancellation(ct);
        }

        private void Start()
        {
            Current = this;
            RunAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid RunAsync(CancellationToken destroyCt)
        {
            CancellationTokenSource timeoutCts = null;
            try
            {
                var initializables = GatherInitializables();
                initializables.Sort((a, b) => b.Priority.CompareTo(a.Priority));

                timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(0.1f, _maxInitSeconds)));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCt, timeoutCts.Token);

                for (int i = 0; i < initializables.Count; i++)
                {
                    var item = initializables[i];
                    try
                    {
                        await item.InitializeAsync(linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 转交外层统一处理
                    }
                    catch (Exception e)
                    {
                        Log.Error("SceneBoot", $"初始化异常 [{item.GetType().Name}] Priority={item.Priority}: {e}");
                    }
                }

                _initialized = true;
                _initTcs.TrySetResult();
                Log.Info("SceneBoot", $"场景初始化完成（{initializables.Count} 项，{gameObject.scene.name}）。");
            }
            catch (OperationCanceledException)
            {
                _initFailed = true;
                _initTcs.TrySetCanceled();
                Log.Warning("SceneBoot", $"场景初始化被取消/超时（{gameObject.scene.name}）。");
            }
            finally
            {
                timeoutCts?.Dispose();
                if (Current == this) Current = null;
            }
        }

        private List<ISceneInitializable> GatherInitializables()
        {
            var result = new List<ISceneInitializable>(8);

            // 1) 手动注册（优先，便于精确控制顺序与不可见对象）
            for (int i = 0; i < _manualRegistrations.Count; i++)
            {
                if (_manualRegistrations[i] is ISceneInitializable init)
                {
                    if (!result.Contains(init)) result.Add(init);
                }
                else
                {
                    Log.Warning("SceneBoot",
                        $"手动注册项 '{_manualRegistrations[i]?.GetType().Name}' 未实现 ISceneInitializable，已忽略。");
                }
            }

            // 2) 自动扫描子层级
            if (_includeChildren)
            {
                var monos = GetComponentsInChildren<MonoBehaviour>(_includeInactive);
                for (int i = 0; i < monos.Length; i++)
                {
                    if (monos[i] is ISceneInitializable init && !result.Contains(init))
                        result.Add(init);
                }
            }

            return result;
        }
    }
}
