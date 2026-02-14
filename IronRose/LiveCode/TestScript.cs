using System;
using IronRose.API;

public class TestScript
{
    private int _frameCount = 0;

    public void Update()
    {
        _frameCount++;

        // 빨간색으로 변경!
        Screen.SetClearColor(1.0f, 0.0f, 0.0f);

        if (_frameCount % 60 == 0)
        {
            Console.WriteLine($"[TestScript] Frame: {_frameCount} | ClearColor: RED");
        }
    }
}
