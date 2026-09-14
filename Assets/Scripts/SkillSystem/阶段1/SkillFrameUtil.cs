using UnityEngine;

namespace SkillSystem
{
    /// <summary>
    /// 帧 <-> 时间 换算的唯一出处（编辑器预览 + 运行时帧事件共用）。
    /// 帧率取 clip.frameRate，不再在编辑器/运行时两处各自硬编码 30fps。
    /// 说明：TimeToFrame 用 floor，与原 30fps 实现的语义完全一致（30fps 素材行为不变）。
    /// </summary>
    public static class SkillFrameUtil
    {
        /// <summary>拿不到 clip（或 clip.frameRate 非法）时的兜底帧率</summary>
        public const float DefaultFrameRate = 30f;

        public static float GetFrameRate(AnimationClip clip)
        {
            if (clip == null) return DefaultFrameRate;
            return clip.frameRate > 0f ? clip.frameRate : DefaultFrameRate;
        }

        /// <summary>时间(秒) -> 帧号（floor）</summary>
        public static int TimeToFrame(AnimationClip clip, float time)
        {
            if (time <= 0f) return 0;
            return Mathf.FloorToInt(time * GetFrameRate(clip));
        }

        /// <summary>帧号 -> 时间(秒)，夹取到 [0, clip.length]</summary>
        public static float FrameToTime(AnimationClip clip, int frame)
        {
            float t = frame / GetFrameRate(clip);
            if (clip != null) t = Mathf.Clamp(t, 0f, clip.length);
            return t;
        }

        /// <summary>整个 clip 的总帧数</summary>
        public static int TotalFrames(AnimationClip clip)
        {
            if (clip == null) return 0;
            return Mathf.Max(0, Mathf.FloorToInt(clip.length * GetFrameRate(clip)));
        }

        /// <summary>把任意时间吸附到最近的整数帧（拖动时间轴用）</summary>
        public static float SnapTimeToFrame(AnimationClip clip, float time)
        {
            if (clip == null) return time;
            return FrameToTime(clip, Mathf.RoundToInt(time * GetFrameRate(clip)));
        }
    }
}
