using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PumpGF
{
    /// <summary>
    /// 游戏音频统一播放与管理中枢。通过 SO 配置驱动，业务只需 <see cref="Play"/> 即可播放，
    /// AudioMgr 据 <see cref="AudioEntry.Type"/> 自动路由到 BGM/SFX/UI/Voice 通道。
    /// <para>BGM：单曲播放 + DOTween 淡入淡出切换 + 循环；SFX/UI/Voice：AudioSource 池并发 + Fire-and-Forget。</para>
    /// </summary>
    /// <remarks>
    /// <b>初始化注意</b>：<see cref="Init"/> 为同步无法加载默认配置，业务应在启动时调用
    /// <see cref="LoadConfigAsync"/>（如 key=<c>"AudioConfig"</c>）。未加载配置时 <see cref="Play"/> 会 Warning。
    /// </remarks>
    public sealed class AudioMgr : IModule
    {
        private ResMgr _resMgr;
        private Scheduler _scheduler;
        private GameObject _root;
        private AudioSource _bgmSource;

        // BGM 状态
        private AssetHandle<AudioClip> _bgmClipHandle;
        private string _bgmKey;
        private bool _bgmActive;
        private Tween _bgmFadeTween;

        // SFX 池
        private readonly List<AudioSource> _pool = new(16);
        private readonly List<SfxInstance> _activeSfx = new(16);
        private int _poolCapacity = 16;

        // 配置查找表
        private readonly Dictionary<string, AudioEntry> _entries = new(64);
        private readonly Dictionary<string, AssetHandle<AudioConfigSO>> _configHandles = new(8);
        private readonly Dictionary<string, List<string>> _configEntryKeys = new(8);

        // SFX clip 缓存（引用计数托管）
        private readonly Dictionary<string, AssetHandle<AudioClip>> _clipCache = new(64);
        // 并发计数
        private readonly Dictionary<string, int> _concurrentCount = new(16);

        // 音量组
        public ReactiveProperty<float> BgmVolume { get; } = new(1f);
        public ReactiveProperty<float> SfxVolume { get; } = new(1f);
        public ReactiveProperty<float> UiVolume { get; } = new(1f);
        public ReactiveProperty<float> VoiceVolume { get; } = new(1f);
        public ReactiveProperty<bool> BgmMuted { get; } = new(false);
        public ReactiveProperty<bool> SfxMuted { get; } = new(false);
        public ReactiveProperty<bool> UiMuted { get; } = new(false);
        public ReactiveProperty<bool> VoiceMuted { get; } = new(false);

        private bool _paused;
        private float _fadeInDuration = 0.5f;
        private float _fadeOutDuration = 0.5f;
        private IDisposable _reclaimTick;

        // ──────────────────────────────────────────────
        //  IModule
        // ──────────────────────────────────────────────

        public void Init()
        {
            _resMgr = GameGlobal.ResMgr;
            _scheduler = GameGlobal.Scheduler;

            _root = new GameObject("PumpGF_AudioRoot");
            Object.DontDestroyOnLoad(_root);

            _bgmSource = CreateAudioSource("BGM_Source", _root.transform);
            _bgmSource.loop = true;

            // 预创建 SFX 池
            for (int i = 0; i < 4; i++)
                _pool.Add(CreateAudioSource($"SFX_Pool_{i}", _root.transform));

            // 每帧检查 SFX 播放完回收
            _reclaimTick = _scheduler.ScheduleEveryFrame(ReclaimFinished, 1, UpdateChannel.Default);

            Log.Info("AudioMgr", "AudioMgr initialized.");
        }

        public void Dispose()
        {
            _reclaimTick?.Dispose();
            _reclaimTick = null;

            _bgmFadeTween?.Kill();
            _bgmSource?.Stop();
            _bgmClipHandle?.Dispose();
            _bgmClipHandle = null;
            _bgmActive = false;

            foreach (var inst in _activeSfx)
            {
                if (inst.Source != null) inst.Source.Stop();
            }
            _activeSfx.Clear();

            foreach (var kvp in _clipCache) kvp.Value.Dispose();
            _clipCache.Clear();

            foreach (var kvp in _configHandles) kvp.Value.Dispose();
            _configHandles.Clear();
            _configEntryKeys.Clear();

            _entries.Clear();
            _concurrentCount.Clear();
            _pool.Clear();

            BgmVolume.Dispose(); SfxVolume.Dispose(); UiVolume.Dispose(); VoiceVolume.Dispose();
            BgmMuted.Dispose(); SfxMuted.Dispose(); UiMuted.Dispose(); VoiceMuted.Dispose();

            if (_root != null) { Object.Destroy(_root); _root = null; }
        }

        // ──────────────────────────────────────────────
        //  播放（统一入口，配置路由）
        // ──────────────────────────────────────────────

        /// <summary>2D 播放。类型由配置决定路由（BGM/SFX/UI/Voice）。</summary>
        public IAudioHandle Play(string key)
        {
            if (!TryGetEntry(key, out var entry)) return null;
            return PlayInternal(entry, null, null);
        }

        /// <summary>3D 定点播放（entry.Is3D=true 生效）</summary>
        public IAudioHandle Play3D(string key, Vector3 position)
        {
            if (!TryGetEntry(key, out var entry)) return null;
            if (entry.Type == AudioType.BGM)
            {
                Log.Warning("AudioMgr", "BGM 不支持 3D，改用 2D 播放。");
                return PlayInternal(entry, null, null);
            }
            return PlayInternal(entry, position, null);
        }

        /// <summary>3D 跟随目标播放（AudioSource 挂到目标下）</summary>
        public IAudioHandle Play3D(string key, Transform followTarget)
        {
            if (!TryGetEntry(key, out var entry)) return null;
            if (entry.Type == AudioType.BGM)
            {
                Log.Warning("AudioMgr", "BGM 不支持 3D，改用 2D 播放。");
                return PlayInternal(entry, null, null);
            }
            return PlayInternal(entry, null, followTarget);
        }

        private IAudioHandle PlayInternal(AudioEntry entry, Vector3? position, Transform followTarget)
        {
            if (entry.Type == AudioType.BGM)
            {
                PlayBgmAsync(entry).Forget();
                return new BgmHandle(this);
            }
            return PlaySfx(entry, position, followTarget);
        }

        // ──────────────────────────────────────────────
        //  BGM 控制
        // ──────────────────────────────────────────────

        /// <summary>停止 BGM（淡出）</summary>
        public void StopBgm(float fadeOutDuration = -1f)
        {
            StopBgmAsync(fadeOutDuration < 0 ? _fadeOutDuration : fadeOutDuration).Forget();
        }

        /// <summary>暂停 BGM（不淡出）</summary>
        public void PauseBgm()
        {
            if (_bgmSource != null) _bgmSource.Pause();
        }

        /// <summary>恢复 BGM</summary>
        public void ResumeBgm()
        {
            if (_bgmSource != null && _bgmActive) _bgmSource.UnPause();
        }

        /// <summary>BGM 是否在播放</summary>
        public bool IsBgmPlaying => _bgmSource != null && _bgmSource.isPlaying;

        /// <summary>当前 BGM key</summary>
        public string CurrentBgmKey => _bgmKey;

        private async UniTaskVoid PlayBgmAsync(AudioEntry entry)
        {
            var ct = GameGlobal.LifecycleMgr.CreateAppLifetimeToken();

            // 淡出旧 BGM
            if (_bgmActive)
            {
                _bgmFadeTween?.Kill();
                _bgmFadeTween = _bgmSource.DOFade(0f, _fadeOutDuration);
                try { await _bgmFadeTween.ToUniTask(cancellationToken: ct); }
                catch (OperationCanceledException) { return; } // UniTaskVoid：吞 OCE，不污染日志
                catch (Exception e) { Log.Error("AudioMgr", $"BGM 淡出异常: {e.Message}"); }
                _bgmSource.Stop();
                ReleaseBgmClip();
            }

            // 加载新 BGM clip
            try
            {
                _bgmClipHandle = await _resMgr.LoadAssetAsync<AudioClip>(entry.AddressablesKey, ct: ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                Log.Error("AudioMgr", $"BGM 加载失败 '{entry.Key}': {e.Message}");
                return;
            }

            _bgmKey = entry.Key;
            _bgmActive = true;
            var targetVol = entry.Volume * BgmVolume.CurrentValue * (BgmMuted.CurrentValue ? 0f : 1f);
            _bgmSource.clip = _bgmClipHandle.Asset;
            _bgmSource.loop = true;
            _bgmSource.pitch = entry.Pitch;
            _bgmSource.spatialBlend = 0f;
            _bgmSource.mute = _paused;
            _bgmSource.volume = 0f;
            _bgmSource.Play();

            _bgmFadeTween?.Kill();
            _bgmFadeTween = _bgmSource.DOFade(targetVol, _fadeInDuration);
            try { await _bgmFadeTween.ToUniTask(cancellationToken: ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Log.Error("AudioMgr", $"BGM 淡入异常: {e.Message}"); }

            Log.Info("AudioMgr", $"BGM 播放: {entry.Key}");
        }

        private async UniTaskVoid StopBgmAsync(float fadeOut)
        {
            if (!_bgmActive) return;
            var ct = GameGlobal.LifecycleMgr.CreateAppLifetimeToken();
            _bgmFadeTween?.Kill();
            _bgmFadeTween = _bgmSource.DOFade(0f, fadeOut);
            try { await _bgmFadeTween.ToUniTask(cancellationToken: ct); }
            catch (OperationCanceledException) { return; } // UniTaskVoid：吞 OCE
            catch (Exception e) { Log.Error("AudioMgr", $"BGM 停止淡出异常: {e.Message}"); }
            _bgmSource.Stop();
            ReleaseBgmClip();
            _bgmActive = false;
            _bgmKey = null;
        }

        private void ReleaseBgmClip()
        {
            if (_bgmSource != null) _bgmSource.clip = null;
            _bgmClipHandle?.Dispose();
            _bgmClipHandle = null;
        }

        // ──────────────────────────────────────────────
        //  SFX 播放（池化并发）
        // ──────────────────────────────────────────────

        private IAudioHandle PlaySfx(AudioEntry entry, Vector3? position, Transform followTarget)
        {
            // 并发限制
            if (entry.MaxConcurrent > 0)
            {
                _concurrentCount.TryGetValue(entry.Key, out var c);
                if (c >= entry.MaxConcurrent)
                {
                    Log.Warning("AudioMgr", $"SFX '{entry.Key}' 达到并发上限 {entry.MaxConcurrent}，丢弃。");
                    return null;
                }
                _concurrentCount[entry.Key] = c + 1;
            }

            var source = AcquireSfxSource();
            var inst = new SfxInstance { Source = source, Entry = entry };
            _activeSfx.Add(inst);

            // 3D 设置
            source.spatialBlend = entry.Is3D ? entry.SpatialBlend : 0f;
            source.pitch = entry.Pitch;
            source.mute = _paused;
            if (position.HasValue) source.transform.position = position.Value;
            if (followTarget != null) source.transform.SetParent(followTarget, false);

            // clip 加载
            if (_clipCache.TryGetValue(entry.AddressablesKey, out var h) && h.Asset != null)
            {
                StartSfxPlay(inst, h);
            }
            else
            {
                inst.Loading = true;
                LoadAndPlaySfxAsync(inst).Forget();
            }

            return new SfxHandle(this, inst);
        }

        private void StartSfxPlay(SfxInstance inst, AssetHandle<AudioClip> clipHandle)
        {
            var src = inst.Source;
            src.clip = clipHandle.Asset;
            inst.BaseVolume = GetEffectiveVolume(inst.Entry);
            src.volume = inst.BaseVolume;
            src.Play();
            inst.Loading = false;
        }

        private async UniTaskVoid LoadAndPlaySfxAsync(SfxInstance inst)
        {
            var ct = GameGlobal.LifecycleMgr.CreateAppLifetimeToken();
            try
            {
                var handle = await GetOrLoadClipAsync(inst.Entry.AddressablesKey, ct);
                if (inst.Released) return; // 加载期间已被停止
                StartSfxPlay(inst, handle);
            }
            catch (OperationCanceledException)
            {
                // UniTaskVoid 中吞 OCE，防止污染 UniTaskScheduler.UnobservedTaskException
                ReleaseSfx(inst);
            }
            catch (Exception e)
            {
                Log.Error("AudioMgr", $"SFX 加载失败 '{inst.Entry.Key}': {e.Message}");
                ReleaseSfx(inst);
            }
        }

        private void ReclaimFinished()
        {
            for (int i = _activeSfx.Count - 1; i >= 0; i--)
            {
                var inst = _activeSfx[i];
                if (inst.Loading || inst.Released) continue;
                var src = inst.Source;
                if (src == null || (!src.isPlaying && src.time <= 0f))
                {
                    ReleaseSfx(inst);
                    _activeSfx.RemoveAt(i);
                }
            }
        }

        private void ReleaseSfx(SfxInstance inst)
        {
            if (inst.Released) return;
            inst.Released = true;
            var src = inst.Source;
            if (src != null)
            {
                src.Stop();
                src.clip = null;
                src.transform.SetParent(_root != null ? _root.transform : null, false);
                src.transform.localPosition = Vector3.zero;
                src.spatialBlend = 0f;
                _pool.Add(src);
            }
            // 并发计数递减
            if (inst.Entry != null && inst.Entry.MaxConcurrent > 0
                && _concurrentCount.TryGetValue(inst.Entry.Key, out var c))
            {
                _concurrentCount[inst.Entry.Key] = Mathf.Max(0, c - 1);
            }
        }

        private void StopSfx(SfxInstance inst, float fadeOut)
        {
            if (inst == null || inst.Released || inst.Source == null) return;
            if (fadeOut <= 0f)
            {
                ReleaseSfx(inst);
                _activeSfx.Remove(inst);
            }
            else
            {
                inst.Source.DOFade(0f, fadeOut).OnComplete(() =>
                {
                    ReleaseSfx(inst);
                    _activeSfx.Remove(inst);
                });
            }
        }

        private AudioSource AcquireSfxSource()
        {
            int n = _pool.Count;
            if (n > 0)
            {
                var s = _pool[n - 1];
                _pool.RemoveAt(n - 1);
                return s;
            }
            return CreateAudioSource($"SFX_{_activeSfx.Count + _pool.Count}", _root.transform);
        }

        // ──────────────────────────────────────────────
        //  音量组
        // ──────────────────────────────────────────────

        /// <summary>暂停/恢复所有音频（业务决定调用时机，如订阅 Lifecycle 暂停）</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_bgmSource != null) _bgmSource.mute = paused;
            for (int i = 0; i < _activeSfx.Count; i++)
            {
                var src = _activeSfx[i].Source;
                if (src != null) src.mute = paused;
            }
        }

        /// <summary>设置 BGM 淡入淡出默认时长</summary>
        public void SetFadeDuration(float fadeIn, float fadeOut)
        {
            _fadeInDuration = Mathf.Max(0f, fadeIn);
            _fadeOutDuration = Mathf.Max(0f, fadeOut);
        }

        /// <summary>设置 SFX 池容量（影响预创建数，运行时仍可扩容）</summary>
        public void SetPoolCapacity(int capacity) => _poolCapacity = Mathf.Max(1, capacity);

        private float GetEffectiveVolume(AudioEntry entry)
        {
            float groupVol = entry.Type switch
            {
                AudioType.BGM => BgmVolume.CurrentValue,
                AudioType.SFX => SfxVolume.CurrentValue,
                AudioType.UI => UiVolume.CurrentValue,
                AudioType.Voice => VoiceVolume.CurrentValue,
                _ => 1f,
            };
            bool muted = entry.Type switch
            {
                AudioType.BGM => BgmMuted.CurrentValue,
                AudioType.SFX => SfxMuted.CurrentValue,
                AudioType.UI => UiMuted.CurrentValue,
                AudioType.Voice => VoiceMuted.CurrentValue,
                _ => false,
            };
            return entry.Volume * groupVol * (muted ? 0f : 1f);
        }

        // ──────────────────────────────────────────────
        //  配置加载
        // ──────────────────────────────────────────────

        /// <summary>异步加载 AudioConfigSO，合并 entry 到查找表。key 重复时 Warning + 保留首份跳过（不覆盖）。</summary>
        public async UniTask LoadConfigAsync(string configKey, CancellationToken ct = default)
        {
            if (_configHandles.ContainsKey(configKey))
            {
                Log.Warning("AudioMgr", $"配置 '{configKey}' 已加载，跳过。");
                return;
            }
            var handle = await _resMgr.LoadAssetAsync<AudioConfigSO>(configKey, ct: ct);
            _configHandles[configKey] = handle;

            var addedKeys = new List<string>(handle.Asset != null ? handle.Asset.Entries.Count : 0);
            if (handle.Asset != null)
            {
                foreach (var e in handle.Asset.Entries)
                {
                    if (string.IsNullOrEmpty(e.Key)) continue;
                    if (_entries.ContainsKey(e.Key))
                    {
                        Log.Warning("AudioMgr", $"音频 key '{e.Key}' 重复，保留首份并跳过（不覆盖）。");
                        continue;
                    }
                    _entries[e.Key] = e;
                    addedKeys.Add(e.Key);
                }
            }
            _configEntryKeys[configKey] = addedKeys;
            Log.Info("AudioMgr", $"音频配置加载: '{configKey}'（{addedKeys.Count} 条）");
        }

        /// <summary>卸载配置（移除其 entry + 释放 handle）</summary>
        public void UnloadConfig(string configKey)
        {
            if (!_configHandles.TryGetValue(configKey, out var handle)) return;
            if (_configEntryKeys.TryGetValue(configKey, out var keys))
            {
                foreach (var k in keys) _entries.Remove(k);
                _configEntryKeys.Remove(configKey);
            }
            handle.Dispose();
            _configHandles.Remove(configKey);
        }

        // ──────────────────────────────────────────────
        //  预加载
        // ──────────────────────────────────────────────

        /// <summary>按预加载标签预载 SFX clip（加载到缓存，避免首次播放卡顿）</summary>
        public async UniTask PreloadSfxAsync(string label, IProgress<float> progress = null, CancellationToken ct = default)
        {
            var keys = new List<string>(8);
            foreach (var e in _entries.Values)
            {
                if (e.PreloadLabel == label && !string.IsNullOrEmpty(e.AddressablesKey))
                    keys.Add(e.AddressablesKey);
            }
            int total = Mathf.Max(1, keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!_clipCache.ContainsKey(keys[i]))
                    _clipCache[keys[i]] = await _resMgr.LoadAssetAsync<AudioClip>(keys[i], ct: ct);
                progress?.Report((float)(i + 1) / total);
            }
        }

        private async UniTask<AssetHandle<AudioClip>> GetOrLoadClipAsync(string addressablesKey, CancellationToken ct)
        {
            if (_clipCache.TryGetValue(addressablesKey, out var existing) && existing.IsValid)
                return existing;
            var handle = await _resMgr.LoadAssetAsync<AudioClip>(addressablesKey, ct: ct);
            _clipCache[addressablesKey] = handle;
            return handle;
        }

        // ──────────────────────────────────────────────
        //  查询
        // ──────────────────────────────────────────────

        public bool HasAudioEntry(string key) => _entries.ContainsKey(key);
        public bool IsClipLoaded(string key) => _clipCache.ContainsKey(key);

        private bool TryGetEntry(string key, out AudioEntry entry)
        {
            if (_entries.TryGetValue(key, out entry)) return true;
            Log.Warning("AudioMgr", $"未注册的音频 key '{key}'，请先 LoadConfigAsync。");
            entry = null;
            return false;
        }

        // ──────────────────────────────────────────────
        //  内部辅助
        // ──────────────────────────────────────────────

        private static AudioSource CreateAudioSource(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            return src;
        }

        // ──────────────────────────────────────────────
        //  内部类型
        // ──────────────────────────────────────────────

        private sealed class SfxInstance
        {
            public AudioSource Source;
            public AudioEntry Entry;
            public bool Loading;
            public bool Released;
            public float BaseVolume;
        }

        private sealed class BgmHandle : IAudioHandle
        {
            private readonly AudioMgr _mgr;
            public BgmHandle(AudioMgr mgr) { _mgr = mgr; }
            public bool IsValid => _mgr._bgmActive && _mgr._bgmSource != null && _mgr._bgmSource.isPlaying;
            public float Volume
            {
                get => _mgr._bgmSource != null ? _mgr._bgmSource.volume : 0f;
                set { if (_mgr._bgmSource != null) _mgr._bgmSource.volume = value; }
            }
            public void Stop(float fadeOut = 0f) => _mgr.StopBgm(fadeOut);
            public void Pause() => _mgr.PauseBgm();
            public void Resume() => _mgr.ResumeBgm();
        }

        private sealed class SfxHandle : IAudioHandle
        {
            private readonly AudioMgr _mgr;
            private SfxInstance _inst;
            public SfxHandle(AudioMgr mgr, SfxInstance inst) { _mgr = mgr; _inst = inst; }

            public bool IsValid => _inst != null && !_inst.Released && _inst.Source != null;

            public float Volume
            {
                get => _inst != null && _inst.Source != null ? _inst.Source.volume : 0f;
                set
                {
                    if (_inst != null && !_inst.Released && _inst.Source != null)
                        _inst.Source.volume = value;
                }
            }

            public void Stop(float fadeOut = 0f)
            {
                if (_inst == null || _inst.Released) return;
                var inst = _inst;
                _inst = null;
                _mgr.StopSfx(inst, fadeOut);
            }

            public void Pause()
            {
                if (IsValid) _inst.Source.Pause();
            }

            public void Resume()
            {
                if (IsValid) _inst.Source.UnPause();
            }
        }
    }
}
