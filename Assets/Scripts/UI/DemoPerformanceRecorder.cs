using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// F8 开始/停止真实帧时间采样；结果写入 persistentDataPath/Performance。
/// 编辑器数据和 PC 构建数据分别标记，采样本身不代表已经满足 60 FPS 指标。
/// </summary>
public sealed class DemoPerformanceRecorder : MonoBehaviour
{
    [Min(0f)] public float sampleDuration = 60f;
    public bool showOverlay = true;
    public KeyCode toggleKey = KeyCode.F8;

    private readonly List<double> _frameSeconds = new List<double>(10000);
    private readonly Queue<double> _rollingFrames = new Queue<double>();
    private double _previousFrameTime;
    private double _elapsed;
    private double _rollingSeconds;
    private double _minRollingFps;
    private string _startedAt;
    private string _status = "F8：开始性能采样";
    private int _width;
    private int _height;
    private string _quality;
    private int _vSync;
    private int _targetFrameRate;
    private bool _settingsChanged;
    private GUIStyle _labelStyle;

    public bool IsRecording { get; private set; }
    public string LastReportPath { get; private set; }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (IsRecording) StopRecording();
            else StartRecording();
            return;
        }
        if (!IsRecording) return;

        double now = Time.realtimeSinceStartupAsDouble;
        double frameTime = now - _previousFrameTime;
        _previousFrameTime = now;
        if (frameTime <= 0d) return;

        _frameSeconds.Add(frameTime);
        _elapsed += frameTime;
        _rollingFrames.Enqueue(frameTime);
        _rollingSeconds += frameTime;
        // 保留覆盖至少一秒的最小连续帧窗口，避免把单帧倒数当作最低 FPS。
        while (_rollingFrames.Count > 1 && _rollingSeconds - _rollingFrames.Peek() >= 1d)
            _rollingSeconds -= _rollingFrames.Dequeue();
        if (_rollingSeconds >= 1d)
            _minRollingFps = Math.Min(_minRollingFps, _rollingFrames.Count / _rollingSeconds);

        if (Screen.width != _width || Screen.height != _height ||
            QualitySettings.vSyncCount != _vSync || Application.targetFrameRate != _targetFrameRate ||
            QualitySettings.names[QualitySettings.GetQualityLevel()] != _quality)
            _settingsChanged = true;

        if (sampleDuration > 0f && _elapsed >= sampleDuration)
            FinishRecording("duration_reached");
    }

    public void StartRecording()
    {
        if (IsRecording) return;
        _frameSeconds.Clear();
        _rollingFrames.Clear();
        _elapsed = 0d;
        _rollingSeconds = 0d;
        _minRollingFps = double.PositiveInfinity;
        _startedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        _width = Screen.width;
        _height = Screen.height;
        _quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
        _vSync = QualitySettings.vSyncCount;
        _targetFrameRate = Application.targetFrameRate;
        _settingsChanged = false;
        _previousFrameTime = Time.realtimeSinceStartupAsDouble;
        IsRecording = true;
        _status = "采样中；F8 提前结束";
        Debug.Log($"[Performance] 开始采样，Editor={Application.isEditor}，结果目录：{Path.Combine(Application.persistentDataPath, "Performance")}", this);
    }

    public void StopRecording()
    {
        FinishRecording("manual_stop");
    }

    private void OnDisable()
    {
        if (IsRecording) FinishRecording("component_disabled");
    }

    private void FinishRecording(string reason)
    {
        if (!IsRecording) return;
        IsRecording = false;
        if (_frameSeconds.Count == 0 || _elapsed <= 0d)
        {
            _status = "未收集到帧，请重新按 F8 采样";
            return;
        }

        double averageFps = _frameSeconds.Count / _elapsed;
        double[] sorted = _frameSeconds.ToArray();
        Array.Sort(sorted);
        int percentileIndex = Math.Max(0, (int)Math.Ceiling(sorted.Length * 0.95d) - 1);
        double p95Milliseconds = sorted[percentileIndex] * 1000d;
        string minFps = double.IsPositiveInfinity(_minRollingFps) ? "" : Number(_minRollingFps);

        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "Performance");
            Directory.CreateDirectory(directory);
            string stem = "performance-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string reportPath = Path.Combine(directory, stem + ".csv");
            string framesPath = Path.Combine(directory, stem + "-frames.csv");
            string header = "started_at,platform,is_editor,unity_version,os,cpu,gpu,system_memory_mb,width,height,quality,v_sync,target_frame_rate,settings_changed,frame_count,duration_seconds,avg_fps,min_rolling_1s_fps,p95_frame_ms,stop_reason,frames_file";
            string[] values =
            {
                _startedAt, Application.platform.ToString(), Application.isEditor.ToString(), Application.unityVersion,
                SystemInfo.operatingSystem, SystemInfo.processorType, SystemInfo.graphicsDeviceName,
                SystemInfo.systemMemorySize.ToString(CultureInfo.InvariantCulture),
                _width.ToString(CultureInfo.InvariantCulture), _height.ToString(CultureInfo.InvariantCulture), _quality,
                _vSync.ToString(CultureInfo.InvariantCulture), _targetFrameRate.ToString(CultureInfo.InvariantCulture),
                _settingsChanged.ToString(), _frameSeconds.Count.ToString(CultureInfo.InvariantCulture),
                Number(_elapsed), Number(averageFps), minFps, Number(p95Milliseconds), reason, Path.GetFileName(framesPath)
            };
            for (int i = 0; i < values.Length; ++i) values[i] = Csv(values[i]);
            File.WriteAllText(reportPath, header + "\n" + string.Join(",", values) + "\n", new UTF8Encoding(true));

            using (var writer = new StreamWriter(framesPath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("frame,elapsed_seconds,frame_ms");
                double time = 0d;
                for (int i = 0; i < _frameSeconds.Count; ++i)
                {
                    time += _frameSeconds[i];
                    writer.WriteLine((i + 1).ToString(CultureInfo.InvariantCulture) + "," + Number(time) + "," + Number(_frameSeconds[i] * 1000d));
                }
            }
            LastReportPath = reportPath;
            _status = $"已保存：平均 {averageFps:F1} FPS，P95 {p95Milliseconds:F2} ms\n" +
                (Application.isEditor ? "编辑器样本；验收请在 PC 构建重复采样" : "PC 样本；请结合最低滚动 FPS 验收") +
                (_settingsChanged ? "\n采样期间设置有变化，请固定设置重新采样" : "");
            Debug.Log($"[Performance] {_status}\n报告：{reportPath}\n逐帧记录：{framesPath}", this);
        }
        catch (Exception exception)
        {
            _status = "保存性能报告失败，请查看 Console / Player.log";
            Debug.LogError($"[Performance] {exception.Message}", this);
        }
    }

    private static string Number(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private void OnGUI()
    {
        if (!showOverlay) return;
        if (_labelStyle == null)
            _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
        float width = Mathf.Min(350f, Screen.width - 24f);
        Rect area = new Rect(Screen.width - width - 12f, 12f, width, 125f);
        GUI.Box(area, GUIContent.none);
        string text = IsRecording
            ? $"性能采样 {_elapsed:F1} / {(sampleDuration > 0f ? sampleDuration.ToString("F0") + "秒" : "手动结束")}\n" +
              $"平均 {(_elapsed > 0d ? _frameSeconds.Count / _elapsed : 0d):F1} FPS · F8 停止\n" +
              (Application.isEditor ? "当前为编辑器样本" : "当前为 PC 构建样本")
            : _status;
        GUI.Label(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f), text, _labelStyle);
    }
}
