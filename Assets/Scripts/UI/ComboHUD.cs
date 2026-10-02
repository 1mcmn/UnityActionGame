using UnityEngine;

/// <summary>演示用连招提示。段数表示攻击序列，不冒充连续命中次数。</summary>
public class ComboHUD : MonoBehaviour
{
    [SerializeField] private PlayerCombat player;
    [SerializeField] private bool showHelp = true;
    private GUIStyle _label, _title;

    private void Awake()
    {
        if (player == null) player = GetComponent<PlayerCombat>();
        if (player == null) player = FindObjectOfType<PlayerCombat>(true);
    }

    private void OnGUI()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return;
        if (_label == null)
        {
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _title = new GUIStyle(_label) { fontSize = 19, fontStyle = FontStyle.Bold };
        }
        float scale = Mathf.Clamp(Screen.height / 900f, .75f, 1.4f);
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUILayout.BeginArea(new Rect(18, 18, 340, showHelp ? 195 : 135), GUI.skin.box);
        GUILayout.Label(player.IsComboActive ? $"连招：第 {player.CurrentStepIndex + 1} / {player.StepCount} 段" : "连招：待起手", _title);
        GUILayout.Label(player.UsesConfiguredCombo ? $"配置：{player.Data.displayName} · 已加载 v{player.LoadedRevision}" : "配置：旧参数回退（需配置连招资产）", _label);
        GUILayout.Label(player.ComboInputOpen ? "接段窗口开启 · 点击左键" : (player.HitWindowOpen ? "攻击判定开启" : "接段窗口关闭"), _label);
        GUILayout.Label(player.ReloadStatus, _label);
        if (showHelp) GUILayout.Label("WASD 移动  Shift 点按闪避/按住跑\n空格跳跃  左键攻击  右键弹反/格挡\nF5 重载已保存配置  F8 性能记录", _label);
        GUILayout.EndArea();
        GUI.matrix = old;
    }
}
