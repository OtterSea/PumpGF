using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PumpGF.Editor
{
    /// <summary>代码模板类型（9 种）</summary>
    public enum TemplateType
    {
        MonoBehaviour,
        View,
        ViewModel,
        State,
        ConfigTableSO,
        Command,
        Event,
        Component,
        ConfigSO,
    }

    /// <summary>代码模板生成配置</summary>
    [Serializable]
    public class TemplateConfig
    {
        public string Namespace = "PumpGF";
        public string OutputPath;
        public string Description = "TODO: 描述";
        // View
        public string ViewModelName;
        public bool IsPage;
        public bool IsPopup;
        public bool IsHud;
        // State
        public bool WithConstructorInjection = true;
        public string DependencyType = "object";
        // ConfigTableSO
        public string RowName = "Row";
        public string KeyType = "int";
        public string ConfigKey = "ConfigKey";
        // ConfigSO
        public bool WithValidator;
        // MonoBehaviour
        public bool WithDisposableBag = true;
        public bool WithAwake = true;
        // Command/Event
        public string ParamType = "int";
        public string ParamName = "value";
    }

    /// <summary>
    /// 代码骨架生成器。覆盖 9 种常见类型，AI/菜单按模板生成骨架保证结构一致。
    /// </summary>
    public static class CodeTemplateGenerator
    {
        /// <summary>生成代码文件</summary>
        public static void Generate(TemplateType type, string className, TemplateConfig config = null)
        {
            config ??= new TemplateConfig();
            string content = GetTemplateContent(type, className, config);
            string path = string.IsNullOrEmpty(config.OutputPath) ? GetDefaultPath(type) : config.OutputPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            string fullPath = Path.Combine(path, className + ".cs");
            File.WriteAllText(fullPath, content);
            AssetDatabase.Refresh();
            Log.Info("PumpGFEditor", $"生成代码: {fullPath}");
        }

        /// <summary>获取模板内容（不写文件，用于预览/snippet）</summary>
        public static string GetTemplateContent(TemplateType type, string className, TemplateConfig config = null)
        {
            config ??= new TemplateConfig();
            return type switch
            {
                TemplateType.MonoBehaviour => MonoBehaviourTemplate(className, config),
                TemplateType.View => ViewTemplate(className, config),
                TemplateType.ViewModel => ViewModelTemplate(className, config),
                TemplateType.State => StateTemplate(className, config),
                TemplateType.ConfigTableSO => ConfigTableSOTemplate(className, config),
                TemplateType.Command => CommandTemplate(className, config),
                TemplateType.Event => EventTemplate(className, config),
                TemplateType.Component => ComponentTemplate(className, config),
                TemplateType.ConfigSO => ConfigSOTemplate(className, config),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }

        private static string GetDefaultPath(TemplateType type) => type switch
        {
            TemplateType.MonoBehaviour => "Assets/Scripts",
            TemplateType.View or TemplateType.ViewModel => "Assets/Scripts/UI",
            TemplateType.State => "Assets/Scripts/FSM",
            TemplateType.ConfigTableSO or TemplateType.ConfigSO => "Assets/Scripts/Config",
            TemplateType.Command => "Assets/Scripts/Data/Commands",
            TemplateType.Event => "Assets/Scripts/Events",
            TemplateType.Component => "Assets/Scripts/Entity",
            _ => "Assets/Scripts",
        };

        // ── 模板内容 ──

        private static string MonoBehaviourTemplate(string name, TemplateConfig c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using UnityEngine;"); sb.AppendLine("using R3;");
            sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {name} : MonoBehaviour"); sb.AppendLine("{");
            if (c.WithDisposableBag) sb.AppendLine("    private DisposableBag _bag;");
            if (c.WithAwake) { sb.AppendLine(); sb.AppendLine("    private void Awake()"); sb.AppendLine("    {"); sb.AppendLine("        // TODO: 初始化"); sb.AppendLine("    }"); }
            sb.AppendLine(); sb.AppendLine("    private void OnDestroy()"); sb.AppendLine("    {");
            sb.AppendLine(c.WithDisposableBag ? "        _bag.Dispose();" : "        // TODO: 清理");
            sb.AppendLine("    }"); sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ViewTemplate(string name, TemplateConfig c)
        {
            string vm = string.IsNullOrEmpty(c.ViewModelName) ? name + "ViewModel" : c.ViewModelName;
            string iface = c.IsPage ? ", IPage" : c.IsPopup ? ", IPopup" : c.IsHud ? ", IHud" : "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using UnityEngine;"); sb.AppendLine("using R3;");
            sb.AppendLine("using Cysharp.Threading.Tasks;"); sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {name} : View<{vm}>{iface}"); sb.AppendLine("{");
            sb.AppendLine($"    protected override void OnBind({vm} vm)"); sb.AppendLine("    {");
            sb.AppendLine("        // TODO: 绑定逻辑"); sb.AppendLine("        // BindText(vm.Hp, hpText);"); sb.AppendLine("    }"); sb.AppendLine();
            sb.AppendLine("    protected override UniTask PlayEnterAnimation(CancellationToken ct)"); sb.AppendLine("    {"); sb.AppendLine("        // TODO: 入场动画"); sb.AppendLine("        return UniTask.CompletedTask;"); sb.AppendLine("    }"); sb.AppendLine();
            sb.AppendLine("    protected override UniTask PlayExitAnimation(CancellationToken ct)"); sb.AppendLine("    {"); sb.AppendLine("        // TODO: 出场动画"); sb.AppendLine("        return UniTask.CompletedTask;"); sb.AppendLine("    }"); sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ViewModelTemplate(string name, TemplateConfig c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using R3;"); sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {name} : ViewModel"); sb.AppendLine("{");
            sb.AppendLine("    // TODO: ReactiveProperty 状态"); sb.AppendLine("    // public ReactiveProperty<int> Score { get; } = new(0);"); sb.AppendLine();
            sb.AppendLine($"    public {name}()"); sb.AppendLine("    {"); sb.AppendLine("        // TODO: 订阅 GameDataStore"); sb.AppendLine("    }"); sb.AppendLine("}");
            return sb.ToString();
        }

        private static string StateTemplate(string name, TemplateConfig c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {name} : State"); sb.AppendLine("{");
            if (c.WithConstructorInjection)
            {
                sb.AppendLine($"    private readonly {c.DependencyType} _dependency;"); sb.AppendLine();
                sb.AppendLine($"    public {name}({c.DependencyType} dependency)"); sb.AppendLine("    {"); sb.AppendLine("        _dependency = dependency;"); sb.AppendLine("    }");
            }
            sb.AppendLine(); sb.AppendLine("    public override void OnEnter()"); sb.AppendLine("    {"); sb.AppendLine("        // TODO: 进入状态"); sb.AppendLine("    }"); sb.AppendLine();
            sb.AppendLine("    public override void OnUpdate(float dt)"); sb.AppendLine("    {"); sb.AppendLine("        // TODO: 状态更新"); sb.AppendLine("    }"); sb.AppendLine();
            sb.AppendLine("    public override void OnExit()"); sb.AppendLine("    {"); sb.AppendLine("        // TODO: 退出状态"); sb.AppendLine("    }"); sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ConfigTableSOTemplate(string name, TemplateConfig c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using System;"); sb.AppendLine("using UnityEngine;"); sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine("[Serializable]"); sb.AppendLine($"public class {c.RowName}"); sb.AppendLine("{");
            sb.AppendLine("    // TODO: 行字段"); sb.AppendLine("    // public int Id;"); sb.AppendLine("}"); sb.AppendLine();
            sb.AppendLine($"[ConfigTable(\"{c.ConfigKey}\")]"); sb.AppendLine($"public class {name} : ConfigTableSO<{c.RowName}, {c.KeyType}>"); sb.AppendLine("{");
            sb.AppendLine($"    protected override {c.KeyType} GetKey({c.RowName} row)"); sb.AppendLine("    {"); sb.AppendLine("        // TODO: 返回行的 key"); sb.AppendLine("        return default;"); sb.AppendLine("    }"); sb.AppendLine("}");
            return sb.ToString();
        }

        private static string CommandTemplate(string name, TemplateConfig c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public readonly struct {name} : IDataCommand"); sb.AppendLine("{");
            sb.AppendLine($"    public readonly {c.ParamType} {c.ParamName};"); sb.AppendLine();
            sb.AppendLine($"    public {name}({c.ParamType} {c.ParamName.ToLowerInvariant()})"); sb.AppendLine("    {"); sb.AppendLine($"        {c.ParamName} = {c.ParamName.ToLowerInvariant()};"); sb.AppendLine("    }"); sb.AppendLine("}");
            return sb.ToString();
        }

        private static string EventTemplate(string name, TemplateConfig c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public readonly struct {name}"); sb.AppendLine("{");
            sb.AppendLine($"    public readonly {c.ParamType} {c.ParamName};"); sb.AppendLine();
            sb.AppendLine($"    public {name}({c.ParamType} {c.ParamName.ToLowerInvariant()})"); sb.AppendLine("    {"); sb.AppendLine($"        {c.ParamName} = {c.ParamName.ToLowerInvariant()};"); sb.AppendLine("    }"); sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ComponentTemplate(string name, TemplateConfig c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using R3;"); sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {name} : IComponent"); sb.AppendLine("{");
            sb.AppendLine("    // TODO: 普通字段（配置值）"); sb.AppendLine("    // TODO: ReactiveProperty（关心变更的属性）"); sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ConfigSOTemplate(string name, TemplateConfig c)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using System.Collections.Generic;"); sb.AppendLine("using UnityEngine;"); sb.AppendLine("using PumpGF;"); sb.AppendLine();
            sb.AppendLine($"/// <summary>"); sb.AppendLine($"/// {c.Description}"); sb.AppendLine($"/// </summary>");
            sb.AppendLine($"[Config(\"{c.ConfigKey}\")]"); sb.AppendLine($"public class {name} : ScriptableObject{(c.WithValidator ? ", IConfigValidator" : "")}"); sb.AppendLine("{");
            sb.AppendLine("    // TODO: 配置字段"); sb.AppendLine("    // [Range(0, 9999)] public int MaxHp = 100;");
            if (c.WithValidator)
            {
                sb.AppendLine(); sb.AppendLine("    public IReadOnlyList<string> Validate()"); sb.AppendLine("    {"); sb.AppendLine("        var errors = new List<string>();"); sb.AppendLine("        // TODO: 校验逻辑"); sb.AppendLine("        return errors;"); sb.AppendLine("    }");
            }
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
