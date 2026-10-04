using UnityEngine;

/// <summary>菜单风格随转头变化，前景/背景以不同幅度响应鼠标；不写共享材质。</summary>
[DefaultExecutionOrder(50)]
public sealed class MenuConceptMotion : MonoBehaviour
{
    [SerializeField] private MenuPortraitMotion portrait;
    [SerializeField] private Transform portraitPivot;
    [SerializeField] private Transform network;
    [SerializeField] private Renderer[] headRenderers;
    [SerializeField] private RectTransform[] foreground;
    [SerializeField, Range(0f,40f)] private float parallaxPixels=18f;
    private Vector3 networkOrigin;
    private Vector2[] foregroundOrigins;
    private Quaternion initialRotation;
    private MaterialPropertyBlock block;
    private Vector2 pointer;
    private bool focused=true;
    private static readonly int StyleAngle=Shader.PropertyToID("_StyleAngle");

    public void Configure(MenuPortraitMotion motion,Transform pivot,Transform background,
        Renderer[] renderers,RectTransform[] front)
    { portrait=motion; portraitPivot=pivot; network=background; headRenderers=renderers; foreground=front; }

    private void Awake()
    {
        initialRotation=portraitPivot.localRotation;
        networkOrigin=network.localPosition;
        foregroundOrigins=new Vector2[foreground.Length];
        for(int i=0;i<foreground.Length;i++) foregroundOrigins[i]=foreground[i].anchoredPosition;
        block=new MaterialPropertyBlock();
    }
    private void OnApplicationFocus(bool value) {focused=value;}
    private void LateUpdate()
    {
        Vector2 target=focused?new Vector2(Mathf.Clamp(Input.mousePosition.x/Mathf.Max(1,Screen.width)*2-1,-1,1),
            Mathf.Clamp(Input.mousePosition.y/Mathf.Max(1,Screen.height)*2-1,-1,1)):Vector2.zero;
        pointer=Vector2.Lerp(pointer,target,1-Mathf.Exp(-6f*Time.unscaledDeltaTime));
        float yaw=Mathf.DeltaAngle(0,(Quaternion.Inverse(initialRotation)*portraitPivot.localRotation).eulerAngles.y);
        ApplyStyle(Mathf.Clamp(yaw/20f,-1f,1f));
        network.localPosition=networkOrigin+new Vector3(pointer.x*.08f,pointer.y*.025f,0);
        for(int i=0;i<foreground.Length;i++)
            foreground[i].anchoredPosition=foregroundOrigins[i]+pointer*parallaxPixels*(1f+i*.45f);
    }
    public void ApplyStyle(float normalizedAngle)
    {
        if(block==null) block=new MaterialPropertyBlock();
        foreach(Renderer renderer in headRenderers)
        {
            if(renderer==null) continue;
            renderer.GetPropertyBlock(block);
            block.SetFloat(StyleAngle,Mathf.Clamp(normalizedAngle,-1f,1f));
            renderer.SetPropertyBlock(block);
        }
    }
}
