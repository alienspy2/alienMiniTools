using UnityEngine;

/// <summary>
/// Demo 5 — 3D Physics Rigidbody
/// Cubes and spheres falling onto a static floor with bouncing and collisions.
/// Press SPACE to launch an impulse sphere upward.
/// </summary>
public class PhysicsDemo3D : MonoBehaviour
{
    private Rigidbody? _launchBallRb;

    public override void Awake()
    {
        Debug.Log("[PhysicsDemo3D] Setting up 3D Physics scene...");

        // Camera
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0, 3f, -12f);
        camObj.transform.Rotate(10, 0, 0);

        // Light
        var lightObj = new GameObject("Scene Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.white;
        light.intensity = 2.5f;
        light.range = 30f;
        lightObj.transform.position = new Vector3(0, 8f, -2f);

        // --- Static floor ---
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.GetComponent<MeshRenderer>()!.material = new Material(new Color(0.3f, 0.3f, 0.35f));
        floor.transform.localScale = new Vector3(12f, 0.5f, 8f);
        floor.transform.position = new Vector3(0, -1f, 0);
        var floorCol = floor.AddComponent<BoxCollider>();
        floorCol.size = floor.transform.localScale;
        var floorRb = floor.AddComponent<Rigidbody>();
        floorRb.isKinematic = true;
        floorRb.useGravity = false;

        // --- Left ramp (kinematic tilted platform) ---
        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "Ramp";
        ramp.GetComponent<MeshRenderer>()!.material = new Material(new Color(0.5f, 0.4f, 0.2f));
        ramp.transform.localScale = new Vector3(4f, 0.3f, 3f);
        ramp.transform.position = new Vector3(-3f, 1.5f, 0);
        ramp.transform.Rotate(0, 0, -25);
        var rampCol = ramp.AddComponent<BoxCollider>();
        rampCol.size = ramp.transform.localScale;
        var rampRb = ramp.AddComponent<Rigidbody>();
        rampRb.isKinematic = true;
        rampRb.useGravity = false;

        // --- Dynamic cubes (stacked) ---
        float[] xPositions = { -0.6f, 0f, 0.6f };
        Color[] cubeColors =
        {
            new Color(0.9f, 0.2f, 0.2f),
            new Color(0.2f, 0.7f, 0.2f),
            new Color(0.2f, 0.3f, 0.9f)
        };

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Cube_{row}_{col}";
                cube.GetComponent<MeshRenderer>()!.material = new Material(cubeColors[col]);
                cube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                cube.transform.position = new Vector3(
                    xPositions[col],
                    3f + row * 0.6f,
                    0);
                var bc = cube.AddComponent<BoxCollider>();
                bc.size = cube.transform.localScale;
                var rb = cube.AddComponent<Rigidbody>();
                rb.mass = 1.0f;
                rb.useGravity = true;
            }
        }

        // --- Dynamic spheres (dropped from height) ---
        float[] sphereX = { -2f, 2f };
        Color[] sphereColors =
        {
            new Color(1f, 0.7f, 0.1f),
            new Color(0.8f, 0.2f, 0.8f)
        };

        for (int i = 0; i < 2; i++)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"Sphere_{i}";
            sphere.GetComponent<MeshRenderer>()!.material = new Material(sphereColors[i]);
            sphere.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            sphere.transform.position = new Vector3(sphereX[i], 6f + i * 1.5f, 0);
            var sc = sphere.AddComponent<SphereCollider>();
            sc.radius = 0.4f;
            var rb = sphere.AddComponent<Rigidbody>();
            rb.mass = 2.0f;
            rb.useGravity = true;
        }

        // --- Launch ball (press SPACE to impulse) ---
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "LaunchBall";
        ball.GetComponent<MeshRenderer>()!.material = new Material(new Color(1f, 1f, 0.2f));
        ball.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        ball.transform.position = new Vector3(3f, 0f, 0);
        var ballCol = ball.AddComponent<SphereCollider>();
        ballCol.radius = 0.3f;
        _launchBallRb = ball.AddComponent<Rigidbody>();
        _launchBallRb.mass = 1.5f;
        _launchBallRb.useGravity = true;

        Debug.Log("[PhysicsDemo3D] Scene ready — 9 cubes, 2 spheres, 1 launch ball");
        Debug.Log("[PhysicsDemo3D] Press SPACE to launch the yellow ball upward");
    }

    public override void Update()
    {
        if (_launchBallRb != null && Input.GetKeyDown(KeyCode.Space))
        {
            _launchBallRb.AddForce(new Vector3(-2f, 12f, 0), ForceMode.Impulse);
            Debug.Log("[PhysicsDemo3D] Launch!");
        }
    }
}
