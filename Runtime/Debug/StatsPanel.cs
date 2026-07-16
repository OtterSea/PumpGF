#if DEBUG
using UnityEngine;
using UnityEngine.Profiling;

namespace PumpGF
{
    /// <summary>
    /// 预置性能面板。显示 FPS/帧时间/GC 内存/Unity 内存/实体数/分辨率。
    /// </summary>
    internal sealed class StatsPanel : IDebugPanel
    {
        public string Title => "Stats";

        public void OnGUI(Rect rect)
        {
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("<b>Stats</b>", new GUILayoutOption[0]);
            GUILayout.Label($"FPS: {1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime):F1}");
            GUILayout.Label($"Frame: {Time.unscaledDeltaTime * 1000f:F2} ms");
            GUILayout.Label($"GC Mem: {System.GC.GetTotalMemory(false) / 1024 / 1024} MB");
            GUILayout.Label($"Unity Mem: {Profiler.GetTotalAllocatedMemoryLong() / 1024 / 1024} MB");
            if (GameGlobal.EntityManager != null)
                GUILayout.Label($"Entities: {GameGlobal.EntityManager.Count}");
            GUILayout.Label($"Screen: {Screen.width}x{Screen.height}");
            GUILayout.EndArea();
        }
    }
}
#endif
