using System;
using UnityEngine;

public class SpriteDemo : MonoBehaviour
{
    private GameObject? _starObj;

    public override void Awake()
    {
        Debug.Log("[SpriteDemo] Setting up Sprite Renderer demo...");

        // Camera
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0, 0, -8f);

        // 1. Checkerboard sprite (basic rendering)
        var checkerTex = CreateCheckerboardTexture(64, 64, 8);
        var checkerSprite = Sprite.Create(checkerTex,
            new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        var checkerObj = new GameObject("Checkerboard");
        var checkerSR = checkerObj.AddComponent<SpriteRenderer>();
        checkerSR.sprite = checkerSprite;
        checkerObj.transform.position = new Vector3(-3f, 1.5f, 0);

        // 2. Semi-transparent tinted sprite (alpha blending + color tint)
        var gradientTex = CreateGradientTexture(64, 64);
        var gradientSprite = Sprite.Create(gradientTex,
            new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        var tintObj = new GameObject("TintedSprite");
        var tintSR = tintObj.AddComponent<SpriteRenderer>();
        tintSR.sprite = gradientSprite;
        tintSR.color = new Color(0.3f, 0.8f, 1.0f, 0.7f);
        tintObj.transform.position = new Vector3(0f, 1.5f, 0);

        // 3. FlipX / FlipY sprites
        var arrowTex = CreateArrowTexture(64, 64);
        var arrowSprite = Sprite.Create(arrowTex,
            new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));

        var normalObj = new GameObject("Arrow Normal");
        var normalSR = normalObj.AddComponent<SpriteRenderer>();
        normalSR.sprite = arrowSprite;
        normalObj.transform.position = new Vector3(-2f, -1f, 0);

        var flipXObj = new GameObject("Arrow FlipX");
        var flipXSR = flipXObj.AddComponent<SpriteRenderer>();
        flipXSR.sprite = arrowSprite;
        flipXSR.flipX = true;
        flipXObj.transform.position = new Vector3(-0.5f, -1f, 0);

        var flipYObj = new GameObject("Arrow FlipY");
        var flipYSR = flipYObj.AddComponent<SpriteRenderer>();
        flipYSR.sprite = arrowSprite;
        flipYSR.flipY = true;
        flipYObj.transform.position = new Vector3(1f, -1f, 0);

        // 4. Rotating star (Y-axis rotation to demonstrate 3D space)
        var starTex = CreateStarTexture(64, 64);
        var starSprite = Sprite.Create(starTex,
            new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
        _starObj = new GameObject("Rotating Star");
        var starSR = _starObj.AddComponent<SpriteRenderer>();
        starSR.sprite = starSprite;
        starSR.color = new Color(1f, 0.9f, 0.2f, 1f);
        _starObj.transform.position = new Vector3(3f, 1.5f, 0);

        // 5. Sorting order overlap demo (red behind, blue in front)
        var solidTex = CreateSolidTexture(32, 32, 255, 255, 255, 200);
        var solidSprite = Sprite.Create(solidTex,
            new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));

        var redObj = new GameObject("Red Behind");
        var redSR = redObj.AddComponent<SpriteRenderer>();
        redSR.sprite = solidSprite;
        redSR.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        redSR.sortingOrder = 0;
        redObj.transform.position = new Vector3(2.8f, -1f, 0);

        var blueObj = new GameObject("Blue Front");
        var blueSR = blueObj.AddComponent<SpriteRenderer>();
        blueSR.sprite = solidSprite;
        blueSR.color = new Color(0.2f, 0.4f, 1f, 0.8f);
        blueSR.sortingOrder = 1;
        blueObj.transform.position = new Vector3(3.2f, -0.8f, 0);

        Debug.Log("[SpriteDemo] Scene ready — 5 sprite demos active");
    }

    public override void Update()
    {
        // Rotate the star around Y axis to show 3D nature of sprite quad
        if (_starObj != null)
        {
            _starObj.transform.Rotate(0, 90f * Time.deltaTime, 0);
        }
    }

    // --- Procedural texture helpers ---

    private static Texture2D CreateCheckerboardTexture(int w, int h, int cellSize)
    {
        var tex = new Texture2D(w, h);
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool isWhite = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                byte val = isWhite ? (byte)240 : (byte)60;
                int idx = (y * w + x) * 4;
                data[idx] = val;
                data[idx + 1] = val;
                data[idx + 2] = val;
                data[idx + 3] = 255;
            }
        }
        tex.SetPixels(data);
        return tex;
    }

    private static Texture2D CreateGradientTexture(int w, int h)
    {
        var tex = new Texture2D(w, h);
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1);
                float v = (float)y / (h - 1);
                int idx = (y * w + x) * 4;
                data[idx] = (byte)(u * 255);
                data[idx + 1] = (byte)(v * 255);
                data[idx + 2] = 200;
                data[idx + 3] = (byte)(128 + u * 127); // alpha gradient
            }
        }
        tex.SetPixels(data);
        return tex;
    }

    private static Texture2D CreateArrowTexture(int w, int h)
    {
        var tex = new Texture2D(w, h);
        var data = new byte[w * h * 4];

        // Fill with transparent
        for (int i = 0; i < data.Length; i += 4)
            data[i + 3] = 0;

        // Draw a right-pointing arrow
        int cx = w / 2, cy = h / 2;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Arrow body (horizontal bar)
                bool body = x >= w / 5 && x <= w * 3 / 5 &&
                            y >= h * 2 / 5 && y <= h * 3 / 5;
                // Arrow head (triangle)
                float fx = (float)(x - w * 3 / 5) / (w * 2 / 5);
                float dy = MathF.Abs(y - cy);
                bool head = x >= w * 3 / 5 && fx >= 0 && dy <= (1f - fx) * h * 0.4f;

                if (body || head)
                {
                    int idx = (y * w + x) * 4;
                    data[idx] = 60;
                    data[idx + 1] = 200;
                    data[idx + 2] = 60;
                    data[idx + 3] = 255;
                }
            }
        }
        tex.SetPixels(data);
        return tex;
    }

    private static Texture2D CreateStarTexture(int w, int h)
    {
        var tex = new Texture2D(w, h);
        var data = new byte[w * h * 4];

        float cx = w * 0.5f, cy = h * 0.5f;
        float outerR = w * 0.45f, innerR = w * 0.18f;
        int points = 5;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x - cx, dy = y - cy;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float angle = MathF.Atan2(dy, dx);
                if (angle < 0) angle += MathF.PI * 2;

                // Star radius at this angle
                float sector = MathF.PI * 2 / points;
                float halfSector = sector * 0.5f;
                float localAngle = angle % sector;
                if (localAngle > halfSector) localAngle = sector - localAngle;
                float t = localAngle / halfSector;
                float starR = innerR + (outerR - innerR) * (1f - t);

                int idx = (y * w + x) * 4;
                if (dist <= starR)
                {
                    data[idx] = 255;
                    data[idx + 1] = 230;
                    data[idx + 2] = 50;
                    data[idx + 3] = 255;
                }
                else
                {
                    data[idx + 3] = 0;
                }
            }
        }
        tex.SetPixels(data);
        return tex;
    }

    private static Texture2D CreateSolidTexture(int w, int h, byte r, byte g, byte b, byte a)
    {
        var tex = new Texture2D(w, h);
        var data = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            data[i * 4] = r;
            data[i * 4 + 1] = g;
            data[i * 4 + 2] = b;
            data[i * 4 + 3] = a;
        }
        tex.SetPixels(data);
        return tex;
    }
}
