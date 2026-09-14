using UnityEngine;
using SkillSystem;

public class TestSkillLauncher : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))   // 释放技能
        {
            Vector3 targetPos = transform.position + transform.forward * 2f;
            SkillManager.Instance.PlaySkill("player_uppercut", transform, targetPos);
        }
        if (Input.GetKeyDown(KeyCode.K))   // 打断技能
        {
            SkillManager.Instance.InterruptSkill("player_uppercut");
        }
    }
}