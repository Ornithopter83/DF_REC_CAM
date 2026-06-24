namespace DFBlackbox.Models;

public enum BlackboxState
{
    Idle,
    Watching,
    PreEvent,
    Recording,
    WaitForStableHome,
    Cooldown,
    Error
}
