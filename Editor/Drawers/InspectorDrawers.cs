using UnityEditor;
using UnityEngine;
using PumpGF;

namespace PumpGF.Editor
{
    /// <summary>
    /// 通用特性 PropertyDrawer：[Required]/[UniqueId]/[ConfigKey]/[NonNegative] 视觉提示。
    /// </summary>
    [CustomPropertyDrawer(typeof(RequiredAttribute))]
    public class RequiredDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            bool invalid = (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null)
                        || (property.propertyType == SerializedPropertyType.String && string.IsNullOrEmpty(property.stringValue));
            Color old = GUI.color;
            if (invalid) GUI.color = new Color(1f, 0.4f, 0.4f);
            EditorGUI.PropertyField(position, property, label, true);
            GUI.color = old;
        }
    }

    [CustomPropertyDrawer(typeof(NonNegativeAttribute))]
    public class NonNegativeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            bool negative = (property.propertyType == SerializedPropertyType.Float && property.floatValue < 0f)
                         || (property.propertyType == SerializedPropertyType.Integer && property.intValue < 0);
            Color old = GUI.color;
            if (negative) GUI.color = new Color(1f, 0.4f, 0.4f);
            EditorGUI.PropertyField(position, property, label, true);
            GUI.color = old;
        }
    }

    [CustomPropertyDrawer(typeof(UniqueIdAttribute))]
    public class UniqueIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // 基础绘制（重复检测需访问兄弟字段，留 TODO）
            EditorGUI.PropertyField(position, property, label, true);
        }
    }

    [CustomPropertyDrawer(typeof(ConfigKeyAttribute))]
    public class ConfigKeyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // 基础绘制（Addressables 一致性校验留 TODO）
            EditorGUI.PropertyField(position, property, label, true);
        }
    }
}
