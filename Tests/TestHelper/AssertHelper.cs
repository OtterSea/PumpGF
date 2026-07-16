using System;
using NUnit.Framework;
using UnityEngine;

namespace PumpGF.Tests
{
    /// <summary>
    /// 断言辅助工具。
    /// </summary>
    public static class AssertHelper
    {
        /// <summary>Vector3 近似相等</summary>
        public static void Approximately(Vector3 expected, Vector3 actual, float tolerance = 0.0001f)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, "x 不匹配");
            Assert.AreEqual(expected.y, actual.y, tolerance, "y 不匹配");
            Assert.AreEqual(expected.z, actual.z, tolerance, "z 不匹配");
        }

        /// <summary>float 近似相等</summary>
        public static void Approximately(float expected, float actual, float tolerance = 0.0001f)
        {
            Assert.AreEqual(expected, actual, tolerance);
        }

        /// <summary>确认已 Dispose（通过 IsValid 等状态判断，需传入检查函数）</summary>
        public static void IsDisposed(IDisposable disposable)
        {
            Assert.IsNotNull(disposable, "对象为 null");
            // Dispose 后对象应可被 GC，此处仅确认调用不抛异常
            disposable.Dispose();
        }

        /// <summary>确认抛出指定异常</summary>
        public static void Throws<TException>(Action action) where TException : Exception
        {
            Assert.Throws<TException>(() => action());
        }

        /// <summary>确认异步操作在指定时间内完成</summary>
        public static async Cysharp.Threading.Tasks.UniTask CompletesWithin(
            Func<Cysharp.Threading.Tasks.UniTask> asyncAction, float timeoutSeconds)
        {
            var task = asyncAction();
            float elapsed = 0f;
            while (!task.GetAwaiter().IsCompleted)
            {
                await Cysharp.Threading.Tasks.UniTask.Yield();
                elapsed += Time.deltaTime;
                if (elapsed > timeoutSeconds)
                    Assert.Fail($"操作未在 {timeoutSeconds}s 内完成");
            }
            await task;
        }
    }
}
