using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 单个伤害数字。由 DamagePopupManager 对象池管理。
/// 表现：弹出 → 上升 + 淡出 + 缩放，约 1 秒后回收。
/// </summary>
public class DamagePopup : MonoBehaviour
{
    [SerializeField] private TextMeshPro _text;
    [SerializeField] private float _duration  = 1f;
    [SerializeField] private float _riseSpeed = 1.5f;
    [SerializeField] private AnimationCurve _alphaCurve = AnimationCurve.EaseInOut(0, 1, 0.7f, 0);
    [SerializeField] private AnimationCurve _scaleCurve = AnimationCurve.EaseInOut(0, 0.3f, 0.2f, 1);

    private System.Action _onComplete;
    private float   _timer;
    private Vector3 _startPos;

    public void Activate(Vector3 worldPos, float damage, System.Action onComplete)
    {
        if (_text == null)
        {
            Debug.LogError("[DamagePopup] _text 未赋值！请在预制体上把 TextMeshPro 组件拖到 DamagePopup 的 Text 字段。");
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        transform.position = worldPos;
        _startPos  = worldPos;
        _timer     = 0f;
        _onComplete = onComplete;

        _text.text  = Mathf.RoundToInt(damage).ToString();
        _text.alpha = 1f;
        transform.localScale = Vector3.zero;
        Debug.Log($"[DamagePopup] 显示伤害 {damage} 在 {worldPos}");
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        float t = _timer / _duration;

        if (t >= 1f)
        {
            gameObject.SetActive(false);
            _onComplete?.Invoke();
            _onComplete = null;
            return;
        }

        // 上升
        transform.position = _startPos + Vector3.up * (_riseSpeed * _timer);

        // 面朝摄像机
        if (Camera.main != null)
            transform.forward = Camera.main.transform.forward;

        // 透明度 + 缩放
        _text.alpha = _alphaCurve.Evaluate(t);
        float s = _scaleCurve.Evaluate(t);
        transform.localScale = new Vector3(s, s, s);
    }
}