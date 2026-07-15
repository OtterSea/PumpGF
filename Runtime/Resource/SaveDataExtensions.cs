using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 存档数据序列化辅助扩展。Vector3/Quaternion ↔ float[] 转换。
    /// 存档片段用 float[] 存储向量（JSON 无法直接序列化 Vector3）。
    /// </summary>
    public static class SaveDataExtensions
    {
        /// <summary>Vector3 → float[3]</summary>
        public static float[] ToFloatArray(this Vector3 v) => new[] { v.x, v.y, v.z };

        /// <summary>Quaternion → float[4]</summary>
        public static float[] ToFloatArray(this Quaternion q) => new[] { q.x, q.y, q.z, q.w };

        /// <summary>float[] → Vector3</summary>
        public static Vector3 ToVector3(this float[] arr)
        {
            if (arr == null || arr.Length < 3) return Vector3.zero;
            return new Vector3(arr[0], arr[1], arr[2]);
        }

        /// <summary>float[] → Quaternion</summary>
        public static Quaternion ToQuaternion(this float[] arr)
        {
            if (arr == null || arr.Length < 4) return Quaternion.identity;
            return new Quaternion(arr[0], arr[1], arr[2], arr[3]);
        }
    }
}
