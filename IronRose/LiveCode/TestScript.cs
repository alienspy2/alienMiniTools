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
    }
}
