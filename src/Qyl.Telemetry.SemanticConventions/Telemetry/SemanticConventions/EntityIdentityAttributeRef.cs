namespace Qyl.Telemetry.SemanticConventions;

/// <summary>An identifying entity attribute. Identity has no signal requirement level.</summary>
[JetBrains.Annotations.PublicAPI]
public sealed class EntityIdentityAttributeRef
{
    /// <summary>Initializes an identifying attribute from the resolved entity model.</summary>
    /// <param name="name">The identifying attribute key.</param>
    /// <param name="type">The attribute's registry value type.</param>
    /// <param name="isDeprecated">Whether the identifying attribute is deprecated.</param>
    public EntityIdentityAttributeRef(string name, string type, bool isDeprecated)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        IsDeprecated = isDeprecated;
    }

    /// <summary>The identifying attribute key.</summary>
    public string Name { get; }

    /// <summary>The attribute's registry value type.</summary>
    public string Type { get; }

    /// <summary>Whether the identifying attribute is deprecated.</summary>
    public bool IsDeprecated { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
