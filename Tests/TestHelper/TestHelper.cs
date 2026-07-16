using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace PumpGF.Tests
{
    /// <summary>
    /// 测试辅助工具。创建临时 GameObject（自动清理）、异步等待、GameGlobal 初始化。
    /// </summary>
    public static class TestHelper
    {
        private static readonly List<GameObject> _tempObjects = new(8);

        /// <summary>创建临时 GameObject（测试结束 CleanupGameGlobal 时自动销毁）</summary>
        public static GameObject CreateGameObject(string name = "TestObject")
        {
            var go = new GameObject(name);
            _tempObjects.Add(go);
            return go;
        }

        /// <summary>创建临时 GameObject 并挂载组件</summary>
        public static T CreateComponent<T>(string name = "TestObject") where T : Component
        {
            return CreateGameObject(name).AddComponent<T>();
        }

        /// <summary>等待指定帧数</summary>
        public static UniTask WaitFrames(int frames) => UniTask.DelayFrame(frames);

        /// <summary>等待条件满足（超时抛 TimeoutException）</summary>
        public static async UniTask WaitForCondition(Func<bool> condition, float timeoutSeconds = 5f)
        {
            float elapsed = 0f;
            while (!condition())
            {
                await UniTask.Yield();
                elapsed += Time.deltaTime;
                if (elapsed > timeoutSeconds)
                    throw new TimeoutException($"WaitForCondition 超时（{timeoutSeconds}s）");
            }
        }

        /// <summary>等待指定秒数</summary>
        public static UniTask WaitForSeconds(float seconds) => UniTask.Delay(TimeSpan.FromSeconds(seconds));

        /// <summary>初始化 GameGlobal（Play Mode 用）。GameGlobal 由 RuntimeInitializeOnLoadMethod 自动初始化，此处仅等待一帧确保就绪。</summary>
        public static async UniTask InitializeGameGlobalAsync()
        {
            await UniTask.Yield();
        }

        /// <summary>清理临时 GameObject</summary>
        public static void CleanupGameGlobal()
        {
            foreach (var go in _tempObjects)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            _tempObjects.Clear();
        }

        /// <summary>在 Play Mode 中运行异步测试</summary>
        public static IEnumerator RunAsync(Func<UniTask> testAction) => testAction().ToCoroutine();
    }
}
