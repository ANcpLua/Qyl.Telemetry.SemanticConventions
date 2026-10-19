namespace Qyl.Telemetry.SemanticConventions;

/// <summary>An entity reference or a nested logical expression over entity references.</summary>
[JetBrains.Annotations.PublicAPI]
public abstract class EntityAssociation
{
    private protected EntityAssociation() { }

    /// <summary>At least one of the alternatives must be satisfied.</summary>
    public sealed class OneOf : EntityAssociation
    {
        /// <summary>Initializes an any-of expression without flattening its children.</summary>
        public OneOf(IReadOnlyList<EntityAssociation> alternatives)
            => Alternatives = alternatives ?? throw new ArgumentNullException(nameof(alternatives));

        /// <summary>The expressions whose alternatives are preserved in registry order.</summary>
        public IReadOnlyList<EntityAssociation> Alternatives { get; }
    }

    /// <summary>Every contained expression must be satisfied.</summary>
    public sealed class AllOf : EntityAssociation
    {
        /// <summary>Initializes an all-of expression without flattening its children.</summary>
        public AllOf(IReadOnlyList<EntityAssociation> requirements)
            => Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));

        /// <summary>The expressions that must all be satisfied, in registry order.</summary>
        public IReadOnlyList<EntityAssociation> Requirements { get; }
    }
}
