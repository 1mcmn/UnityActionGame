using UnityEngine;
using UnityEditor;

public class EditorTool_EnemySpawner : EditorWindow
{
    [SerializeField] private GameObject enemyPrefab;
    private int spawnCount = 10;
    private float radius = 5f;

    [MenuItem("Tools/一键生成敌人")]
    public static void ShowWindow()
    {
        GetWindow<EditorTool_EnemySpawner>("敌人生成器");
    }

    private void OnGUI()
    {
        GUILayout.Label("配置生成参数", EditorStyles.boldLabel);
        enemyPrefab = (GameObject)EditorGUILayout.ObjectField("敌人预制体", enemyPrefab, typeof(GameObject), false);
        spawnCount = EditorGUILayout.IntField("生成数量", spawnCount);
        radius = EditorGUILayout.FloatField("生成范围半径", radius);

        if (GUILayout.Button("执行生成"))
        {
            SpawnEnemies();
        }
    }

    private void SpawnEnemies()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("请先指定敌人预制体！");
            return;
        }

        for (int i = 0; i < spawnCount; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * radius;
            Vector3 spawnPos = new Vector3(randomCircle.x, 0, randomCircle.y);
            GameObject newEnemy = PrefabUtility.InstantiatePrefab(enemyPrefab) as GameObject;
            newEnemy.transform.position = spawnPos;
            Undo.RegisterCreatedObjectUndo(newEnemy, "Spawn Enemies");
        }
        Debug.Log($"成功在范围半径 {radius} 内生成 {spawnCount} 个敌人！");
    }
}