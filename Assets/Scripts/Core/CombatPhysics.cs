using System;
using UnityEngine;

/// <summary>角色移动与攻击的公共空间规则；不应用位置或速度。</summary>
public static class CombatPhysics
{
    private static RaycastHit[] _rays = new RaycastHit[32], _sweeps = new RaycastHit[32];
    private static int Ray(Vector3 from, Vector3 direction, float length)
    {
        int count;
        while (true) { count = Physics.RaycastNonAlloc(from, direction, _rays, length, ~0, QueryTriggerInteraction.Ignore); if (count < _rays.Length) return count; Array.Resize(ref _rays, _rays.Length * 2); }
    }
    public static bool Obstructed(Vector3 from, Vector3 to, Transform attacker, Transform target)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .0001f) return false;
        int count = Ray(from, delta.normalized, delta.magnitude);
        for (int i = 0; i < count; i++)
        {
            var hit = _rays[i];
            if (hit.collider.transform.IsChildOf(attacker) || (target != null && hit.collider.transform.IsChildOf(target))) continue;
            return true;
        }
        return false;
    }

    public static bool Ground(Vector3 position, Transform owner, out float height)
    {
        height = position.y;
        float nearest = float.MaxValue;
        int count = Ray(position + Vector3.up * .5f, Vector3.down, 12f);
        for (int i = 0; i < count; i++)
        {
            var hit = _rays[i];
            if (hit.collider.transform.IsChildOf(owner) || hit.normal.y < .6f ||
                hit.collider.GetComponentInParent<Enemy>() != null || hit.collider.GetComponentInParent<ThirdPersonController>() != null) continue;
            if (hit.distance < nearest) { nearest = hit.distance; height = hit.point.y; }
        }
        return nearest < float.MaxValue;
    }

    public static Vector3 LimitMotion(Rigidbody body, Collider collider, Vector3 delta)
    {
        if (body == null || collider == null || delta.sqrMagnitude < .000001f) return delta;
        // 与当前渲染插值解耦，按刚体位置构造胶囊。
        Vector3 center;
        float radius, height;
        var capsule = collider as CapsuleCollider;
        if (capsule != null && capsule.direction == 1)
        {
            var scale = body.transform.lossyScale;
            radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            height = capsule.height * Mathf.Abs(scale.y);
            center = body.position + body.rotation * Vector3.Scale(capsule.center, scale);
        }
        else
        {
            var bounds = collider.bounds;
            radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
            height = bounds.size.y;
            center = bounds.center + (body.position - body.transform.position);
        }
        radius = Mathf.Max(.05f, radius - .015f);
        float half = Mathf.Max(0, height * .5f - radius);
        float length = delta.magnitude;
        int count;
        while (true)
        {
            count = Physics.CapsuleCastNonAlloc(center + Vector3.up * half, center - Vector3.up * half,
                radius, delta / length, _sweeps, length + .015f, ~0, QueryTriggerInteraction.Ignore);
            if (count < _sweeps.Length) break; Array.Resize(ref _sweeps, _sweeps.Length * 2);
        }
        for (int i = 0; i < count; i++)
        {
            var hit = _sweeps[i];
            if (hit.collider.transform.IsChildOf(body.transform) || Vector3.Dot(delta.normalized, hit.normal) >= -.05f) continue;
            if (hit.normal.y > .6f && delta.y >= -.001f) continue;
            length = Mathf.Min(length, Mathf.Max(0, hit.distance - .015f));
        }
        return delta.normalized * length;
    }
}
