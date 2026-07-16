using System;
using System.Collections.Generic;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 实体。纯 C# 数据与逻辑的组合，持有多个 <see cref="IComponent"/>，
    /// 可选关联 GameObject（View）与 <see cref="StateMachine"/>（FSM）。
    /// </summary>
    public sealed class Entity
    {
        private readonly Dictionary<Type, IComponent> _components = new(4);

        /// <summary>全局唯一 Id（由 EntityManager 分配）</summary>
        public int Id { get; internal set; }

        /// <summary>关联的 GameObject（View 层，可选）</summary>
        public GameObject GameObject { get; set; }

        /// <summary>关联的状态机（可选）</summary>
        public StateMachine StateMachine { get; set; }

        /// <summary>所属 EntityManager（内部，组件索引维护用）</summary>
        internal EntityManager Manager { get; set; }

        /// <summary>池化 key（回池用，内部）</summary>
        internal string PoolKey { get; set; }

        // ──────────────────────────────────────────────
        //  组件操作
        // ──────────────────────────────────────────────

        /// <summary>获取组件（不存在返回 default）</summary>
        public T Get<T>() where T : IComponent
        {
            return _components.TryGetValue(typeof(T), out var c) ? (T)c : default;
        }

        /// <summary>是否拥有指定组件</summary>
        public bool Has<T>() where T : IComponent => _components.ContainsKey(typeof(T));

        /// <summary>是否拥有指定组件（别名：<see cref="Has{T}"/>）</summary>
        public bool HasComponent<T>() where T : IComponent => _components.ContainsKey(typeof(T));

        /// <summary>获取双组件</summary>
        public (T1, T2) Get<T1, T2>() where T1 : IComponent where T2 : IComponent
        {
            return (Get<T1>(), Get<T2>());
        }

        /// <summary>
        /// 获取组件，若不存在则创建并注册（需要 <typeparamref name="T"/> 有无参构造）。
        /// 业务常见的 <c>if (e.Get&lt;X&gt;() == null) e.Add&lt;X&gt;()</c> 简写为 <c>e.GetOrAdd&lt;X&gt;()</c>。
        /// </summary>
        public T GetOrAdd<T>() where T : class, IComponent, new()
        {
            if (_components.TryGetValue(typeof(T), out var existing))
                return (T)existing;
            var c = new T();
            _components[typeof(T)] = c;
            Manager?.OnComponentAdded(this, typeof(T));
            return c;
        }

        /// <summary>获取组件，不存在则通过工厂创建并注册（用于无无参构造的类型）</summary>
        public T GetOrAdd<T>(Func<T> factory) where T : class, IComponent
        {
            if (_components.TryGetValue(typeof(T), out var existing))
                return (T)existing;
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var c = factory();
            if (c == null) throw new InvalidOperationException("factory returned null");
            _components[typeof(T)] = c;
            Manager?.OnComponentAdded(this, typeof(T));
            return c;
        }

        /// <summary>TryGet 语义（避免 null 检查歧义，struct 组件也能安全查询）</summary>
        public bool TryGet<T>(out T component) where T : IComponent
        {
            if (_components.TryGetValue(typeof(T), out var c))
            {
                component = (T)c;
                return true;
            }
            component = default;
            return false;
        }

        /// <summary>添加组件实例</summary>
        public void Add<T>(T component) where T : IComponent
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            _components[typeof(T)] = component;
            Manager?.OnComponentAdded(this, typeof(T));
        }

        /// <summary>添加并配置组件（new + configure）</summary>
        public void Add<T>(Action<T> configure = null) where T : IComponent, new()
        {
            var c = new T();
            configure?.Invoke(c);
            _components[typeof(T)] = c;
            Manager?.OnComponentAdded(this, typeof(T));
        }

        /// <summary>移除组件（返回是否移除成功）</summary>
        public bool Remove<T>() where T : IComponent
        {
            var type = typeof(T);
            if (_components.Remove(type))
            {
                Manager?.OnComponentRemoved(this, type);
                return true;
            }
            return false;
        }

        /// <summary>移除所有组件</summary>
        public void RemoveAll()
        {
            if (Manager != null)
            {
                foreach (var kvp in _components)
                    Manager.OnComponentRemoved(this, kvp.Key);
            }
            _components.Clear();
        }

        // ──────────────────────────────────────────────
        //  内部：销毁/重置
        // ──────────────────────────────────────────────

        /// <summary>重置实体（回池复用前调用）：清组件 + 解除关联</summary>
        internal void Reset()
        {
            // IResettable 组件先 Reset
            foreach (var kvp in _components)
            {
                if (kvp.Value is IResettable r) r.Reset();
            }
            RemoveAll();
            StateMachine = null;
            GameObject = null;
        }

        /// <summary>销毁清理（EntityManager.Destroy 调用）</summary>
        internal void DisposeInternal()
        {
            RemoveAll();
            StateMachine = null;
            GameObject = null;
            Manager = null;
        }
    }
}
