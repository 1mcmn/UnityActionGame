using UnityEngine;

/// <summary>菜单音频：主音量沿用项目偏好，音乐与音效各自持久化。</summary>
public sealed class NativeMenuAudio : MonoBehaviour
{
    public const string MasterKey = "Menu.MasterVolume";
    public const string MusicKey = "Menu.MusicVolume";
    public const string EffectsKey = "Menu.SfxVolume";
    public static float EffectsVolume => Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 1));
    private AudioSource music, effects;
    private AudioClip ambient, tick, confirm;

    private void Awake()
    {
        music = gameObject.AddComponent<AudioSource>(); effects = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = effects.playOnAwake = false; music.loop = true;
        ambient = MakeClip("MenuAmbient", 4f, false, false);
        tick = MakeClip("MenuTick", .07f, true, false);
        confirm = MakeClip("MenuConfirm", .13f, true, true);
        music.clip = ambient; Apply(); music.Play();
    }
    public float Get(int channel) => Mathf.Clamp01(PlayerPrefs.GetFloat(channel == 0 ? MasterKey : channel == 1 ? MusicKey : EffectsKey, 1));
    public void Set(int channel, float value)
    {
        PlayerPrefs.SetFloat(channel == 0 ? MasterKey : channel == 1 ? MusicKey : EffectsKey, Mathf.Clamp01(value));
        Apply(); PlayerPrefs.Save();
        if (SoundManager.Instance != null) SoundManager.Instance.SetEffectsVolume(EffectsVolume);
    }
    private void Apply()
    {
        AudioListener.volume = Get(0);
        if (music != null) music.volume = Get(1) * .15f;
        if (effects != null) effects.volume = Get(2) * .24f;
    }
    public void Tick(bool strong = false)
    {
        if (effects != null) effects.PlayOneShot(strong ? confirm : tick);
    }
    private static AudioClip MakeClip(string name, float seconds, bool transient, bool strong)
    {
        const int rate = 22050;
        var samples = new float[Mathf.RoundToInt(seconds * rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            float envelope = transient ? Mathf.Sin(Mathf.PI * i / samples.Length) * Mathf.Exp(-t * 28) : 1;
            float note = transient ? Mathf.Sin(t * (strong ? 880 : 1320) * Mathf.PI * 2) :
                Mathf.Sin(t * 110 * Mathf.PI * 2) * .32f + Mathf.Sin(t * 165 * Mathf.PI * 2) * .2f + Mathf.Sin(t * 220 * Mathf.PI * 2) * .08f;
            samples[i] = note * envelope * .35f;
        }
        var clip = AudioClip.Create(name, samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
    }
    private void OnDestroy()
    {
        if (ambient != null) Destroy(ambient); if (tick != null) Destroy(tick); if (confirm != null) Destroy(confirm);
    }
}
