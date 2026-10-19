// Copyright (c) Alexander Nachtmann
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotCov;
using DotCov.Fallout;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Solutions;
using Serilog;

namespace Qyl.Telemetry.SemanticConventions.Build;

/// <summary>
///   Repository-local build host: compile, the coverage gate, and the analyzer documentation
///   targets. The packages' generated surface is committed and guarded by
///   scripts/check-generated.sh, so nothing here regenerates it.
/// </summary>
internal sealed class Build : FalloutBuild, ICoverageReport
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Solution("Qyl.Telemetry.SemanticConventions.slnx", GenerateProjects = false)]
    readonly Solution _solution = null!;

    AbsolutePath DocsGeneratorProject =>
        RootDirectory / "tools"
        / "Qyl.Telemetry.SemanticConventions.Analyzers.DocsGenerator"
        / "Qyl.Telemetry.SemanticConventions.Analyzers.DocsGenerator.csproj";

    AbsolutePath PipelineTestsProject =>
        RootDirectory / "tests"
        / "Qyl.Telemetry.SemanticConventions.Pipeline.Tests"
        / "Qyl.Telemetry.SemanticConventions.Pipeline.Tests.csproj";

    AbsolutePath CoverageDirectory => RootDirectory / "artifacts" / "coverage";

    // The coverage gate's thresholds sit a few points under what the pipeline tests measured over
    // this repository's sources (test code excluded, identical over two runs): line 30.2 %
    // (3568/11831), branch 26.3 % (1053/3997). The untested generated helpers in the Incubating
    // package (Spans, Metrics, Events, Entities) hold the numbers down.
    // --coverage-min-line and --coverage-min-branch override a threshold for one run.
    string ICoverageReport.MinLine => ((ICoverageReport)this).TryGetValue(() => ((ICoverageReport)this).MinLine) ?? "27";
    string ICoverageReport.MinBranch => ((ICoverageReport)this).TryGetValue(() => ((ICoverageReport)this).MinBranch) ?? "23";
    string ICoverageReport.ExcludeTestCodeParam => "true";
    AbsolutePath ICoverageReport.CoverageSearchDirectory => CoverageDirectory;

    /// <summary>Restore + compile every project in the solution.</summary>
    Target Compile => target => target
        .Executes(() =>
        {
            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(_solution)
                .SetConfiguration("Release")
                .EnableNoLogo()
                .SetProcessAdditionalArguments("-m:4"));
        });

    /// <summary>
    ///   Run the pipeline tests under Microsoft code coverage into a clean artifacts/coverage,
    ///   then let dotcov's ReportCoverage gate the Cobertura report against the thresholds above.
    ///   A failed test run exits 6 like <c>dotcov test</c>: coverage of failing tests is not
    ///   evaluated. ReportCoverage exits with dotcov's gate codes (1 FAIL, 2 invalid parameter,
    ///   3 NODATA, 4 DISABLED, 5 unreadable report); scripts/quiet.sh names them.
    /// </summary>
    [JetBrains.Annotations.UsedImplicitly]
    Target Coverage => target => target
        .Description("Run the pipeline tests with coverage and gate line and branch coverage.")
        .DependsOn(Compile)
        .Triggers<ICoverageReport>(x => x.ReportCoverage)
        .Executes(() =>
        {
            CoverageDirectory.CreateOrCleanDirectory();
            // Only sources under this repository: ANcpLua.Roslyn.Utilities.Sources compiles 127
            // files from the NuGet cache into the analyzers, and they are not this repository's
            // code. A CI build's mapped PDB paths (/_/, /_1/) still match: Microsoft code coverage
            // resolves them to the local paths before it applies the filter.
            var settings = CoverageDirectory / "settings.xml";
            new XElement("Configuration",
                new XElement("CodeCoverage",
                    new XElement("Sources",
                        new XElement("Include",
                            new XElement("Source", $"^{Regex.Escape(RootDirectory.ToString())}[\\\\/].*")))))
                .Save(settings);
            try
            {
                DotNetTasks.DotNetRun(s => s
                    .SetProjectFile(PipelineTestsProject)
                    .SetConfiguration("Release")
                    .EnableNoBuild()
                    .SetApplicationArguments(
                        "--coverage", "--coverage-output-format", "cobertura",
                        "--coverage-output", "coverage.cobertura.xml",
                        "--coverage-settings", settings,
                        "--results-directory", CoverageDirectory));
            }
            catch (ProcessException exception)
            {
                ExitCode = ExitCodes.TestRunFailed;
                throw new InvalidOperationException(
                    $"dotnet test exited with code {exception.ExitCode}; coverage was not evaluated.", exception);
            }
        });

    /// <summary>
    ///   Regenerate the analyzer index, per-rule pages, migration catalog, SARIF, and
    ///   editorconfig profiles from <c>DiagnosticDescriptors</c> and
    ///   <c>SemconvMigrationCatalog</c>. Every QYL rule's <c>HelpLinkUri</c> resolves to its
    ///   generated per-rule page.
    /// </summary>
    [JetBrains.Annotations.UsedImplicitly]
    Target GenerateDocs => target => target
        .Description("Regenerate analyzer documentation and machine-readable rule artifacts.")
        .Executes(() => RunDocsGenerator(buildGenerator: true));

    /// <summary>Print catalog statistics (curated vs supplemental, fixable counts, ...). No file I/O.</summary>
    [JetBrains.Annotations.UsedImplicitly]
    Target AuditDocs => target => target
        .Description("Print analyzer catalog statistics.")
        .DependsOn(Compile)
        .Executes(() => RunDocsGenerator(buildGenerator: false, "--audit"));

    /// <summary>
    ///   Rewrite analyzer source files in-place to make class names, class XML doc summaries, and
    ///   DiagnosticId-const docs match the runtime <c>DiagnosticDescriptor.Id</c> they register.
    ///   Run after an intentional rule-id change; review the diff before committing.
    /// </summary>
    [JetBrains.Annotations.UsedImplicitly]
    Target EnforceIdsApply => target => target
        .Description("Rewrite analyzer files to align class/doc/id-const with runtime descriptors.")
        .DependsOn(Compile)
        .Executes(() => RunDocsGenerator(buildGenerator: false, "--enforce-ids", "--apply"));

    /// <summary>Each application argument reaches the generator as its own argv entry.</summary>
    void RunDocsGenerator(bool buildGenerator, params string[] applicationArguments)
    {
        var settings = new DotNetRunSettings()
            .SetProjectFile(DocsGeneratorProject)
            .SetConfiguration("Release");

        settings = buildGenerator
            ? settings.SetProcessEnvironmentVariable("_QylInsideConsistencyCheck", "true")
            : settings.EnableNoBuild().EnableNoRestore();

        DotNetTasks.DotNetRun(settings.SetApplicationArguments(applicationArguments));
        Log.Information("DocsGenerator finished ({Mode}).",
            applicationArguments.Length == 0 ? "generate" : string.Join(' ', applicationArguments));
    }
}
