using UnityEngine;

/// <summary>
/// 菜单与战斗共用的玩家设置（PlayerPrefs）。音量沿用主菜单的键，全屏/垂直同步沿用旧菜单的键，保证新旧界面读写一致。
/// </summary>
public static class GameSettings
{
    public const string SensitivityKey = "Settings.MouseSensitivity";
    public const string FullscreenKey = "Menu.Fullscreen";
    public const string VSyncKey = "Menu.VSync";

    public static float Volume(int channel) => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey(channel), 1));
    public static string VolumeKey(int channel) => channel == 0 ? NativeMenuAudio.MasterKey : channel == 1 ? NativeMenuAudio.MusicKey : NativeMenuAudio.EffectsKey;

    /// <summary>鼠标灵敏度倍率（0.2～2，默认 1），乘在 CameraFollow 的基础灵敏度上。</summary>
    public static float Sensitivity => Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1), .2f, 2f);
    public static bool Fullscreen => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
    public static bool VSync => PlayerPrefs.GetInt(VSyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;

    public static void SetVolume(int channel, float value)
    {
        PlayerPrefs.SetFloat(VolumeKey(channel), Mathf.Clamp01(value)); ApplyAudio(); PlayerPrefs.Save();
    }
    public static void SetSensitivity(float value) { PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, .2f, 2f)); PlayerPrefs.Save(); }
    public static void SetFullscreen(bool value) { PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); PlayerPrefs.Save(); Screen.fullScreen = value; }
    public static void SetVSync(bool value) { PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0); PlayerPrefs.Save(); QualitySettings.vSyncCount = value ? 1 : 0; }

    public static void ApplyAudio()
    {
        AudioListener.volume = Volume(0);
        if (SoundManager.Instance != null) SoundManager.Instance.SetEffectsVolume(Volume(2));
    }
}

/// <summary>战斗暂停状态：时间冻结、输入屏蔽、鼠标解锁；顿帧协程结束时据此判断是否恢复时间。</summary>
public static class GamePause
{
    public static bool IsPaused { get; private set; }

    public static void Pause()
    {
        if (IsPaused) return;
        IsPaused = true; Time.timeScale = 0f; AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    public static void Resume(bool lockCursor = true)
    {
        if (!IsPaused) return;
        IsPaused = false; Time.timeScale = 1f; AudioListener.pause = false;
        if (lockCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    /// <summary>离开战斗场景前清理，避免时间冻结带入下一个场景。</summary>
    public static void Clear() { IsPaused = false; Time.timeScale = 1f; AudioListener.pause = false; }
}
