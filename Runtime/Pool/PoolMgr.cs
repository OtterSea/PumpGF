using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace PumpGF
{
    /// <summary>
    /// 对象池管理模块。通过 key 管理多个 ObjectPool&lt;GameObject&gt;。
    /// 支持按 key 注册池、取用实例、归还实例，无需调用方记录 key。
    /// </summary>
    public sealed class PoolMgr : IModule
    {
        Transform _root;
        Dictionary<string, ObjectPool<GameObject>> _pools;

        public void Init()
        {
            var go = new GameObject("[PumpGF] PoolMgr");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;

            _pools = new Dictionary<string, ObjectPool<GameObject>>(32);
        }

        /// <summary>
        /// 注册一个对象池。
        /// </summary>
        /// <param name="key">池的唯一标识（通常与资源 key 一致）</param>
        /// <param name="prefab">用于实例化的预制体</param>
        /// <param name="capacity">初始容量</param>
        /// <param name="maxSize">池上限，超出则销毁</param>
        public void Register(string key, GameObject prefab, int capacity = 16, int maxSize = 100)
        {
            if (_pools.ContainsKey(key))
            {
                Log.Warning("PoolMgr", $"Pool '{key}' already registered, skipping.");
                return;
            }

            var pool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var go = Object.Instantiate(prefab, _root);
                    var tracker = go.GetComponent<PooledObjectTracker>();
                    if (tracker == null)
                        tracker = go.AddComponent<PooledObjectTracker>();
                    tracker.poolKey = key;
                    return go;
                },
                actionOnGet: obj => obj.SetActive(true),
                actionOnRelease: obj =>
                {
                    obj.SetActive(false);
                    obj.transform.SetParent(_root);
                },
                actionOnDestroy: obj => Object.Destroy(obj),
                collectionCheck: true,
                defaultCapacity: capacity,
                maxSize: maxSize
            );

            _pools[key] = pool;
        }

        /// <summary>
        /// 是否已注册指定 key 的池。
        /// </summary>
        public bool HasPool(string key)
        {
            return _pools != null && _pools.ContainsKey(key);
        }

        /// <summary>
        /// 从池中取用实例。
        /// </summary>
        public GameObject Get(string key, Vector3 position, Quaternion rotation)
        {
            var pool = GetPool(key);
            var obj = pool.Get();
            obj.transform.SetPositionAndRotation(position, rotation);
            return obj;
        }

        /// <summary>
        /// 从池中取用实例（零参数，保持默认位置）。
        /// </summary>
        public GameObject Get(string key)
        {
            return GetPool(key).Get();
        }

        /// <summary>
        /// 归还实例到对应的池。无需手动指定 key，自动通过 Tracker 查找。
        /// </summary>
        public void Release(GameObject obj)
        {
            if (obj == null)
            {
                Log.Warning("PoolMgr", "Release called with null.");
                return;
            }

            var tracker = obj.GetComponent<PooledObjectTracker>();
            if (tracker == null)
            {
                Log.Warning("PoolMgr", $"Object '{obj.name}' has no PooledObjectTracker, destroying.");
                Object.Destroy(obj);
                return;
            }

            if (_pools.TryGetValue(tracker.poolKey, out var pool))
            {
                pool.Release(obj);
            }
            else
            {
                Log.Warning("PoolMgr", $"Pool '{tracker.poolKey}' not found, destroying object.");
                Object.Destroy(obj);
            }
        }

        /// <summary>
        /// 归还实例到指定的池（显式指定 key）。
        /// </summary>
        public void Release(string key, GameObject obj)
        {
            if (obj == null) return;
            GetPool(key).Release(obj);
        }

        /// <summary>
        /// 清空指定 key 的池（销毁所有闲置对象）。
        /// </summary>
        public void Clear(string key)
        {
            if (_pools.TryGetValue(key, out var pool))
            {
                pool.Clear();
            }
        }

        /// <summary>
        /// 清空所有池。
        /// </summary>
        public void ClearAll()
        {
            foreach (var pool in _pools.Values)
            {
                pool.Clear();
            }
        }

        /// <summary>
        /// 获取指定池中闲置对象数量。
        /// </summary>
        public int CountInactive(string key)
        {
            if (_pools.TryGetValue(key, out var pool))
            {
                return pool.CountInactive;
            }
            return 0;
        }

        public void Dispose()
        {
            if (_pools != null)
            {
                foreach (var pool in _pools.Values)
                {
                    pool.Dispose();
                }
                _pools.Clear();
                _pools = null;
            }

            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
                _root = null;
            }
        }

        ObjectPool<GameObject> GetPool(string key)
        {
            if (_pools == null)
                throw new InvalidOperationException("[PoolMgr] Not initialized. Call Init() first.");
            if (!_pools.TryGetValue(key, out var pool))
                throw new InvalidOperationException($"[PoolMgr] Pool '{key}' not registered. Call Register() first.");
            return pool;
        }
    }
}
