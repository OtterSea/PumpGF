#if DEBUG
using UnityEngine;
using UnityEngine.InputSystem;

namespace PumpGF
{
    /// <summary>
    /// DebugConsole 驱动 MonoBehaviour。负责 OnGUI 绘制控制台/面板 + OnDrawGizmos 统一调用。
    /// 仅 #if DEBUG 生效。
    /// </summary>
    internal sealed class DebugConsoleDriver : MonoBehaviour
    {
        private DebugConsole _console;
        private string _input = "";
        private Vector2 _logScroll;
        private bool _showStats = true;
        private int _historyIndex = -1;

        internal void Init(DebugConsole console)
        {
            _console = console;
            _showStats = console.Config.ShowStatsByDefault;
        }

        private void Update()
        {
            // 编辑器快捷键（反引号）
            if (Keyboard.current != null && Keyboard.current[Key.Backquote].wasPressedThisFrame)
            {
                _console.Toggle();
            }

            // 切换 Stats 面板（F1）
            if (Keyboard.current != null && Keyboard.current[Key.F1].wasPressedThisFrame)
            {
                _showStats = !_showStats;
            }
        }

        private void OnGUI()
        {
            if (_showStats)
            {
                var statsRect = new Rect(Screen.width - 230, 10, 220, 140);
                _console.DrawPanels(statsRect);
            }

            if (_console.IsVisible)
                DrawConsole();
        }

        private void DrawConsole()
        {
            float h = Screen.height * 0.4f;
            var rect = new Rect(0, Screen.height - h, Screen.width, h);
            GUI.Box(rect, "PumpGF Debug Console  (F1 切换 Stats)");

            GUILayout.BeginArea(new Rect(10, rect.y + 22, rect.width - 20, rect.height - 32));

            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.Height(rect.height - 60));
            var log = _console.GetConsoleLog();
            for (int i = 0; i < log.Count; i++)
                GUILayout.Label(log[i]);
            GUILayout.EndScrollView();

            // 命令历史浏览（上/下）
            if (Event.current.isKey && Event.current.type == EventType.KeyDown
                && GUI.GetNameOfFocusedControl() == "CmdInput")
            {
                if (Event.current.keyCode == KeyCode.UpArrow)
                {
                    _input = _console.GetPreviousHistory(ref _historyIndex);
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.DownArrow)
                {
                    _input = _console.GetNextHistory(ref _historyIndex);
                    Event.current.Use();
                }
            }

            GUI.SetNextControlName("CmdInput");
            _input = GUILayout.TextField(_input);
            GUI.FocusControl("CmdInput");

            if (Event.current.isKey && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == "CmdInput")
            {
                if (!string.IsNullOrWhiteSpace(_input))
                {
                    _console.Execute(_input);
                    _input = "";
                    _historyIndex = -1;
                }
                Event.current.Use();
            }

            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (_console != null && _console.GizmosEnabled)
                _console.DrawGizmos();
        }
    }
}
#endif
