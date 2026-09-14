using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 屏幕空间（Screen Space Overlay）的敌人血条 + 僵直条。
/// 放在 HUDCanvas 下，锚定屏幕底部中央，用于显示"当前目标敌人"的状态。
/// 通过 Enemy 的事件驱动，不跟随敌人移动。
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    [Header("UI 引用")]
    [SerializeField] private Slider _healthSlider;
    [SerializeField] private Slider _poiseSlider;
    [SerializeField] private GameObject _root;

    private Enemy _enemy;

    /// <summary>绑定要显示的敌人</summary>
    public void Bind(Enemy enemy)
    {
        Unbind();

        _enemy = enemy;
        if (_enemy == null) return;

        SetupSlider(_healthSlider, _enemy.MaxHealth, _enemy.CurrentHealth);
        SetupSlider(_poiseSlider, _enemy.MaxPoise, _enemy.CurrentPoise);

        _enemy.OnHealthChanged += OnHealthChanged;
        _enemy.OnPoiseChanged += OnPoiseChanged;
        _enemy.OnDeath += OnDeath;

        if (_root != null) _root.SetActive(true);
    }

    public void Unbind()
    {
        if (_enemy != null)
        {
            _enemy.OnHealthChanged -= OnHealthChanged;
            _enemy.OnPoiseChanged -= OnPoiseChanged;
            _enemy.OnDeath -= OnDeath;
            _enemy = null;
        }

        if (_root != null) _root.SetActive(false);
    }

    private void OnHealthChanged(float value)
    {
        if (_healthSlider != null) _healthSlider.value = value;
    }

    private void OnPoiseChanged(float value)
    {
        if (_poiseSlider != null) _poiseSlider.value = value;
    }

    private void OnDeath()
    {
        Unbind();
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void SetupSlider(Slider slider, float max, float current)
    {
        if (slider == null) return;
        slider.minValue = 0f;
        slider.maxValue = max;
        slider.value = current;
    }
}
