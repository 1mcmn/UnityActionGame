using UnityEngine;

/// <summary>
/// 全局日志开关。Enabled 为 false 时屏蔽常规 Log（Warning/Error 始终输出），
/// 用于抑制 EnemyAI 等高频状态日志刷屏。
/// </summary>
public static class GameLog
{
    public static bool Enabled = false;

    public static void Log(object message)
    {
        if (Enabled) Debug.Log(message);
    }

    public static void Warning(object message) => Debug.LogWarning(message);
    public static void Error(object message) => Debug.LogError(message);
}
