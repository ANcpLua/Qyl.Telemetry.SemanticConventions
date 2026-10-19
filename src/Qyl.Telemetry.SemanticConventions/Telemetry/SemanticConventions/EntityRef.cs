namespace Qyl.Telemetry.SemanticConventions;

/// <summary>
/// A reference to an entity a signal is associated with. Carries the entity type name from
/// the registry; the full <see cref="EntityDefinition"/> (its identifying attributes) lives
/// with the entity conventions.
/// </summary>
[JetBrains.Annotations.PublicAPI]
public sealed class EntityRef : EntityAssociation
{
    /// <summary>Initializes a new entity reference.</summary>
    /// <param name="type">The entity type name (e.g. <c>process</c>, <c>host</c>).</param>
    /// <param name="provenance">The registry that defines this entity.</param>
    public EntityRef(string type, RegistryProvenance provenance)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    }

    /// <summary>The entity type name (e.g. <c>process</c>, <c>host</c>).</summary>
    public string Type { get; }

    /// <summary>The registry that owns the referenced definition.</summary>
    public RegistryProvenance Provenance { get; }

    /// <inheritdoc/>
    public override string ToString() => Type;
}
