// Copyright (c) Alexander Nachtmann
// SPDX-License-Identifier: Apache-2.0

extern alias analyzers;

using RuleDocs = analyzers::Qyl.Telemetry.SemanticConventions.Analyzers.RuleDocs;

namespace Qyl.Telemetry.SemanticConventions.Analyzers.DocsGenerator;

/// <summary>
///   Top-level orchestrator. Owns the mode dispatch (<see cref="CliModes.Parse"/>),
///   the four generated-artifact pipelines (<c>Generate</c> + <c>Check</c>), and the
///   two source-side rewriters (<c>EnforceIds</c>, <c>RewriteShipped</c>). Every other
///   class in this project is pure logic invoked from here.
///   Each generated artifact (index, per-rule pages, migration catalog, SARIF,
///   editorconfig, and Shipped.md notes) has a focused renderer and matching numbered
///   steps in <see cref="Generate"/> and <see cref="Check"/>.
/// </summary>
internal static class DocsGenerator
{
    public static int Run(string[] args)
    {
        var repoRoot = RepoLayout.FindRepoRoot(AppContext.BaseDirectory);
        var outputPath = RepoLayout.IndexPath(repoRoot);
        var mode = CliModes.Parse(args);

        // EnforceIds modes don't need the catalog (they walk source files), and we
        // want them to run on a clean assembly load even if the catalog has issues.
        if (mode is Mode.EnforceIdsCheck or Mode.EnforceIdsApply)
            return EnforceIdsRewriter.Run(repoRoot, apply: mode == Mode.EnforceIdsApply);

        // RewriteShipped also bypasses the catalog — it just walks DiagnosticAnalyzer
        // subclasses via reflection to map Id → ClassName for the
        // AnalyzerReleases.Shipped.md Notes column.
        if (mode == Mode.RewriteShipped)
            return ShippedNotesRewriter.Run(repoRoot);

        var stats = CatalogStatistics.Compute();
        return mode switch
        {
            Mode.Audit => Audit(stats),
            Mode.Check => Check(stats, outputPath, repoRoot),
            _ => Generate(stats, outputPath, repoRoot),
        };
    }

    private static int Audit(CatalogStatistics stats)
    {
        Console.Write(stats.RenderAudit());
        return 0;
    }

    /// <summary>
    ///   Drift detection: every generated artifact must already exist on disk and match
    ///   what the generator would emit right now. Returns 1 on the first mismatch so CI
    ///   surfaces the staleness early without printing the full diff.
    /// </summary>
    private static int Check(CatalogStatistics stats, string outputPath, string repoRoot)
    {
        var descriptors = DescriptorCatalog.GetDescriptors();
        var fixableIds = DescriptorCatalog.GetFixableDiagnosticIds();
        var idToClass = DescriptorCatalog.BuildIdToClassMap();

        if (!IsCurrent(repoRoot, outputPath, IndexDocsRenderer.Render(descriptors, fixableIds, idToClass), "index page"))
            return 1;

        var rulesDir = RepoLayout.RulesDir(repoRoot);
        var expectedRuleFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in descriptors)
        {
            if (!idToClass.TryGetValue(d.Id, out var className))
            {
                Console.Error.WriteLine($"Descriptor {d.Id} has no owning DiagnosticAnalyzer class — cannot place per-rule page.");
                return 1;
            }
            var symbolic = RuleDocs.SymbolicNameFromFile(className);
            var rulePath = RepoLayout.RulePath(repoRoot, d.Id, symbolic);
            expectedRuleFiles.Add(Path.GetFileName(rulePath));

            if (!IsCurrent(repoRoot, rulePath, RulePageRenderer.Render(d, className, fixableIds), "per-rule page"))
                return 1;

            // HelpLinkUri drift: descriptor's URL must equal what the generator would emit now.
            var expectedUri = RuleDocs.HelpLink(d.Id, symbolic);
            if (!string.Equals(d.HelpLinkUri, expectedUri, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"HelpLinkUri drift on {d.Id}: descriptor='{d.HelpLinkUri}' expected='{expectedUri}'");
                return 1;
            }
        }

        // Fail on stale files left over in docs/rules/ that no descriptor produces.
        if (Directory.Exists(rulesDir))
        {
            foreach (var file in Directory.EnumerateFiles(rulesDir, "*.md"))
            {
                if (!expectedRuleFiles.Contains(Path.GetFileName(file)))
                {
                    Console.Error.WriteLine($"Stale per-rule page (no matching descriptor): {Path.GetRelativePath(repoRoot, file)}");
                    return 1;
                }
            }
        }

        var catalogPath = RepoLayout.MigrationCatalogPath(repoRoot);
        if (!IsCurrent(repoRoot, catalogPath, MigrationCatalogRenderer.Render(stats), "migration catalog"))
            return 1;

        var sarifPath = RepoLayout.SarifPath(repoRoot);
        if (!IsCurrent(repoRoot, sarifPath, SarifRenderer.Render(descriptors, idToClass), "SARIF manifest"))
            return 1;

        foreach (var (path, expected) in EditorconfigRenderer.EnumerateProfiles(repoRoot, descriptors))
        {
            if (!IsCurrent(repoRoot, path, expected, "editorconfig profile"))
                return 1;
        }

        // RS2008 covers rows; this additionally locks their documentation links.
        var shippedPath = RepoLayout.ShippedReleasesPath(repoRoot);
        if (File.Exists(shippedPath))
        {
            var existingShipped = File.ReadAllText(shippedPath);
            var expectedShipped = ShippedNotesRewriter.Rewrite(existingShipped);
            if (!string.Equals(existingShipped, expectedShipped, StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"Shipped.md Notes column is stale (run --rewrite-shipped): {Path.GetRelativePath(repoRoot, shippedPath)}");
                return 1;
            }
        }

        Console.WriteLine($@"Index docs are up to date: {Path.GetRelativePath(repoRoot, outputPath)}");
        Console.WriteLine($@"Per-rule pages are up to date ({descriptors.Count}).");
        Console.WriteLine($@"Migration catalog is up to date: {Path.GetRelativePath(repoRoot, catalogPath)}");
        Console.WriteLine($@"SARIF manifest is up to date: {Path.GetRelativePath(repoRoot, sarifPath)}");
        Console.WriteLine(@"Editorconfig profiles are up to date.");
        Console.WriteLine(@"Shipped.md Notes column is up to date.");
        Console.WriteLine(@"HelpLinkUri values match per-rule page URLs.");
        return 0;
    }

    /// <summary>
    ///   Whether the generated <paramref name="artifact"/> at <paramref name="path"/> exists and
    ///   equals <paramref name="expected"/>; writes <c>Missing {artifact}</c> or
    ///   <c>{artifact} is stale</c> with the repository-relative path otherwise.
    /// </summary>
    private static bool IsCurrent(string repoRoot, string path, string expected, string artifact)
    {
        var relativePath = Path.GetRelativePath(repoRoot, path);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Missing {artifact}: {relativePath}");
            return false;
        }
        if (!string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"{char.ToUpperInvariant(artifact[0])}{artifact[1..]} is stale: {relativePath}");
            return false;
        }
        return true;
    }

    /// <summary>
    ///   Writes every generated artifact. The order matches the numbering in
    ///   <see cref="Check"/> so the two methods stay in lockstep when a new artifact
    ///   is added.
    /// </summary>
    private static int Generate(CatalogStatistics stats, string outputPath, string repoRoot)
    {
        var descriptors = DescriptorCatalog.GetDescriptors();
        var fixableIds = DescriptorCatalog.GetFixableDiagnosticIds();
        var idToClass = DescriptorCatalog.BuildIdToClassMap();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, IndexDocsRenderer.Render(descriptors, fixableIds, idToClass));
        Console.WriteLine($@"Wrote {Path.GetRelativePath(repoRoot, outputPath)}");

        var rulesDir = RepoLayout.RulesDir(repoRoot);
        Directory.CreateDirectory(rulesDir);
        var expectedRuleFiles = new HashSet<string>(StringComparer.Ordinal);
        var pages = new List<(string Path, string Content)>();
        foreach (var d in descriptors)
        {
            if (!idToClass.TryGetValue(d.Id, out var className)) continue;
            var symbolic = RuleDocs.SymbolicNameFromFile(className);
            var rulePath = RepoLayout.RulePath(repoRoot, d.Id, symbolic);
            expectedRuleFiles.Add(Path.GetFileName(rulePath));
            pages.Add((rulePath, RulePageRenderer.Render(d, className, fixableIds)));
        }
        foreach (var file in Directory.EnumerateFiles(rulesDir, "*.md"))
        {
            if (!expectedRuleFiles.Contains(Path.GetFileName(file)))
            {
                File.Delete(file);
                Console.WriteLine($@"Removed stale {Path.GetRelativePath(repoRoot, file)}");
            }
        }
        // Delete obsolete filenames before writing replacements: on a case-insensitive
        // filesystem, writing first leaves an old casing that cleanup would then delete.
        foreach (var page in pages)
            File.WriteAllText(page.Path, page.Content);
        Console.WriteLine($@"Wrote {pages.Count} per-rule pages under docs/rules/");

        var catalogPath = RepoLayout.MigrationCatalogPath(repoRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        File.WriteAllText(catalogPath, MigrationCatalogRenderer.Render(stats));
        Console.WriteLine($@"Wrote {Path.GetRelativePath(repoRoot, catalogPath)}");

        var sarifPath = RepoLayout.SarifPath(repoRoot);
        File.WriteAllText(sarifPath, SarifRenderer.Render(descriptors, idToClass));
        Console.WriteLine($@"Wrote {Path.GetRelativePath(repoRoot, sarifPath)}");

        foreach (var (path, content) in EditorconfigRenderer.EnumerateProfiles(repoRoot, descriptors))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            Console.WriteLine($@"Wrote {Path.GetRelativePath(repoRoot, path)}");
        }
        return 0;
    }
}
