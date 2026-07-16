using System;
using System.Collections.Generic;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 实体全局管理模块。负责 Entity 创建/销毁/查询/池化，维护组件类型索引供高效 Query。
    /// </summary>
    public sealed class EntityManager : IModule
    {
        private readonly Dictionary<int, Entity> _entities = new(128);
        private readonly Dictionary<Type, HashSet<Entity>> _componentIndex = new(16);
        private int _nextId = 1;

        // 池化
        private readonly Dictionary<string, Func<Entity>> _poolFactories = new(8);
        private readonly Dictionary<string, Stack<Entity>> _pools = new(8);

        public void Init() { }

        public void Dispose()
        {
            DestroyAll();
            _entities.Clear();
            foreach (var kvp in _componentIndex) kvp.Value.Clear();
            _componentIndex.Clear();
            foreach (var kvp in _pools) kvp.Value.Clear();
            _pools.Clear();
            _poolFactories.Clear();
        }

        // ──────────────────────────────────────────────
        //  创建
        // ──────────────────────────────────────────────

        /// <summary>创建构建器（链式构建 Entity）</summary>
        public EntityBuilder Create() => EntityBuilder.Create(this);

        /// <summary>直接创建空 Entity</summary>
        public Entity CreateEntity()
        {
            var e = NewEntity();
            Register(e);
            return e;
        }

        internal Entity NewEntity()
        {
            return new Entity { Id = _nextId++, Manager = this };
        }

        internal void Register(Entity e)
        {
            _entities[e.Id] = e;
        }

        // ──────────────────────────────────────────────
        //  销毁
        // ──────────────────────────────────────────────

        /// <summary>销毁实体（移除索引 + 清理）。禁止直接 entity.Dispose。</summary>
        public void Destroy(Entity entity)
        {
            if (entity == null || !_entities.ContainsKey(entity.Id)) return;
            RemoveFromIndex(entity);
            _entities.Remove(entity.Id);
            entity.DisposeInternal();
        }

        /// <summary>销毁所有实体</summary>
        public void DestroyAll()
        {
            foreach (var kvp in _entities)
            {
                kvp.Value.DisposeInternal();
            }
            _entities.Clear();
            foreach (var kvp in _componentIndex) kvp.Value.Clear();
        }

        // ──────────────────────────────────────────────
        //  查询
        // ──────────────────────────────────────────────

        /// <summary>按 Id 获取实体</summary>
        public Entity GetById(int id)
        {
            return _entities.TryGetValue(id, out var e) ? e : null;
        }

        /// <summary>实体总数</summary>
        public int Count => _entities.Count;

        /// <summary>获取指定组件类型的实体集合（内部，Query 用）</summary>
        internal HashSet<Entity> GetIndex(Type type)
        {
            return _componentIndex.TryGetValue(type, out var set) ? set : null;
        }

        internal void OnComponentAdded(Entity e, Type type)
        {
            if (!_componentIndex.TryGetValue(type, out var set))
            {
                set = new HashSet<Entity>();
                _componentIndex[type] = set;
            }
            set.Add(e);
        }

        internal void OnComponentRemoved(Entity e, Type type)
        {
            if (_componentIndex.TryGetValue(type, out var set))
                set.Remove(e);
        }

        private void RemoveFromIndex(Entity e)
        {
            // 遍历所有索引移除（Entity 不知道自己有哪些组件类型，用 Entity 内部）
            // 简化：Entity.RemoveAll 已调 OnComponentRemoved，Destroy 前若未 RemoveAll 则此处补
            // DisposeInternal 会 RemoveAll（触发 OnComponentRemoved），故此处无需重复
        }

        // ──────────────────────────────────────────────
        //  池化
        // ──────────────────────────────────────────────

        /// <summary>注册实体池</summary>
        /// <param name="key">池 key</param>
        /// <param name="factory">创建工厂（从池取空时创建新 Entity）</param>
        /// <param name="preheat">预热数量</param>
        public void RegisterPool(string key, Func<Entity> factory, int preheat = 0)
        {
            _poolFactories[key] = factory;
            if (!_pools.ContainsKey(key))
                _pools[key] = new Stack<Entity>(preheat > 0 ? preheat : 4);

            for (int i = 0; i < preheat; i++)
            {
                var e = factory();
                e.PoolKey = key;
                _pools[key].Push(e);
            }
            Log.Info("EntityManager", $"注册实体池 '{key}'（预热 {preheat}）");
        }

        /// <summary>从池取实体（池空则用工厂创建）</summary>
        public Entity Get(string key)
        {
            if (_pools.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                var e = stack.Pop();
                // 重新注册（之前 Destroy 时已移除？池中实体未 Destroy，仅 Reset）
                if (!_entities.ContainsKey(e.Id))
                    _entities[e.Id] = e;
                return e;
            }
            if (_poolFactories.TryGetValue(key, out var factory))
            {
                var e = factory();
                e.PoolKey = key;
                return e;
            }
            Log.Warning("EntityManager", $"未注册实体池 '{key}'。");
            return null;
        }

        /// <summary>回池（重置后归还复用）</summary>
        public void Release(Entity entity)
        {
            if (entity == null || entity.PoolKey == null)
            {
                Log.Warning("EntityManager", "实体未关联池 key，无法回池，改为 Destroy。");
                Destroy(entity);
                return;
            }
            entity.Reset();
            // 从实体表移除（回池期间不可被 Query 命中）
            _entities.Remove(entity.Id);
            // 从组件索引移除
            // Reset 已 RemoveAll → OnComponentRemoved 已移除
            if (_pools.TryGetValue(entity.PoolKey, out var stack))
                stack.Push(entity);
            else
                Destroy(entity);
        }
    }

    /// <summary>
    /// EntityManager 类型安全 Query 扩展。
    /// </summary>
    public static class EntityManagerQueryExtensions
    {
        /// <summary>查询拥有 T1 组件的所有实体</summary>
        public static IEnumerable<Entity> Query<T1>(this EntityManager mgr) where T1 : IComponent
        {
            var set = mgr.GetIndex(typeof(T1));
            if (set == null) yield break;
            foreach (var e in set) yield return e;
        }

        /// <summary>查询同时拥有 T1、T2 组件的实体</summary>
        public static IEnumerable<Entity> Query<T1, T2>(this EntityManager mgr)
            where T1 : IComponent where T2 : IComponent
        {
            var s1 = mgr.GetIndex(typeof(T1));
            var s2 = mgr.GetIndex(typeof(T2));
            if (s1 == null || s2 == null) yield break;
            var (smaller, larger) = s1.Count <= s2.Count ? (s1, s2) : (s2, s1);
            foreach (var e in smaller)
                if (larger.Contains(e)) yield return e;
        }

        /// <summary>查询同时拥有 T1、T2、T3 组件的实体</summary>
        public static IEnumerable<Entity> Query<T1, T2, T3>(this EntityManager mgr)
            where T1 : IComponent where T2 : IComponent where T3 : IComponent
        {
            var s1 = mgr.GetIndex(typeof(T1));
            var s2 = mgr.GetIndex(typeof(T2));
            var s3 = mgr.GetIndex(typeof(T3));
            if (s1 == null || s2 == null || s3 == null) yield break;
            // 取最小集合遍历
            var smallest = s1;
            if (s2.Count < smallest.Count) smallest = s2;
            if (s3.Count < smallest.Count) smallest = s3;
            foreach (var e in smallest)
            {
                if (s1.Contains(e) && s2.Contains(e) && s3.Contains(e))
                    yield return e;
            }
        }
    }
}
