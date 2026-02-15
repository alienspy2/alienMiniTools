using RoseEngine;

public class TextDemo : MonoBehaviour
{
    public override void Start()
    {
        // Camera setup
        var camGo = new GameObject("Camera");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = new Vector3(0, 0, -5);

        // Load font from Assets
        var fontPath = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(), "Assets", "Fonts", "NotoSans_eng.ttf");
        var fontLarge = Font.CreateFromFile(fontPath, 48);
        var fontSmall = Font.CreateFromFile(fontPath, 24);

        // --- HUD: top-left info overlay ---
        // FOV=60, distance=5 -> halfH=2.887, halfW=5.132

        // 1. Basic text (white, left-aligned)
        var go1 = new GameObject("BasicText");
        var tr1 = go1.AddComponent<TextRenderer>();
        tr1.font = fontLarge;
        tr1.text = "Hello, IronRose!";
        tr1.color = Color.white;
        go1.transform.position = new Vector3(-2f, 1.5f, 0f);

        // 2. Colored text (red)
        var go2 = new GameObject("ColorText");
        var tr2 = go2.AddComponent<TextRenderer>();
        tr2.font = fontLarge;
        tr2.text = "Red Text";
        tr2.color = new Color(1f, 0.2f, 0.2f, 1f);
        go2.transform.position = new Vector3(-2f, 0.5f, 0f);

        // 3. Center-aligned text
        var go3 = new GameObject("CenterText");
        var tr3 = go3.AddComponent<TextRenderer>();
        tr3.font = fontLarge;
        tr3.text = "Centered";
        tr3.color = Color.yellow;
        tr3.alignment = TextAlignment.Center;
        go3.transform.position = new Vector3(0f, -0.5f, 0f);

        // 4. Multiline text
        var go4 = new GameObject("MultilineText");
        var tr4 = go4.AddComponent<TextRenderer>();
        tr4.font = fontLarge;
        tr4.text = "Line 1\nLine 2\nLine 3";
        tr4.color = new Color(0.5f, 1f, 0.5f, 1f);
        go4.transform.position = new Vector3(-2f, -1.5f, 0f);

        // 5. 3D rotating text (Y-axis rotation to demonstrate 3D)
        var go5 = new GameObject("RotatingText");
        var tr5 = go5.AddComponent<TextRenderer>();
        tr5.font = fontLarge;
        tr5.text = "3D Text!";
        tr5.color = new Color(0.5f, 0.8f, 1f, 1f);
        go5.transform.position = new Vector3(2f, -2.5f, 0f);
        go5.AddComponent<TextRotator>();

        Debug.Log("[TextDemo] 6 text objects created (1 info + 5 demo)");
    }
}

// Rotation helper
public class TextRotator : MonoBehaviour
{
    public override void Update()
    {
        transform.rotation *= Quaternion.Euler(0, 60f * Time.deltaTime, 0);
    }
}
