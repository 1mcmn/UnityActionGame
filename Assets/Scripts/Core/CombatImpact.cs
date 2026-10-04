using System;
using UnityEngine;

/// <summary>命中表现的统一广播：战斗逻辑只报告“发生了什么、多重”，镜头震动与 HUD 各自订阅，互不引用。</summary>
public static class CombatImpact
{
    /// <summary>Quake 为环境冲击（如落地），只震镜头，不计入连段、不触发顿帧。</summary>
    public enum Kind { Hit, Finisher, Launch, Slam, Parry, Guarded, Quake }

    public readonly struct Info
    {
        public readonly Kind kind;
        public readonly Vector3 point;
        public readonly float strength;
        public Info(Kind kind, Vector3 point, float strength) { this.kind = kind; this.point = point; this.strength = strength; }
        public bool Heavy => kind != Kind.Hit && kind != Kind.Guarded && kind != Kind.Quake;
        public bool CountsAsHit => kind == Kind.Hit || kind == Kind.Finisher || kind == Kind.Launch || kind == Kind.Slam;
    }

    public static event Action<Info> OnImpact;

    /// <summary>默认强度：顿帧倍率与震动幅度共用同一刻度，便于统一调参。</summary>
    public static float DefaultStrength(Kind kind)
    {
        switch (kind)
        {
            case Kind.Finisher: return 2.2f;
            case Kind.Launch: return 2.4f;
            case Kind.Slam: return 3f;
            case Kind.Parry: return 2f;
            case Kind.Guarded: return .6f;
            default: return 1f;
        }
    }

    public static void Raise(Kind kind, Vector3 point) => OnImpact?.Invoke(new Info(kind, point, DefaultStrength(kind)));
    public static void Raise(Kind kind, Vector3 point, float strength) => OnImpact?.Invoke(new Info(kind, point, strength));
}
