using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 音效管理器（单窗口三页签）—— 把"找素材 → 裁切 → 入库 → 编辑"串成一条流水线：
///   ① 资源浏览器：搜索/试听项目里所有 AudioClip，一键"加入条目表"
///   ② 批量导入  ：选文件夹 + 命名规则（正则/手工）→ 预览对照表 → 确认导入
///   ③ 条目表    ：编辑 soundID/clip、试听、查重、排序、批量删除、批量改 ID
///
/// 数据落在 SoundLibrary 资产上（soundID ↔ AudioClip），游戏运行时由 SoundManager 读取。
/// 命名规则默认按项目现有素材配好（FOOT 文件夹），导入完不用改代码就能响。
/// </summary>
public class 音效管理器 : EditorWindow
{
    private enum Page { Browser, Import, Entries }

    private const string DefaultLibraryPath = "Assets/Scripts/Audio/PlayerSounds.asset";
    private const string DefaultImportFolder = "Assets/Imports/FOOT";

    // ── 共用 ──
    private Page page = Page.Browser;
    private SoundLibrary library;
    private SerializedObject libSo;
    private SerializedProperty soundsProp;
    private AudioSource previewSource;

    // ── ① 资源浏览器 ──
    private string searchKeyword = "";
    private string[] audioGuids;
    private Vector2 browserScroll;

    // ── ② 批量导入 ──
    [Serializable]
    private class ImportRule
    {
        public string pattern;       // 正则（针对"不含扩展名的文件名"）
        public string replacement;   // 替换串，可用 $1 引用捕获组
        public ImportRule(string p, string r) { pattern = p; replacement = r; }
    }

    private class ImportRow
    {
        public string fileName;
        public string path;
        public AudioClip clip;
        public string soundId;
        public bool selected = true;
        public string status = "";
        public bool existsInLibrary;
    }

    private string importFolder = DefaultImportFolder;
    private readonly List<ImportRule> rules = new List<ImportRule>
    {
        // ⚠ 顺序有意义：先匹配更具体的（先"受击音"，再"攻击"）
        // 受击音用 hit_0X：技能系统的 HitFeedbackComponent 默认前缀就是 "hit_"
        new ImportRule(@"^安比攻击受击音(\d+)$", "hit_0$1"),
        new ImportRule(@"^安比攻击(\d+)$",       "atk0$1_swing"),
        new ImportRule(@"^脚步声(\d+)$",         "foot_step_0$1"),
        new ImportRule(@"^脚步声$",              "foot_step_00"),
        new ImportRule(@"^收脚音(\d+)$",         "foot_stop_0$1"),
    };
    private readonly List<ImportRow> importRows = new List<ImportRow>();
    private Vector2 importScroll;
    private bool importSkipExisting = true;
    private bool importApplySettings = true;

    // ── ③ 条目表 ──
    private Vector2 entryScroll;
    private readonly HashSet<int> selectedEntries = new HashSet<int>();
    private string batchFind = "";
    private string batchReplace = "";
    private string batchPrefix = "";
    private string batchSuffix = "";
    private int pendingDeleteIndex = -1;
    private bool pendingDeleteSelected = false;
    private int pendingCleanEmpty = 0;          // 0=不清理 1=清理空ID 2=清理空ID或空clip

    // ════════════ 生命周期 ════════════

    [MenuItem("Tools/音效管理器")]
    [MenuItem("自定义菜单/音效管理器")]
    private static void Open()
    {
        var w = GetWindow<音效管理器>("音效管理器");
        w.minSize = new Vector2(920, 540);
    }

    private void OnEnable()
    {
        if (library == null)
            library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(DefaultLibraryPath);
        RebindLibrary();
    }

    private void OnDisable()
    {
        if (previewSource != null)
        {
            DestroyImmediate(previewSource.gameObject);
            previewSource = null;
        }
    }

    private void RebindLibrary()
    {
        selectedEntries.Clear();
        if (library == null)
        {
            libSo = null;
            soundsProp = null;
            return;
        }
        libSo = new SerializedObject(library);
        soundsProp = libSo.FindProperty("sounds");
    }

    // ════════════ 主界面 ════════════

    private void OnGUI()
    {
        DrawHeader();

        if (library == null || soundsProp == null)
        {
            EditorGUILayout.HelpBox(
                $"请先指定一个 SoundLibrary 资产（默认会去找 {DefaultLibraryPath}）。",
                MessageType.Warning);
            return;
        }

        string[] tabs = { "① 资源浏览器", "② 批量导入", "③ 条目表" };
        page = (Page)GUILayout.Toolbar((int)page, tabs, GUILayout.Height(24));
        EditorGUILayout.Space(4);

        switch (page)
        {
            case Page.Browser: DrawBrowserTab(); break;
            case Page.Import: DrawImportTab(); break;
            case Page.Entries: DrawEntriesTab(); break;
        }
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal();
        var newLib = (SoundLibrary)EditorGUILayout.ObjectField("音效库", library, typeof(SoundLibrary), false);
        if (newLib != library)
        {
            library = newLib;
            RebindLibrary();
        }
        if (GUILayout.Button("打开波形编辑器（裁剪/导出 WAV）", EditorStyles.miniButton, GUILayout.Width(230)))
            GetWindow<音频资源浏览器>(false, "音频资源浏览器");

        GUILayout.FlexibleSpace();
        if (soundsProp != null)
            EditorGUILayout.LabelField($"条目数：{soundsProp.arraySize}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ════════════ ① 资源浏览器 ════════════

    private void DrawBrowserTab()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("刷新列表", GUILayout.Width(80))) audioGuids = null;
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("提示：点「+入库」把素材加进条目表，ID 按命名规则自动生成", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        searchKeyword = EditorGUILayout.TextField("搜索", searchKeyword);

        if (audioGuids == null) audioGuids = AssetDatabase.FindAssets("t:AudioClip");

        browserScroll = EditorGUILayout.BeginScrollView(browserScroll);
        int shown = 0;
        foreach (var guid in audioGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(searchKeyword) &&
                fileName.IndexOf(searchKeyword, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;
            shown++;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(fileName, GUILayout.Width(260));
            EditorGUILayout.LabelField($"{clip.length:0.00}s", GUILayout.Width(60));
            if (GUILayout.Button("▶试听", EditorStyles.miniButton, GUILayout.Width(60))) PlayPreview(clip);
            if (GUILayout.Button("+入库", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                string id = MakeId(fileName);
                AddOrUpdateEntry(id, clip);
            }
            EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.LabelField($"共显示 {shown} / {audioGuids.Length} 个音频", EditorStyles.miniLabel);
    }

    // ════════════ ② 批量导入 ════════════

    private void DrawImportTab()
    {
        EditorGUILayout.HelpBox(
            "流程：选文件夹 → 配命名规则 → 扫描生成对照表 → 逐条检查/改名 → 勾选后导入。\n" +
            "规则按顺序取第一条能匹配上的；用 $1 $2 引用正则捕获组。",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        importFolder = EditorGUILayout.TextField("素材文件夹", importFolder);
        if (GUILayout.Button("选文件夹", GUILayout.Width(80)))
        {
            string start = string.IsNullOrEmpty(importFolder) || !Directory.Exists(importFolder)
                ? Application.dataPath : importFolder;
            string picked = EditorUtility.OpenFolderPanel("选择音频素材文件夹", start, "");
            if (!string.IsNullOrEmpty(picked))
            {
                int idx = picked.Replace('\\', '/').IndexOf("Assets", StringComparison.Ordinal);
                importFolder = idx >= 0 ? picked.Replace('\\', '/').Substring(idx) : picked;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("命名规则（正则 → 替换）", EditorStyles.boldLabel);
        for (int i = 0; i < rules.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{i + 1}.", GUILayout.Width(20));
            rules[i].pattern = EditorGUILayout.TextField(rules[i].pattern, GUILayout.Width(240));
            EditorGUILayout.LabelField("→", GUILayout.Width(18));
            rules[i].replacement = EditorGUILayout.TextField(rules[i].replacement, GUILayout.Width(200));
            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24)))
            {
                rules.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 加一条规则", EditorStyles.miniButton, GUILayout.Width(110)))
            rules.Add(new ImportRule("", ""));
        if (GUILayout.Button("恢复默认规则（FOOT 素材）", EditorStyles.miniButton, GUILayout.Width(200)))
        {
            rules.Clear();
            rules.Add(new ImportRule(@"^安比攻击受击音(\d+)$", "hit_0$1"));
            rules.Add(new ImportRule(@"^安比攻击(\d+)$", "atk0$1_swing"));
            rules.Add(new ImportRule(@"^脚步声(\d+)$", "foot_step_0$1"));
            rules.Add(new ImportRule(@"^脚步声$", "foot_step_00"));
            rules.Add(new ImportRule(@"^收脚音(\d+)$", "foot_stop_0$1"));
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        if (GUILayout.Button("扫描文件夹 → 生成对照表", GUILayout.Height(24))) ScanImportFolder();

        if (importRows.Count == 0) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        importSkipExisting = EditorGUILayout.ToggleLeft("跳过库中已存在的 ID", importSkipExisting, GUILayout.Width(180));
        importApplySettings = EditorGUILayout.ToggleLeft("统一音频导入设置（短音效）", importApplySettings, GUILayout.Width(220));
        if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(50)))
            importRows.ForEach(r => r.selected = true);
        if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(60)))
            importRows.ForEach(r => r.selected = false);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("勾选 | 文件名 | 生成 ID（可改） | 状态", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        importScroll = EditorGUILayout.BeginScrollView(importScroll, GUILayout.MinHeight(200));
        foreach (var row in importRows)
        {
            EditorGUILayout.BeginHorizontal();
            row.selected = EditorGUILayout.Toggle(row.selected, GUILayout.Width(20));
            EditorGUILayout.LabelField(row.fileName, GUILayout.Width(200));
            row.soundId = EditorGUILayout.TextField(row.soundId, GUILayout.Width(200));

            var prev = GUI.color;
            if (row.status == "ID为空" || row.status == "重复") GUI.color = new Color(1f, 0.5f, 0.5f);
            else if (row.existsInLibrary) GUI.color = new Color(1f, 0.9f, 0.5f);
            EditorGUILayout.LabelField(row.status, GUILayout.Width(150));
            GUI.color = prev;

            if (GUILayout.Button("▶", EditorStyles.miniButton, GUILayout.Width(24))) PlayPreview(row.clip);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4);
        if (GUILayout.Button($"导入勾选的条目（{CountSelectedImportRows()} 条）", GUILayout.Height(28)))
            DoImport();
    }

    private int CountSelectedImportRows()
    {
        int n = 0;
        foreach (var r in importRows) if (r.selected) n++;
        return n;
    }

    private void ScanImportFolder()
    {
        importRows.Clear();
        if (string.IsNullOrEmpty(importFolder) || !AssetDatabase.IsValidFolder(importFolder))
        {
            EditorUtility.DisplayDialog("扫描失败", $"文件夹不存在：{importFolder}", "知道了");
            return;
        }

        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { importFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            importRows.Add(new ImportRow
            {
                fileName = fileName,
                path = path,
                clip = clip,
                soundId = MakeId(fileName),
            });
        }

        importRows.Sort((a, b) => string.CompareOrdinal(a.fileName, b.fileName));
        MarkImportRowStatus();
        Debug.Log($"[音效管理器] 扫描到 {importRows.Count} 个音频（{importFolder}）");
    }

    private void MarkImportRowStatus()
    {
        var idCount = new Dictionary<string, int>();
        foreach (var r in importRows)
        {
            string id = r.soundId ?? "";
            idCount[id] = idCount.TryGetValue(id, out var c) ? c + 1 : 1;
        }

        foreach (var r in importRows)
        {
            r.existsInLibrary = IndexOfSoundId(r.soundId) >= 0;
            if (string.IsNullOrEmpty(r.soundId)) r.status = "ID为空";
            else if (idCount[r.soundId] > 1) r.status = "重复";
            else if (r.existsInLibrary) r.status = "已存在（覆盖 clip）";
            else r.status = "新增";
        }
    }

    private void DoImport()
    {
        libSo.Update();

        int added = 0, updated = 0, skipped = 0;
        foreach (var row in importRows)
        {
            if (!row.selected) continue;
            if (string.IsNullOrEmpty(row.soundId)) { skipped++; continue; }
            if (importSkipExisting && row.existsInLibrary && UpdatedOnce(row.soundId)) { skipped++; continue; }

            int idx = IndexOfSoundId(row.soundId);
            if (idx >= 0)
            {
                var el = soundsProp.GetArrayElementAtIndex(idx);
                el.FindPropertyRelative("clip").objectReferenceValue = row.clip;
                updated++;
            }
            else
            {
                int newIndex = soundsProp.arraySize;
                soundsProp.arraySize = newIndex + 1;
                var el = soundsProp.GetArrayElementAtIndex(newIndex);
                el.FindPropertyRelative("soundID").stringValue = row.soundId;
                el.FindPropertyRelative("clip").objectReferenceValue = row.clip;
                added++;
            }
        }

        libSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();

        if (importApplySettings)
        {
            foreach (var row in importRows)
            {
                if (!row.selected || string.IsNullOrEmpty(row.soundId)) continue;
                ApplyAudioImportSettings(row.path);
            }
        }

        MarkImportRowStatus();
        SoundLibraryDirtyHint();
        Debug.Log($"[音效管理器] 导入完成：新增 {added}，更新 {updated}，跳过 {skipped}");
    }

    // 同一批次里同 ID 只更新一次（重复 ID 行直接跳过后面的）
    private readonly HashSet<string> updatedOnce = new HashSet<string>();
    private bool UpdatedOnce(string id)
    {
        if (updatedOnce.Contains(id)) return true;
        updatedOnce.Add(id);
        return false;
    }

    // ════════════ ③ 条目表 ════════════

    private void DrawEntriesTab()
    {
        libSo.Update();

        var dupIds = FindDuplicateIds();

        // ── 工具条 ──
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(50)))
            for (int i = 0; i < soundsProp.arraySize; i++) selectedEntries.Add(i);
        if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(60))) selectedEntries.Clear();
        EditorGUILayout.LabelField($"已选 {selectedEntries.Count}", EditorStyles.miniLabel, GUILayout.Width(70));

        GUI.enabled = selectedEntries.Count > 0;
        if (GUILayout.Button("删除选中", EditorStyles.miniButton, GUILayout.Width(80)))
        {
            if (EditorUtility.DisplayDialog("批量删除",
                    $"确定从库里删除选中的 {selectedEntries.Count} 条？\n（只删库里的引用，不会删音频文件）", "删除", "取消"))
                pendingDeleteSelected = true;
        }
        GUI.enabled = true;

        if (GUILayout.Button("按 ID 排序", EditorStyles.miniButton, GUILayout.Width(90)))
        {
            SortById();
            libSo.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }
        if (GUILayout.Button("清理空条目", EditorStyles.miniButton, GUILayout.Width(90))) pendingCleanEmpty = 2;
        GUILayout.FlexibleSpace();
        if (dupIds.Count > 0)
            EditorGUILayout.LabelField($"⚠ 重复 ID：{string.Join("、", dupIds)}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        // ── 批量改 ID ──
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("批量改 ID：", GUILayout.Width(70));
        batchFind = EditorGUILayout.TextField(batchFind, GUILayout.Width(110));
        EditorGUILayout.LabelField("→", GUILayout.Width(18));
        batchReplace = EditorGUILayout.TextField(batchReplace, GUILayout.Width(110));
        GUI.enabled = selectedEntries.Count > 0 && !string.IsNullOrEmpty(batchFind);
        if (GUILayout.Button("替换", EditorStyles.miniButton, GUILayout.Width(50)))
        {
            BatchTransformId((id) => id.Replace(batchFind, batchReplace));
            libSo.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }
        GUI.enabled = selectedEntries.Count > 0;

        EditorGUILayout.LabelField("加前缀", GUILayout.Width(50));
        batchPrefix = EditorGUILayout.TextField(batchPrefix, GUILayout.Width(80));
        if (GUILayout.Button("加", EditorStyles.miniButton, GUILayout.Width(30)) && !string.IsNullOrEmpty(batchPrefix))
        {
            BatchTransformId((id) => batchPrefix + id);
            libSo.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.LabelField("加后缀", GUILayout.Width(50));
        batchSuffix = EditorGUILayout.TextField(batchSuffix, GUILayout.Width(80));
        if (GUILayout.Button("加", EditorStyles.miniButton, GUILayout.Width(30)) && !string.IsNullOrEmpty(batchSuffix))
        {
            BatchTransformId((id) => id + batchSuffix);
            libSo.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // ── 表头 ──
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("#", EditorStyles.miniLabel, GUILayout.Width(30));
        EditorGUILayout.LabelField("soundID", EditorStyles.miniLabel, GUILayout.Width(210));
        EditorGUILayout.LabelField("AudioClip", EditorStyles.miniLabel, GUILayout.Width(240));
        EditorGUILayout.LabelField("时长", EditorStyles.miniLabel, GUILayout.Width(60));
        EditorGUILayout.LabelField("试听", EditorStyles.miniLabel, GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();

        // ── 行 ──
        entryScroll = EditorGUILayout.BeginScrollView(entryScroll);
        for (int i = 0; i < soundsProp.arraySize; i++)
        {
            var element = soundsProp.GetArrayElementAtIndex(i);
            var idProp = element.FindPropertyRelative("soundID");
            var clipProp = element.FindPropertyRelative("clip");
            var clip = clipProp.objectReferenceValue as AudioClip;

            EditorGUILayout.BeginHorizontal();

            bool sel = selectedEntries.Contains(i);
            bool newSel = EditorGUILayout.Toggle(sel, GUILayout.Width(20));
            if (newSel != sel) { if (newSel) selectedEntries.Add(i); else selectedEntries.Remove(i); }

            EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(28));

            var prevColor = GUI.color;
            bool dup = !string.IsNullOrEmpty(idProp.stringValue) && dupIds.Contains(idProp.stringValue);
            bool bad = string.IsNullOrEmpty(idProp.stringValue) || clip == null;
            if (dup) GUI.color = new Color(1f, 0.5f, 0.5f);
            else if (bad) GUI.color = new Color(1f, 0.85f, 0.5f);
            idProp.stringValue = EditorGUILayout.TextField(idProp.stringValue, GUILayout.Width(210));
            GUI.color = prevColor;

            clipProp.objectReferenceValue = EditorGUILayout.ObjectField(clipProp.objectReferenceValue, typeof(AudioClip), false, GUILayout.Width(240));
            EditorGUILayout.LabelField(clip != null ? $"{clip.length:0.00}s" : "—", GUILayout.Width(60));

            GUI.enabled = clip != null;
            if (GUILayout.Button("▶", EditorStyles.miniButton, GUILayout.Width(26))) PlayPreview(clip);
            GUI.enabled = true;

            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24))) pendingDeleteIndex = i;

            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        libSo.ApplyModifiedProperties();

        ApplyPendingEntryChanges();
    }

    private void ApplyPendingEntryChanges()
    {
        bool changed = false;

        if (pendingDeleteIndex >= 0 && pendingDeleteIndex < soundsProp.arraySize)
        {
            soundsProp.DeleteArrayElementAtIndex(pendingDeleteIndex);
            pendingDeleteIndex = -1;
            changed = true;
        }
        else pendingDeleteIndex = -1;

        if (pendingDeleteSelected)
        {
            pendingDeleteSelected = false;
            var idxs = new List<int>(selectedEntries);
            idxs.Sort();
            for (int k = idxs.Count - 1; k >= 0; k--)
                if (idxs[k] >= 0 && idxs[k] < soundsProp.arraySize)
                    soundsProp.DeleteArrayElementAtIndex(idxs[k]);
            selectedEntries.Clear();
            changed = true;
        }

        if (pendingCleanEmpty != 0)
        {
            int mode = pendingCleanEmpty;
            pendingCleanEmpty = 0;
            for (int i = soundsProp.arraySize - 1; i >= 0; i--)
            {
                var el = soundsProp.GetArrayElementAtIndex(i);
                var id = el.FindPropertyRelative("soundID");
                var clip = el.FindPropertyRelative("clip");
                bool empty = string.IsNullOrEmpty(id.stringValue) || (mode == 2 && clip.objectReferenceValue == null);
                if (empty) soundsProp.DeleteArrayElementAtIndex(i);
            }
            selectedEntries.Clear();
            changed = true;
        }

        if (!changed) return;

        libSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        SoundLibraryDirtyHint();
        GUIUtility.ExitGUI();
    }

    private void BatchTransformId(Func<string, string> transform)
    {
        foreach (int i in selectedEntries)
        {
            if (i < 0 || i >= soundsProp.arraySize) continue;
            var idProp = soundsProp.GetArrayElementAtIndex(i).FindPropertyRelative("soundID");
            idProp.stringValue = transform(idProp.stringValue ?? "");
        }
    }

    private void SortById()
    {
        // 插入排序 + MoveArrayElement（保住所有字段，不依赖字段个数）
        for (int i = 1; i < soundsProp.arraySize; i++)
        {
            int j = i;
            while (j > 0)
            {
                var prev = soundsProp.GetArrayElementAtIndex(j - 1).FindPropertyRelative("soundID").stringValue ?? "";
                var cur = soundsProp.GetArrayElementAtIndex(j).FindPropertyRelative("soundID").stringValue ?? "";
                if (string.CompareOrdinal(prev, cur) <= 0) break;
                soundsProp.MoveArrayElement(j, j - 1);
                j--;
            }
        }
        selectedEntries.Clear();
    }

    private List<string> FindDuplicateIds()
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        for (int i = 0; i < soundsProp.arraySize; i++)
        {
            var id = soundsProp.GetArrayElementAtIndex(i).FindPropertyRelative("soundID").stringValue;
            if (string.IsNullOrEmpty(id)) continue;
            if (!seen.Add(id) && !result.Contains(id)) result.Add(id);
        }
        return result;
    }

    // ════════════ 公共工具 ════════════

    private int IndexOfSoundId(string soundId)
    {
        if (string.IsNullOrEmpty(soundId)) return -1;
        for (int i = 0; i < soundsProp.arraySize; i++)
        {
            var id = soundsProp.GetArrayElementAtIndex(i).FindPropertyRelative("soundID").stringValue;
            if (id == soundId) return i;
        }
        return -1;
    }

    private void AddOrUpdateEntry(string soundId, AudioClip clip)
    {
        if (string.IsNullOrEmpty(soundId) || clip == null) return;

        libSo.Update();
        int idx = IndexOfSoundId(soundId);
        if (idx >= 0)
        {
            soundsProp.GetArrayElementAtIndex(idx).FindPropertyRelative("clip").objectReferenceValue = clip;
            Debug.Log($"[音效管理器] 已更新条目：{soundId}");
        }
        else
        {
            int n = soundsProp.arraySize;
            soundsProp.arraySize = n + 1;
            var el = soundsProp.GetArrayElementAtIndex(n);
            el.FindPropertyRelative("soundID").stringValue = soundId;
            el.FindPropertyRelative("clip").objectReferenceValue = clip;
            Debug.Log($"[音效管理器] 已新增条目：{soundId}");
        }
        libSo.ApplyModifiedProperties();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        SoundLibraryDirtyHint();
    }

    /// <summary>文件名 → soundID（按规则表第一条能匹配的）</summary>
    private string MakeId(string fileName)
    {
        foreach (var rule in rules)
        {
            if (string.IsNullOrEmpty(rule.pattern)) continue;
            try
            {
                if (Regex.IsMatch(fileName, rule.pattern))
                    return Regex.Replace(fileName, rule.pattern, rule.replacement ?? "");
            }
            catch (Exception)
            {
                // 规则写错了就跳过这条，不中断导入
            }
        }
        return fileName;   // 没规则命中就照抄（可能是中文，自己改）
    }

    private void PlayPreview(AudioClip clip)
    {
        if (clip == null) return;
        if (previewSource == null)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags("_SfxMgrPreview", HideFlags.HideAndDontSave);
            previewSource = go.AddComponent<AudioSource>();
        }
        previewSource.Stop();
        previewSource.clip = clip;
        previewSource.Play();
    }

    private static void ApplyAudioImportSettings(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;

        importer.loadInBackground = false;   // 短音效不需要后台加载

        var s = importer.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;   // 短音效：直接解到内存，避免首次播放延迟
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.7f;
        s.preloadAudioData = true;   // Unity 2022.3 起 preloadAudioData 从 AudioImporter 移到 SampleSettings
        importer.defaultSampleSettings = s;

        importer.SaveAndReimport();
    }

    /// <summary>提醒用户"改完库要让游戏里的 SoundManager 重新构建字典"</summary>
    private void SoundLibraryDirtyHint()
    {
        Debug.Log("[音效管理器] 库已修改。游戏里 SoundManager.BuildDictionary 在 Awake 时构建字典 —— " +
                  "编辑器里改了库，需要重新进 Play 模式（或右键 SoundManager → Rebuild Dictionary）才生效。");
    }
}
