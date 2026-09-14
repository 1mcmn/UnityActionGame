using UnityEditor;
using UnityEngine;

// 标记：这是 PlayerStats 组件的自定义编辑器，并且支持多选编辑
[CustomEditor(typeof(PlayerStats)), CanEditMultipleObjects]
public class PlayerStatsEditor : Editor
{
    // ========== 声明多个 SerializedProperty（字段操作手柄）==========
    // 每个 SerializedProperty 对应目标对象里的一个序列化字段
    private SerializedProperty _levelProp;
    private SerializedProperty _healthProp;
    private SerializedProperty _invincibleProp;
    private SerializedProperty _speedProp;

    private void OnEnable()
    {
        // ===================== 核心关系 1 =====================
        // serializedObject 是基类 Editor 自带的属性
        // 它 = 把当前选中的所有 PlayerStats 组件，包装成一个统一的「编辑缓冲区」
        // 你不直接碰原对象，所有修改都先落在这个缓冲区里
        // =====================================================

        // ===================== 核心关系 2 =====================
        // FindProperty("字段名")：从缓冲区里，找到对应字段的「操作手柄」
        // 注意：必须写字段名（level），不能写属性名（CurrentLevel）
        // =====================================================
        _levelProp = serializedObject.FindProperty("level");
        _healthProp = serializedObject.FindProperty("health");
        _invincibleProp = serializedObject.FindProperty("isInvincible");
        _speedProp = serializedObject.FindProperty("moveSpeed");
    }

    public override void OnInspectorGUI()
    {
        // ===================== 核心步骤 1：同步数据 =====================
        // 把目标对象的最新真实数据，同步到序列化缓冲区里
        // 如果用 base.OnInspectorGUI()，它内部会自动调用这行
        // 完全自己画界面时，必须手动写
        serializedObject.Update();

        // ========== 用法1：用官方控件自动绘制字段 ==========
        // PropertyField 会根据字段类型，自动生成输入框、勾选框、向量框
        EditorGUILayout.PropertyField(_levelProp);
        EditorGUILayout.PropertyField(_healthProp);
        EditorGUILayout.PropertyField(_invincibleProp);
        EditorGUILayout.PropertyField(_speedProp);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("=== 功能测试区 ===", EditorStyles.boldLabel);

        // ========== 用法2：代码修改字段值（正规写法） ==========
        if (GUILayout.Button("【正规写法】一键满血"))
        {
            // 只修改缓冲区里的值，原对象此时还没变
            _healthProp.floatValue = 100f;

            // ===================== 核心步骤 2：提交修改 =====================
            // 把缓冲区的修改真正写回原对象
            // 这一步会自动完成：注册撤销、标记场景修改、同步多选对象、处理预制体重载
            serializedObject.ApplyModifiedProperties();
        }

        if (GUILayout.Button("【正规写法】等级+1 (支持多选)"))
        {
            _levelProp.intValue += 1;
            serializedObject.ApplyModifiedProperties();
        }

        EditorGUILayout.Space(5);

        // ========== 用法3：直接改原对象（反面教材） ==========
        if (GUILayout.Button("【反面教材】直接加10血 (无法撤销)"))
        {
            // 直接操作原对象本身，绕开了序列化缓冲区
            var player = (PlayerStats)target;
            player.health += 10;

            // 缺点：
            // 1. Ctrl+Z 撤销不了
            // 2. 场景左上角不会出现 * 号（Unity不知道你改了）
            // 3. 选多个对象时，只改第一个
            // 4. 预制体不会记录属性重载
        }
    }
}