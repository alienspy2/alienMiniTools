using UnityEngine;

public class CornellBoxDemo : MonoBehaviour
{
    public override void Awake()
    {
        Debug.Log("[CornellBoxDemo] Setting up Cornell Box scene...");

        // Camera — centered, looking into the box
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0, 0, -8f);

        // --- Cornell Box Light (ceiling area, white) ---
        var lightObj = new GameObject("Ceiling Light");
        var ceilingLight = lightObj.AddComponent<Light>();
        ceilingLight.type = LightType.Point;
        ceilingLight.color = Color.white;
        ceilingLight.intensity = 2.5f;
        ceilingLight.range = 10f;
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

        Debug.Log($"[CornellBoxDemo] Cornell Box ready - Screen: {Screen.width}x{Screen.height}");
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
