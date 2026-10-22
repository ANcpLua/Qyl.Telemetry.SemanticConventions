using ANcpLua.Roslyn.Utilities.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
[TestClass]
public sealed class SupplementalSemconvMigrationAnalyzerTests(TestContext testContext)
{
    public static IEnumerable<object[]> PayloadContexts =>
    [
        [
            "activity.SetTag(\"gen_ai.system\", \"openai\");",
            "QYL0009",
            1
        ],
        [
            "activity.AddTag(\"gen_ai.system\", \"openai\");",
            "QYL0009",
            1
        ],
        [
            "var values = new Dictionary<string, object?>(); values.Add(\"gen_ai.system\", \"openai\");",
            "QYL0010",
            1
        ],
        [
            "var tags = new Dictionary<string, object?>(); tags.Add(\"gen_ai.system\", \"openai\"); source.StartActivity(\"operation\", ActivityKind.Internal, default(ActivityContext), tags: tags);",
            "QYL0009",
            1
        ],
        [
            "var tags = new Dictionary<string, object?> { [\"gen_ai.system\"] = \"openai\" }; source.StartActivity(\"operation\", ActivityKind.Internal, default(ActivityContext), tags: tags);",
            "QYL0009",
            1
        ],
        [
            "counter.Add(1, new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\"));",
            "QYL0009",
            1
        ],
        [
            "_ = new Measurement<int>(1, new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\"));",
            "QYL0009",
            1
        ],
        [
            "logger.BeginScope(new[] { new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\") });",
            "QYL0009",
            1
        ],
        [
            "logger.Log(LogLevel.Information, new EventId(0), new[] { new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\") }, null, static (state, error) => string.Empty);",
            "QYL0009",
            1
        ],
        [
            "_ = new ActivityEvent(\"event\", tags: new ActivityTagsCollection(new[] { new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\") }));",
            "QYL0009",
            1
        ],
        [
            "_ = new ActivityLink(default, new ActivityTagsCollection(new[] { new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\") }));",
            "QYL0009",
            1
        ],
        [
            "KeyValuePair<string, object?>[] tags = [.. new[] { new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\") }]; source.StartActivity(\"operation\", ActivityKind.Internal, default(ActivityContext), tags: tags);",
            "QYL0009",
            1
        ],
        [
            "tagList.Add(\"gen_ai.system\", \"openai\");",
            "QYL0009",
            1
        ],
        [
            "var tags = new[] { new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\") }; source.StartActivity(\"operation\", ActivityKind.Internal, default(ActivityContext), tags: tags);",
            "QYL0009",
            1
        ],
        [
            "resourceBuilder.AddAttributes(new[] { new KeyValuePair<string, object?>(\"gen_ai.system\", \"openai\") });",
            "QYL0009",
            1
        ],
    ];

    [TestMethod]
    [DynamicData(nameof(PayloadContexts))]
    public async Task Shared_payload_detection_preserves_supplemental_contexts(string statement, string expectedId, int expectedCount)
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture(statement))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.HasCount(expectedCount, diagnostics);
        Assert.IsTrue(diagnostics.All(diagnostic => diagnostic.Id == expectedId));
    }

    [TestMethod]
    [DataRow("meter.CreateCounter<int>(\"system.memory.shared\");", "QYL0009")]
    [DataRow("activity.AddEvent(new ActivityEvent(\"rpc.message\"));", "QYL0010")]
    [DataRow("_ = new ActivityEvent(\"rpc.message\");", "QYL0010")]
    public async Task Metric_and_event_name_paths_remain_owned(string statement, string expectedId)
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture(statement))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(expectedId, diagnostics[0].Id);
    }

    [TestMethod]
    public async Task Two_dictionaries_in_one_method_are_classified_by_which_one_reaches_telemetry()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("""
                var tags = new Dictionary<string, object?>();
                tags.Add("gen_ai.system", "openai");
                var values = new Dictionary<string, object?>();
                values.Add("gen_ai.system", "openai");
                source.StartActivity("operation", ActivityKind.Internal, default(ActivityContext), tags: tags);
                """))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.HasCount(2, diagnostics);
        Assert.ContainsSingle(diagnostics.Where(diagnostic => diagnostic.Id == "QYL0009"));
        Assert.ContainsSingle(diagnostics.Where(diagnostic => diagnostic.Id == "QYL0010"));
    }

    [TestMethod]
    public async Task Constant_key_is_not_reclassified_as_a_hard_coded_literal()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(GenAiSystem, \"openai\");"))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Bare_value_is_reported_but_constant_value_is_not()
    {
        var bare = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"cloud.platform\", \"azure_aks\");"))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        var constant = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"cloud.platform\", AzureAks);"))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(bare);
        Assert.AreEqual("QYL0009", bare[0].Id);
        Assert.IsEmpty(constant);
    }

    [TestMethod]
    public async Task Deprecated_first_input_delay_value_reports_inp_replacement()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"browser.web_vital.name\", \"fid\");"))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0009", diagnostics[0].Id);
        Assert.AreEqual("inp", diagnostics[0].Properties["ReplacementName"]);
    }

    [TestMethod]
    public async Task Catalog_owner_source_is_not_diagnosed()
    {
        var compilation = CompileTelemetry(Compile.Source(Fixture("var values = new Dictionary<string, object?>(); values.Add(\"gen_ai.system\", \"openai\");")));
        var tree = compilation.SyntaxTrees.Single();
        var diagnostics = await compilation.ReplaceSyntaxTree(tree, tree.WithFilePath("/repo/OpenTelemetryDeprecatedSemconvCatalog.cs")).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Generated_source_is_excluded_by_Roslyn()
    {
        var compilation = CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"gen_ai.system\", \"openai\");")));
        var tree = compilation.SyntaxTrees.Single();
        var diagnostics = await compilation.ReplaceSyntaxTree(tree, tree.WithFilePath("/repo/Telemetry.g.cs")).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Catalog_fallback_reports_one_terminal_GenAi_replacement_without_live_metadata()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"gen_ai.system\", \"openai\");"))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer(), new LiteralMatchesDeprecatedSemconvAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0009", diagnostics[0].Id);
        Assert.AreEqual("gen_ai.provider.name", diagnostics[0].Properties["ReplacementName"]);
    }

    [TestMethod]
    public async Task Live_metadata_wins_with_one_terminal_GenAi_replacement()
    {
        var liveSemconv = CompileTelemetry(Compile.Source("""
                namespace OpenTelemetry.SemanticConventions.Attributes.GenAi
                {
                    public static class GenAiAttributes
                    {
                        [System.Obsolete("Replaced by gen_ai.provider.name.")]
                        public const string AttributeGenAiSystem = "gen_ai.system";
                    }
                }
                """).WithAssemblyName("LiveSemconv"));
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"gen_ai.system\", \"openai\");"))).AddReferences(liveSemconv.ToMetadataReference()).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer(), new LiteralMatchesDeprecatedSemconvAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0005", diagnostics[0].Id);
        Assert.AreEqual("gen_ai.provider.name", diagnostics[0].Properties["ReplacementValue"]);
    }

    [TestMethod]
    public async Task Qyl0009_NonPublicObsoleteConstant_ReportsCatalogMigration()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"gen_ai.system\", \"openai\");")).WithSource("""
                namespace OpenTelemetry.SemanticConventions.Attributes.GenAi
                {
                    public static class GenAiAttributes
                    {
                        [System.Obsolete("Replaced by gen_ai.provider.name.")]
                        internal const string AttributeGenAiSystem = "gen_ai.system";
                    }
                }
                """)).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer(), new LiteralMatchesDeprecatedSemconvAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        var diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0009", diagnostic.Id);
    }

    [TestMethod]
    public async Task Qyl0009_WildcardPrefixKey_ReplacesPrefixOfConcreteKey()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"db.elasticsearch.path_parts.index\", \"orders\");"))).WithAnalyzers([new SupplementalSemconvMigrationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        var diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("db.operation.parameter.index", diagnostic.Properties["ReplacementName"]);
    }

    [TestMethod]
    public async Task Qyl0007_ObsoleteValueOfQylPackage_WinsOverCatalogFallback()
    {
        const string source = """
            using System.Diagnostics;
            public static class Instrumentation
            {
                public static void Stamp(Activity activity) => activity.SetTag("cpu.mode", "kernel");
            }
            """;
        var live = await AnalyzerRun.DiagnosticsAsync(new DeprecatedSemconvValueAnalyzer(), "QYL0007", source, cancellationToken: testContext.CancellationToken);
        var catalog = await AnalyzerRun.DiagnosticsAsync(new SupplementalSemconvMigrationAnalyzer(), "QYL0009", source, cancellationToken: testContext.CancellationToken);
        Assert.ContainsSingle(live);
        Assert.IsEmpty(catalog);
    }

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation CompileTelemetry(Compile source) =>
        AnalyzerRun.CompileSource(source.WithReference<System.Diagnostics.Activity>()
            .WithReference<System.Diagnostics.Metrics.Meter>()
            .WithReference<OpenTelemetry.Resources.ResourceBuilder>()
            .WithReference<Microsoft.Extensions.Logging.ILogger>());

    private static string Fixture(string statement) => SourceFixture.InMethod(statement, declarations: """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Diagnostics.Metrics;
        using Microsoft.Extensions.Logging;
        using OpenTelemetry.Resources;
        """, members: """
        private const string GenAiSystem = "gen_ai.system";
        private const string AzureAks = "azure_aks";
        private static readonly ActivitySource source = new("test.telemetry");
        private static readonly Meter meter = new("test.telemetry");
        private static readonly Counter<int> counter = meter.CreateCounter<int>("requests");
        private readonly Activity activity = new("operation");
        private readonly ILogger logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        private TagList tagList = new();
        private readonly ResourceBuilder resourceBuilder = ResourceBuilder.CreateEmpty();
        """);
}
