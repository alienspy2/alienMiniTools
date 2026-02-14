using UnityEngine;
using UnityEngine.InputSystem;
using IronRose.API;

public class TestScript : MonoBehaviour
{
    private int _currentDemo = 0;

    public override void Awake()
    {
        Debug.Log("=== IronRose Demo Selector ===");
        Debug.Log("[1] Cornell Box");
        Debug.Log("[2] Asset Import");
        Debug.Log("[3] (reserved)");
        Debug.Log("[4] (reserved)");
        Debug.Log("[5] (reserved)");
        Debug.Log("[F1] Wireframe toggle | [ESC] Quit");
        Debug.Log("==============================");
    }

    public override void Update()
    {
        // Demo selection
        if (Input.GetKeyDown(KeyCode.Alpha1)) LoadDemo(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) LoadDemo(2);
        if (Input.GetKeyDown(KeyCode.Alpha3)) LoadDemo(3);
        if (Input.GetKeyDown(KeyCode.Alpha4)) LoadDemo(4);
        if (Input.GetKeyDown(KeyCode.Alpha5)) LoadDemo(5);

        // Wireframe toggle
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.wireframe = !Debug.wireframe;
            Debug.Log($"[Debug] Wireframe: {(Debug.wireframe ? "ON" : "OFF")}");
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            Application.Quit();
    }

    private void LoadDemo(int demoIndex)
    {
        if (demoIndex == _currentDemo)
        {
            Debug.Log($"[Demo] Demo {demoIndex} already active");
            return;
        }

        Debug.Log($"[Demo] Loading demo {demoIndex}...");

        // Clear current scene (except this selector)
        SceneManager.Clear();

        // Re-register self
        var selectorGo = new GameObject("DemoSelector");
        var selector = selectorGo.AddComponent<TestScript>();
        selector._currentDemo = demoIndex;

        // Launch selected demo
        switch (demoIndex)
        {
            case 1:
                var go1 = new GameObject("CornellBoxDemo");
                go1.AddComponent<CornellBoxDemo>();
                Debug.Log("[Demo] >> Cornell Box");
                break;

            case 2:
                var go2 = new GameObject("AssetImportDemo");
                go2.AddComponent<AssetImportDemo>();
                Debug.Log("[Demo] >> Asset Import");
                break;

            case 3:
                Debug.Log("[Demo] Demo 3 not yet implemented");
                break;

            case 4:
                Debug.Log("[Demo] Demo 4 not yet implemented");
                break;

            case 5:
                Debug.Log("[Demo] Demo 5 not yet implemented");
                break;
        }
    }
}
