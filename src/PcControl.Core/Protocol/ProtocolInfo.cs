namespace PcControl.Core.Protocol;

/// <summary>Identity of the wire protocol. Keep in step with docs/protocol.</summary>
public static class ProtocolInfo
{
    public const string AppName = "pc-control";

    /// <summary>Bumped on breaking changes to the REST or WebSocket contract.</summary>
    public const int Version = 1;
}
