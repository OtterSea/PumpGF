using System;
using System.Collections.Generic;
using UnityEngine;

namespace PumpGF.Editor
{
    /// <summary>校验问题类型</summary>
    public enum IssueType
    {
        Error,
        Warning,
    }

    /// <summary>单个校验问题</summary>
    [Serializable]
    public struct ValidationIssue
    {
        /// <summary>模块名</summary>
        public string Module;
        /// <summary>问题类型</summary>
        public IssueType Type;
        /// <summary>描述</summary>
        public string Message;
        /// <summary>相关资产（可点击跳转）</summary>
        public UnityEngine.Object Target;
        /// <summary>是否可自动修复</summary>
        public bool AutoFixable;
    }

    /// <summary>验证器接口。各模块实现并注册到 PumpGFEditor。</summary>
    public interface IValidator
    {
        /// <summary>验证器名</summary>
        string Name { get; }
        /// <summary>执行校验，返回报告</summary>
        ValidationReport Validate();
    }

    /// <summary>
    /// 校验报告。汇总多个验证器的结果。
    /// </summary>
    [Serializable]
    public class ValidationReport
    {
        private readonly List<ValidationIssue> _issues = new();

        /// <summary>是否含错误</summary>
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < _issues.Count; i++)
                    if (_issues[i].Type == IssueType.Error) return true;
                return false;
            }
        }

        /// <summary>是否含警告</summary>
        public bool HasWarnings
        {
            get
            {
                for (int i = 0; i < _issues.Count; i++)
                    if (_issues[i].Type == IssueType.Warning) return true;
                return false;
            }
        }

        /// <summary>所有问题</summary>
        public IReadOnlyList<ValidationIssue> Issues => _issues;

        /// <summary>添加错误</summary>
        public void AddError(string module, string message, UnityEngine.Object target = null, bool autoFixable = false)
        {
            _issues.Add(new ValidationIssue
            {
                Module = module,
                Type = IssueType.Error,
                Message = message,
                Target = target,
                AutoFixable = autoFixable,
            });
        }

        /// <summary>添加警告</summary>
        public void AddWarning(string module, string message, UnityEngine.Object target = null)
        {
            _issues.Add(new ValidationIssue
            {
                Module = module,
                Type = IssueType.Warning,
                Message = message,
                Target = target,
                AutoFixable = false,
            });
        }

        /// <summary>合并其他报告</summary>
        public void Merge(ValidationReport other)
        {
            if (other == null) return;
            _issues.AddRange(other._issues);
        }

        /// <summary>自动修复可修复问题（标记式，具体修复由各验证器实现）</summary>
        public int FixAll()
        {
            int fixedCount = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].AutoFixable) fixedCount++;
            }
            return fixedCount;
        }
    }
}
