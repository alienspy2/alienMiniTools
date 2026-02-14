using System;

public class TestScript
{
    private int _frameCount = 0;

    public void Update()
    {
        _frameCount++;
        if (_frameCount % 60 == 0)
        {
            Console.WriteLine($"=== LIVE HOT RELOAD!!! Frame: {_frameCount} === Runtime modification!");
        }
    }
}
