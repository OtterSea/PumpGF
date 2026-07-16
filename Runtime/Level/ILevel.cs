using Cysharp.Threading.Tasks;
using System.Threading;

namespace PumpGF
{
    /// <summary>
    /// 关卡数据接口。LoadLevelAsync 时传入，向关卡注入配置（难度/敌人/时限等）。
    /// </summary>
    public interface ILevelData
    {
        /// <summary>关卡 Id</summary>
        string LevelId { get; }
    }

    /// <summary>
    /// 关卡标准化入口。由业务实现，LevelManager 驱动生命周期。
    /// </summary>
    public interface ILevel
    {
        /// <summary>进入关卡：加载场景、初始化、注入数据</summary>
        UniTask OnEnterAsync(ILevelData data, CancellationToken ct);

        /// <summary>退出关卡：清理、卸载场景</summary>
        UniTask OnExitAsync(CancellationToken ct);

        /// <summary>关卡 Update（由 LevelManager 绑定 Lifecycle 通道驱动）</summary>
        void OnUpdate(float dt);
    }
}
