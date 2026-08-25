using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class WeaponPoseTool
{
    const string PrefKey = "WeaponPose_";

    // ---------- 捕获 / 应用 ----------
    [MenuItem("Tools/Weapon/Capture Weapon Pose (Play Mode)", true)]
    static bool CanCapture()
    {
        return EditorApplication.isPlaying && Selection.activeGameObject != null;
    }

    [MenuItem("Tools/Weapon/Capture Weapon Pose (Play Mode)")]
    static void CapturePose()
    {
        var go = Selection.activeGameObject;
        var t = go.transform;
        var path = GetScenePath(t);
        var data = new PoseData
        {
            path = path,
            pos = t.localPosition,
            euler = t.localEulerAngles,
            scale = t.localScale
        };
        EditorPrefs.SetString(PrefKey + path, JsonUtility.ToJson(data));
        Debug.Log("[WeaponPose] 捕获 " + path + " pos=" + data.pos + " euler=" + data.euler + " scale=" + data.scale, go);
    }

    [MenuItem("Tools/Weapon/Apply Weapon Pose (Edit Mode)", true)]
    static bool CanApply()
    {
        return !EditorApplication.isPlaying && Selection.activeGameObject != null;
    }

    [MenuItem("Tools/Weapon/Apply Weapon Pose (Edit Mode)")]
    static void ApplyPose()
    {
        var go = Selection.activeGameObject;
        var t = go.transform;
        var path = GetScenePath(t);
        var json = EditorPrefs.GetString(PrefKey + path, null);
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[WeaponPose] 未捕获过该路径的姿势: " + path);
            return;
        }
        var data = JsonUtility.FromJson<PoseData>(json);
        Undo.RecordObject(t, "Apply Weapon Pose");
        t.localPosition = data.pos;
        t.localEulerAngles = data.euler;
        t.localScale = data.scale;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[WeaponPose] 已应用: " + path, go);
    }

    // ---------- 对齐右手 ----------
    [MenuItem("Tools/Weapon/Align Weapon to Right Hand (Play Mode)", true)]
    static bool CanAlign()
    {
        return EditorApplication.isPlaying && Selection.activeGameObject != null;
    }

    [MenuItem("Tools/Weapon/Align Weapon to Right Hand (Play Mode)")]
    static void AlignToRightHand()
    {
        var socket = Selection.activeGameObject.transform;
        var targetRight = FindChild(socket, "Target_Right");
        if (targetRight == null)
        {
            Debug.LogWarning("[WeaponPose] 在选中的物体下找不到 Target_Right");
            return;
        }

        var anim = socket.root.GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman)
        {
            Debug.LogWarning("[WeaponPose] 未找到 Humanoid Animator");
            return;
        }

        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null)
        {
            Debug.LogWarning("[WeaponPose] 未找到右手骨");
            return;
        }

        // 刚性对齐：绕 Target_Right 当前世界位置旋转 + 平移，完全不碰 scale，
        // 规避「骨骼链 Armature/Body=100 + socket=0.01」缩放导致的矩阵分解偏差
        Vector3 pivot = targetRight.position;
        Quaternion delta = hand.rotation * Quaternion.Inverse(targetRight.rotation);

        socket.position = delta * (socket.position - pivot) + pivot;
        socket.rotation = delta * socket.rotation;
        socket.position += (hand.position - targetRight.position);

        Debug.Log("[WeaponPose] 已把 " + socket.name + " 的 Target_Right 对齐到右手，localPos=" + socket.localPosition + " localEuler=" + socket.localEulerAngles, socket);
    }

    static string GetScenePath(Transform t)
    {
        string p = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var r = FindChild(c, name);
            if (r != null) return r;
        }
        return null;
    }

    [System.Serializable]
    class PoseData
    {
        public string path;
        public Vector3 pos;
        public Vector3 euler;
        public Vector3 scale;
    }
}
