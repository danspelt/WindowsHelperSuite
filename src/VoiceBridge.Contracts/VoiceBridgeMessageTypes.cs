namespace VoiceBridge.Contracts;

/// <summary>Wire-level <c>type</c> discriminator values (V1). Android and Windows must stay aligned.</summary>
public static class VoiceBridgeMessageTypes
{
    // Session / auth
    public const string Hello = "hello";
    public const string AuthChallenge = "auth_challenge";
    public const string AuthResponse = "auth_response";

    public const string PairRequest = "pair_request";
    public const string PairAccept = "pair_accept";
    public const string PairReject = "pair_reject";

    // Text + commands
    public const string TextSend = "text_send";
    public const string TextReceived = "text_received";
    public const string Command = "command";
    public const string CommandAck = "command_ack";

    // Optional audio streaming
    public const string AudioChunk = "audio_chunk";

    public const string TranscriptPartial = "transcript_partial";
    public const string TranscriptFinal = "transcript_final";
    public const string IntentResult = "intent_result";
    public const string ConfirmationRequest = "confirmation_request";
    public const string ConfirmationResponse = "confirmation_response";
    public const string ActionResult = "action_result";
    public const string Ping = "ping";
    public const string Pong = "pong";
}
