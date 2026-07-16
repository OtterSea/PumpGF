namespace PumpGF
{
    /// <summary>
    /// 组件标记接口。Component 是纯 C# 类，不依赖 MonoBehaviour，可单元测试。
    /// </summary>
    public interface IComponent { }

    /// <summary>
    /// 可重置组件接口（池化回池时调用，可选实现）。
    /// <para>未实现时回池走 <see cref="Entity.RemoveAll"/> + 重新配置。</para>
    /// </summary>
    public interface IResettable
    {
        /// <summary>重置组件状态（回池复用前调用）</summary>
        void Reset();
    }
}
