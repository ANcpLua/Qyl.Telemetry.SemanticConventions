using System.Collections.Immutable;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class MigrationCatalogEntryTests
{
    [TestMethod]
    public void Equivalent_catalog_entries_with_independently_allocated_replacements_deduplicate()
    {
        var first = Rename("gen_ai.provider.name");
        var second = Rename("gen_ai.provider.name");
        var entries = ImmutableDictionary<string, SemconvMigrationCatalogEntry>.Empty
            .Add(first.OldName, first).Add(second.OldName, second);
        var distinct = new HashSet<SemconvMigrationCatalogEntry> { first, second };

        Assert.HasCount(1, entries);
        Assert.AreSequenceEqual(["gen_ai.provider.name"], entries["gen_ai.system"].ReplacementNames);
        Assert.HasCount(1, distinct);
    }

    [TestMethod]
    public void CatalogVersionOrder_TwoDigitMinor_SortsNumerically()
    {
        string[] versions = ["1.40.0", "unknown", "1.9.0", "1.13.0"];

        var ordered = versions.Order(SemconvMigrationCatalog.VersionComparer).ToArray();

        Assert.AreSequenceEqual(["unknown", "1.9.0", "1.13.0", "1.40.0"], ordered);
    }

    [TestMethod]
    public void Conflicting_replacements_for_the_same_catalog_key_are_rejected()
    {
        var entries = ImmutableDictionary<string, SemconvMigrationCatalogEntry>.Empty
            .Add("gen_ai.system", Rename("gen_ai.provider.name"));

        Assert.ThrowsExactly<ArgumentException>(() => entries.Add("gen_ai.system", Rename("gen_ai.operation.name")));
        Assert.AreSequenceEqual(["gen_ai.provider.name"], entries["gen_ai.system"].ReplacementNames);
    }

    private static SemconvMigrationCatalogEntry Rename(string replacement) => new(
        "gen_ai.system", SemconvMigrationItemKind.AttributeKey, "trace", "gen_ai", "1.44.0",
        [replacement], SemconvMigrationKind.ExactRename, "Semantic-convention rename");
}
