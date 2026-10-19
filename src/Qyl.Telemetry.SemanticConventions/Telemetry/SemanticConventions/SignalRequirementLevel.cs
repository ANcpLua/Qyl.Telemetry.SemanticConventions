namespace Qyl.Telemetry.SemanticConventions;

/// <summary>Whether collection of a signal is recommended or requires explicit opt-in.</summary>
[JetBrains.Annotations.PublicAPI]
public enum SignalRequirementLevel
{
    /// <summary>Collect by default; the upstream default when no level is specified.</summary>
    Recommended,

    /// <summary>Collect only when explicitly enabled.</summary>
    OptIn
}
