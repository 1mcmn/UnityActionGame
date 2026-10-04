using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>以矢量几何绘制菜单线框和节点，不依赖图片或占用点击射线。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MenuWireGraphic : MaskableGraphic
{
    [SerializeField] private Vector2[] points = Array.Empty<Vector2>();
    [SerializeField, Min(.2f)] private float lineWidth = 1.5f;
    [SerializeField, Min(0f)] private float nodeRadius = 3.5f;
    [SerializeField] private bool closed;

    public void Configure(Vector2[] vertices, Color tint, float width, float radius, bool loop)
    {
        points = vertices; color = tint; lineWidth = width; nodeRadius = radius;
        closed = loop; raycastTarget = false; SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = rectTransform.rect;
        if (points == null || points.Length == 0) return;
        for (int i=0; i<points.Length; i++)
        {
            Vector2 p = new Vector2(rect.xMin+points[i].x*rect.width,rect.yMin+points[i].y*rect.height);
            if (i+1<points.Length || closed)
            {
                Vector2 next = points[(i+1)%points.Length];
                Vector2 q = new Vector2(rect.xMin+next.x*rect.width,rect.yMin+next.y*rect.height);
                Vector2 normal = new Vector2(-(q-p).y,(q-p).x).normalized * lineWidth*.5f;
                AddQuad(helper,p-normal,p+normal,q+normal,q-normal);
            }
            if (nodeRadius>0) AddNode(helper,p);
        }
    }

    private void AddQuad(VertexHelper helper,Vector2 a,Vector2 b,Vector2 c,Vector2 d)
    {
        int start=helper.currentVertCount;
        helper.AddVert(a,color,Vector2.zero); helper.AddVert(b,color,Vector2.zero);
        helper.AddVert(c,color,Vector2.zero); helper.AddVert(d,color,Vector2.zero);
        helper.AddTriangle(start,start+1,start+2); helper.AddTriangle(start,start+2,start+3);
    }
    private void AddNode(VertexHelper helper,Vector2 center)
    {
        const int segments=16;
        int start=helper.currentVertCount;
        helper.AddVert(center,color,Vector2.zero);
        for(int i=0;i<=segments;i++)
        {
            float angle=i*2f*Mathf.PI/segments;
            helper.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*nodeRadius,color,Vector2.zero);
            if(i>0) helper.AddTriangle(start,start+i,start+i+1);
        }
    }
}
