using System;
using UnityEngine;

/// <summary>原生矢量背景与独立描线；装饰不占用 UI 射线。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class NativeMenuGraphic : UnityEngine.UI.MaskableGraphic
{
    public enum Pattern { Floor, Halftone, Orbit, Grain, Stroke, Wave }
    public Pattern pattern;
    public Vector2 pointer;
    public Vector2[] path = Array.Empty<Vector2>();
    [Range(0, 1)] public float progress = 1;
    public float strokeWidth = 1;
    public float amplitude = 1;

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear(); Rect r = rectTransform.rect;
        if (r.width <= 0 || r.height <= 0) return;
        if (pattern == Pattern.Stroke)
        {
            if (path == null || path.Length < 2) return;
            float total = 0;
            for (int i = 1; i < path.Length; i++) total += Vector2.Distance(path[i - 1], path[i]);
            float remaining = total * progress;
            for (int i = 1; i < path.Length && remaining > 0; i++)
            {
                float length = Vector2.Distance(path[i - 1], path[i]);
                Segment(vh, path[i - 1], Vector2.Lerp(path[i - 1], path[i], Mathf.Min(1, remaining / Mathf.Max(.001f, length))), color, strokeWidth);
                remaining -= length;
            }
            return;
        }
        if (pattern == Pattern.Floor)
        {
            float vx = r.xMin + r.width * .65f + pointer.x * 13, hy = r.yMin + r.height * .39f - pointer.y * 7;
            for (int row = 1; row <= 17; row++)
            {
                float d = Mathf.Pow(row / 17f, 2.4f), y = Mathf.Lerp(hy, r.yMin, d);
                Segment(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), Tint(.025f + d * .19f), .75f);
                for (int col = -24; col <= 24; col++)
                {
                    float x = vx + col * r.width / 13 * d * 1.4f;
                    if (x >= r.xMin && x <= r.xMax) Dot(vh, new Vector2(x, y), .7f + d * .7f, Tint(.04f + d * .21f));
                }
            }
            for (int col = -24; col <= 24; col++)
                Segment(vh, new Vector2(vx + col * r.width / 13 * .003f, hy), new Vector2(vx + col * r.width / 13 * 1.4f, r.yMin), Tint(.15f), .75f);
            float[] origins = { .48f, .82f, .91f };
            for (int branch = 0; branch < 3; branch++)
            {
                Vector2 p = new Vector2(r.xMin + origins[branch] * r.width - pointer.x * 6, r.yMin + (.35f - branch * .015f) * r.height);
                for (int n = 0; n < 5; n++)
                {
                    Vector2 q = p + new Vector2(Mathf.Sin(n * 4.7f + branch * 7) * 45, n == 0 ? 48 : 25 + n * 7);
                    Segment(vh, p, q, Tint(.28f), .8f); Dot(vh, p, 1.7f, Tint(.62f)); p = q;
                }
                Dot(vh, p, 1.8f, Tint(.62f));
            }
        }
        else if (pattern == Pattern.Halftone)
        {
            float step = Mathf.Max(5, r.width / 100);
            Vector2 center = r.center;
            for (float y = r.yMin; y < r.yMax; y += step)
                for (float x = r.xMin; x < r.xMax; x += step)
                {
                    Vector2 p = new Vector2(x, y), v = new Vector2((x - center.x) / (r.width * .5f), (y - center.y) / (r.height * .5f));
                    if (v.sqrMagnitude <= 1) Dot(vh, p, step * .18f, Tint(Mathf.Clamp01((v.x + .8f) * .55f)));
                }
        }
        else if (pattern == Pattern.Orbit)
        {
            Vector2 previous = r.center + new Vector2(r.width * .5f, 0);
            for (int i = 1; i <= 128; i++)
            {
                float angle = i * Mathf.PI * 2 / 128;
                Vector2 p = r.center + new Vector2(Mathf.Cos(angle) * r.width * .5f, Mathf.Sin(angle) * r.height * .5f);
                Segment(vh, previous, p, color, strokeWidth); previous = p;
            }
        }
        else if (pattern == Pattern.Wave)
        {
            for (int i = 0; i < 57; i++)
            {
                float x = r.xMin + i / 56f * r.width, h = (4 + Mathf.Pow(Mathf.Sin(i * .77f), 2) * 32 * amplitude);
                Segment(vh, new Vector2(x, r.center.y - h * .5f), new Vector2(x, r.center.y + h * .5f), color, 3);
            }
        }
        else
        {
            var random = new System.Random(109);
            for (int i = 0; i < 3600; i++)
                Dot(vh, new Vector2(r.xMin + (float)random.NextDouble() * r.width, r.yMin + (float)random.NextDouble() * r.height), .35f, color);
        }
    }
    private Color Tint(float alpha) { Color c = color; c.a *= alpha; return c; }
    private static void Segment(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Color c, float width)
    {
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        int i = vh.currentVertCount;
        vh.AddVert(a - n, c, Vector2.zero); vh.AddVert(a + n, c, Vector2.zero);
        vh.AddVert(b + n, c, Vector2.zero); vh.AddVert(b - n, c, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
    private static void Dot(UnityEngine.UI.VertexHelper vh, Vector2 p, float radius, Color c)
    {
        const int sides = 6; int i = vh.currentVertCount;
        vh.AddVert(p, c, Vector2.zero);
        for (int n = 0; n <= sides; n++)
        {
            float a = n * Mathf.PI * 2 / sides;
            vh.AddVert(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, c, Vector2.zero);
            if (n > 0) vh.AddTriangle(i, i + n, i + n + 1);
        }
    }
}
