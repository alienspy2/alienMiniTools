using RoseEngine;

/// <summary>데모 공통 유틸리티 — 카메라 생성, 폰트 로딩 보일러플레이트 제거용.</summary>
public static class DemoUtils
{
    /// <summary>카메라 생성. lookAt이 null이면 LookAt 생략.</summary>
    public static (Camera cam, Transform transform) CreateCamera(
        Vector3 position, Vector3? lookAt = null,
        CameraClearFlags clearFlags = CameraClearFlags.Skybox,
        Color? backgroundColor = null)
    {
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        cam.clearFlags = clearFlags;
        if (backgroundColor.HasValue)
            cam.backgroundColor = backgroundColor.Value;
        camObj.transform.position = position;
        if (lookAt.HasValue)
            camObj.transform.LookAt(lookAt.Value);
        return (cam, camObj.transform);
    }

    /// <summary>NotoSans 폰트 로드 (fallback: CreateDefault).</summary>
    public static Font LoadFont(int size = 32)
    {
        var fontPath = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(), "Assets", "Fonts", "NotoSans_eng.ttf");
        try { return Font.CreateFromFile(fontPath, size); }
        catch { return Font.CreateDefault(size); }
    }
}
