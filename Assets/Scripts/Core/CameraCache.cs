using UnityEngine;

/// <summary>
/// 缓存 Camera.main，避免每帧重复查找 MainCamera。
/// Unity 的 Object == null 能识别已销毁对象，场景重载后会自动重新查找。
/// </summary>
public static class CameraCache
{
    private static Camera _main;

    public static Camera Main
    {
        get
        {
            if (_main == null)
                _main = Camera.main;
            return _main;
        }
    }

    public static void Reset() => _main = null;
}
