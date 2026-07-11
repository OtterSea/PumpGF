namespace PumpGF
{
    /// <summary>
    /// 所有功能模块的基类接口。定义统一的生命周期管理。
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// 初始化模块。在 GameGlobal 中按依赖顺序调用。
        /// </summary>
        void Init();

        /// <summary>
        /// 释放模块资源。在 Application.quitting 或 GameGlobal.Dispose 时调用。
        /// </summary>
        void Dispose();
    }
}
