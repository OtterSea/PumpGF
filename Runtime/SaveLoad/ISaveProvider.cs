using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    /// 存储后端抽象。默认基于 <see cref="File"/> 的实现覆盖 PC/Mobile/Editor；
    /// 未来 WebGL / 主机平台可实现 PlayerPrefs / 平台专属 API 后端。
    /// </summary>
    public interface IStorageBackend
    {
        /// <summary>文件是否存在</summary>
        bool Exists(string path);
        /// <summary>删除文件（不存在时静默）</summary>
        void Delete(string path);
        /// <summary>确保所在目录存在</summary>
        void EnsureDirectory(string dir);
        /// <summary>移动 srcPath → dstPath（srcPath 必须存在，dstPath 必须不存在，调用方保证）</summary>
        void Move(string srcPath, string dstPath);
        /// <summary>异步读取全部字节；文件不存在返回 null（不抛异常）</summary>
        UniTask<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default);
        /// <summary>异步写入字节（覆盖）</summary>
        UniTask WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default);
        /// <summary>同步读取文本（用于 meta.json 小文件）</summary>
        string ReadAllText(string path);
        /// <summary>同步写入文本（用于 meta.json 小文件）</summary>
        void WriteAllText(string path, string content);
        /// <summary>删除目录（递归）</summary>
        void DeleteDirectory(string dir);
    }

    /// <summary>
    /// 默认文件系统存储后端。基于 <see cref="System.IO.File"/> API + 线程池异步。
    /// 单机 PC / Mobile 平台适用；WebGL 请自行实现 PlayerPrefs 版本。
    /// </summary>
    public sealed class FileStorageBackend : IStorageBackend
    {
        public bool Exists(string path) => File.Exists(path);

        public void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Log.Warning("StorageBackend", $"Delete '{path}' 失败: {e.Message}"); }
        }

        public void EnsureDirectory(string dir)
        {
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        public void Move(string srcPath, string dstPath) => File.Move(srcPath, dstPath);

        public async UniTask<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
        {
            try
            {
                return await UniTask.RunOnThreadPool(() => File.ReadAllBytes(path), cancellationToken: ct);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        public UniTask WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
        {
            return UniTask.RunOnThreadPool(() => File.WriteAllBytes(path, bytes), cancellationToken: ct);
        }

        public string ReadAllText(string path) => File.ReadAllText(path);

        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

        public void DeleteDirectory(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
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
