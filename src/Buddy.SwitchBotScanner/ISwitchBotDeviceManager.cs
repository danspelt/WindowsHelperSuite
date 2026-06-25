namespace Buddy.SwitchBotScanner;

public interface ISwitchBotDeviceManager
{
    /// <summary>
    /// Tries to use the preferred saved SwitchBot, then scans and tries candidates
    /// until one responds. Returns the working candidate or null if none respond.
    /// </summary>
    Task<BleCandidate?> OpenDoorAsync();

    /// <summary>
    /// Loads the current preferred SwitchBot candidate, if any.
    /// </summary>
    Task<BleCandidate?> GetPreferredAsync();

    /// <summary>
    /// Saves a candidate as the preferred SwitchBot.
    /// </summary>
    Task SetPreferredAsync(BleCandidate candidate);
}
