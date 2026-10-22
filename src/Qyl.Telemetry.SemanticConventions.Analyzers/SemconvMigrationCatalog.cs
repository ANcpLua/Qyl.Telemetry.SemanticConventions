// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

internal static class SemconvMigrationCatalog
{
    public const int ExpectedCuratedMentionCount = 158;

    public static ImmutableArray<SemconvMigrationCatalogEntry> Entries { get; } = BuildEntries();

    public static ImmutableArray<SemconvMigrationCatalogEntry> SupplementalAttributeValueEntries { get; } =
        BuildSupplementalAttributeValueEntries();

    private static readonly ImmutableDictionary<string, SemconvMigrationCatalogEntry> EntriesByOldName =
        BuildEntriesByOldName(Entries);

    private static readonly ImmutableDictionary<string, SemconvMigrationCatalogEntry> AttributeValueEntriesByOldName =
        BuildEntriesByOldName(SupplementalAttributeValueEntries);

    private static readonly ImmutableArray<SemconvMigrationCatalogEntry> WildcardEntries =
        BuildWildcardEntries(Entries);

    public static bool TryGetMigrationByName(
        string oldName,
        out SemconvMigrationCatalogEntry entry)
    {
        if (EntriesByOldName.TryGetValue(oldName, out entry))
        {
            return true;
        }

        foreach (var wildcardEntry in WildcardEntries)
        {
            if (!MatchesWildcard(oldName, wildcardEntry.OldName))
            {
                continue;
            }

            entry = wildcardEntry;
            return true;
        }

        entry = default;
        return false;
    }

    public static bool TryGetAttributeValueMigration(
        string attributeName,
        string attributeValue,
        out SemconvMigrationCatalogEntry entry)
    {
        var key = attributeName + "=" + attributeValue;
        if (AttributeValueEntriesByOldName.TryGetValue(key, out entry))
        {
            return true;
        }

        entry = default;
        return false;
    }

    public static bool IsSupplementalDiagnosticEntry(SemconvMigrationCatalogEntry entry) =>
        entry.Kind != SemconvMigrationItemKind.GuidanceOnly;

    /// <summary>
    /// Follow <see cref="SemconvMigrationKind.ExactRename"/> /
    /// <see cref="SemconvMigrationKind.ExactValueRename"/> chains transitively
    /// so the code-fix lands consumers on the terminal symbol, not on a still-deprecated
    /// mid-state symbol from a multi-version rename like
    /// <c>http.host → net.host.name → server.address</c>.
    ///
    /// Returns the terminal entry's single replacement name. A name matched by a wildcard entry
    /// keeps the segment the wildcard covered. If <paramref name="oldName"/>
    /// has no exact-replacement entry, returns <paramref name="oldName"/> unchanged.
    /// If a cycle is detected (catalog bug) or the chain length exceeds
    /// <c>MaxResolutionDepth</c>, returns the last safe replacement found.
    /// </summary>
    public static string ResolveTerminalReplacement(string oldName)
    {
        const int maxResolutionDepth = 8;

        if (string.IsNullOrEmpty(oldName))
        {
            return oldName;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { oldName };
        var current = oldName;

        for (var step = 0; step < maxResolutionDepth; step++)
        {
            if (!TryGetMigrationByName(current, out var entry) || !entry.HasExactReplacement)
            {
                return current;
            }

            var next = ExpandWildcardReplacement(current, entry);
            if (string.IsNullOrEmpty(next) || !visited.Add(next))
            {
                // Cycle or empty replacement; bail out at the last known-good name.
                return current;
            }

            current = next;
        }

        // Hit the depth ceiling: chain too long, return the last name reached
        // rather than loop forever. A real chain longer than 8 hops is almost
        // certainly a catalog data bug worth surfacing in `Validate()`.
        return current;
    }

    public static void Validate()
    {
        if (Entries.Length != ExpectedCuratedMentionCount)
        {
            throw new InvalidOperationException(
                $"Expected {ExpectedCuratedMentionCount} curated semantic-convention migration mentions, found {Entries.Length}.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            RequireCompleteEntry(entry);

            if (!seen.Add(entry.OldName))
            {
                throw new InvalidOperationException($"Duplicate migration catalog key '{entry.OldName}'.");
            }

            if (entry.HasExactReplacement && entry.ReplacementNames.Length != 1)
            {
                throw new InvalidOperationException($"Exact migration '{entry.OldName}' must have one replacement.");
            }
        }

        seen.Clear();
        foreach (var entry in SupplementalAttributeValueEntries)
        {
            RequireCompleteEntry(entry);

            if (entry.Kind != SemconvMigrationItemKind.AttributeValue)
            {
                throw new InvalidOperationException($"Supplemental value entry '{entry.OldName}' must be an AttributeValue.");
            }

            if (!seen.Add(entry.OldName))
            {
                throw new InvalidOperationException($"Duplicate supplemental value migration key '{entry.OldName}'.");
            }
        }
    }

    private static ImmutableArray<SemconvMigrationCatalogEntry> BuildEntries()
    {
        var builder = ImmutableArray.CreateBuilder<SemconvMigrationCatalogEntry>(
            OpenTelemetryDeprecatedSemconvCatalog.Entries.Length);

        foreach (var entry in OpenTelemetryDeprecatedSemconvCatalog.Entries)
        {
            if (ShouldExcludeFromCuratedInventory(entry))
            {
                continue;
            }

            builder.Add(Normalize(entry));
        }

        builder.Add(CreateGenAiChatHistoryEventEntry());
        return builder.ToImmutable();
    }

    private static ImmutableArray<SemconvMigrationCatalogEntry> BuildSupplementalAttributeValueEntries()
    {
        var builder = ImmutableArray.CreateBuilder<SemconvMigrationCatalogEntry>();

        foreach (var entry in OpenTelemetryDeprecatedSemconvCatalog.Entries)
        {
            if (entry.Kind == SemconvMigrationItemKind.AttributeValue)
            {
                builder.Add(Normalize(entry));
            }
        }

        return builder.ToImmutable();
    }

    private static bool ShouldExcludeFromCuratedInventory(SemconvMigrationCatalogEntry entry)
    {
        if (entry.Kind == SemconvMigrationItemKind.AttributeValue)
        {
            return true;
        }

        // QYL0005 owns the v1.41.0 RPC server-span client.* removal directly.
        if (entry.OldName is "client.address" or "client.port")
        {
            return true;
        }

        // Keep the actual event name (`rpc.message`) and drop the legacy helper key.
        if (entry.OldName == "event.rpc.message")
        {
            return true;
        }

        return entry.OldName is "event.gen_ai.assistant.message"
            or "event.gen_ai.choice"
            or "event.gen_ai.system.message"
            or "event.gen_ai.tool.message"
            or "event.gen_ai.user.message";
    }

    private static SemconvMigrationCatalogEntry CreateGenAiChatHistoryEventEntry() =>
        new(
            "event.gen_ai.*",
            SemconvMigrationItemKind.EventName,
            "event",
            "gen_ai",
            "1.37.0",
            [
                "gen_ai.input.messages",
                "gen_ai.output.messages",
                "gen_ai.system_instructions",
                "gen_ai.client.inference.operation.details"],
            SemconvMigrationKind.ContextSensitive,
            "GenAI chat history events were replaced by structured span attributes or the gen_ai.client.inference.operation.details event.");

    private static SemconvMigrationCatalogEntry Normalize(SemconvMigrationCatalogEntry entry)
    {
        var isGeneratedMetadata = IsGeneratedMetadataEntry(entry);
        var migrationKind = isGeneratedMetadata
            ? SemconvMigrationKind.DeprecatedButGenerated
            : entry.MigrationKind;
        var version = VersionOrKnownUnknown(entry.Version);
        var evidence = string.IsNullOrWhiteSpace(entry.ChangelogEvidence)
            ? "Curated changelog migration entry."
            : entry.ChangelogEvidence;

        if (isGeneratedMetadata)
        {
            evidence = "Covered by generated [Obsolete] metadata when the consumer references OpenTelemetry.SemanticConventions. " + evidence;
        }

        return new SemconvMigrationCatalogEntry(
            entry.OldName,
            entry.Kind,
            entry.Signal,
            entry.Domain,
            version,
            entry.ReplacementNames,
            migrationKind,
            evidence);
    }

    private static bool IsGeneratedMetadataEntry(SemconvMigrationCatalogEntry entry) =>
        entry is { Kind: SemconvMigrationItemKind.AttributeKey, MigrationKind: SemconvMigrationKind.ExactRename }
        && (entry.ChangelogEvidence.StartsWith("semantic-conventions/model deprecated", StringComparison.Ordinal)
            || entry.ChangelogEvidence.StartsWith("semantic-conventions/model deprecated GenAI", StringComparison.Ordinal));

    private static string VersionOrKnownUnknown(string version) =>
        string.IsNullOrWhiteSpace(version) ? "unknown" : version;

    /// <summary>Orders catalog versions numerically, so 1.9.0 precedes 1.40.0; <c>unknown</c> sorts first.</summary>
    public static IComparer<string> VersionComparer { get; } =
        Comparer<string>.Create(static (left, right) => ParseVersion(left).CompareTo(ParseVersion(right)));

    private static Version ParseVersion(string version) =>
        Version.TryParse(version, out var parsed) ? parsed : new Version(0, 0, 0);

    private static ImmutableDictionary<string, SemconvMigrationCatalogEntry> BuildEntriesByOldName(
        ImmutableArray<SemconvMigrationCatalogEntry> entries)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, SemconvMigrationCatalogEntry>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.OldName.IndexOf('*') >= 0)
            {
                continue;
            }

            if (!builder.ContainsKey(entry.OldName))
            {
                builder.Add(entry.OldName, entry);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<SemconvMigrationCatalogEntry> BuildWildcardEntries(
        ImmutableArray<SemconvMigrationCatalogEntry> entries)
    {
        var builder = ImmutableArray.CreateBuilder<SemconvMigrationCatalogEntry>();

        foreach (var entry in entries)
        {
            if (entry.OldName.IndexOf('*') >= 0)
            {
                builder.Add(entry);
            }
        }

        return builder.ToImmutable();
    }

    private static bool MatchesWildcard(string oldName, string wildcard)
    {
        var starIndex = wildcard.IndexOf('*');
        if (starIndex < 0)
        {
            return string.Equals(oldName, wildcard, StringComparison.Ordinal);
        }

        var prefix = wildcard.Substring(0, starIndex);
        var suffix = wildcard.Substring(starIndex + 1);
        return oldName.StartsWith(prefix, StringComparison.Ordinal)
            && oldName.EndsWith(suffix, StringComparison.Ordinal);
    }

    private static string ExpandWildcardReplacement(string name, SemconvMigrationCatalogEntry entry)
    {
        var replacement = entry.ReplacementNames[0];
        var oldStar = entry.OldName.IndexOf('*');
        var replacementStar = replacement.IndexOf('*');
        if (oldStar < 0 || replacementStar < 0)
        {
            return replacement;
        }

        var covered = name.Substring(oldStar, name.Length - (entry.OldName.Length - 1));
        return replacement.Substring(0, replacementStar) + covered + replacement.Substring(replacementStar + 1);
    }

    private static void Require(string value, string propertyName, SemconvMigrationCatalogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Catalog entry '{entry.OldName}' has empty {propertyName}.");
        }
    }

    private static void RequireCompleteEntry(SemconvMigrationCatalogEntry entry)
    {
        Require(entry.OldName, nameof(entry.OldName), entry);
        Require(entry.Signal, nameof(entry.Signal), entry);
        Require(entry.Domain, nameof(entry.Domain), entry);
        Require(entry.Version, nameof(entry.Version), entry);
        Require(entry.ChangelogEvidence, nameof(entry.ChangelogEvidence), entry);
    }
}
