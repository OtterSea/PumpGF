using System;
using System.Collections.Generic;
using R3;

namespace PumpGF
{
    /// <summary>
    /// Command 历史条目（内存，Debug 用，不持久化）。
    /// </summary>
    public readonly struct CommandHistoryEntry
    {
        /// <summary>Command 类型</summary>
        public readonly Type CommandType;
        /// <summary>Command 实例（boxed，仅 Debug）</summary>
        public readonly object Command;
        /// <summary>时间戳</summary>
        public readonly DateTime Timestamp;

        public CommandHistoryEntry(Type type, object cmd, DateTime time)
        {
            CommandType = type;
            Command = cmd;
            Timestamp = time;
        }
    }

    /// <summary>
    /// 游戏运行时数据状态的中枢。所有可持久化数据集中在数据域中。
    /// 修改经统一入口（ReactiveProperty 或 Command），变更自动通知且可审计。
    /// Save/Load System 在其之下负责纯持久化。
    /// </summary>
    public sealed class GameDataStore : IModule
    {
        // 数据域（按类型注册，类型安全）
        private readonly Dictionary<Type, object> _domains = new();

        // Command 路由
        private readonly Dictionary<Type, Action<object>> _handlers = new();

        // Command 历史（内存，Debug）
        private readonly List<CommandHistoryEntry> _history = new();
        private int _historyLimit = 1000;

        public void Init() { }

        // ──────────────────────────────────────────────
        //  数据域管理
        // ──────────────────────────────────────────────

        /// <summary>注册数据域（类型安全，按类型作 key）</summary>
        public void RegisterDomain<T>(T domain) where T : class
        {
            if (domain == null) throw new ArgumentNullException(nameof(domain));
            var type = typeof(T);
            if (_domains.ContainsKey(type))
            {
                Log.Warning("GameDataStore", $"Domain '{type.Name}' already registered, overwriting.");
            }
            _domains[type] = domain;
        }

        /// <summary>获取数据域</summary>
        public T GetDomain<T>() where T : class
        {
            return _domains.TryGetValue(typeof(T), out var d) ? d as T : null;
        }

        /// <summary>是否有指定数据域</summary>
        public bool HasDomain<T>() where T : class
        {
            return _domains.ContainsKey(typeof(T));
        }

        // ──────────────────────────────────────────────
        //  Command 系统（模式 B：严格模式）
        // ──────────────────────────────────────────────

        /// <summary>注册 Command 处理器</summary>
        public void RegisterHandler<TCommand>(Action<TCommand> handler)
            where TCommand : struct, IDataCommand
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var type = typeof(TCommand);
            if (_handlers.ContainsKey(type))
            {
                Log.Warning("GameDataStore", $"Handler for '{type.Name}' already registered, overwriting.");
            }
            _handlers[type] = cmd => handler((TCommand)cmd);
        }

        /// <summary>执行 Command（记录历史 + 调用 handler）</summary>
        public void Execute<TCommand>(TCommand command)
            where TCommand : struct, IDataCommand
        {
            var type = typeof(TCommand);
            if (_handlers.TryGetValue(type, out var handler))
            {
#if DEBUG
                // 用环形逻辑：仅在 DEBUG 构建记录，且用 List 但用尾进头出。
                // 用 List 保留因 API 兼容；参见 P2-9 备注。
                if (_history.Count >= _historyLimit)
                    _history.RemoveAt(0);
                _history.Add(new CommandHistoryEntry(type, command, DateTime.Now));
#endif
                handler(command);
            }
            else
            {
                Log.Warning("GameDataStore", $"No handler registered for command: {type.Name}");
            }
        }

        /// <summary>是否有指定 Command 的处理器</summary>
        public bool HasHandler<TCommand>() where TCommand : struct, IDataCommand
        {
            return _handlers.ContainsKey(typeof(TCommand));
        }

        // ──────────────────────────────────────────────
        //  Command 历史（Debug 专用，Release 包为空）
        // ──────────────────────────────────────────────

        /// <summary>
        /// 获取 Command 历史（Debug 面板用）。
        /// <b>注意</b>：仅在 DEBUG 构建下有效，Release 包始终返回空列表。
        /// </summary>
        public IReadOnlyList<CommandHistoryEntry> GetCommandHistory()
        {
#if !DEBUG
            Log.Warning("GameDataStore", "GetCommandHistory 在 Release 构建下不记录数据，返回空列表。");
#endif
            return _history;
        }

        /// <summary>清空 Command 历史</summary>
        public void ClearCommandHistory() => _history.Clear();

        /// <summary>设置历史上限</summary>
        public void SetHistoryLimit(int limit) => _historyLimit = limit;

        // ──────────────────────────────────────────────
        //  校验
        // ──────────────────────────────────────────────

        /// <summary>全量校验所有数据域（Debug 用）</summary>
        public IReadOnlyList<string> ValidateAll()
        {
            var errors = new List<string>();
            // 各数据域可自行校验，业务层扩展
            return errors;
        }

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Dispose()
        {
            _handlers.Clear();
            _history.Clear();
            _domains.Clear();
        }
    }
}
