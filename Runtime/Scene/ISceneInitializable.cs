using Cysharp.Threading.Tasks;
using System.Threading;

namespace PumpGF
{
    /// <summary>
    /// 场景内有序初始化契约。由 <see cref="SceneBootstrapper"/> 在场景加载后
    /// <strong>按优先级顺序</strong> await 调用，替代散落的 Awake/Start 跨对象依赖。
    /// </summary>
    /// <remarks>
    /// <b>职责边界</b>：Awake/Start 只做<strong>自身局部</strong>的初始化（字段赋值、组件引用同对象）；
    /// 任何<strong>跨对象</strong>的初始化（获取其它 GameObject 的引用、订阅其它模块、加载依赖资源）
    /// 都应放到 <see cref="InitializeAsync"/>，由 SceneBootstrapper 在依赖项就绪后按序触发。
    /// <para>
    /// <b>优先级</b>：数值越大越早执行；同优先级按 SceneBootstrapper 发现顺序。
    /// 典型分级：全局服务 1000 → 玩家 900 → 敌人 800 → UI 700 → 杂项 0。
    /// </para>
    /// </remarks>
    public interface ISceneInitializable
    {
        /// <summary>初始化优先级，数值越大越早执行。</summary>
        int Priority { get; }

        /// <summary>
        /// 跨对象初始化（获取依赖引用、订阅事件、加载依赖资源）。
        /// 由 SceneBootstrapper 按优先级顺序 await 调用，
        /// <b>保证此方法被调用时，所有更高优先级的 ISceneInitializable 已完成初始化</b>。
        /// </summary>
        UniTask InitializeAsync(CancellationToken ct);
    }
}
