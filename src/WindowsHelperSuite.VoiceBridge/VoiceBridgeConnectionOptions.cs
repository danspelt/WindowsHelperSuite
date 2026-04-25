namespace WindowsHelperSuite.VoiceBridge;

public sealed class VoiceBridgeConnectionOptions
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 53742;
    public bool ListenOnAllInterfaces { get; init; }
    public string SharedToken { get; init; } = "";
}
