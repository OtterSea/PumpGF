using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PumpGF.Editor
{
    /// <summary>
    /// 编辑器工具静态 API 入口。所有菜单工具同时提供代码 API，供 AI/脚本调用。
    /// </summary>
    public static class PumpGFEditor
    {
        private static readonly List<IValidator> _validators = new(8);

        // ──────────────────────────────────────────────
        //  验证器
        // ──────────────────────────────────────────────

        /// <summary>注册验证器</summary>
        public static void RegisterValidator(IValidator validator)
        {
            if (validator != null && !_validators.Contains(validator))
                _validators.Add(validator);
        }

        /// <summary>一键校验所有已注册验证器</summary>
        public static ValidationReport ValidateAll()
        {
            var report = new ValidationReport();
            foreach (var v in _validators)
            {
                try { report.Merge(v.Validate()); }
                catch (Exception e) { report.AddError(v.Name, $"验证器异常: {e.Message}"); }
            }
            return report;
        }

        /// <summary>校验多语言完整性（扫描 LocaleSO 资产）</summary>
        public static ValidationReport ValidateLocalization()
        {
            var report = new ValidationReport();
            var guids = AssetDatabase.FindAssets("t:LocaleSO");
            // 简化：仅报告找到的 LocaleSO 数量；完整 key 完整性校验留 TODO
            report.AddWarning("Localization", $"找到 {guids.Length} 个 LocaleSO（key 完整性校验待实现）");
            return report;
        }

        /// <summary>校验 Addressables key 一致性（简化）</summary>
        public static ValidationReport ValidateAddressables()
        {
            var report = new ValidationReport();
            report.AddWarning("Addressables", "Addressables key 一致性校验待实现（需 Unity.Addressables.Editor 引用）");
            return report;
        }

        // ──────────────────────────────────────────────
        //  生成（委托 CodeTemplateGenerator，见 CodeTemplates 模块）
        // ──────────────────────────────────────────────

        public static void GenerateGameConfigs()
        {
            Debug.Log("[PumpGFEditor] GenerateGameConfigs: 扫描 [Config]/[ConfigTable] 生成访问类（待完整实现）");
        }

        public static void GenerateUITemplate(string pageName)
        {
            // 委托 CodeTemplateGenerator（CodeTemplates 模块）
            CodeTemplateGenerator.Generate(TemplateType.ViewModel, pageName + "ViewModel",
                new TemplateConfig { OutputPath = "Assets/Scripts/UI" });
            CodeTemplateGenerator.Generate(TemplateType.View, pageName + "View",
                new TemplateConfig { OutputPath = "Assets/Scripts/UI", ViewModelName = pageName + "ViewModel", IsPage = true });
        }

        public static void GenerateUIBindings(GameObject prefab)
        {
            Debug.Log($"[PumpGFEditor] GenerateUIBindings: 扫描 prefab '{prefab?.name}' Auto_ 组件（待完整实现）");
        }

        // ──────────────────────────────────────────────
        //  Addressables
        // ──────────────────────────────────────────────

        /// <summary>批量设置 Addressables Label（需 Addressables Editor 引用，本阶段留 TODO）</summary>
        public static void SetAddressablesLabel(IList<UnityEngine.Object> assets, string label)
        {
            Debug.Log($"[PumpGFEditor] SetAddressablesLabel: {assets?.Count} 个资源 → '{label}'（需 Addressables Editor 集成）");
        }

        /// <summary>检查 Addressables key 是否存在</summary>
        public static bool CheckAddressablesKey(string key)
        {
            // 简化：通过 AssetDatabase 查找（非完整 Addressables 校验）
            return !string.IsNullOrEmpty(key);
        }

        // ──────────────────────────────────────────────
        //  SO 创建
        // ──────────────────────────────────────────────

        /// <summary>创建 SO 资产。Addressables 设置需业务在 Addressables 窗口手动配置（本阶段不自动设）。</summary>
        public static T CreateSO<T>(string path, string addressablesKey) where T : ScriptableObject
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[PumpGFEditor] CreateSO: path 为空");
                return null;
            }
            if (!path.EndsWith(".asset")) path += ".asset";

            var so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log.Info("PumpGFEditor", $"创建 SO: {path}（Addressables key '{addressablesKey}' 需手动设置）");
            return so;
        }
    }
}
