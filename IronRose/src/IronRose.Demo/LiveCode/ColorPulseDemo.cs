using RoseEngine;
using IronRose.Scripting;

/// <summary>
/// LiveCode 핫 리로드 테스트용 데모.
/// IHotReloadable을 구현하여 코드 수정 후에도 상태가 보존됩니다.
/// </summary>
public class ColorPulseDemo : MonoBehaviour, IHotReloadable
{
    private Material? _cubeMat;
    private GameObject? _cube;
    private float _rotSpeed = 45f;
    private Vector3 _savedRotation;

    public override void Awake()
    {
        Debug.Log("[ColorPulseDemo] LiveCode demo starting...");

        // Camera
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        camObj.transform.position = new Vector3(0, 1.5f, -5f);

        // Light
        var lightGo = new GameObject("Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 3f;
        lightGo.transform.position = new Vector3(2, 4, -2);
        lightGo.transform.LookAt(Vector3.zero);

        // Pulsing cube
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube.name = "PulseCube";
        _cube.transform.localScale = new Vector3(2f, 2f, 2f);
        _cubeMat = new Material();
        _cubeMat.metallic = 0.3f;
        _cubeMat.roughness = 0.4f;
        _cube.GetComponent<MeshRenderer>()!.material = _cubeMat;

        Debug.Log("[ColorPulseDemo] Ready! Edit this file to hot-reload.");
    }

    public override void Update()
    {
        if (_cube == null || _cubeMat == null) return;

        // 시간에 따른 색상 변화
        float t = Time.time * 1;
        float r = (Mathf.Sin(t * 1.0f) + 1f) * 0.5f;
        float g = (Mathf.Sin(t * 1.3f + 2f) + 1f) * 0.5f;
        float b = (Mathf.Sin(t * 1.7f + 4f) + 1f) * 0.5f;
        _cubeMat.color = new Color(r, g, b);

        // 회전
        var rot = _cube.transform.rotation;
        _cube.transform.rotation = Quaternion.Euler(
            rot.eulerAngles.x + _rotSpeed * Time.deltaTime * 0.7f,
            rot.eulerAngles.y + _rotSpeed * Time.deltaTime,
            rot.eulerAngles.z + _rotSpeed * Time.deltaTime * 0.3f);

        // 스케일 펄스
        float scale = 2f + Mathf.Sin(t * 2f) * 0.3f;
        _cube.transform.localScale = new Vector3(scale, scale, scale);
    }

    // === IHotReloadable: 핫 리로드 시 상태 보존 ===

    public string SerializeState()
    {
        var rot = _cube?.transform.rotation.eulerAngles ?? Vector3.zero;
        return $"rotSpeed={_rotSpeed}\nrotX={rot.x}\nrotY={rot.y}\nrotZ={rot.z}";
    }

    public void DeserializeState(string state)
    {
        foreach (var line in state.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line.Substring(0, eq);
            var val = line.Substring(eq + 1);
            switch (key)
            {
                case "rotSpeed": float.TryParse(val, out _rotSpeed); break;
                case "rotX": _savedRotation.x = float.TryParse(val, out var rx) ? rx : 0; break;
                case "rotY": _savedRotation.y = float.TryParse(val, out var ry) ? ry : 0; break;
                case "rotZ": _savedRotation.z = float.TryParse(val, out var rz) ? rz : 0; break;
            }
        }

        // 복원된 회전 적용
        if (_cube != null)
            _cube.transform.rotation = Quaternion.Euler(_savedRotation);

        Debug.Log($"[ColorPulseDemo] State restored: rotSpeed={_rotSpeed}");
    }
}
