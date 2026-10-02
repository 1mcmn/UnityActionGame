using UnityEngine;

/// <summary>短刀光、池内粒子与材质属性块；不修改共享材质。</summary>
public class CombatFeedback : MonoBehaviour
{
    public Transform weaponTip;
    public Material effectMaterial;
    public Color trailColor = new Color(.15f, .8f, 1f);
    private TrailRenderer _trail;
    private ParticleSystem _sparks;
    private Renderer[] _renderers;
    private Color[] _colors;
    private MaterialPropertyBlock _block;
    private float _pulseEnd;
    private Color _pulse;
    private bool _warning;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        _renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _colors = new Color[_renderers.Length]; _block = new MaterialPropertyBlock();
        for (int i = 0; i < _renderers.Length; i++)
            _colors[i] = _renderers[i].sharedMaterial != null && _renderers[i].sharedMaterial.HasProperty(BaseColor) ? _renderers[i].sharedMaterial.GetColor(BaseColor) : Color.white;
        if (weaponTip != null && effectMaterial != null)
        {
            _trail = weaponTip.gameObject.AddComponent<TrailRenderer>(); _trail.sharedMaterial = effectMaterial;
            _trail.time = .12f; _trail.minVertexDistance = .025f; _trail.widthMultiplier = .18f;
            _trail.startColor = trailColor; _trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0);
            _trail.emitting = false; _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var go = new GameObject("CombatSparks"); go.transform.SetParent(transform, false);
        _sparks = go.AddComponent<ParticleSystem>();
        var main = _sparks.main; main.playOnAwake = false; main.loop = false; main.duration = .35f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.1f, .28f); main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(.035f, .075f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 96;
        var emission = _sparks.emission; emission.enabled = false;
        var shape = _sparks.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .05f;
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = effectMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    public void SetTrail(bool enabled) { if (_trail != null) { if (enabled && !_trail.emitting) _trail.Clear(); _trail.emitting = enabled; } }
    public void SetWarning(bool enabled) { _warning = enabled; }
    public void Pulse(Color color, float duration = .12f) { _pulse = color; _pulseEnd = Time.time + duration; }
    public void Impact(Vector3 position, Color color)
    {
        if (_sparks == null) return;
        _sparks.transform.position = position;
        var emit = new ParticleSystem.EmitParams { startColor = color };
        _sparks.Emit(emit, 14);
    }
    private void LateUpdate()
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            _renderers[i].GetPropertyBlock(_block);
            _block.SetColor(BaseColor, _warning ? new Color(1f, .12f, .1f) : Time.time < _pulseEnd ? _pulse : _colors[i]);
            _renderers[i].SetPropertyBlock(_block);
        }
    }
    private void OnDisable()
    {
        _warning = false; _pulseEnd = 0; SetTrail(false); if (_trail != null) _trail.Clear();
        if (_sparks != null) _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_renderers != null) for (int i = 0; i < _renderers.Length; i++)
        { _renderers[i].GetPropertyBlock(_block); _block.SetColor(BaseColor, _colors[i]); _renderers[i].SetPropertyBlock(_block); }
    }
}
