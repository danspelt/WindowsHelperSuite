using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>Layered detection for password / PIN / token fields — fail-safe toward suppression when uncertain.</summary>
public interface ISecretFieldDetector
{
    SecretFieldSnapshot GetSnapshot();
}
