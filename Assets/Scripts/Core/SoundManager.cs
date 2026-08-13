using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局音效管理单例。
/// 聚合所有 SoundLibrary 资产，提供按 ID 查表播放的唯一切入点。
/// 挂在一个场景持久 GameObject 上（例如 "Audio" 空物体）。
/// 使用 AudioSource 对象池，避免每次播放都 new GameObject 造成 GC 压力。
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] private SoundLibrary[] _libraries;
    [SerializeField] private bool _logPlayback;  // 勾选后在 Console 显示每次播放的音效

    private Dictionary<string, AudioClip> _dict = new Dictionary<string, AudioClip>();

    // 对象池
    private const int PoolSize = 16;
    private Queue<AudioSource> _pool = new Queue<AudioSource>();
    private GameObject _poolRoot;

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
        InitPool();
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

    private void InitPool()
    {
        _poolRoot = new GameObject("_sfx_pool");
        DontDestroyOnLoad(_poolRoot);

        for (int i = 0; i < PoolSize; i++)
        {
            var src = CreateSource(i);
            _pool.Enqueue(src);
        }
    }

    private AudioSource CreateSource(int index)
    {
        var go = new GameObject($"_sfx_{index}");
        go.transform.SetParent(_poolRoot.transform);
        go.SetActive(false);

        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;                       // 3D 音效
        src.rolloffMode = AudioRolloffMode.Linear;
        src.maxDistance = 30f;
        return src;
    }

    // ==================== 公共接口 ====================

    /// <summary>按 ID 播放音效（3D 空间），pitch 默认 1.0</summary>
    public void Play(string soundId, Vector3 position, float pitch = 1f)
    {
        PlayInternal(soundId, position, pitch);
    }

    /// <summary>
    /// 根据前缀自动查找所有变体并随机播放。命名规则：{prefix}_*。
    /// </summary>
    public void PlayByPrefix(string prefix, Vector3 position)
    {
        PlayByPrefix(prefix, position, 1f, 1f);
    }

    /// <summary>
    /// 根据前缀随机播放 + 随机 pitch（用于脚步声等需要变化的短音效）。
    /// </summary>
    public void PlayByPrefix(string prefix, Vector3 position, float pitchMin, float pitchMax)
    {
        var matches = new List<string>();
        foreach (var key in _dict.Keys)
        {
            if (key.StartsWith(prefix + "_"))
                matches.Add(key);
        }

        if (matches.Count == 0)
        {
            // fallback：尝试精确匹配 prefix 本身
            if (_dict.TryGetValue(prefix, out var exactClip))
            {
                PlayInternal(prefix, position, Random.Range(pitchMin, pitchMax));
                return;
            }
            Debug.LogWarning($"[SoundManager] 未找到前缀为 {prefix}_ 的音效，精确匹配 {prefix} 也未找到");
            return;
        }

        var id = matches[Random.Range(0, matches.Count)];
        PlayInternal(id, position, Random.Range(pitchMin, pitchMax));
    }

    /// <summary>检查某个 ID 是否已注册</summary>
    public bool HasSound(string soundId) => _dict.ContainsKey(soundId);

    // ==================== 内部 ====================

    private void PlayInternal(string soundId, Vector3 position, float pitch)
    {
        if (!_dict.TryGetValue(soundId, out var clip))
        {
            Debug.LogWarning($"[SoundManager] 未找到音效: {soundId}");
            return;
        }

        if (_logPlayback)
            Debug.Log($"[SoundManager] ▶ {soundId}  pitch:{pitch:F2}");

        AudioSource src = GetPooledSource();
        src.transform.position = position;
        src.clip = clip;
        src.pitch = pitch;
        src.Play();

        StartCoroutine(Recycle(src, clip.length / pitch + 0.1f));
    }

    private AudioSource GetPooledSource()
    {
        AudioSource src = _pool.Count > 0 ? _pool.Dequeue() : CreateSource(_pool.Count + PoolSize);
        src.gameObject.SetActive(true);
        return src;
    }

    private IEnumerator Recycle(AudioSource src, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (src != null)
        {
            src.Stop();
            src.clip = null;
            src.gameObject.SetActive(false);
            _pool.Enqueue(src);
        }
    }

#if UNITY_EDITOR
    /// <summary>编辑器下运行时重新构建字典（用于修改 SoundLibrary 后刷新）</summary>
    [ContextMenu("Rebuild Dictionary")]
    private void RebuildDict() { BuildDictionary(); }
#endif
}
