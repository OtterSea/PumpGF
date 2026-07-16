using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 隐藏的 MonoBehaviour 驱动节点。挂在 DontDestroyOnLoad 根下。
    /// 只做一件事：在 Update/FixedUpdate/LateUpdate 里回调 LifecycleMgr。
    /// 同时转发 OnApplicationFocus/Pause/Quit。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class LifecycleDriver : MonoBehaviour
    {
        private LifecycleMgr _mgr;
        private static GameObject _instance;

        /// <summary>
        /// 创建或获取驱动节点。由 LifecycleMgr.Init 调用。
        /// </summary>
        internal static LifecycleDriver Create(LifecycleMgr mgr)
        {
            if (_instance != null)
            {
                var existing = _instance.GetComponent<LifecycleDriver>();
                if (existing != null)
                {
                    existing._mgr = mgr;
                    return existing;
                }
            }

            _instance = new GameObject("[PumpGF] LifecycleDriver");
            _instance.hideFlags = HideFlags.NotEditable;
            DontDestroyOnLoad(_instance);
            var driver = _instance.AddComponent<LifecycleDriver>();
            driver._mgr = mgr;
            return driver;
        }

        private void Update()
        {
            _mgr?.OnUpdate(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            _mgr?.OnFixedUpdate(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            _mgr?.OnLateUpdate();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _mgr?.NotifyApplicationFocus(hasFocus);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            _mgr?.NotifyApplicationPause(pauseStatus);
        }

        private void OnApplicationQuit()
        {
            _mgr?.NotifyApplicationQuit();
        }

        private void OnDestroy()
        {
            _mgr = null;
            _instance = null;
        }
    }
}
