using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private Slider healthSlider;               // 放血条滑块的槽
    [SerializeField] private ThirdPersonController playerController; // 放玩家脚本的槽

    private void Start()
    {
        Refresh();
    }

    /// <summary>读取玩家当前血量并刷新血条（游戏开始时也会被 GameManager 调用）</summary>
    public void Refresh()
    {
        if (playerController == null || healthSlider == null) return;
        healthSlider.maxValue = playerController.MaxHealth;
        healthSlider.value = playerController.CurrentHealth;
    }

    // 当脚本激活时，订阅受伤事件
    private void OnEnable()
    {
        PlayerCombat.OnPlayerDamaged += UpdateHealthUI;
    }

    // 当脚本禁用或物体销毁时，必须取消订阅！（防止内存泄漏）
    private void OnDisable()
    {
        PlayerCombat.OnPlayerDamaged -= UpdateHealthUI;
    }

    // 接收到广播后执行的函数
    private void UpdateHealthUI(float newHealth)
    {
        if (healthSlider != null)
        {
            healthSlider.value = newHealth;
        }
    }
}
