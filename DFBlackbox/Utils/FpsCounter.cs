namespace DFBlackbox.Utils;

public sealed class FpsCounter
{
    private DateTime _last = DateTime.Now;
    private int _frames;

    public double CurrentFps { get; private set; }

    public void Tick()
    {
        _frames++;
        DateTime now = DateTime.Now;
        double elapsed = (now - _last).TotalSeconds;
        if (elapsed < 1)
        {
            return;
        }

        CurrentFps = _frames / elapsed;
        _frames = 0;
        _last = now;
    }

    public void Reset()
    {
        _frames = 0;
        _last = DateTime.Now;
        CurrentFps = 0;
    }
}
