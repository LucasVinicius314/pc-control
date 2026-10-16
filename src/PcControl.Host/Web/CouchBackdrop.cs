namespace PcControl.Host.Web;

/// <summary>
/// The picture of the screen taken just before the controller UI opened, which the UI shows blurred
/// behind itself. Only held in memory, only ever the latest one.
/// </summary>
internal sealed class CouchBackdrop
{
    private readonly object _gate = new();
    private byte[]? _jpeg;

    public byte[]? Jpeg
    {
        get
        {
            lock (_gate)
            {
                return _jpeg;
            }
        }
        set
        {
            lock (_gate)
            {
                _jpeg = value;
            }
        }
    }
}
