namespace PcControl.Host;

/// <summary>The tray's kill switch. While paused, the PC ignores all remote input.</summary>
internal sealed class RemoteControlState
{
    private volatile bool _paused;

    public event EventHandler? Changed;

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
            {
                return;
            }

            _paused = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
