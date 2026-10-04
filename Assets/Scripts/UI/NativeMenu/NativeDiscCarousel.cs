using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class NativeDiscCarousel : MonoBehaviour
{
    private sealed class Disc
    {
        public Transform root, face;
        public MeshRenderer renderer;
        public float alpha, diameter;
        public Tween hover;
    }
    public NativeMenuUI ui;
    public NativeMenuAssets assets;
    public Transform discContainer;
    public NativeMenuAudio menuAudio;
    public NativeDiscPath path;
    public event Action<int, int> SelectionChanged;
    public int Selection { get; private set; }
    public float Position => position;
    public bool InputEnabled { get; set; }
    public int Count => discs.Count;
    public bool Moving => move.isAlive;
    private readonly List<Disc> discs = new List<Disc>();
    private Tween move, confirmation;
    private float position, lastWheel;
    private int hoverIndex = -1;
    private Vector2 lastPointer;
    private MaterialPropertyBlock block;

    public void Rebuild(IReadOnlyList<NativeMenuSave> records, int selected)
    {
        if (block == null) block = new MaterialPropertyBlock();
        StopMotion();
        foreach (var disc in discs) { disc.hover.Stop(); Destroy(disc.root.gameObject); }
        discs.Clear();
        for (int i = 0; i <= records.Count; i++)
        {
            var root = new GameObject(i == records.Count ? "NewStoryDisc" : "SaveDisc_" + records[i].id).transform;
            root.SetParent(discContainer, false); root.gameObject.layer = 29;
            var face = new GameObject("TiltPivot", typeof(MeshFilter), typeof(MeshRenderer)).transform; face.SetParent(root, false); face.gameObject.layer = 29;
            face.GetComponent<MeshFilter>().sharedMesh = assets.discMesh;
            var renderer = face.GetComponent<MeshRenderer>(); renderer.sharedMaterial = assets.discMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            block.Clear(); block.SetTexture("_MainTex", assets.discFaces[i == records.Count ? 3 : records[i].art]); renderer.SetPropertyBlock(block);
            discs.Add(new Disc { root = root, face = face, renderer = renderer });
        }
        Selection = Wrap(selected); position = Selection; Draw(); SelectionChanged?.Invoke(Selection, 1);
    }
    public void Select(int index, bool confirm = false)
    {
        if (!InputEnabled || discs.Count == 0) return;
        int next = Wrap(index), direction = next >= Selection ? 1 : -1;
        ClearInk(); ResetHover(); move.Stop(); Selection = next; SelectionChanged?.Invoke(next, direction);
        UiSfx.Play(confirm ? UiSfx.Cue.Confirm : UiSfx.Cue.DiscStep);
        if (assets.reduceMotion || Mathf.Abs(position - next) < .001f)
        { position = next; Draw(); if (confirm) Confirm(); return; }
        move = Tween.Custom(this, position, (float)next, assets.carouselDuration, (target, value) => { target.position = value; target.Draw(); }, Ease.OutQuart, useUnscaledTime: true);
        if (confirm) move.OnComplete(this, target => target.Confirm());
        else move.OnComplete(this, target => UiSfx.Play(UiSfx.Cue.DiscLand));
    }
    public void Step(int direction) => Select(Selection + direction);
    public void Wheel(float delta)
    {
        if (Mathf.Abs(delta) < .01f || Time.unscaledTime - lastWheel < .17f) return;
        lastWheel = Time.unscaledTime; Step(delta > 0 ? -1 : 1);
    }
    public void ConfirmSelection() => Select(Selection, true);
    public void StopMotion() { move.Stop(); ClearInk(); ResetHover(); }
    public void ClearInk() { confirmation.Stop(); if (ui != null && ui.inkGroup != null) ui.inkGroup.alpha = 0; }
    private int Wrap(int value) => discs.Count == 0 ? 0 : (value % discs.Count + discs.Count) % discs.Count;
    private void LateUpdate()
    {
        if (discs.Count == 0) return;
        Draw();
        if (!InputEnabled || assets.reduceMotion || Input.GetMouseButton(0)) { if (hoverIndex >= 0) ResetHover(); return; }
        Vector2 pointer = Input.mousePosition;
        int hit = RectTransformUtility.RectangleContainsScreenPoint(ui.stage, pointer, ui.menuCamera) ? Hit(pointer) : -1;
        if (hit != hoverIndex) { ResetHover(); hoverIndex = hit; }
        if (hit >= 0 && (pointer - lastPointer).sqrMagnitude > .01f)
        {
            Vector3 p = ui.menuCamera.WorldToScreenPoint(discs[hit].root.position);
            float d = discs[hit].diameter / ui.Unit;
            Hover(hit, Mathf.Clamp((pointer.x - p.x) / (d * .5f), -1, 1), Mathf.Clamp((pointer.y - p.y) / (d * .5f), -1, 1));
        }
        lastPointer = pointer;
    }
    public void Hover(int index, float x, float y)
    {
        if (index < 0 || index >= discs.Count || assets.reduceMotion) return;
        Disc disc = discs[index]; disc.hover.Stop(); Quaternion from = disc.face.localRotation;
        Quaternion to = Quaternion.Euler(-y * assets.hoverAngles.x, -x * assets.hoverAngles.y, 0);
        Vector3 p = disc.face.localPosition, s = disc.face.localScale;
        disc.hover = Tween.Custom(0f, 1f, assets.hoverDuration, value => {
            if (disc.face == null) return;
            disc.face.localRotation = Quaternion.Slerp(from, to, value);
            disc.face.localPosition = Vector3.Lerp(p, new Vector3(0, 0, -8 * ui.Unit / disc.diameter), value);
            disc.face.localScale = Vector3.Lerp(s, Vector3.one * 1.012f, value);
        }, Ease.OutCubic, useUnscaledTime: true);
    }
    private void ResetHover()
    {
        hoverIndex = -1;
        foreach (var disc in discs)
        {
            disc.hover.Stop(); if (disc.face == null) continue;
            Quaternion q = disc.face.localRotation; Vector3 p = disc.face.localPosition, s = disc.face.localScale;
            if (q == Quaternion.identity && p == Vector3.zero && s == Vector3.one) continue;
            disc.hover = Tween.Custom(0f, 1f, assets.hoverDuration, t => {
                if (disc.face == null) return; disc.face.localRotation = Quaternion.Slerp(q, Quaternion.identity, t);
                disc.face.localPosition = Vector3.Lerp(p, Vector3.zero, t); disc.face.localScale = Vector3.Lerp(s, Vector3.one, t);
            }, Ease.OutCubic, useUnscaledTime: true);
        }
    }
    public void Click(Vector2 screenPoint)
    {
        int hit = Hit(screenPoint); if (hit < 0) return;
        // 点正中已落定的光盘＝进入（空白盘由控制器转为新建存档）；点两侧光盘只是转过来。
        if (hit == Selection && Mathf.Abs(position - Selection) < .1f) Activate();
        else Select(hit);
    }
    /// <summary>请求进入当前选中光盘；由菜单控制器决定进入战斗或新建存档。</summary>
    public event Action Activated;
    public void Activate() { if (InputEnabled && discs.Count > 0) Activated?.Invoke(); }
    public int Hit(Vector2 screenPoint)
    {
        Ray ray = ui.menuCamera.ScreenPointToRay(screenPoint); int result = -1; float nearest = float.MaxValue;
        for (int i = 0; i < discs.Count; i++)
        {
            Disc disc = discs[i]; if (disc.alpha < .2f) continue;
            var plane = new Plane(disc.root.forward, disc.root.position);
            if (!plane.Raycast(ray, out float distance) || distance >= nearest) continue;
            Vector3 local = disc.root.InverseTransformPoint(ray.GetPoint(distance));
            float radius = new Vector2(local.x, local.y).magnitude;
            if (radius > .5f || radius < .0275f) continue;
            result = i; nearest = distance;
        }
        return result;
    }
    public void Draw()
    {
        if (ui == null || discs.Count == 0) return;
        float width = ui.Viewport.x, u = ui.Unit, focal = 1100 * u;
        Vector3[] stageCorners = new Vector3[4]; ui.stage.GetWorldCorners(stageCorners);
        Vector2 lo = ui.menuCamera.WorldToScreenPoint(stageCorners[0]), hi = ui.menuCamera.WorldToScreenPoint(stageCorners[2]);
        Rect area = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        Vector2 center = area.center;
        float d = Mathf.Min(Mathf.Clamp(width * .25f, 240, 350), Mathf.Max(90, area.height - 50));
        float radius = Mathf.Max(0, width * .5f - d * .61f - 24), h = area.height;
        for (int i = 0; i < discs.Count; i++)
        {
            Disc disc = discs[i]; float offset = i - position, theta = Mathf.Clamp(offset, -2.5f, 2.5f) * assets.angularSpacing;
            float sine = Mathf.Sin(theta), depth = (Mathf.Cos(theta) - 1) * 230 + sine * 150;
            float factor = 1100 / (1100 - depth);
            disc.diameter = d * u;
            disc.root.position = new Vector3(((center.x - width * .5f) / factor + sine * radius) * u,
                ((center.y - ui.Viewport.y * .5f) / factor + sine * Mathf.Min(h * .22f, 85)) * u, -depth * u);
            disc.root.rotation = Quaternion.AngleAxis(13 - sine * 4, Vector3.forward) * Quaternion.AngleAxis(20 - sine * 12, Vector3.up) * Quaternion.AngleAxis(10, Vector3.right);
            disc.root.localScale = Vector3.one * disc.diameter * (1 + sine * .055f);
            Rect bounds = Bounds(disc.root);
            float fit = Mathf.Min(1, (area.width - 48) / Mathf.Max(1, bounds.width), (area.height - 48) / Mathf.Max(1, bounds.height));
            if (fit < 1) disc.root.localScale *= fit * .98f;
            for (int pass = 0; pass < 3; pass++)
            {
                bounds = Bounds(disc.root);
                float dx = Mathf.Max(0, area.xMin + 24 - bounds.xMin) - Mathf.Max(0, bounds.xMax - area.xMax + 24);
                float dy = Mathf.Max(0, area.yMin + 24 - bounds.yMin) - Mathf.Max(0, bounds.yMax - area.yMax + 24);
                disc.root.position += new Vector3(dx, dy, 0) * u / factor;
            }
            disc.alpha = Mathf.Clamp01((2.55f - Mathf.Abs(offset)) / .45f);
            ApplyLaunch(i, disc);
            disc.renderer.enabled = disc.alpha > .02f && gameObject.activeInHierarchy;
            disc.renderer.sortingOrder = launchStart >= 0 && i == Selection ? 1000 : Mathf.RoundToInt(depth + 300);
            disc.renderer.GetPropertyBlock(block); block.SetColor("_Color", new Color(1, 1, 1, disc.alpha)); disc.renderer.SetPropertyBlock(block);
        }
        UpdatePath();
    }

    private readonly List<Vector2> pathPoints = new List<Vector2>();
    private readonly Vector2[] ringPoints = new Vector2[48];
    /// <summary>把可见光盘中心（两端外延）与选中光盘外圈的旋转虚线点交给背景连线绘制。</summary>
    private void UpdatePath()
    {
        if (path == null || !path.isActiveAndEnabled || Selection >= discs.Count) return;
        pathPoints.Clear();
        for (int i = 0; i < discs.Count; i++)
            if (discs[i].alpha > .02f) pathPoints.Add(ui.menuCamera.WorldToScreenPoint(discs[i].root.position));
        if (pathPoints.Count >= 2)
        {
            pathPoints.Insert(0, pathPoints[0] + (pathPoints[0] - pathPoints[1]) * .9f);
            int n = pathPoints.Count; pathPoints.Add(pathPoints[n - 1] + (pathPoints[n - 1] - pathPoints[n - 2]) * .9f);
        }
        Transform root = discs[Selection].root;
        float spin = assets.reduceMotion ? 0 : Time.unscaledTime * .35f, since = Time.unscaledTime - confirmedAt;
        float radius = .58f + (since < .45f && !assets.reduceMotion ? Mathf.Sin(since / .45f * Mathf.PI) * .07f : 0);
        for (int k = 0; k < ringPoints.Length; k++)
        {
            float angle = k * Mathf.PI * 2 / ringPoints.Length + spin;
            ringPoints[k] = ui.menuCamera.WorldToScreenPoint(root.TransformPoint(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0)));
        }
        // 切换途中环淡出，落定后淡入，避免环在两张光盘之间“漂移”。
        float launch = LaunchProgress;
        path.SetShape(launch > 0 ? System.Array.Empty<Vector2>() : pathPoints.ToArray(), ringPoints, Mathf.Clamp01(1 - Mathf.Abs(position - Selection) * 2.5f) * (1 - Mathf.Clamp01(launch * 3)));
    }
    private Rect Bounds(Transform root)
    {
        Vector2 low = Vector2.one * float.MaxValue, high = Vector2.one * float.MinValue;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
        {
            Vector2 point = ui.menuCamera.WorldToScreenPoint(root.TransformPoint(new Vector3(x * .5f, y * .5f, 0)));
            low = Vector2.Min(low, point); high = Vector2.Max(high, point);
        }
        return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
    }
    // 进入战斗的光盘推进：选中光盘边旋转边转正、朝镜头放大直至铺满，其余光盘与连线淡出，随后由转场幕布接管。
    private float launchStart = -1, launchDuration = .7f;
    private float LaunchProgress => launchStart < 0 ? 0 : Mathf.Clamp01((Time.unscaledTime - launchStart) / launchDuration);
    public void Launch(float duration) { launchDuration = Mathf.Max(.01f, duration); launchStart = Time.unscaledTime; InputEnabled = false; ResetHover(); }
    public void ResetLaunch() { launchStart = -1; Draw(); }
    private void ApplyLaunch(int index, Disc disc)
    {
        float p = LaunchProgress; if (p <= 0) return;
        float e = p * p * (3 - 2 * p);
        if (index != Selection) { disc.alpha *= 1 - Mathf.Clamp01(p * 2.5f); return; }
        Transform cam = ui.menuCamera.transform;
        disc.root.position = Vector3.Lerp(disc.root.position, cam.position + cam.forward * 520 * ui.Unit, e * e);
        disc.root.rotation = Quaternion.Slerp(disc.root.rotation, cam.rotation, e) * Quaternion.AngleAxis(-e * 720, Vector3.forward);
        disc.root.localScale *= Mathf.Lerp(1, 1.6f, e);
    }

    public Rect[] VisibleBounds()
    {
        var result = new List<Rect>(); foreach (Disc d in discs) if (d.alpha > .02f) result.Add(Bounds(d.root)); return result.ToArray();
    }
    // 确认反馈：旧手绘描线已移除，改为选中虚线环向外扩张后回落（见 UpdatePath）。
    private float confirmedAt = -9;
    private void Confirm() { if (gameObject.activeInHierarchy && Selection < discs.Count) confirmedAt = Time.unscaledTime; }
    private void OnDisable() { StopMotion(); foreach (Disc d in discs) { d.hover.Stop(); if (d.renderer != null) d.renderer.enabled = false; } }
    private void OnDestroy() { StopMotion(); foreach (Disc d in discs) d.hover.Stop(); }
}

