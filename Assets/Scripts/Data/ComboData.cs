using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 连击数据资产。把攻击时长/半径/各段伤害从代码解耦，
/// 供 Inspector 直接配置，运行时读取。
/// </summary>
[CreateAssetMenu(fileName = "ComboData", menuName = "Combat/ComboData")]
public class ComboData : ScriptableObject
{
    [Header("逐段连招（为空时使用旧参数回退）")]
    public string displayName = "普通连招";
    [HideInInspector] public int schemaVersion = 1;
    [HideInInspector] public int revision;
    public List<ComboStep> steps = new List<ComboStep>();
    public bool HasSteps => steps != null && steps.Count > 0;

    /// <summary>编辑器与运行时共用校验；不修正或覆盖用户的草稿。</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (!HasSteps) { errors.Add("连招至少需要一段；正式验收请配置至少三段。"); return errors; }
        var ids = new HashSet<string>();
        for (int i = 0; i < steps.Count; i++)
        {
            string prefix = $"第{i + 1}段：";
            var s = steps[i];
            if (s == null) { errors.Add(prefix + "数据为空。"); continue; }
            if (string.IsNullOrWhiteSpace(s.stepId) || !ids.Add(s.stepId)) errors.Add(prefix + "内部标识为空或重复，请复制/新建该段。");
            if (s.animationClip == null) errors.Add(prefix + "未指定动画片段。");
            else
            {
                if (s.animationClip.legacy || s.animationClip.isLooping) errors.Add(prefix + "攻击动画须为非 Legacy、非循环片段。");
                float length = s.animationClip.length;
                if (!ValidWindow(s.hitStart, s.hitEnd, length)) errors.Add(prefix + "判定区间需满足 0 ≤ 开始 < 结束 ≤ 动画时长。");
                if (i < steps.Count - 1 && !ValidWindow(s.comboStart, s.comboEnd, length)) errors.Add(prefix + "接段区间需满足 0 ≤ 开始 < 结束 ≤ 动画时长。");
                if (s.allowCancel && !ValidWindow(s.cancelStart, s.cancelEnd, length)) errors.Add(prefix + "取消区间超出动画或起止无效。");
            }
            if (!Finite(s.damage) || s.damage < 0) errors.Add(prefix + "伤害必须为非负有限数。");
            if (!Finite(s.playbackSpeed) || s.playbackSpeed < ComboStep.MinPlaybackSpeed || s.playbackSpeed > ComboStep.MaxPlaybackSpeed)
                errors.Add(prefix + "播放倍速需在 0.1～5 之间。");
            if (!Finite(s.rootMotionScaleXZ) || s.rootMotionScaleXZ < 0) errors.Add(prefix + "位移倍率必须为非负有限数。");
            if (!Finite(s.hitRadius) || s.hitRadius <= 0) errors.Add(prefix + "判定半径必须大于0。");
            if (!Finite(s.hitOffset.x) || !Finite(s.hitOffset.y) || !Finite(s.hitOffset.z)) errors.Add(prefix + "判定偏移必须为有限数。");
            if (s.soundEvents == null) continue;
            foreach (var sound in s.soundEvents)
                if (sound == null || !Finite(sound.time) || sound.time < 0 || s.animationClip == null || sound.time >= s.animationClip.length || string.IsNullOrWhiteSpace(sound.soundId))
                    errors.Add(prefix + "音效事件需填写音效ID，时间须在 [0,动画时长) 内。");
        }
        return errors;
    }

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool ValidWindow(float start, float end, float length) => Finite(start) && Finite(end) && start >= 0 && start < end && end <= length;

    [Header("攻击参数")]
    public float attackDuration = 0.45f;    // 每段攻击锁定时间
    public float attackRadius   = 2.5f;     // 球形判定半径

    [Header("连击手感")]
    [Tooltip("连击窗口起点（动画进度比例）：动画播到此进度后才接受连击输入，窗口外点击直接忽视。0.35 = 播到 65% 后接受（A2 修改，防动画被切半截）")]
    [Range(0.1f, 0.5f)]
    public float comboWindowPercent = 0.35f;

    [Tooltip("残响窗口：攻击计时结束后、动画回 Idle 前的兜底时长（秒），让慢点击也能续招")]
    public float comboGraceWindow = 0.15f;

    [Tooltip("最大连击段索引（0 起；4 = 共 5 段），防止越界与触发器空转")]
    public int maxComboStep = 4;

    [Header("连击伤害（第 1~N 段）")]
    public float[] comboDamages = { 15f, 18f, 22f, 28f, 35f };
}

[Serializable]
public class ComboStep
{
    public const float MinPlaybackSpeed = .1f;
    public const float MaxPlaybackSpeed = 5f;
    [HideInInspector] public string stepId = Guid.NewGuid().ToString("N");
    public string displayName = "攻击段";
    public AnimationClip animationClip;
    [Tooltip("1 为原速，2 为两倍速。窗口和音效时间仍使用原动画时间轴秒数。")]
    [Range(MinPlaybackSpeed, MaxPlaybackSpeed)] public float playbackSpeed = 1f;
    public float PlaybackDuration => animationClip != null ? animationClip.length / Mathf.Max(MinPlaybackSpeed, playbackSpeed) : 0f;
    [Min(0)] public float damage = 15f;
    public float hitStart = 0.15f, hitEnd = 0.3f;
    public float comboStart = 0.35f, comboEnd = 0.65f;
    [Min(0)] public float rootMotionScaleXZ = 1f;
    [Min(0.01f)] public float hitRadius = 0.75f;
    public Vector3 hitOffset = new Vector3(0f, 1f, 1.2f);
    public bool allowCancel = true;
    public float cancelStart = 0.4f, cancelEnd = 0.7f;
    public List<ComboSoundEvent> soundEvents = new List<ComboSoundEvent>();

    public ComboStep Copy()
    {
        var copy = (ComboStep)MemberwiseClone();
        copy.soundEvents = new List<ComboSoundEvent>();
        if (soundEvents != null)
            foreach (var e in soundEvents)
                copy.soundEvents.Add(e == null ? null : new ComboSoundEvent { time = e.time, soundId = e.soundId });
        return copy;
    }

    public bool AcceptsCombo(float time) => time >= comboStart && time < comboEnd;
    public bool CanCancel(float time) => allowCancel && time >= cancelStart && time < cancelEnd;
}

[Serializable]
public class ComboSoundEvent
{
    public float time;
    public string soundId = "atk01_swing";
}
