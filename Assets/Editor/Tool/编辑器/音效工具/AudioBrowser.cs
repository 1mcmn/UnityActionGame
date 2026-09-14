using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class 音频资源浏览器 : EditorWindow
{
    private Vector2 scrollPos;
    private string searchKeyword = "";
    private string selectedPath = "";
    private string[] audioGuids;

    // 播放状态相关
    private AudioSource previewSource;       // 临时播放器
    private bool isPlaying = false;          // 是否在播放
    private float playTime = 0f;             // 当前播放进度（秒）
    private float totalLength = 0f;          // 总长度（秒）

    // 波形数据相关
    private float[] waveformData;            // 存放音频采样数据
    private bool waveformLoaded = false;     // 是否已经加载过波形
    private AudioClip lastLoadedClip;        // 记录上一次加载波形的音频

    // 裁剪相关
    private float cropStart = 0f;
    private float cropEnd = 0f;

    private bool isDraggingCrop = false;        // 是否正在拖拽裁切选区
    private float cropStartNormalized = 0f;     // 裁切起始点（占波形宽度的 0~1 比例）
    private float cropEndNormalized = 0f;       // 裁切结束点
    private bool isDraggingPlayhead = false;    // 是否正在拖拽播放竖线

    [MenuItem("自定义菜单/音频资源浏览器")]
    static void 打开窗口()
    {
        窗口();
    }

    private static void 窗口()
    {
        GetWindow<音频资源浏览器>(false, "音频资源浏览器");
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        // 销毁临时播放器避免残留
        if (previewSource != null)
        {
            DestroyImmediate(previewSource.gameObject);
            previewSource = null;
        }
    }

    private void OnEditorUpdate()
    {
        if (isPlaying && previewSource != null && previewSource.isPlaying)
        {
            playTime = previewSource.time;
            Repaint(); // 强制界面刷新，让进度条动起来
        }
    }

    private void OnGUI()
    {
        // 1. 搜索框
        searchKeyword = EditorGUILayout.TextField("搜索音频", searchKeyword);

        // 2. 获取所有音频 GUID
        if (audioGuids == null || audioGuids.Length == 0)
        {
            audioGuids = AssetDatabase.FindAssets("t:AudioClip");
        }

        // 3. 开启左右分栏
        EditorGUILayout.BeginHorizontal();

        // --- 左侧列表区（70%） ---
        EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.7f));
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        for (int i = 0; i < audioGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(audioGuids[i]);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

            // 搜索过滤
            if (!string.IsNullOrEmpty(searchKeyword) && !fileName.ToLower().Contains(searchKeyword.ToLower()))
            {
                continue;
            }

            if (GUILayout.Button(fileName, GUILayout.Height(20)))
            {
                if (selectedPath != path)
                {
                    // 清理旧播放器
                    if (previewSource != null)
                    {
                        previewSource.Stop();
                        DestroyImmediate(previewSource.gameObject);
                        previewSource = null;
                    }
                    // 重置播放状态
                    isPlaying = false;
                    playTime = 0f;
                    // 更新选中路径
                    selectedPath = path;
                }
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // --- 右侧详情区（30%） ---
        EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.3f));

        if (!string.IsNullOrEmpty(selectedPath))
        {
            EditorGUILayout.LabelField("当前选中: " + selectedPath);

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(selectedPath);

            if (clip != null)
            {
                totalLength = clip.length;
                EditorGUILayout.ObjectField("音频文件", clip, typeof(AudioClip), false);
                EditorGUILayout.LabelField("时长: " + clip.length + "秒");

                // 进度条
                if (totalLength > 0)
                {
                    float newTime = EditorGUILayout.Slider("进度", playTime, 0f, totalLength);
                    if (newTime != playTime)
                    {
                        playTime = newTime;
                        if (previewSource != null) previewSource.time = playTime;
                    }
                    string formatTime(float t) => $"{Mathf.FloorToInt(t / 60):D2}:{Mathf.FloorToInt(t % 60):D2}";
                    EditorGUILayout.LabelField($"{formatTime(playTime)} / {formatTime(totalLength)}");
                }

                // ---------- 波形绘制 ----------
                if (lastLoadedClip != clip)
                {
                    waveformData = null;
                    waveformLoaded = false;
                    lastLoadedClip = clip;
                }

                if (!waveformLoaded && clip != null)
                {
                    int sampleCount = Mathf.Min(clip.samples, clip.frequency * 5);
                    if (sampleCount > 0)
                    {
                        waveformData = new float[sampleCount];
                        clip.GetData(waveformData, 0);
                        waveformLoaded = true;
                    }
                }

                Rect waveformRect = EditorGUILayout.GetControlRect(false, 120);
                EditorGUI.DrawRect(waveformRect, new Color(0.1f, 0.1f, 0.1f));

                if (waveformLoaded && waveformData != null && waveformData.Length > 0)
                {
                    // 1. 准备坐标点和波形绘制
                    List<Vector3> points = new List<Vector3>();
                    float rectWidth = waveformRect.width;
                    float rectHeight = waveformRect.height;
                    float halfHeight = rectHeight / 2f;
                    int step = Mathf.Max(1, waveformData.Length / (int)(rectWidth / 1.5f));
                    for (int i = 0; i < waveformData.Length; i += step)
                    {
                        float x = (float)i / waveformData.Length * rectWidth;
                        float y = halfHeight + (waveformData[i] * halfHeight * 0.8f);
                        points.Add(new Vector3(waveformRect.x + x, waveformRect.y + y, 0f));
                    }
                    Handles.BeginGUI();
                    Handles.color = Color.green;
                    Handles.DrawAAPolyLine(2f, points.ToArray());

                    // 2. 绘制“播放竖线”（进度线）
                    if (totalLength > 0)
                    {
                        float playheadX = (playTime / totalLength) * rectWidth + waveformRect.x;
                        Handles.color = Color.white;
                        Handles.DrawLine(new Vector3(playheadX, waveformRect.y, 0f), new Vector3(playheadX, waveformRect.y + rectHeight, 0f));
                    }

                    // 3. 绘制“裁剪选区”高亮（蓝框半透明）
                    if (isDraggingCrop)
                    {
                        float startX = Mathf.Min(cropStartNormalized, cropEndNormalized);
                        float endX = Mathf.Max(cropStartNormalized, cropEndNormalized);
                        Rect highlightRect = new Rect(
                            waveformRect.x + startX * rectWidth,
                            waveformRect.y,
                            (endX - startX) * rectWidth,
                            rectHeight
                        );
                        EditorGUI.DrawRect(highlightRect, new Color(0.2f, 0.8f, 1f, 0.3f));
                    }
                    Handles.EndGUI();

                    // 4. 交互事件处理（统一处理）
                    Event currentEvent = Event.current;
                    if (waveformRect.Contains(currentEvent.mousePosition))
                    {
                        // 【修复核心】把 mousePercent 提到整个 if 块的顶层，保证所有分支都能用
                        float mousePercent = 0f;
                        if (rectWidth > 0)
                        {
                            mousePercent = (currentEvent.mousePosition.x - waveformRect.x) / rectWidth;
                        }

                        // --- 鼠标按下 ---
                        if (currentEvent.type == EventType.MouseDown)
                        {
                            if (rectWidth > 0 && totalLength > 0)
                            {
                                float playheadPixel = (playTime / totalLength) * rectWidth + waveformRect.x;
                                if (Mathf.Abs(currentEvent.mousePosition.x - playheadPixel) < 10f)
                                {
                                    isDraggingPlayhead = true;
                                }
                                else
                                {
                                    isDraggingCrop = true;
                                    cropStartNormalized = mousePercent;
                                    cropEndNormalized = mousePercent;
                                }
                            }
                            currentEvent.Use();
                        }

                        // --- 鼠标拖拽 ---
                        if (currentEvent.type == EventType.MouseDrag)
                        {
                            if (isDraggingPlayhead)
                            {
                                // 拖拽进度条
                                playTime = Mathf.Clamp(mousePercent * totalLength, 0f, totalLength);
                                if (previewSource != null && previewSource.isPlaying) previewSource.time = playTime;
                                Repaint();
                            }
                            else if (isDraggingCrop)
                            {
                                // 拖拽扩展裁剪选区
                                cropEndNormalized = mousePercent;
                                float startSec = cropStartNormalized * totalLength;
                                float endSec = cropEndNormalized * totalLength;
                                cropStart = Mathf.Min(startSec, endSec);
                                cropEnd = Mathf.Max(startSec, endSec);
                                Repaint();
                            }
                            currentEvent.Use();
                        }

                        // --- 鼠标松开 ---
                        if (currentEvent.type == EventType.MouseUp)
                        {
                            if (isDraggingPlayhead)
                            {
                                isDraggingPlayhead = false;
                            }
                            if (isDraggingCrop)
                            {
                                isDraggingCrop = false;
                                // 如果鼠标松开时选区的范围太小（小于1%），视为【点击跳转】
                                if (Mathf.Abs(cropEndNormalized - cropStartNormalized) < 0.01f)
                                {
                                    playTime = mousePercent * totalLength;
                                    if (previewSource != null && previewSource.isPlaying) previewSource.time = playTime;
                                }
                                Repaint();
                            }
                            currentEvent.Use();
                        }
                    }

                    // ---------- 裁剪控制（滑块与按钮） ----------
                    if (totalLength > 0)
                    {
                        float maxLength = totalLength;
                        cropStart = EditorGUILayout.Slider("裁剪起点 (秒)", cropStart, 0f, cropEnd);
                        cropEnd = EditorGUILayout.Slider("裁剪终点 (秒)", cropEnd, cropStart, maxLength);
                        if (GUILayout.Button("✂️ 裁剪并保存新音频", GUILayout.Height(30)))
                        {
                            PerformCrop(clip, cropStart, cropEnd);
                        }
                    }
                }

                // ---------- 播放控制 ----------
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(isPlaying ? "暂停" : "播放", GUILayout.Width(60)))
                {
                    if (previewSource == null || previewSource.clip != clip)
                    {
                        if (previewSource != null)
                        {
                            DestroyImmediate(previewSource.gameObject);
                            previewSource = null;
                        }
                        GameObject go = new GameObject("PreviewAudioSource");
                        previewSource = go.AddComponent<AudioSource>();
                        previewSource.clip = clip;
                        previewSource.playOnAwake = false;
                    }

                    if (!isPlaying)
                    {
                        if (previewSource.time == 0f || !previewSource.isPlaying)
                        {
                            previewSource.time = playTime;
                            previewSource.Play();
                        }
                        else
                        {
                            previewSource.UnPause();
                        }
                    }
                    else
                    {
                        previewSource.Pause();
                    }
                    isPlaying = !isPlaying;
                }

                if (GUILayout.Button("停止", GUILayout.Width(60)))
                {
                    if (previewSource != null)
                    {
                        previewSource.Stop();
                        isPlaying = false;
                        playTime = 0f;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("无法加载该音频文件");
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    // ---------- 裁剪核心 ----------
    private void PerformCrop(AudioClip originalClip, float startSec, float endSec)
    {
        if (originalClip == null || startSec >= endSec) return;

        int startSample = Mathf.FloorToInt(startSec * originalClip.frequency);
        int endSample = Mathf.FloorToInt(endSec * originalClip.frequency);
        int totalSamples = endSample - startSample;
        int channels = originalClip.channels;

        float[] originalData = new float[originalClip.samples * channels];
        originalClip.GetData(originalData, 0);

        float[] newData = new float[totalSamples * channels];
        System.Array.Copy(originalData, startSample * channels, newData, 0, newData.Length);

        AudioClip newClip = AudioClip.Create("裁剪_" + originalClip.name, totalSamples, channels, originalClip.frequency, false);
        newClip.SetData(newData, 0);

        string originalPath = AssetDatabase.GetAssetPath(originalClip);
        string directory = System.IO.Path.GetDirectoryName(originalPath);
        string fileName = "裁剪_" + originalClip.name;
        string newPath = System.IO.Path.Combine(directory, fileName + ".wav");
        newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

        SaveWavFile(newPath, newData, originalClip.channels, originalClip.frequency);

        Debug.Log($"裁剪成功！已保存到: {newPath}");
        AssetDatabase.Refresh();
    }

    // ---------- WAV 保存辅助 ----------
    private void SaveWavFile(string filePath, float[] samples, int channels, int sampleRate)
    {
        int sampleCount = samples.Length;
        byte[] wavData = new byte[sampleCount * 2];
        int index = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short intSample = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
            byte[] bytes = System.BitConverter.GetBytes(intSample);
            wavData[index++] = bytes[0];
            wavData[index++] = bytes[1];
        }

        using (System.IO.FileStream fs = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
        using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(fs))
        {
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + wavData.Length);
            writer.Write("WAVE".ToCharArray());

            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);

            writer.Write("data".ToCharArray());
            writer.Write(wavData.Length);
            writer.Write(wavData);
        }
    }
}