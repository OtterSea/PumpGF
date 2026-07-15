using System;
using System.Text;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 序列化 Provider 抽象。JSON 默认，Binary 可切换。
    /// </summary>
    public interface ISaveProvider
    {
        byte[] Serialize<T>(T data);
        T Deserialize<T>(byte[] bytes);
    }

    /// <summary>
    /// JSON 序列化 Provider（默认，基于 JsonUtility）。
    /// 注意：JsonUtility 不支持 Dictionary，复杂结构需用 [Serializable] 类 + List。
    /// </summary>
    public sealed class JsonSaveProvider : ISaveProvider
    {
        public byte[] Serialize<T>(T data)
        {
            var json = JsonUtility.ToJson(data, prettyPrint: true);
            return Encoding.UTF8.GetBytes(json);
        }

        public T Deserialize<T>(byte[] bytes)
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonUtility.FromJson<T>(json);
        }
    }

    /// <summary>
    /// 加密器抽象。默认 NoopEncryptor（不加密），未来可加 AES。
    /// </summary>
    public interface IEncryptor
    {
        byte[] Encrypt(byte[] data);
        byte[] Decrypt(byte[] data);
    }

    /// <summary>
    /// 不加密的默认实现。
    /// </summary>
    public sealed class NoopEncryptor : IEncryptor
    {
        public byte[] Encrypt(byte[] data) => data;
        public byte[] Decrypt(byte[] data) => data;
    }

    /// <summary>
    /// 存档 Schema 版本号。存档结构变化时 +1，并注册迁移函数。
    /// </summary>
    public static class SaveSchema
    {
        /// <summary>当前存档版本号</summary>
        public const int CurrentVersion = 1;
    }

    /// <summary>
    /// 存档槽位元信息。单独存储为 meta.json，加载槽位列表时只读 meta。
    /// </summary>
    [Serializable]
    public class SaveSlotMeta
    {
        /// <summary>角色名</summary>
        public string CharacterName;
        /// <summary>保存时间（Ticks）</summary>
        public long SaveTimeTicks;
        /// <summary>进度（如关卡数）</summary>
        public int Progress;
        /// <summary>游玩时长（秒）</summary>
        public float PlayTime;

        /// <summary>保存时间（DateTime）</summary>
        public DateTime SaveTime => new(SaveTimeTicks);
    }
}
