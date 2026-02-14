using UnityEngine;

public class AnotherScript : MonoBehaviour
{
    public override void Update()
    {
        if (Time.frameCount % 120 == 0)
            Debug.Log($"[AnotherScript] I'm alive! Frame: {Time.frameCount}");
    }
}
