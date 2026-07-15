namespace PumpGF
{
    /// <summary>
    /// 日志级别。FilterLevel 低于此级别的不输出。
    /// </summary>
    public enum LogLevel
    {
        /// <summary>开发期详细日志</summary>
        Debug = 0,
        /// <summary>常规信息</summary>
        Info = 1,
        /// <summary>警告</summary>
        Warning = 2,
        /// <summary>错误</summary>
        Error = 3,
    }
}
