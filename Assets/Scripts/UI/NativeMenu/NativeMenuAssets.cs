using TMPro;
using UnityEngine;

[CreateAssetMenu(menuName = "刃间/原生菜单美术与动效")]
public sealed class NativeMenuAssets : ScriptableObject
{
    public TMP_FontAsset chineseFont;
    public TMP_FontAsset displayFont;
    public TMP_FontAsset condensedFont;
    public Material outlineMaterial;
    public Material heroMaterial;
    public Texture2D blade;
    public Texture2D[] discFaces;
    public Mesh discMesh;
    public Material discMaterial;
    public Color paper = new Color32(239, 240, 236, 255);
    public Color ink = new Color32(24, 35, 40, 255);
    public Color accent = new Color32(215, 250, 82, 255);
    public Color muted = new Color32(82, 96, 87, 255);
    [Min(.01f)] public float carouselDuration = .78f;
    [Min(.01f)] public float hoverDuration = .28f;
    public Vector2 hoverAngles = new Vector2(5, 6);
    public float angularSpacing = .63f;
    public float inkDrawDuration = .56f;
    public float inkStagger = .13f;
    public float inkHold = .24f;
    public float inkFade = .18f;
    public float textExit = .12f;
    public float textEnter = .38f;
    public bool reduceMotion;
}
