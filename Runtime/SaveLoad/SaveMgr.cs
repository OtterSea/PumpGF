using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 存档数据接口。业务 RootSaveData 实现此接口，提供版本号读写。
    /// </summary>
    public interface ISaveData
    {
        /// <summary>存档版本号</summary>
        int SchemaVersion { get; set; }
    }

    /// <summary>
    /// 纯持久化模块。负责序列化、文件读写、版本迁移、槽位管理。
    /// 不关心数据在内存里怎么被修改（那是 <see cref="GameDataStore"/> 的职责）。
    /// </summary>
    /// <remarks>
    /// 文件 I/O 全部走 <see cref="IStorageBackend"/> 抽象，方便 WebGL/主机平台替换。
    /// 默认后端为 <see cref="FileStorageBackend"/>（PC/Mobile/Editor 通用）。
    /// </remarks>
    public sealed class SaveMgr : IModule
    {
        private ISaveProvider _provider;
        private IEncryptor _encryptor;
        private IStorageBackend _storage;
        private string _saveRoot;
        private int _slotCount = 3;

        // 迁移：fromVersion → (toVersion, migration)
        private readonly Dictionary<int, (int ToVersion, Action<object> Migration)> _migrations = new();

        public void Init()
        {
            _provider = new JsonSaveProvider();
            _encryptor = new NoopEncryptor();
            _storage = new FileStorageBackend();
            _saveRoot = Path.Combine(Application.persistentDataPath, "Saves");
        }

        // ──────────────────────────────────────────────
        //  存档读写
        // ──────────────────────────────────────────────

        /// <summary>
        /// 保存存档。原子写入（.tmp + rename + .bak 备份）。
        /// </summary>
        public async UniTask SaveAsync<T>(int slotId, T data, CancellationToken ct = default)
            where T : class, ISaveData
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (slotId < 0 || slotId >= _slotCount)
                throw new ArgumentOutOfRangeException(nameof(slotId));

            data.SchemaVersion = SaveSchema.CurrentVersion;

            var slotDir = GetSlotDir(slotId);
            _storage.EnsureDirectory(slotDir);

            var savePath = Path.Combine(slotDir, "save.dat");
            var tmpPath = savePath + ".tmp";
            var bakPath = savePath + ".bak";

            // 序列化 + 加密
            var bytes = _provider.Serialize(data);
            bytes = _encryptor.Encrypt(bytes);

            // 写入临时文件
            await _storage.WriteAllBytesAsync(tmpPath, bytes, ct);

            // 备份旧的（存在时）
            if (_storage.Exists(savePath))
            {
                _storage.Delete(bakPath);
                _storage.Move(savePath, bakPath);
            }

            // rename tmp → save
            _storage.Move(tmpPath, savePath);

            Log.Info("SaveMgr", $"Saved to slot {slotId} (v{data.SchemaVersion}).");
        }

        /// <summary>
        /// 加载存档。自动检测版本，链式迁移到当前版本，迁移后重新保存。
        /// 存档不存在时返回 null；读取错误时抛异常（调用方可以 try/catch 后回退 <see cref="TryRestoreBackup"/>）。
        /// </summary>
        public async UniTask<T> LoadAsync<T>(int slotId, CancellationToken ct = default)
            where T : class, ISaveData, new()
        {
            if (slotId < 0 || slotId >= _slotCount)
                throw new ArgumentOutOfRangeException(nameof(slotId));

            var savePath = Path.Combine(GetSlotDir(slotId), "save.dat");

            // 直接 try read（避免 Exists→Read 竞态）：不存在时后端返回 null
            var bytes = await _storage.ReadAllBytesAsync(savePath, ct);
            if (bytes == null)
            {
                Log.Warning("SaveMgr", $"No save in slot {slotId}.");
                return null;
            }

            bytes = _encryptor.Decrypt(bytes);

            var data = _provider.Deserialize<T>(bytes);
            if (data == null)
            {
                Log.Error("SaveMgr", $"Deserialize failed for slot {slotId}.");
                return null;
            }

            // 链式迁移
            int migrationsApplied = 0;
            while (data.SchemaVersion < SaveSchema.CurrentVersion)
            {
                if (_migrations.TryGetValue(data.SchemaVersion, out var migration))
                {
                    Log.Info("SaveMgr", $"Migrating v{data.SchemaVersion} → v{migration.ToVersion}.");
                    migration.Migration(data);
                    data.SchemaVersion = migration.ToVersion;
                    migrationsApplied++;
                }
                else
                {
                    Log.Error("SaveMgr", $"No migration from version {data.SchemaVersion}.");
                    break;
                }
            }

            // 迁移后重新保存
            if (migrationsApplied > 0 && data.SchemaVersion == SaveSchema.CurrentVersion)
            {
                await SaveAsync(slotId, data, ct);
            }

            return data;
        }

        // ──────────────────────────────────────────────
        //  槽位管理
        // ──────────────────────────────────────────────

        /// <summary>获取所有槽位元信息。无存档的槽位返回 null。</summary>
        public IReadOnlyList<SaveSlotMeta> GetSlotList()
        {
            var list = new List<SaveSlotMeta>(_slotCount);
            for (int i = 0; i < _slotCount; i++)
            {
                var metaPath = Path.Combine(GetSlotDir(i), "meta.json");
                if (_storage.Exists(metaPath))
                {
                    try
                    {
                        var json = _storage.ReadAllText(metaPath);
                        list.Add(JsonUtility.FromJson<SaveSlotMeta>(json));
                    }
                    catch (Exception e)
                    {
                        Log.Warning("SaveMgr", $"Read meta for slot {i} failed: {e.Message}");
                        list.Add(null);
                    }
                }
                else
                {
                    list.Add(null);
                }
            }
            return list;
        }

        /// <summary>槽位是否有存档</summary>
        public bool HasSave(int slotId)
        {
            return _storage.Exists(Path.Combine(GetSlotDir(slotId), "save.dat"));
        }

        /// <summary>删除槽位存档</summary>
        public void DeleteSave(int slotId)
        {
            var slotDir = GetSlotDir(slotId);
            _storage.DeleteDirectory(slotDir);
            Log.Info("SaveMgr", $"Deleted slot {slotId}.");
        }

        /// <summary>槽位数量</summary>
        public int SlotCount => _slotCount;

        // ──────────────────────────────────────────────
        //  版本迁移
        // ──────────────────────────────────────────────

        /// <summary>注册版本迁移函数</summary>
        public void RegisterMigration(int fromVersion, int toVersion, Action<object> migration)
        {
            if (migration == null) throw new ArgumentNullException(nameof(migration));
            _migrations[fromVersion] = (toVersion, migration);
        }

        // ──────────────────────────────────────────────
        //  备份恢复
        // ──────────────────────────────────────────────

        /// <summary>尝试从 .bak 恢复备份</summary>
        public bool TryRestoreBackup(int slotId)
        {
            var slotDir = GetSlotDir(slotId);
            var savePath = Path.Combine(slotDir, "save.dat");
            var bakPath = savePath + ".bak";

            if (!_storage.Exists(bakPath)) return false;

            _storage.Delete(savePath);
            _storage.Move(bakPath, savePath);

            Log.Warning("SaveMgr", $"Restored backup for slot {slotId}.");
            return true;
        }

        // ──────────────────────────────────────────────
        //  Provider / Encryptor / Storage 切换
        // ──────────────────────────────────────────────

        public void SetProvider(ISaveProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void SetEncryptor(IEncryptor encryptor)
        {
            _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        }

        /// <summary>切换存储后端（WebGL 用 PlayerPrefs 后端时替换）</summary>
        public void SetStorageBackend(IStorageBackend storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        // ──────────────────────────────────────────────
        //  内部
        // ──────────────────────────────────────────────

        private string GetSlotDir(int slotId)
        {
            return Path.Combine(_saveRoot, $"Slot{slotId}");
        }

        /// <summary>更新槽位元信息</summary>
        public void UpdateSlotMeta(int slotId, SaveSlotMeta meta)
        {
            var slotDir = GetSlotDir(slotId);
            _storage.EnsureDirectory(slotDir);
            var metaPath = Path.Combine(slotDir, "meta.json");
            var json = JsonUtility.ToJson(meta, prettyPrint: true);
            _storage.WriteAllText(metaPath, json);
        }

        public void Dispose()
        {
            _provider = null;
            _encryptor = null;
            _storage = null;
            _migrations.Clear();
        }
    }
}
