using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局界面音效：短促反馈用 Kenney CC0 录音（Resources/UIAudio），转场、光盘滚动、确认尾音等需与动画时长对齐的声音程序化合成。
/// 跨场景常驻，音量跟随菜单“音效音量”；悬停/移动等次要声音在重要声音之后短时间内被抑制，避免叠响。
/// </summary>
public sealed class UiSfx : MonoBehaviour
{
    public enum Cue { Hover, Move, Confirm, Back, Open, Close, Error, Slider, DiscStep, DiscLand, Transition, TransitionBack, Start, Reject, Toast, Suppress }

    private sealed class Spec
    {
        public AudioClip clip, layer;
        public float volume = 1, interval, jitter, layerVolume;
        public bool minor, important;
    }

    private const int Voices = 8;
    private const float Rate = 44100;
    private static UiSfx _instance;
    private readonly Dictionary<Cue, Spec> _specs = new Dictionary<Cue, Spec>();
    private readonly Dictionary<Cue, float> _last = new Dictionary<Cue, float>();
    private readonly List<AudioClip> _generated = new List<AudioClip>();
    private AudioSource[] _voices;
    private int _next;
    private float _quietUntil;

    public static void Play(Cue cue, float pitch = 1f)
    {
        if (!Application.isPlaying) return;
        if (_instance == null)
        {
            var host = new GameObject("UiSfx");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<UiSfx>();
        }
        _instance.Emit(cue, pitch);
    }

    private void Awake()
    {
        _voices = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
            _voices[i] = source;
        }
        var swell = Generated("ConfirmTail", Tail(1.6f));
        Define(Cue.Hover, Load("ui_hover"), .35f, .05f, .06f, minor: true);
        Define(Cue.Move, Load("ui_move"), .45f, .05f, .05f, minor: true);
        Define(Cue.Slider, Load("ui_slider"), .4f, .045f, 0, minor: true);
        Define(Cue.Confirm, Load("ui_confirm"), .7f, .08f, .02f, important: true, layer: swell, layerVolume: .35f);
        Define(Cue.Back, Load("ui_back"), .65f, .08f, .03f, important: true);
        Define(Cue.Open, Load("ui_open"), .6f, .1f, 0, important: true);
        Define(Cue.Close, Load("ui_close"), .55f, .1f, 0, important: true);
        Define(Cue.Error, Load("ui_error"), .6f, .15f, 0, important: true);
        Define(Cue.DiscStep, Generated("DiscSwish", Swish(.26f, 900, 3200, true)), .3f, .09f, .05f);
        Define(Cue.DiscLand, Load("ui_disc_land"), .35f, .12f, .05f);
        Define(Cue.Transition, Generated("Transition", Swish(.48f, 500, 3600, false)), .5f, .2f, .03f);
        Define(Cue.TransitionBack, Generated("TransitionBack", Swish(.42f, 3200, 450, false)), .45f, .2f, .03f);
        Define(Cue.Start, Load("ui_start"), .8f, .5f, 0, important: true, layer: Generated("StartRiser", Riser(.9f)), layerVolume: .55f);
        Define(Cue.Reject, Load("hud_reject"), .55f, .2f, 0);
        Define(Cue.Toast, Load("hud_toast"), .5f, .3f, 0);
        Define(Cue.Suppress, Load("hud_suppress"), .7f, .5f, 0);
    }

    private void Define(Cue cue, AudioClip clip, float volume, float interval, float jitter, bool minor = false, bool important = false, AudioClip layer = null, float layerVolume = 0)
    {
        if (clip == null) { Debug.LogWarning("[UiSfx] 缺少界面音效：" + cue); return; }
        _specs[cue] = new Spec { clip = clip, volume = volume, interval = interval, jitter = jitter, minor = minor, important = important, layer = layer, layerVolume = layerVolume };
    }

    private void Emit(Cue cue, float pitch)
    {
        if (!_specs.TryGetValue(cue, out Spec spec)) return;
        float now = Time.unscaledTime;
        if (_last.TryGetValue(cue, out float last) && now - last < spec.interval) return;
        // 场景刚载入时的程序化选中，以及确认/返回之后紧跟的焦点切换，不再额外发声。
        if (spec.minor && (now < _quietUntil || Time.timeSinceLevelLoad < .3f)) return;
        _last[cue] = now;
        if (spec.important) _quietUntil = now + .35f;
        float volume = NativeMenuAudio.EffectsVolume;
        if (volume <= 0) return;
        float variation = spec.jitter > 0 ? Random.Range(-spec.jitter, spec.jitter) : 0;
        Voice(spec.clip, spec.volume * volume, pitch * (1 + variation));
        if (spec.layer != null) Voice(spec.layer, spec.layerVolume * volume, 1);
    }

    private void Voice(AudioClip clip, float volume, float pitch)
    {
        var source = _voices[_next]; _next = (_next + 1) % _voices.Length;
        source.Stop(); source.clip = clip; source.volume = Mathf.Clamp01(volume); source.pitch = Mathf.Clamp(pitch, .5f, 2f); source.Play();
    }

    private static AudioClip Load(string name) => Resources.Load<AudioClip>("UIAudio/" + name);

    private AudioClip Generated(string name, float[] samples)
    {
        var clip = AudioClip.Create("UiSfx_" + name, samples.Length, 1, (int)Rate, false);
        clip.SetData(samples, 0); _generated.Add(clip); return clip;
    }

    // 带通噪声扫频：截止频率从 from 扫到 to，sin² 包络；grain 为 true 时叠加柔和的正弦颗粒调制，模拟光盘转动的摩擦声。
    private static float[] Swish(float seconds, float from, float to, bool grain)
    {
        var data = new float[Mathf.RoundToInt(seconds * Rate)];
        var random = new System.Random(grain ? 7 : 11);
        float low = 0, band = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)data.Length;
            float cutoff = Mathf.Lerp(from, to, t * t * (3 - 2 * t));
            float a = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff / Rate);
            float noise = (float)random.NextDouble() * 2 - 1;
            low += a * (noise - low); band += a * .5f * (low - band);
            float envelope = Mathf.Pow(Mathf.Sin(Mathf.PI * t), 2) * (1 - t * .35f);
            float texture = grain ? .8f + .2f * Mathf.Sin(i / Rate * 2 * Mathf.PI * 55) : 1;
            data[i] = (low - band) * envelope * texture * 1.6f;
        }
        return data;
    }

    // 确认后的空气感尾音：低通噪声 + 两个轻微失谐的正弦泛音，指数衰减，模拟参考视频中确认音后的混响长尾。
    private static float[] Tail(float seconds)
    {
        var data = new float[Mathf.RoundToInt(seconds * Rate)];
        var random = new System.Random(3);
        float low = 0, a = 1 - Mathf.Exp(-2 * Mathf.PI * 1600 / Rate);
        for (int i = 0; i < data.Length; i++)
        {
            float time = i / Rate, attack = Mathf.Clamp01(time / .04f), decay = Mathf.Exp(-time * 2.6f);
            low += a * (((float)random.NextDouble() * 2 - 1) - low);
            float tone = Mathf.Sin(time * 2 * Mathf.PI * 784) * .18f + Mathf.Sin(time * 2 * Mathf.PI * 1178) * .1f;
            data[i] = (low * .55f + tone) * attack * decay * .5f;
        }
        return data;
    }

    // 开始游戏的上升音：正弦 220→880Hz 指数滑音叠加扫频噪声，末端突然收住，与载入转场衔接。
    private static float[] Riser(float seconds)
    {
        var data = new float[Mathf.RoundToInt(seconds * Rate)];
        var noise = Swish(seconds, 300, 6000, false);
        float phase = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)data.Length;
            phase += 2 * Mathf.PI * 220 * Mathf.Pow(4, t) / Rate;
            float envelope = t * t * (t < .94f ? 1 : (1 - t) / .06f);
            data[i] = (Mathf.Sin(phase) * .35f + noise[i] * .8f) * envelope;
        }
        return data;
    }

    private void OnDestroy()
    {
        foreach (var clip in _generated) if (clip != null) Destroy(clip);
        if (_instance == this) _instance = null;
    }
}
