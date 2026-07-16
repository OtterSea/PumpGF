using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PumpGF
{
    /// <summary>
    /// 运行时调试与作弊验证工具。提供命令系统、性能面板、Gizmos 可视化。
    /// <para>主体用 #if DEBUG 包裹，Release 包剔除为空壳（Log 类始终保留，见 <see cref="Log"/>）。</para>
    /// <para>类本身始终存在，GameGlobal 始终注册；Release 时方法为 no-op。</para>
    /// </summary>
    public sealed class DebugConsole : IModule
    {
        // 命令条目（#if DEBUG 内使用）
        private readonly Dictionary<string, CommandEntry> _commands = new();
        private readonly List<string> _history = new(32);
        private readonly List<string> _consoleLog = new(64);
        private readonly List<IDebugPanel> _panels = new(4);
        private readonly List<IGizmosDrawable> _gizmos = new(8);

        public DebugConsoleConfig Config { get; set; } = new();
        public bool GizmosEnabled { get; set; } = true;

#if DEBUG
        private DebugConsoleDriver _driver;
        private bool _visible;

        public bool IsVisible => _visible;

        public void Init()
        {
            ScanCommands();
            var go = new GameObject("PumpGF_DebugConsole");
            Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<DebugConsoleDriver>();
            _driver.Init(this);
            RegisterPanel(new StatsPanel());
            AppendLog("DebugConsole 就绪。输入 help 查看命令。");
            Log.Info("DebugConsole", "DebugConsole initialized.");
        }

        public void Dispose()
        {
            _commands.Clear();
            _history.Clear();
            _consoleLog.Clear();
            _panels.Clear();
            _gizmos.Clear();
            if (_driver != null)
            {
                Object.Destroy(_driver.gameObject);
                _driver = null;
            }
            _visible = false;
        }

        // ── 命令 ──

        public void Execute(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return;
            _history.Add(commandLine);
            if (_history.Count > 64) _history.RemoveAt(0);
            AppendLog("> " + commandLine);

            var parts = commandLine.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string name = parts[0];

            if (name == "help") { ListCommands(); return; }

            if (!_commands.TryGetValue(name, out var entry))
            {
                AppendLog($"未知命令: {name}（输入 help 查看命令列表）");
                return;
            }

            var parameters = entry.Method.GetParameters();
            if (parts.Length - 1 < parameters.Length)
            {
                AppendLog($"参数不足。用法: {entry.Info.Usage}");
                return;
            }

            var args = new object[parameters.Length];
            try
            {
                for (int i = 0; i < parameters.Length; i++)
                    args[i] = ParseArg(parts[i + 1], parameters[i].ParameterType);
            }
            catch (Exception e)
            {
                AppendLog($"参数解析失败: {e.Message}");
                return;
            }

            try
            {
                entry.Method.Invoke(null, args);
            }
            catch (Exception e)
            {
                AppendLog($"命令执行异常: {e.InnerException?.Message ?? e.Message}");
            }
        }

        public IReadOnlyList<CommandInfo> GetCommands()
        {
            var list = new List<CommandInfo>(_commands.Count);
            foreach (var kvp in _commands) list.Add(kvp.Value.Info);
            return list;
        }

        public IReadOnlyList<string> GetCommandHistory() => _history;
        public IReadOnlyList<string> GetConsoleLog() => _consoleLog;

        internal string GetPreviousHistory(ref int index)
        {
            if (_history.Count == 0) return "";
            if (index < 0) index = _history.Count;
            index = Mathf.Max(0, index - 1);
            return index < _history.Count ? _history[index] : "";
        }

        internal string GetNextHistory(ref int index)
        {
            if (_history.Count == 0) return "";
            index = Mathf.Min(_history.Count, index + 1);
            return index < _history.Count ? _history[index] : "";
        }

        // ── 面板 ──

        public void RegisterPanel(IDebugPanel panel)
        {
            if (panel != null && !_panels.Contains(panel)) _panels.Add(panel);
        }

        public void UnregisterPanel(IDebugPanel panel) => _panels.Remove(panel);

        // ── Gizmos ──

        public void RegisterGizmos(IGizmosDrawable drawable)
        {
            if (drawable != null && !_gizmos.Contains(drawable)) _gizmos.Add(drawable);
        }

        public void UnregisterGizmos(IGizmosDrawable drawable) => _gizmos.Remove(drawable);

        // ── 开关 ──

        public void Show() { _visible = true; }
        public void Hide() { _visible = false; }
        public void Toggle() { _visible = !_visible; }

        // ── 内部：Driver 调用 ──

        internal void DrawPanels(Rect rect)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                _panels[i].OnGUI(rect);
                rect.y += rect.height + 4;
            }
        }

        internal void DrawGizmos()
        {
            for (int i = 0; i < _gizmos.Count; i++)
            {
                try { _gizmos[i].DrawGizmos(); }
                catch (Exception e) { Log.Error("DebugConsole", $"Gizmos 异常: {e.Message}"); }
            }
        }

        // ── 反射扫描 ──

        private void ScanCommands()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                    catch { continue; }

                    foreach (var method in methods)
                    {
                        var attr = method.GetCustomAttribute<ConsoleCommandAttribute>();
                        if (attr == null) continue;
                        RegisterCommand(attr, method);
                    }
                }
            }
            Log.Info("DebugConsole", $"扫描注册 {_commands.Count} 个命令。");
        }

        private void RegisterCommand(ConsoleCommandAttribute attr, MethodInfo method)
        {
            var parameters = method.GetParameters();
            var paramTypes = new Type[parameters.Length];
            var usage = attr.CommandName;
            for (int i = 0; i < parameters.Length; i++)
            {
                paramTypes[i] = parameters[i].ParameterType;
                usage += $" <{parameters[i].ParameterType.Name}>";
            }
            var info = new CommandInfo
            {
                Name = attr.CommandName,
                Description = attr.Description ?? "",
                ParameterTypes = paramTypes,
                Usage = usage
            };
            _commands[attr.CommandName] = new CommandEntry { Method = method, Info = info };
        }

        private void ListCommands()
        {
            AppendLog($"共 {_commands.Count} 个命令:");
            foreach (var kvp in _commands)
            {
                var c = kvp.Value.Info;
                AppendLog($"  {c.Usage}  {(string.IsNullOrEmpty(c.Description) ? "" : "// " + c.Description)}");
            }
        }

        private static object ParseArg(string s, Type targetType)
        {
            if (targetType == typeof(int)) return int.Parse(s);
            if (targetType == typeof(float)) return float.Parse(s);
            if (targetType == typeof(bool)) return bool.Parse(s);
            if (targetType == typeof(string)) return s;
            throw new NotSupportedException($"不支持的参数类型: {targetType.Name}");
        }

        private void AppendLog(string line)
        {
            _consoleLog.Add(line);
            if (_consoleLog.Count > 200) _consoleLog.RemoveAt(0);
        }

        private struct CommandEntry
        {
            public MethodInfo Method;
            public CommandInfo Info;
        }

#else
        // Release 空壳
        public bool IsVisible => false;

        public void Init() { }
        public void Dispose() { }
        public void Execute(string commandLine) { }
        public IReadOnlyList<CommandInfo> GetCommands() => Array.Empty<CommandInfo>();
        public IReadOnlyList<string> GetCommandHistory() => Array.Empty<string>();
        public IReadOnlyList<string> GetConsoleLog() => Array.Empty<string>();
        public void RegisterPanel(IDebugPanel panel) { }
        public void UnregisterPanel(IDebugPanel panel) { }
        public void RegisterGizmos(IGizmosDrawable drawable) { }
        public void UnregisterGizmos(IGizmosDrawable drawable) { }
        public void Show() { }
        public void Hide() { }
        public void Toggle() { }
        internal void DrawPanels(Rect rect) { }
        internal void DrawGizmos() { }
#endif
    }
}
