using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    // 👑 必须声明这两个变量，否则代码找不到它们
    [SerializeField] private Slider healthSlider;               // 放血条滑块的槽
    [SerializeField] private ThirdPersonController playerController; // 放玩家脚本的槽

    private void Start()
    {
        // 游戏刚开始时，初始化一下血条（读一次玩家的血量）
        if (playerController != null)
        {
            healthSlider.maxValue = playerController.CurrentHealth;
            healthSlider.value = playerController.CurrentHealth;
        }
    }

    // 当脚本激活时，订阅受伤事件
    private void OnEnable()
    {
        if (playerController != null)
        {
            ThirdPersonController.OnPlayerDamaged += UpdateHealthUI;
        }
    }

    // 当脚本禁用或物体销毁时，必须取消订阅！（防止内存泄漏）
    private void OnDisable()
    {
        if (playerController != null)
        {
            ThirdPersonController.OnPlayerDamaged -= UpdateHealthUI;
        }
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