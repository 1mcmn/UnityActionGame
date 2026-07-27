using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [Header("动画")]
    [SerializeField] private float _animDuration = 0.3f;

    private Enemy _enemy;
    private Slider _slider;
    private Vector3 _offset;
    private Coroutine _animCoroutine;

    public void Bind(Enemy enemy, Vector3 offset)
    {
        _enemy = enemy;
        _offset = offset;
        gameObject.SetActive(true);

        // 找 Slider 组件
        _slider = GetComponentInChildren<Slider>();


        if (_slider != null)
        {
            _slider.minValue = 0f;
            _slider.maxValue = _enemy.MaxHealth;
            _slider.value = _enemy.MaxHealth;
            // 隐藏滑块手柄（血条不需要拖拽手柄）
            if (_slider.handleRect != null)
                _slider.handleRect.gameObject.SetActive(false);
        }

        enemy.OnHealthChanged += UpdateBar;
        enemy.OnDeath += HideBar;
    }

    private void Awake()
    {
        var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler != null)
            scaler.enabled = false;

        var raycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
        if (raycaster != null)
            raycaster.enabled = false;

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
        }

        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(200f, 30f);
        }

        transform.localScale = Vector3.one * 0.05f;

        gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (_enemy == null) return;
        
        transform.position = _enemy.transform.position + _offset;
        
        // 横向朝向摄像机，让血条始终对着玩家
        if (Camera.main != null)
        {
            Vector3 dir = Camera.main.transform.position - transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    private void UpdateBar(float currentHealth)
    {
        if (_enemy == null || _slider == null) return;

        Debug.Log($"[EnemyHealthBar] UpdateBar: health={currentHealth}");

        if (_animCoroutine != null)
            StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimateSlider(currentHealth));
    }

    private System.Collections.IEnumerator AnimateSlider(float target)
    {
        float start = _slider.value;
        float elapsed = 0f;

        while (elapsed < _animDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _animDuration;
            _slider.value = Mathf.Lerp(start, target, 1f - (1f - t) * (1f - t));
            yield return null;
        }

        _slider.value = target;
        _animCoroutine = null;
    }

    public void HideBar()
    {
        gameObject.SetActive(false);
    }

    public void Unbind()
    {
        if (_enemy == null) return;

        _enemy.OnHealthChanged -= UpdateBar;
        _enemy.OnDeath -= HideBar;
        _enemy = null;

        if (_animCoroutine != null)
        {
            StopCoroutine(_animCoroutine);
            _animCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        Unbind();
    }
}