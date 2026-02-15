using IronRose.Rendering;
using RoseEngine;

public class CornellBoxDemo : MonoBehaviour
{
    // Post-processing 파라미터 편집용
    private BloomEffect? _bloom;
    private TonemapEffect? _tonemap;
    private TextRenderer? _hudText;
    private int _selectedIndex;

    private static readonly string[] ParamNames = { "Bloom Threshold", "Bloom SoftKnee", "Bloom Intensity", "Exposure" };
    private static readonly float[] ParamSteps = { 0.05f, 0.05f, 0.1f, 0.05f };
    private static readonly float[] ParamMin = { 0f, 0f, 0f, 0.01f };
    private static readonly float[] ParamMax = { 10f, 1f, 5f, 10f };

    public override void Awake()
    {
        Debug.Log("[DemoLauncher] Setting up Cornell Box scene...");

        // Camera — centered, looking into the box
        DemoUtils.CreateCamera(
            new Vector3(0, 0, -8f),
            clearFlags: CameraClearFlags.SolidColor,
            backgroundColor: Color.black);

        // Indoor scene: minimal sky ambient (enclosed room)
        RenderSettings.ambientIntensity = 0.08f;

        // --- Cornell Box Light (ceiling area, white) ---
        var lightObj = new GameObject("Ceiling Light");
        var ceilingLight = lightObj.AddComponent<Light>();
        ceilingLight.type = LightType.Point;
        ceilingLight.color = Color.white;
        ceilingLight.intensity = 10f;
        ceilingLight.range = 10f;
        ceilingLight.shadows = true;
        lightObj.transform.position = new Vector3(0, 2.3f, 0);

        // --- Cornell Box Walls (5 quads, front open) ---
        float size = 5f;
        float h = size / 2f;

        // White walls: floor, ceiling, back
        CreateWall("Floor",      new Vector3(0, -h, 0), new Vector3(-90, 0, 0),  size, Color.white);
        CreateWall("Ceiling",    new Vector3(0,  h, 0), new Vector3(90, 0, 0),   size, Color.white);
        CreateWall("Back Wall",  new Vector3(0, 0, h),  new Vector3(0, 180, 0),  size, Color.white);

        // Red left wall
        CreateWall("Left Wall",  new Vector3(-h, 0, 0), new Vector3(0, 90, 0),   size, new Color(0.75f, 0.05f, 0.05f));

        // Green right wall
        CreateWall("Right Wall", new Vector3(h, 0, 0),  new Vector3(0, -90, 0),  size, new Color(0.05f, 0.55f, 0.05f));

        // --- Tall white block (left-back, slightly rotated) ---
        var tallBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tallBlock.name = "Tall Block";
        tallBlock.GetComponent<MeshRenderer>()!.material = new Material(Color.white);
        tallBlock.transform.localScale = new Vector3(1.3f, 3.0f, 1.3f);
        tallBlock.transform.position = new Vector3(-0.9f, -h + 1.5f, 0.8f);
        tallBlock.transform.Rotate(0, 17, 0);

        // --- Short white block (right-front, slightly rotated) ---
        var shortBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shortBlock.name = "Short Block";
        shortBlock.GetComponent<MeshRenderer>()!.material = new Material(Color.white);
        shortBlock.transform.localScale = new Vector3(1.3f, 1.5f, 1.3f);
        shortBlock.transform.position = new Vector3(0.9f, -h + 0.75f, -0.7f);
        shortBlock.transform.Rotate(0, -17, 0);

        // --- Post-processing 파라미터 조정 ---
        var stack = RenderSettings.postProcessing;
        if (stack != null)
        {
            _bloom = stack.GetEffect<BloomEffect>();
            if (_bloom != null)
            {
                _bloom.Threshold = 0.8f;
                _bloom.SoftKnee = 0.5f;
                _bloom.Intensity = 2f;
            }

            _tonemap = stack.GetEffect<TonemapEffect>();
            if (_tonemap != null)
            {
                _tonemap.Exposure = 0.8f;
            }
        }

        // --- HUD TextRenderer (화면 오른쪽 위) ---
        var hudObj = new GameObject("PostProcess HUD");
        _hudText = hudObj.AddComponent<TextRenderer>();
        _hudText.font = Font.CreateDefault(24);
        _hudText.color = Color.yellow * .5f;
        _hudText.alignment = TextAlignment.Right;
        _hudText.sortingOrder = 100;
        _hudText.pixelsPerUnit = 200f;
        // 카메라(z=-8)에서 4 unit 앞, FOV 60° 기준 오른쪽 위 코너
        hudObj.transform.position = new Vector3(3.5f, 1.8f, -4f);

        Debug.Log($"[DemoLauncher] Cornell Box ready - Screen: {Screen.width}x{Screen.height}");
    }

    public override void Update()
    {
        if (_bloom == null || _tonemap == null || _hudText == null) return;

        // 위/아래: 항목 선택
        if (Input.GetKeyDown(KeyCode.UpArrow))
            _selectedIndex = (_selectedIndex - 1 + ParamNames.Length) % ParamNames.Length;
        if (Input.GetKeyDown(KeyCode.DownArrow))
            _selectedIndex = (_selectedIndex + 1) % ParamNames.Length;

        // 좌/우: 값 조정
        float delta = 0f;
        if (Input.GetKey(KeyCode.RightArrow)) delta = ParamSteps[_selectedIndex] * Time.deltaTime * 5f;
        if (Input.GetKey(KeyCode.LeftArrow))  delta = -ParamSteps[_selectedIndex] * Time.deltaTime * 5f;

        if (delta != 0f)
        {
            switch (_selectedIndex)
            {
                case 0: _bloom.Threshold = Mathf.Clamp(_bloom.Threshold + delta, ParamMin[0], ParamMax[0]); break;
                case 1: _bloom.SoftKnee  = Mathf.Clamp(_bloom.SoftKnee + delta,  ParamMin[1], ParamMax[1]); break;
                case 2: _bloom.Intensity  = Mathf.Clamp(_bloom.Intensity + delta, ParamMin[2], ParamMax[2]); break;
                case 3: _tonemap.Exposure = Mathf.Clamp(_tonemap.Exposure + delta, ParamMin[3], ParamMax[3]); break;
            }
        }

        // HUD 텍스트 갱신
        float[] values = { _bloom.Threshold, _bloom.SoftKnee, _bloom.Intensity, _tonemap.Exposure };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ParamNames.Length; i++)
        {
            string marker = (i == _selectedIndex) ? "> " : "  ";
            sb.AppendLine($"{marker}{ParamNames[i]}: {values[i]:F2}");
        }
        _hudText.text = sb.ToString().TrimEnd();
    }

    private static void CreateWall(string name, Vector3 position, Vector3 euler, float size, Color color)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
        wall.name = name;
        wall.GetComponent<MeshRenderer>()!.material = new Material(color);
        wall.transform.position = position;
        wall.transform.Rotate(euler.x, euler.y, euler.z);
        wall.transform.localScale = new Vector3(size, size, 1f);
    }
}
