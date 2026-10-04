using UnityEngine;
using UnityEngine.UI;

/// <summary>菜单同款面板：右上切角实心板，或等宽描边框（键帽）。纯顶点绘制，不需要贴图。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HudPlate : MaskableGraphic
{
    [Min(0)] public float cut;
    [Min(0)] public float border;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Rect r = rectTransform.rect;
        if (r.width <= 0 || r.height <= 0) return;
        if (border > 0)
        {
            float b = Mathf.Min(border, r.width * .5f, r.height * .5f);
            Quad(vh, r.xMin, r.yMax - b, r.xMax, r.yMax); Quad(vh, r.xMin, r.yMin, r.xMax, r.yMin + b);
            Quad(vh, r.xMin, r.yMin + b, r.xMin + b, r.yMax - b); Quad(vh, r.xMax - b, r.yMin + b, r.xMax, r.yMax - b);
            return;
        }
        float c = Mathf.Min(cut, r.width, r.height);
        Vector2[] points = c > 0
            ? new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax - c, r.yMax), new Vector2(r.xMax, r.yMax - c), new Vector2(r.xMax, r.yMin) }
            : new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin) };
        vh.AddVert(r.center, color, Vector2.zero);
        foreach (var p in points) vh.AddVert(p, color, Vector2.zero);
        for (int i = 0; i < points.Length; i++) vh.AddTriangle(0, i + 1, (i + 1) % points.Length + 1);
    }

    private void Quad(VertexHelper vh, float x0, float y0, float x1, float y1)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector2(x0, y0), color, Vector2.zero); vh.AddVert(new Vector2(x0, y1), color, Vector2.zero);
        vh.AddVert(new Vector2(x1, y1), color, Vector2.zero); vh.AddVert(new Vector2(x1, y0), color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }
}
