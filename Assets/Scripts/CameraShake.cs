using System.Collections;
using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [SerializeField] private float defaultDuration = 0.15f;
    [SerializeField] private float defaultMagnitude = 0.2f;

    private void Awake()
    {
        // 单例，方便玩家脚本直接调用
        if (Instance == null) Instance = this;
    }

    // 提供给外部调用的震动接口
    public void Shake() => Shake(defaultDuration, defaultMagnitude);

    public void Shake(float duration, float magnitude)
    {
        StartCoroutine(ShakeCoroutine(duration, magnitude));
    }

    private IEnumerator ShakeCoroutine(float duration, float magnitude)
    {
        Vector3 originalPos = transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // 用 PerlinNoise 产生更柔和的随机偏移
            float x = (Mathf.PerlinNoise(0f, Time.time * 20f) - 0.5f) * 2f * magnitude;
            float y = (Mathf.PerlinNoise(1f, Time.time * 20f) - 0.5f) * 2f * magnitude;

            transform.localPosition = new Vector3(originalPos.x + x, originalPos.y + y, originalPos.z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalPos; // 恢复原位置
    }
}