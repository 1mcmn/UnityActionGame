using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 敌人状态条。挂在一个 World Space Canvas 上，管理血条和僵直条两个 Slider。
/// Canvas 内布局（用户自行在 Prefab 中设置）：
///   HealthSlider — 上方，anchor top，sizeDelta ~200x25
///   PoiseSlider  — 下方，anchor bottom，sizeDelta ~160x18
/// </summary>
public class EnemyStatusBar : MonoBehaviour
{
    [Header("子 Slider 引用")]
    [SerializeField] private Slider _healthSlider;
    [SerializeField] private Slider _poiseSlider;

    [Header("动画")]
    [SerializeField] private float _animDuration = 0.3f;

    private Enemy _enemy;
    private Vector3 _offset;
    private Coroutine _healthAnim;
    private Coroutine _poiseAnim;

    // ==================== 公开方法 ====================

    public void Bind(Enemy enemy, Vector3 offset)
    {
        _enemy = enemy;
        _offset = offset;
        gameObject.SetActive(true);

        SetupSlider(_healthSlider, enemy.MaxHealth, enemy.CurrentHealth);
        SetupSlider(_poiseSlider, enemy.MaxPoise, enemy.CurrentPoise);

        enemy.OnHealthChanged += UpdateHealth;
        enemy.OnDeath        += HideAll;
        enemy.OnPoiseChanged += UpdatePoise;
        enemy.OnKnockdown    += HidePoise;
    }

    public void Unbind()
    {
        if (_enemy == null) return;

        _enemy.OnHealthChanged -= UpdateHealth;
        _enemy.OnDeath        -= HideAll;
        _enemy.OnPoiseChanged -= UpdatePoise;
        _enemy.OnKnockdown    -= HidePoise;
        _enemy = null;

        StopAnim(ref _healthAnim);
        StopAnim(ref _poiseAnim);
    }

    // ==================== Unity 生命周期 ====================

    private void Awake()
    {
        // 干掉不需要的组件（直接 inline，不用泛型避免兼容问题）
        var scaler = GetComponent<CanvasScaler>();
        if (scaler != null) scaler.enabled = false;

        var raycaster = GetComponent<GraphicRaycaster>();
        if (raycaster != null) raycaster.enabled = false;

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.referencePixelsPerUnit = 100f;
        }

        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(180f, 40f);
        }

        transform.localScale = Vector3.one * 0.02f;
        gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (_enemy == null) return;

        transform.position = _enemy.transform.position + _offset;

        if (Camera.main != null)
        {
            Vector3 dir = Camera.main.transform.position - transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    private void OnDestroy()
    {
        Unbind();
    }

    // ==================== 事件回调 ====================

    private void UpdateHealth(float current)
    {
        if (_healthSlider == null) return;
        StopAnim(ref _healthAnim);
        _healthAnim = StartCoroutine(AnimateRoutine(_healthSlider, current, () => _healthAnim = null));
    }

    private void UpdatePoise(float current)
    {
        if (_poiseSlider == null) return;
        StopAnim(ref _poiseAnim);
        _poiseAnim = StartCoroutine(AnimateRoutine(_poiseSlider, current, () => _poiseAnim = null));
    }

    private void HideAll()
    {
        gameObject.SetActive(false);
    }

    private void HidePoise()
    {
        if (_poiseSlider != null)
            _poiseSlider.gameObject.SetActive(false);
    }

    // ==================== 内部 ====================

    private void SetupSlider(Slider slider, float max, float current)
    {
        if (slider == null) return;
        slider.minValue = 0f;
        slider.maxValue = max;
        slider.value    = current;
        if (slider.handleRect != null)
            slider.handleRect.gameObject.SetActive(false);
    }

    private IEnumerator AnimateRoutine(Slider slider, float target, Action onComplete)
    {
        float start   = slider.value;
        float elapsed = 0f;

        while (elapsed < _animDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _animDuration;
            slider.value = Mathf.Lerp(start, target, 1f - (1f - t) * (1f - t));
            yield return null;
        }

        slider.value = target;
        onComplete?.Invoke();
    }

    private void StopAnim(ref Coroutine coroutine)
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
            coroutine = null;
        }
    }
}