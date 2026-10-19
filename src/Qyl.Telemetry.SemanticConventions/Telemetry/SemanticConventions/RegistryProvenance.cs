namespace Qyl.Telemetry.SemanticConventions;

/// <summary>The defining registry and optional source path of a resolved convention.</summary>
[JetBrains.Annotations.PublicAPI]
public sealed class RegistryProvenance
{
    /// <summary>Initializes provenance using the defining registry's schema URL.</summary>
    public RegistryProvenance(string schemaUrl, string? path = null)
    {
        SchemaUrl = schemaUrl ?? throw new ArgumentNullException(nameof(schemaUrl));
        Path = path;
    }

    /// <summary>The schema URL identifying the registry that owns the definition.</summary>
    public string SchemaUrl { get; }

    /// <summary>The source path supplied by Weaver, when present.</summary>
    public string? Path { get; }
}
