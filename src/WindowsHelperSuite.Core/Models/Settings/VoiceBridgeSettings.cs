namespace WindowsHelperSuite.Core.Models.Settings;

/// <summary>Local Voice Bridge listener (Android client lives in a separate repository).</summary>
public sealed class VoiceBridgeSettings
{
    /// <summary>When true, starts an embedded WebSocket endpoint while WindowsHelperSuite is running.</summary>
    public bool EnableListener { get; set; }

    /// <summary>TCP port for HTTP + WebSocket upgrade (default 53742).</summary>
    public int ListenPort { get; set; } = 53742;

    /// <summary>
    /// Shared secret for <c>ws://…/ws?token=</c> and optional <c>pair_request</c> body field <c>token</c>.
    /// Leave empty to auto-generate on first successful listener start (persisted to settings.json).
    /// </summary>
    public string SharedToken { get; set; } = "";

    /// <summary>When false, bind to 127.0.0.1 only. When true, bind to all interfaces so a phone on LAN can connect.</summary>
    public bool ListenOnAllInterfaces { get; set; }
}
