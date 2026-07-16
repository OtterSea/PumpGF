using UnityEditor;
using UnityEngine;
using PumpGF;

namespace PumpGF.Editor
{
    /// <summary>
    /// PumpGF 编辑器菜单入口（PumpGF/ 树）。
    /// </summary>
    public static class PumpGFMenus
    {
        // ── Generate ──
        [MenuItem("PumpGF/Generate/GameConfigs", priority = 10)]
        static void GenerateGameConfigs() => PumpGFEditor.GenerateGameConfigs();

        [MenuItem("PumpGF/Generate/UI Template", priority = 11)]
        static void GenerateUITemplate() => PumpGFEditor.GenerateUITemplate("NewPage");

        [MenuItem("PumpGF/Generate/Code Template/MonoBehaviour", priority = 20)]
        static void GenMono() => CodeTemplateGenerator.Generate(TemplateType.MonoBehaviour, "NewMonoBehaviour");

        [MenuItem("PumpGF/Generate/Code Template/View", priority = 21)]
        static void GenView() => CodeTemplateGenerator.Generate(TemplateType.View, "NewView", new TemplateConfig { ViewModelName = "NewViewModel", IsPage = true });

        [MenuItem("PumpGF/Generate/Code Template/ViewModel", priority = 22)]
        static void GenVM() => CodeTemplateGenerator.Generate(TemplateType.ViewModel, "NewViewModel");

        [MenuItem("PumpGF/Generate/Code Template/State", priority = 23)]
        static void GenState() => CodeTemplateGenerator.Generate(TemplateType.State, "NewState");

        [MenuItem("PumpGF/Generate/Code Template/Component", priority = 24)]
        static void GenComponent() => CodeTemplateGenerator.Generate(TemplateType.Component, "NewComponent");

        // ── Validate ──
        [MenuItem("PumpGF/Validate/All Configs", priority = 30)]
        static void ValidateAll() => ValidationReportWindow.Show(PumpGFEditor.ValidateAll());

        [MenuItem("PumpGF/Validate/Localization", priority = 31)]
        static void ValidateLoc() => ValidationReportWindow.Show(PumpGFEditor.ValidateLocalization());

        [MenuItem("PumpGF/Validate/Addressables", priority = 32)]
        static void ValidateAddr() => ValidationReportWindow.Show(PumpGFEditor.ValidateAddressables());

        // ── Create ──
        [MenuItem("PumpGF/Create/Audio Config", priority = 40)]
        static void CreateAudioConfig() => PumpGFEditor.CreateSO<AudioConfigSO>("Assets/NewAudioConfig.asset", "AudioConfig/New");

        [MenuItem("PumpGF/Create/Locale", priority = 41)]
        static void CreateLocale() => PumpGFEditor.CreateSO<LocaleSO>("Assets/NewLocale.asset", "Locale/New");
    }

    /// <summary>
    /// 校验报告窗口。
    /// </summary>
    public class ValidationReportWindow : EditorWindow
    {
        private ValidationReport _report;
        private Vector2 _scroll;

        public static void Show(ValidationReport report)
        {
            var w = GetWindow<ValidationReportWindow>(true, "PumpGF 校验报告", true);
            w._report = report;
            w.minSize = new Vector2(480, 320);
            w.Show();
        }

        private void OnGUI()
        {
            if (_report == null) { GUILayout.Label("无报告"); return; }

            EditorGUILayout.LabelField("校验报告", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"错误: {(_report.HasErrors ? "有" : "无")}    警告: {(_report.HasWarnings ? "有" : "无")}");

            if (GUILayout.Button("刷新"))
            {
                _report = PumpGFEditor.ValidateAll();
            }

            EditorGUILayout.Separator();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var issues = _report.Issues;
            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                Color old = GUI.color;
                GUI.color = issue.Type == IssueType.Error ? new Color(1f, 0.5f, 0.5f) : new Color(1f, 0.9f, 0.5f);
                EditorGUILayout.BeginHorizontal(GUI.skin.box);
                EditorGUILayout.LabelField($"[{issue.Module}] {issue.Type}: {issue.Message}", EditorStyles.wordWrappedLabel);
                if (issue.Target != null && GUILayout.Button("定位", GUILayout.Width(40)))
                    EditorGUIUtility.PingObject(issue.Target);
                EditorGUILayout.EndHorizontal();
                GUI.color = old;
            }
            EditorGUILayout.EndScrollView();

            if (_report.HasErrors && GUILayout.Button("尝试自动修复"))
            {
                int n = _report.FixAll();
                Debug.Log($"[PumpGFEditor] 自动修复标记 {n} 项");
            }
        }
    }
}
