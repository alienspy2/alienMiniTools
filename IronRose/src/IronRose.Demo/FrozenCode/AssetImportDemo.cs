using RoseEngine;

public class AssetImportDemo : MonoBehaviour
{
    private string[] _assetFolders = System.Array.Empty<string>();
    private int _currentIndex;
    private GameObject? _meshObj;
    private GameObject? _spriteObj;
    private TextRenderer? _nameLabel;
    private GameObject? _labelObj;
    private GameObject? _floorObj;
    private Font? _font;

    // Orbit camera
    private Transform? _camTransform;
    private float _orbitYaw = 0f;
    private float _orbitPitch = 15f;
    private float _orbitDistance = 5f;
    private bool _isDragging;

    public override void Awake()
    {
        Debug.Log("[AssetImportDemo] Loading asset from pipeline...");

        // Camera
        var (_, camTransform) = DemoUtils.CreateCamera(Vector3.zero);
        _camTransform = camTransform;
        UpdateOrbitCamera();

        // Light
        var lightObj = new GameObject("Main Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.white;
        light.intensity = 2.0f;
        light.range = 15f;
        lightObj.transform.position = new Vector3(2f, 5f, -2f);

        // Fill light
        var fillObj = new GameObject("Fill Light");
        var fill = fillObj.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(0.6f, 0.7f, 0.9f);
        fill.intensity = 1.0f;
        fill.range = 10f;
        fillObj.transform.position = new Vector3(-3f, 1f, -3f);

        // Font for mesh name label
        _font = DemoUtils.LoadFont(32);

        // Name label (positioned in front of camera each frame)
        _labelObj = new GameObject("MeshNameLabel");
        _nameLabel = _labelObj.AddComponent<TextRenderer>();
        _nameLabel.font = _font;
        _nameLabel.text = "";
        _nameLabel.color = Color.white;
        _nameLabel.alignment = TextAlignment.Right;
        _nameLabel.sortingOrder = 100;
        _labelObj.transform.position = new Vector3(4.9f, 2.6f, 0f);

        // Floor plane (repositioned per asset in LoadCurrentAsset)
        _floorObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
        _floorObj.name = "Floor";
        _floorObj.GetComponent<MeshRenderer>()!.material = new Material(new Color(0.25f, 0.25f, 0.25f))
        {
            roughness = 0.85f,
            metallic = 0.0f,
        };

        // Scan asset folders
        var assetsDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Assets");
        var houseDir = System.IO.Path.Combine(assetsDir, "houseInTheForest");
        if (System.IO.Directory.Exists(houseDir))
        {
            var dirs = System.IO.Directory.GetDirectories(houseDir);
            var validDirs = new System.Collections.Generic.List<string>();
            foreach (var dir in dirs)
            {
                var glb = System.IO.Path.Combine(dir, "model.glb");
                if (System.IO.File.Exists(glb))
                    validDirs.Add(dir);
            }
            validDirs.Sort();
            _assetFolders = validDirs.ToArray();
        }

        if (_assetFolders.Length == 0)
        {
            Debug.LogWarning("[AssetImportDemo] No asset folders found, showing placeholder cube");
            var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = "Placeholder";
            placeholder.GetComponent<MeshRenderer>()!.material = new Material(new Color(0.8f, 0.4f, 0.1f));
            _nameLabel.text = "No assets found";
            return;
        }

        // Find initial index for HandcraftedWoodenLantern_01, fallback to 0
        _currentIndex = 0;
        for (int i = 0; i < _assetFolders.Length; i++)
        {
            if (System.IO.Path.GetFileName(_assetFolders[i]) == "HandcraftedWoodenLantern_01")
            {
                _currentIndex = i;
                break;
            }
        }

        LoadCurrentAsset();
        Debug.Log("[AssetImportDemo] Asset import demo ready! Use Left/Right arrows to browse.");
    }

    public override void Update()
    {
        // Orbit camera drag
        if (Input.GetMouseButtonDown(0))
            _isDragging = true;
        if (Input.GetMouseButtonUp(0))
            _isDragging = false;

        if (_isDragging)
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            _orbitYaw += dx * -0.3f;
            _orbitPitch += dy * 0.3f;
            _orbitPitch = Mathf.Clamp(_orbitPitch, -85f, 85f);
            UpdateOrbitCamera();
        }

        // Scroll to zoom
        float scroll = Input.mouseScrollDelta;
        if (scroll != 0f)
        {
            _orbitDistance -= scroll * 0.5f;
            _orbitDistance = Mathf.Clamp(_orbitDistance, 1f, 20f);
            UpdateOrbitCamera();
        }

        // Asset navigation
        if (_assetFolders.Length == 0) return;

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            _currentIndex = (_currentIndex + 1) % _assetFolders.Length;
            LoadCurrentAsset();
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            _currentIndex = (_currentIndex - 1 + _assetFolders.Length) % _assetFolders.Length;
            LoadCurrentAsset();
        }
    }

    private void UpdateOrbitCamera()
    {
        if (_camTransform == null) return;

        float yawRad = _orbitYaw * Mathf.Deg2Rad;
        float pitchRad = _orbitPitch * Mathf.Deg2Rad;

        float x = _orbitDistance * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad);
        float y = _orbitDistance * Mathf.Sin(pitchRad);
        float z = -_orbitDistance * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad);

        _camTransform.position = new Vector3(x, y, z);
        _camTransform.LookAt(Vector3.zero);

        // Update label to stay in front of camera (top-right)
        if (_labelObj != null)
        {
            var pos = _camTransform.position
                + _camTransform.right * 4.9f
                + _camTransform.up * 2.6f
                + _camTransform.forward * 5f;
            _labelObj.transform.position = pos;
            _labelObj.transform.rotation = _camTransform.rotation;
        }

        // Update preview sprite to stay in front of camera (below label, top-right)
        if (_spriteObj != null)
        {
            var pos = _camTransform.position
                + _camTransform.right * 4.2f
                + _camTransform.up * 1.0f
                + _camTransform.forward * 5f;
            _spriteObj.transform.position = pos;
            _spriteObj.transform.rotation = _camTransform.rotation;
        }
    }

    private void LoadCurrentAsset()
    {
        // Destroy previous objects
        if (_meshObj != null) { RoseEngine.Object.Destroy(_meshObj); _meshObj = null; }
        if (_spriteObj != null) { RoseEngine.Object.Destroy(_spriteObj); _spriteObj = null; }

        var dir = _assetFolders[_currentIndex];
        var folderName = System.IO.Path.GetFileName(dir);
        var glbPath = System.IO.Path.Combine(dir, "model.glb");

        // Update label
        if (_nameLabel != null)
            _nameLabel.text = $"{folderName}\n({_currentIndex + 1}/{_assetFolders.Length})";

        // Load mesh
        var mesh = Resources.Load<Mesh>(glbPath);
        if (mesh == null)
        {
            Debug.LogError($"[AssetImportDemo] Failed to load mesh: {glbPath}");
            return;
        }

        Debug.Log($"[AssetImportDemo] Loaded: {folderName} ({mesh.vertices.Length} verts, {mesh.indices.Length / 3} tris)");

        var material = Resources.Load<Material>(glbPath);

        _meshObj = new GameObject("Imported Mesh");
        var filter = _meshObj.AddComponent<MeshFilter>();
        var renderer = _meshObj.AddComponent<MeshRenderer>();
        filter.mesh = mesh;
        renderer.material = material ?? new Material(new Color(0.85f, 0.65f, 0.4f));
        _meshObj.transform.position = new Vector3(0, -mesh.bounds.min.y, 0);

        // Load preview sprite
        var pngPath = System.IO.Path.Combine(dir, "preview.png");
        if (System.IO.File.Exists(pngPath))
        {
            var tex = Texture2D.LoadFromFile(pngPath);
            var sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

            _spriteObj = new GameObject("Preview Sprite");
            var sr = _spriteObj.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            _spriteObj.transform.localScale = Vector3.one * 0.2f;
        }

        UpdateOrbitCamera();
    }
}
