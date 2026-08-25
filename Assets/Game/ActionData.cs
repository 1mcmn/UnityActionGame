using System.Collections.Generic;
using UnityEngine;

// 用于标记触发的是什么类型的事件
public enum ActionEventType
{
    PlayAudio,         // 播放音效
    SpawnVFX,          // 生成特效
    EnableHitbox,      // 开启攻击判定框
    DisableHitbox,     // 关闭攻击判定框
    OpenInputWindow    // 开启连招预输入窗口（替代你原本的 comboWindow 逻辑）
}

// 单个时间线事件的数据结构
[System.Serializable]
public struct ActionEvent
{
    [Tooltip("0 到 1 的百分比时间，0是开始，1是结束")]
    public float triggerNormalizedTime; // 用归一化时间，无需乘动画长度，彻底避开回绕坑！

    public ActionEventType eventType;

    // 根据类型选填：如果是音效就拖音频，是特效就拖预制体
    public AudioClip audioClip;
    public GameObject vfxPrefab;

    [Tooltip("攻击判定框持续的时间（秒）")]
    public float hitboxDuration;
}

[CreateAssetMenu(fileName = "NewActionData", menuName = "动作系统/ActionData")]
public class ActionData : ScriptableObject
{
    // 你的动画剪辑（做配置时拖入）
    public AnimationClip targetAnimation;

    // 时间线上的所有事件（完全复刻你阶段0的List结构）
    public List<ActionEvent> actionEvents = new List<ActionEvent>();
}