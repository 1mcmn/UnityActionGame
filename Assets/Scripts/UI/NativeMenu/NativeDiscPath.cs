using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 存档光盘的连线与选中环：参考 METAPHOR 章节选择，光盘之间以一条细曲线相连，选中光盘外圈一圈旋转的虚线点。
/// 画在背景画布上，位于光盘之后；数据由轮盘每帧按屏幕坐标提供。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class NativeDiscPath : MaskableGraphic
{
    public Camera eventCamera;
    public float lineWidth = 1.2f;
    public float dotSize = 2.2f;
    [Range(0, 1)] public float lineAlpha = .32f;
    [Range(0, 1)] public float ringAlpha = .62f;
    private Vector2[] line = Array.Empty<Vector2>(), ring = Array.Empty<Vector2>();
    private float ringFade;

    /// <summary>传入屏幕坐标：曲线经过的光盘中心（按顺序）与选中环上的点。</summary>
    public void SetShape(Vector2[] screenLine, Vector2[] screenRing, float ringVisibility)
    {
        line = ToLocal(screenLine, line); ring = ToLocal(screenRing, ring); ringFade = ringVisibility;
        SetVerticesDirty();
    }

    private Vector2[] ToLocal(Vector2[] source, Vector2[] buffer)
    {
        if (buffer.Length != source.Length) buffer = new Vector2[source.Length];
        for (int i = 0; i < source.Length; i++)
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, source[i], eventCamera, out buffer[i]);
        return buffer;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float unit = canvas != null ? 1f / Mathf.Max(.01f, canvas.scaleFactor) : 1f;
        // 曲线：Catmull-Rom 平滑经过各光盘中心，两端各外延一段，让线条从画面边缘“穿入穿出”。
        if (line.Length >= 2)
        {
            Color c = color; c.a *= lineAlpha;
            Vector2 previous = line[0];
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 a = line[Mathf.Max(0, i - 1)], b = line[i], d = line[i + 1], e = line[Mathf.Min(line.Length - 1, i + 2)];
                for (int s = 1; s <= 12; s++)
                {
                    float t = s / 12f;
                    Vector2 p = .5f * ((2 * b) + (-a + d) * t + (2 * a - 5 * b + 4 * d - e) * t * t + (-a + 3 * b - 3 * d + e) * t * t * t);
                    Segment(vh, previous, p, c, lineWidth * unit); previous = p;
                }
            }
        }
        if (ring.Length > 0 && ringFade > .01f)
        {
            Color c = color; c.a *= ringAlpha * ringFade;
            foreach (var p in ring) Square(vh, p, dotSize * unit * .5f, c);
        }
    }

    private static void Segment(VertexHelper vh, Vector2 a, Vector2 b, Color color, float width)
    {
        Vector2 d = b - a; if (d.sqrMagnitude < .0001f) return;
        Vector2 n = new Vector2(-d.y, d.x).normalized * width * .5f;
        int start = vh.currentVertCount;
        vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero); vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Square(VertexHelper vh, Vector2 center, float half, Color color)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center + new Vector2(-half, -half), color, Vector2.zero); vh.AddVert(center + new Vector2(-half, half), color, Vector2.zero);
        vh.AddVert(center + new Vector2(half, half), color, Vector2.zero); vh.AddVert(center + new Vector2(half, -half), color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }
}
