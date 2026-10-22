// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

internal enum SemconvMigrationItemKind
{
    AttributeKey,
    AttributeValue,
    MetricName,
    EventName,
    SpanName,
    GuidanceOnly,
}

internal enum SemconvMigrationKind
{
    ExactRename,
    ExactValueRename,
    RemovedNoReplacement,
    ContextSensitive,
    ManualReview,
    DeprecatedButGenerated,
}

/// <summary>One migration of the catalog, compared by value including its replacement names.</summary>
/// <param name="OldName">The deprecated name, or a prefix ending in <c>*</c>.</param>
/// <param name="Kind">What the old name names.</param>
/// <param name="Signal">The signal the old name belongs to.</param>
/// <param name="Domain">The semantic-convention domain of the old name.</param>
/// <param name="Version">
/// Semantic-conventions version the deprecation landed in (without the
/// leading 'v'), or empty when the upstream model carries no version.
/// </param>
/// <param name="ReplacementNames">The names that replace the old name.</param>
/// <param name="MigrationKind">How the old name migrates.</param>
/// <param name="ChangelogEvidence">Human-readable summary surfaced in docs and diagnostic messages.</param>
internal readonly record struct SemconvMigrationCatalogEntry(
    string OldName,
    SemconvMigrationItemKind Kind,
    string Signal,
    string Domain,
    string Version,
    ImmutableArray<string> ReplacementNames,
    SemconvMigrationKind MigrationKind,
    string ChangelogEvidence)
{
    public bool HasExactReplacement =>
        MigrationKind is SemconvMigrationKind.ExactRename or SemconvMigrationKind.ExactValueRename or SemconvMigrationKind.DeprecatedButGenerated
        && ReplacementNames.Length == 1
        && !string.IsNullOrWhiteSpace(ReplacementNames[0]);

    public bool Equals(SemconvMigrationCatalogEntry other) =>
        OldName == other.OldName && Kind == other.Kind && Signal == other.Signal
        && Domain == other.Domain && Version == other.Version && MigrationKind == other.MigrationKind
        && ChangelogEvidence == other.ChangelogEvidence
        && ReplacementNames.IsDefault == other.ReplacementNames.IsDefault
        && (ReplacementNames.IsDefault || ReplacementNames.SequenceEqual(other.ReplacementNames));

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = OldName is null ? 0 : StringComparer.Ordinal.GetHashCode(OldName);
            hash = (hash * 397) ^ Kind.GetHashCode();
            hash = (hash * 397) ^ (Signal is null ? 0 : StringComparer.Ordinal.GetHashCode(Signal));
            hash = (hash * 397) ^ (Domain is null ? 0 : StringComparer.Ordinal.GetHashCode(Domain));
            hash = (hash * 397) ^ (Version is null ? 0 : StringComparer.Ordinal.GetHashCode(Version));
            hash = (hash * 397) ^ MigrationKind.GetHashCode();
            hash = (hash * 397) ^ (ChangelogEvidence is null ? 0 : StringComparer.Ordinal.GetHashCode(ChangelogEvidence));
            if (!ReplacementNames.IsDefault)
                foreach (var name in ReplacementNames)
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(name);
            return hash;
        }
    }
}
