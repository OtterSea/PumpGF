using System;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace PumpGF
{
    /// <summary>
    /// 两阶段加载关卡契约。把关卡加载拆为 <b>Preload（异步预载）</b> 与 <b>Activate（就绪激活）</b>
    /// 两个阶段，从流程上消除"边加载边消费"的时序竞态窗口。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>与 <see cref="ILevel"/> 的关系</b>：两者并列，<see cref="LevelManager"/> 分别提供
    /// <see cref="LevelManager.LoadLevelAsync"/>（旧 ILevel 单阶段）与
    /// <see cref="LevelManager.LoadLevelPhasedAsync"/>（本接口两阶段）。新关卡应优先使用本接口。
    /// </para>
    /// <para>
    /// <b>阶段语义</b>：
    /// <list type="number">
    /// <item><description><see cref="OnPreloadAsync"/>：异步阶段。加载场景、实例化对象、加载依赖资源。
    /// <b>禁止</b>在此阶段进行任何"消费"——不绑定 Update、不获取其它对象引用、不触发依赖就绪的逻辑。
    /// 此阶段可带进度上报，用于 Loading UI。</description></item>
    /// <item><description><see cref="OnActivateAsync"/>：激活阶段。此时所有 Preload 产出的对象已就绪，
    /// 在此进行跨对象引用装配、事件订阅、<see cref="ReadinessRegistry.MarkReady{T}(T)"/> 宣告就绪等。
    /// 本阶段返回后，LevelManager 才绑定 Update Tick，关卡正式运转。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>关键保证</b>：Activate 在 Preload 完全结束后才执行，因此 Activate 内访问任何 Preload 产出的对象
    /// 都是安全的——彻底消灭"生成 A 与脚本 B 获取 A 引用"的竞态。
    /// </para>
    /// </remarks>
    public interface IPhasedLevel
    {
        /// <summary>预载阶段：异步加载场景 / 实例化对象 / 加载依赖资源。禁止在此消费。</summary>
        UniTask OnPreloadAsync(ILevelData data, IProgress<float> progress, CancellationToken ct);

        /// <summary>激活阶段：Preload 全部就绪后的跨对象装配与就绪宣告。返回后 Update 才绑定。</summary>
        UniTask OnActivateAsync(CancellationToken ct);

        /// <summary>关卡 Update（由 LevelManager 绑定 Lifecycle 通道驱动，Activate 完成后生效）。</summary>
        void OnUpdate(float dt);

        /// <summary>退出关卡：清理、卸载场景、注销就绪标记。</summary>
        UniTask OnExitAsync(CancellationToken ct);
    }
}
