namespace Qyl.Telemetry.SemanticConventions;

/// <summary>
/// A semantic-convention span as a first-class object: its registry id and the span type it
/// specializes, span kind (as a marker type), stability, brief, structured deprecation, attribute
/// references (with requirement levels) and the span-name chain all travel with it.
/// </summary>
/// <remarks>
/// Rows are keyed by the registry's span refinement id. A specialization (<c>db.postgresql.client</c>,
/// <c>gen_ai.execute_tool.load_skill.internal</c>) shares its <see cref="Type"/> with the base group and
/// carries its own attributes and naming rule.
/// </remarks>
/// <typeparam name="TKind">The span-kind marker (<see cref="Client"/>, <see cref="Server"/>, <see cref="Internal"/>, <see cref="Producer"/>, or <see cref="Consumer"/>).</typeparam>
[JetBrains.Annotations.PublicAPI]
public sealed class SpanDefinition<TKind>
    where TKind : struct, ISpanKind
{
    /// <summary>Initializes a span using the original attribute and naming constructor.</summary>
    public SpanDefinition(string id, string type, string brief, Stability stability, Deprecation deprecation,
        IReadOnlyList<AttributeRef> attributes, IReadOnlyList<string> spanName)
        : this(id, type, brief, stability, deprecation, attributes, spanName, null)
    {
    }

    /// <summary>Initializes a new span definition.</summary>
    /// <param name="id">The span refinement id (e.g. <c>http.client</c>, <c>db.postgresql.client</c>).</param>
    /// <param name="type">The span type the row belongs to; equals <paramref name="id"/> for a base group.</param>
    /// <param name="brief">Human-readable brief.</param>
    /// <param name="stability">The stability tier.</param>
    /// <param name="deprecation">Structured deprecation; <see cref="Deprecation.None"/> when current.</param>
    /// <param name="attributes">Attributes this span carries, with requirement levels.</param>
    /// <param name="spanName">The span-name chain (registry annotation <c>qyl.span_name</c>); empty when the registry states no rule qyl encodes.</param>
    /// <param name="entities">Entities this span is associated with.</param>
    public SpanDefinition(
        string id,
        string type,
        string brief,
        Stability stability,
        Deprecation deprecation,
        IReadOnlyList<AttributeRef> attributes,
        IReadOnlyList<string> spanName,
        IReadOnlyList<EntityRef>? entities)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        SpanName = spanName ?? throw new ArgumentNullException(nameof(spanName));
        Brief = brief ?? throw new ArgumentNullException(nameof(brief));
        Stability = stability;
        Deprecation = deprecation ?? throw new ArgumentNullException(nameof(deprecation));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        Entities = entities ?? Array.Empty<EntityRef>();
    }

    /// <summary>The span refinement id (e.g. <c>http.client</c>, <c>db.postgresql.client</c>).</summary>
    public string Id { get; }

    /// <summary>The span type the row belongs to; equals <see cref="Id"/> for a base group.</summary>
    public string Type { get; }

    /// <summary>Whether the row specializes another group of the same <see cref="Type"/>.</summary>
    public bool IsSpecialization => !string.Equals(Id, Type, StringComparison.Ordinal);

    /// <summary>
    /// The span-name fallback chain: the first template whose every <c>{key}</c> placeholder has a
    /// value names the span. Empty when the registry states no naming rule qyl encodes.
    /// </summary>
    public IReadOnlyList<string> SpanName { get; }

    /// <summary>The span kind (e.g. <c>client</c>), from the marker type.</summary>
    public string SpanKind => default(TKind).Kind;

    /// <summary>Human-readable brief.</summary>
    public string Brief { get; }

    /// <summary>The stability tier.</summary>
    public Stability Stability { get; }

    /// <summary>Structured deprecation; <see cref="Deprecation.None"/> when current.</summary>
    public Deprecation Deprecation { get; }

    /// <summary>Attributes this span carries, with requirement levels.</summary>
    public IReadOnlyList<AttributeRef> Attributes { get; }

    /// <summary>Entities this span is associated with.</summary>
    public IReadOnlyList<EntityRef> Entities { get; }

    /// <inheritdoc/>
    public override string ToString() => Id;
}
