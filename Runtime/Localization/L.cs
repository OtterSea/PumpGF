namespace PumpGF
{
    /// <summary>
    /// 本地化文本快捷访问类。
    /// <para><c>L.T("Welcome")</c> → 当前语言文本</para>
    /// <para><c>L.T("KillCount", 42)</c> → 格式化文本</para>
    /// </summary>
    public static class L
    {
        /// <summary>获取 key 对应的当前语言文本</summary>
        public static string T(string key)
        {
            return GameGlobal.Localization.GetText(key);
        }

        /// <summary>获取格式化文本（如 <c>L.T("KillCount", 42)</c> → "击杀了 42 个敌人"）</summary>
        public static string T(string key, params object[] args)
        {
            return string.Format(GameGlobal.Localization.GetText(key), args);
        }
    }
}
