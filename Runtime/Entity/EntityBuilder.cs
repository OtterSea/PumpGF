using System;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 实体构建器。链式 API 创建 Entity。
    /// </summary>
    public class EntityBuilder
    {
        private readonly EntityManager _mgr;
        private readonly Entity _entity;

        private EntityBuilder(EntityManager mgr)
        {
            _mgr = mgr;
            _entity = mgr.NewEntity();
        }

        /// <summary>创建构建器（默认使用 GameGlobal.EntityManager）</summary>
        public static EntityBuilder Create() => new EntityBuilder(GameGlobal.EntityManager);

        /// <summary>创建构建器（指定 EntityManager）</summary>
        public static EntityBuilder Create(EntityManager mgr) => new EntityBuilder(mgr);

        /// <summary>添加并配置组件</summary>
        public EntityBuilder With<T>(Action<T> configure = null) where T : IComponent, new()
        {
            _entity.Add(configure);
            return this;
        }

        /// <summary>添加组件实例</summary>
        public EntityBuilder With<T>(T component) where T : IComponent
        {
            _entity.Add(component);
            return this;
        }

        /// <summary>关联 GameObject（View）</summary>
        public EntityBuilder WithGameObject(GameObject go)
        {
            _entity.GameObject = go;
            return this;
        }

        /// <summary>关联状态机</summary>
        public EntityBuilder WithStateMachine(StateMachine sm)
        {
            _entity.StateMachine = sm;
            return this;
        }

        /// <summary>构建并注册到 EntityManager</summary>
        public Entity Build()
        {
            _mgr.Register(_entity);
            return _entity;
        }
    }
}
