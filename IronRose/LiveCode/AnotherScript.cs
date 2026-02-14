using System;

public class AnotherScript
{
    private int _counter = 0;

    public void Update()
    {
        _counter++;
        if (_counter % 120 == 0)  // 2초마다
        {
            Console.WriteLine($"[AnotherScript] I'm alive! Counter: {_counter}");
        }
    }
}
