using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局音效管理单例。
/// 聚合所有 SoundLibrary 资产，提供按 ID 查表播放的唯一切入点。
/// 挂在一个场景持久 GameObject 上（例如 "Audio" 空物体）。
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] private SoundLibrary[] _libraries;
    [SerializeField] private bool _logPlayback;  // 勾选后在 Console 显示每次播放的音效

    private Dictionary<string, AudioClip> _dict = new Dictionary<string, AudioClip>();

    // ==================== 生命周期 ====================

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildDictionary();
    }

    private void BuildDictionary()
    {
        _dict.Clear();

        foreach (var lib in _libraries)
        {
            if (lib == null) continue;
            foreach (var item in lib.sounds)
            {
                if (string.IsNullOrEmpty(item.soundID) || item.clip == null) continue;

                if (_dict.ContainsKey(item.soundID))
                    Debug.LogWarning($"[SoundManager] 重复的 soundID: {item.soundID}，后者覆盖前者");
                _dict[item.soundID] = item.clip;
            }
        }

        Debug.Log($"[SoundManager] 字典构建完成，共 {_dict.Count} 条音效");
    }

    // ==================== 公共接口 ====================

    /// <summary>按 ID 播放音效（3D 空间），pitch 默认 1.0</summary>
    public void Play(string soundId, Vector3 position, float pitch = 1f)
    {
        PlayInternal(soundId, position, pitch);
    }

    /// <summary>
    /// 根据前缀自动查找所有变体并随机播放。命名规则：{prefix}_*。
    /// 例如字典里有 foot_step_01 / _02 / _05（不连续也行），
    /// PlayByPrefix("foot_step", pos) 会从已有条目中随机选一个。
    /// </summary>
    public void PlayByPrefix(string prefix, Vector3 position)
    {
        PlayByPrefix(prefix, position, 1f, 1f);
    }

    /// <summary>
    /// 根据前缀随机播放 + 随机 pitch（用于脚步声等需要变化的短音效）。
    /// pitchMin/pitchMax 为闭区间，例如 (0.9f, 1.1f) 会让每次播放略有不同。
    /// </summary>
    public void PlayByPrefix(string prefix, Vector3 position, float pitchMin, float pitchMax)
    {
        var matches = new System.Collections.Generic.List<string>();
        foreach (var key in _dict.Keys)
        {
            if (key.StartsWith(prefix + "_"))
                matches.Add(key);
        }

        if (matches.Count == 0)
        {
            // fallback：尝试精确匹配 prefix 本身（适合 sword_sheath 这类单条命名）
            if (_dict.TryGetValue(prefix, out var exactClip))
            {
                PlayInternal(prefix, position, Random.Range(pitchMin, pitchMax));
                return;
            }
            Debug.LogWarning($"[SoundManager] 未找到前缀为 {prefix}_ 的音效，精确匹配 {prefix} 也未找到");
            return;
        }

        var id = matches[UnityEngine.Random.Range(0, matches.Count)];
        PlayInternal(id, position, Random.Range(pitchMin, pitchMax));
    }

    /// <summary>检查某个 ID 是否已注册</summary>
    public bool HasSound(string soundId) => _dict.ContainsKey(soundId);

    // ==================== 内部 ====================

    /// <summary>
    /// 核心播放：创建临时 GameObject + AudioSource（支持 pitch，PlayClipAtPoint 不支持）。
    /// 播放完毕后自动销毁。
    /// </summary>
    private void PlayInternal(string soundId, Vector3 position, float pitch)
    {
        if (!_dict.TryGetValue(soundId, out var clip))
        {
            Debug.LogWarning($"[SoundManager] 未找到音效: {soundId}");
            return;
        }

        if (_logPlayback)
            Debug.Log($"[SoundManager] ▶ {soundId}  pitch:{pitch:F2}");

        var go = new GameObject($"_sfx_{soundId}");
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.pitch = pitch;
        src.spatialBlend = 1f;       // 3D 音效
        src.rolloffMode = AudioRolloffMode.Linear;
        src.maxDistance = 30f;
        src.Play();
        Destroy(go, clip.length / pitch + 0.1f);
    }

#if UNITY_EDITOR
    /// <summary>编辑器下运行时重新构建字典（用于修改 SoundLibrary 后刷新）</summary>
    [ContextMenu("Rebuild Dictionary")]
    private void RebuildDict() { BuildDictionary(); }
#endif
}