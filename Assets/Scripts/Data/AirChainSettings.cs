using UnityEngine;

/// <summary>有限浮空链；动作仍用 ComboData，在正式连招窗口中编辑。</summary>
[CreateAssetMenu(menuName = "Combat/AirChainSettings", fileName = "AirChainSettings")]
public class AirChainSettings : ScriptableObject
{
    public ComboData launcherCombo;
    public ComboData airStrikeCombo;
    [Min(.1f)] public float targetRange = 2.8f;
    [Min(.05f)] public float chaseDuration = .24f;
    [Min(.1f)] public float chaseSpeed = 12f;
    [Min(.1f)] public float strikeDistance = 1.15f;
    [Min(.1f)] public float gravity = 18f;
    [Min(.1f)] public float maximumAirDuration = 2.5f;
    [Min(0f)] public float launcherCooldown = 1.5f;
    [Min(0f)] public float poiseMultiplier = 1.5f;
    public System.Collections.Generic.List<string> Validate()
    {
        var errors = new System.Collections.Generic.List<string>();
        foreach (float value in new[] { targetRange, chaseDuration, chaseSpeed, strikeDistance, gravity, maximumAirDuration })
            if (!ComboData.Finite(value) || value <= 0) errors.Add("范围、追击与落地参数必须为正有限数。");
        if (!ComboData.Finite(launcherCooldown) || launcherCooldown < 0 || !ComboData.Finite(poiseMultiplier) || poiseMultiplier < 0)
            errors.Add("冷却和架势倍率非法。");
        return errors;
    }
}
