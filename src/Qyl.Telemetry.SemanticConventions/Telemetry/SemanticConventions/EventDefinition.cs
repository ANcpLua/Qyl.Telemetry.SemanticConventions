namespace Qyl.Telemetry.SemanticConventions;

/// <summary>
/// A semantic-convention event as a first-class object: its event name, stability, brief,
/// structured deprecation, and attribute references travel with it. Events have no kind,
/// so this type is not generic.
/// </summary>
[JetBrains.Annotations.PublicAPI]
public sealed class EventDefinition
{
    /// <summary>Initializes an event using the original attribute-only constructor.</summary>
    public EventDefinition(string name, string brief, Stability stability, Deprecation deprecation,
        IReadOnlyList<AttributeRef> attributes)
        : this(name, brief, stability, deprecation, attributes, null)
    {
    }

    /// <summary>Initializes a new event definition.</summary>
    /// <param name="name">The event name (e.g. <c>app.crash</c>).</param>
    /// <param name="brief">Human-readable brief.</param>
    /// <param name="stability">The stability tier.</param>
    /// <param name="deprecation">Structured deprecation; <see cref="Deprecation.None"/> when current.</param>
    /// <param name="attributes">Attributes this event carries, with requirement levels.</param>
    /// <param name="entityAssociations">Entity association expressions; the list is an implicit one-of.</param>
    /// <param name="requirementLevel">Whether this signal is recommended or opt-in.</param>
    public EventDefinition(
        string name,
        string brief,
        Stability stability,
        Deprecation deprecation,
        IReadOnlyList<AttributeRef> attributes,
        IReadOnlyList<EntityAssociation>? entityAssociations,
        SignalRequirementLevel requirementLevel = SignalRequirementLevel.Recommended)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Brief = brief ?? throw new ArgumentNullException(nameof(brief));
        Stability = stability;
        Deprecation = deprecation ?? throw new ArgumentNullException(nameof(deprecation));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        EntityAssociations = entityAssociations ?? Array.Empty<EntityAssociation>();
        RequirementLevel = requirementLevel;
    }

    /// <summary>The event name (e.g. <c>app.crash</c>).</summary>
    public string Name { get; }

    /// <summary>Human-readable brief.</summary>
    public string Brief { get; }

    /// <summary>The stability tier.</summary>
    public Stability Stability { get; }

    /// <summary>Structured deprecation; <see cref="Deprecation.None"/> when current.</summary>
    public Deprecation Deprecation { get; }

    /// <summary>Attributes this event carries, with requirement levels.</summary>
    public IReadOnlyList<AttributeRef> Attributes { get; }

    /// <summary>Entity association expressions. At least one list entry must be satisfied.</summary>
    public IReadOnlyList<EntityAssociation> EntityAssociations { get; }

    /// <summary>The collection requirement for this signal, independent of attribute requirements.</summary>
    public SignalRequirementLevel RequirementLevel { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
