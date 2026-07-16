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
    /// 不关心数据在内存里怎么被修改（那是 GameDataStore 的职责）。
    /// </summary>
    public sealed class SaveMgr : IModule
    {
        private ISaveProvider _provider;
        private IEncryptor _encryptor;
        private string _saveRoot;
        private int _slotCount = 3;

        // 迁移：fromVersion → (toVersion, migration)
        private readonly Dictionary<int, (int ToVersion, Action<object> Migration)> _migrations = new();

        public void Init()
        {
            _provider = new JsonSaveProvider();
            _encryptor = new NoopEncryptor();
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
            Directory.CreateDirectory(slotDir);

            var savePath = Path.Combine(slotDir, "save.dat");
            var tmpPath = savePath + ".tmp";

            // 序列化 + 加密
            var bytes = _provider.Serialize(data);
            bytes = _encryptor.Encrypt(bytes);

            // 写入临时文件
            await UniTask.RunOnThreadPool(() => File.WriteAllBytes(tmpPath, bytes), cancellationToken: ct);

            // 备份旧的
            var bakPath = savePath + ".bak";
            if (File.Exists(savePath))
            {
                if (File.Exists(bakPath)) File.Delete(bakPath);
                File.Move(savePath, bakPath);
            }

            // rename tmp → save
            File.Move(tmpPath, savePath);

            Log.Info("SaveMgr", $"Saved to slot {slotId} (v{data.SchemaVersion}).");
        }

        /// <summary>
        /// 加载存档。自动检测版本，链式迁移到当前版本，迁移后重新保存。
        /// </summary>
        public async UniTask<T> LoadAsync<T>(int slotId, CancellationToken ct = default)
            where T : class, ISaveData, new()
        {
            if (slotId < 0 || slotId >= _slotCount)
                throw new ArgumentOutOfRangeException(nameof(slotId));

            var savePath = Path.Combine(GetSlotDir(slotId), "save.dat");

            if (!File.Exists(savePath))
            {
                Log.Warning("SaveMgr", $"No save in slot {slotId}.");
                return null;
            }

            var bytes = await UniTask.RunOnThreadPool(() => File.ReadAllBytes(savePath), cancellationToken: ct);
            bytes = _encryptor.Decrypt(bytes);

            var data = _provider.Deserialize<T>(bytes);

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
                if (File.Exists(metaPath))
                {
                    var json = File.ReadAllText(metaPath);
                    list.Add(JsonUtility.FromJson<SaveSlotMeta>(json));
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
            return File.Exists(Path.Combine(GetSlotDir(slotId), "save.dat"));
        }

        /// <summary>删除槽位存档</summary>
        public void DeleteSave(int slotId)
        {
            var slotDir = GetSlotDir(slotId);
            if (Directory.Exists(slotDir))
            {
                Directory.Delete(slotDir, recursive: true);
                Log.Info("SaveMgr", $"Deleted slot {slotId}.");
            }
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

            if (!File.Exists(bakPath)) return false;

            if (File.Exists(savePath)) File.Delete(savePath);
            File.Move(bakPath, savePath);

            Log.Warning("SaveMgr", $"Restored backup for slot {slotId}.");
            return true;
        }

        // ──────────────────────────────────────────────
        //  Provider / Encryptor 切换
        // ──────────────────────────────────────────────

        public void SetProvider(ISaveProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void SetEncryptor(IEncryptor encryptor)
        {
            _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
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
            var metaPath = Path.Combine(GetSlotDir(slotId), "meta.json");
            Directory.CreateDirectory(GetSlotDir(slotId));
            var json = JsonUtility.ToJson(meta, prettyPrint: true);
            File.WriteAllText(metaPath, json);
        }

        public void Dispose()
        {
            _provider = null;
            _encryptor = null;
            _migrations.Clear();
        }
    }
}
