namespace PumpGF
{
    /// <summary>
    /// 数据命令标记接口。Command 是 readonly struct，通过 GameDataStore.Execute&lt;TCommand&gt; 执行。
    /// 用于需要审计的数据修改（伤害结算、经济交易、任务进度等）。
    /// </summary>
    public interface IDataCommand { }
}
