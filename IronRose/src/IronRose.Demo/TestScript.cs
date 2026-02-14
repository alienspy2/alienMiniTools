using UnityEngine;
using UnityEngine.InputSystem;
using IronRose.API;

public class TestScript : MonoBehaviour
{
    private InputAction moveAction = null!;
    private InputAction jumpAction = null!;

    private GameObject cube = null!;
    private GameObject sphere = null!;
    private GameObject capsule = null!;
    private GameObject plane = null!;
    private GameObject quad = null!;

    public override void Awake()
    {
        // Camera setup - pull back to see all primitives
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0, 1.5f, -8f);
        camObj.transform.Rotate(10, 0, 0);

        // --- Primitives showcase ---

        // Sphere (leftmost)
        sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.GetComponent<MeshRenderer>()!.material = new Material(Color.cyan);
        sphere.transform.position = new Vector3(-4f, 0.5f, 0);

        // Cube
        cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.GetComponent<MeshRenderer>()!.material = new Material(new Color(1f, 0.5f, 0.2f));
        cube.transform.position = new Vector3(-1.5f, 0.5f, 0);

        // Capsule (center)
        capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.GetComponent<MeshRenderer>()!.material = new Material(Color.green);
        capsule.transform.position = new Vector3(1.5f, 1f, 0);

        // Quad
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.GetComponent<MeshRenderer>()!.material = new Material(Color.magenta);
        quad.transform.position = new Vector3(4f, 0.5f, 0);

        // Plane (ground)
        plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.GetComponent<MeshRenderer>()!.material = new Material(new Color(0.3f, 0.3f, 0.35f));
        plane.transform.position = new Vector3(0, -0.5f, 0);
        plane.transform.localScale = new Vector3(0.15f, 1f, 0.15f); // 10x10 → 1.5x1.5

        Debug.Log("[TestScript] All primitives created: Sphere, Cube, Capsule, Quad, Plane");

        // InputSystem action setup
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
        jumpAction.performed += ctx => Debug.Log("[InputSystem] Jump!");

        moveAction.Enable();
        jumpAction.Enable();
    }

    public override void Update()
    {
        float t = Time.deltaTime;

        // Rotate objects
        if (cube != null)
            cube.transform.Rotate(0, t * 45, 0);
        if (sphere != null)
            sphere.transform.Rotate(0, t * 30, t * 15);
        if (capsule != null)
            capsule.transform.Rotate(0, t * 60, 0);
        if (quad != null)
            quad.transform.Rotate(0, t * 40, 0);

        if (Time.frameCount % 60 == 0)
            Debug.Log($"[TestScript] Frame: {Time.frameCount}");

        // InputSystem demo
        Vector2 move = moveAction.ReadValue<Vector2>();
        if (move.x != 0 || move.y != 0)
            Debug.Log($"[InputSystem] Move: {move}");

        // Wireframe toggle
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.wireframe = !Debug.wireframe;
            Debug.Log($"[Debug] Wireframe: {(Debug.wireframe ? "ON" : "OFF")}");
        }

        // Legacy input demo
        if (Input.GetKeyDown(KeyCode.Escape))
            Application.Quit();

        if (Input.GetMouseButtonDown(0))
            Debug.Log($"[Input] Mouse0 click at {Input.mousePosition}");
        if (Input.GetMouseButtonDown(1))
            Debug.Log($"[Input] Mouse1 click at {Input.mousePosition}");
    }
}
