namespace Qyl.Telemetry.SemanticConventions;

/// <summary>
/// A semantic-convention entity as a first-class object: its type name, stability, brief,
/// structured deprecation, identifying attributes without requirement levels, and descriptive
/// attribute references. This is the resolved entity definition, distinct from the
/// provenance-bearing <see cref="EntityRef"/> in a signal's association expression.
/// </summary>
[JetBrains.Annotations.PublicAPI]
public sealed class EntityDefinition
{
    /// <summary>Initializes an entity using the original descriptive-attribute constructor.</summary>
    public EntityDefinition(string name, string brief, Stability stability, Deprecation deprecation,
        IReadOnlyList<AttributeRef> attributes)
        : this(name, brief, stability, deprecation, attributes, null)
    {
    }

    /// <summary>Initializes a new entity definition.</summary>
    /// <param name="name">The entity type name (e.g. <c>host</c>, <c>process</c>).</param>
    /// <param name="brief">Human-readable brief.</param>
    /// <param name="stability">The stability tier.</param>
    /// <param name="deprecation">Structured deprecation; <see cref="Deprecation.None"/> when current.</param>
    /// <param name="attributes">The attributes that describe this entity.</param>
    /// <param name="identity">The attributes that identify this entity, without requirement levels.</param>
    /// <param name="requirementLevel">Whether this signal is recommended or opt-in.</param>
    public EntityDefinition(
        string name,
        string brief,
        Stability stability,
        Deprecation deprecation,
        IReadOnlyList<AttributeRef> attributes,
        IReadOnlyList<EntityIdentityAttributeRef>? identity,
        SignalRequirementLevel requirementLevel = SignalRequirementLevel.Recommended)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Brief = brief ?? throw new ArgumentNullException(nameof(brief));
        Stability = stability;
        Deprecation = deprecation ?? throw new ArgumentNullException(nameof(deprecation));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Identity = identity ?? Array.Empty<EntityIdentityAttributeRef>();
        RequirementLevel = requirementLevel;
    }

    /// <summary>The entity type name (e.g. <c>host</c>, <c>process</c>).</summary>
    public string Name { get; }

    /// <summary>The collection requirement for this signal, independent of attribute requirements.</summary>
    public SignalRequirementLevel RequirementLevel { get; }

    /// <summary>Human-readable brief.</summary>
    public string Brief { get; }

    /// <summary>The stability tier.</summary>
    public Stability Stability { get; }

    /// <summary>Structured deprecation; <see cref="Deprecation.None"/> when current.</summary>
    public Deprecation Deprecation { get; }

    /// <summary>The attributes that describe this entity.</summary>
    public IReadOnlyList<AttributeRef> Attributes { get; }

    /// <summary>The immutable identifying tuple; identifying attributes have no requirement level.</summary>
    public IReadOnlyList<EntityIdentityAttributeRef> Identity { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
