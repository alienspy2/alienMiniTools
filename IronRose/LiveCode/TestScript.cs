using UnityEngine;
using IronRose.API;

public class TestScript : MonoBehaviour
{
    public override void Update()
    {
        //Screen.SetClearColor(1.0f, 0.0f, 0.0f);
        transform.Rotate(0, Time.deltaTime * 45, 0);
        if (Time.frameCount % 60 == 0)
            Debug.Log($"[TestScript] Frame: {Time.frameCount} | Rotation: {transform.rotation}");

        // 입력 시스템 데모
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        if (h != 0 || v != 0)
            Debug.Log($"[Input] Move: ({h}, {v})");

        if (Input.GetKeyDown(KeyCode.Space))
            Debug.Log("[Input] Space pressed!");
        if (Input.GetKeyDown(KeyCode.Escape))
            Debug.Log("[Input] Escape pressed!");

        if (Input.GetMouseButtonDown(0))
            Debug.Log($"[Input] Mouse0 click at {Input.mousePosition}");
        if (Input.GetMouseButtonDown(1))
            Debug.Log($"[Input] Mouse1 click at {Input.mousePosition}");
    }
}
