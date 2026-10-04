using UnityEngine;

public class CombatCycleHUD : MonoBehaviour
{
    public PlayerAirCombat player;
    public GreatSwordEnemyBrain enemy;
    private GUIStyle _style;
    private void OnGUI()
    {
        if (BattleHUD.StyledActive && !BattleHUD.DebugVisible) return;
        if (player == null || !player.gameObject.activeInHierarchy || enemy == null || !enemy.gameObject.activeInHierarchy) return;
        if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        GUILayout.BeginArea(new Rect(Mathf.Max(365, Screen.width - 345), 18, 325, 145), GUI.skin.box);
        GUILayout.Label(enemy.ActionHint, _style);
        GUILayout.Label(player.CanFollow ? "E · 空中追击（一次）" : player.Status, _style);
        GUILayout.Label("架势损伤累积满 → 破防\nQ 挑飞 → E 追击 → 自动落地", _style);
        GUILayout.EndArea();
    }
}
