using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 伤害数字对象池单例。挂在一个场景持久 GameObject 上。
/// PlayerCombat.PerformHitDetection() 命中后调 Show()。
/// </summary>
public class DamagePopupManager : MonoBehaviour
{
    public static DamagePopupManager Instance { get; private set; }

    [SerializeField] private DamagePopup _popupPrefab;
    [SerializeField] private int          _poolSize      = 15;
    [SerializeField] private float        _randomOffsetX = 0.4f;  // 水平随机散开

    private Queue<DamagePopup> _pool = new Queue<DamagePopup>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitPool();
    }

    private void InitPool()
    {
        for (int i = 0; i < _poolSize; i++)
        {
            var popup = Instantiate(_popupPrefab, transform);
            popup.gameObject.SetActive(false);
            _pool.Enqueue(popup);
        }
    }

    /// <summary>在 worldPos 上方显示伤害数字</summary>
    public void Show(Vector3 worldPos, float damage)
    {
        if (_popupPrefab == null)
        {
            Debug.LogError("[DamagePopupManager] _popupPrefab 未赋值！请在 Inspector 拖入 DamagePopup 预制体。");
            return;
        }

        DamagePopup popup;
        if (_pool.Count > 0)
            popup = _pool.Dequeue();
        else
            popup = Instantiate(_popupPrefab, transform);  // 池耗尽就扩

        float xOffset = Random.Range(-_randomOffsetX, _randomOffsetX);
        Vector3 spawnPos = worldPos + new Vector3(xOffset, 1.5f, 0f);

        Debug.Log($"[DamagePopupManager] Show({damage}) at {spawnPos}, pool剩余={_pool.Count}");
        popup.Activate(spawnPos, damage, () => _pool.Enqueue(popup));
    }
}