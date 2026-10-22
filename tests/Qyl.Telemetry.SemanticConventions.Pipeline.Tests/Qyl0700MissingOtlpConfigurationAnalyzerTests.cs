using ANcpLua.Roslyn.Utilities.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>
///   QYL0700: an OTLP exporter call without an endpoint is reported; an endpoint passed as a
///   <c>Uri</c> or set through <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> before the call satisfies it.
/// </summary>
[TestClass]
public sealed class Qyl0700MissingOtlpConfigurationAnalyzerTests(TestContext testContext)
{
    [TestMethod]
    public async Task Qyl0700_EndpointFromEnvironmentOrTargetTypedUri_IsSatisfied()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source("""
            using System;
            public sealed class Builder { }
            public static class OtlpExtensions
            {
                public static Builder UseOtlpExporter(this Builder builder) => builder;
                public static Builder UseOtlpExporter(this Builder builder, int protocol, Uri baseUrl) => builder;
            }
            public sealed class Startup
            {
                public Startup(Builder builder)
                {
                    Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "https://collector.example:4317");
                    builder.UseOtlpExporter();
                }

                public static void Configure(Builder builder) =>
                    builder.UseOtlpExporter(0, new("https://collector.example:4317"));
            }
            """).WithReference<Uri>())
            .WithAnalyzers([new Qyl0700MissingOtlpConfigurationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0700_EndpointFromEnvironmentInTopLevelStatements_IsSatisfied()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source($$"""
            using System;
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "https://collector.example:4317");
            new Builder().UseOtlpExporter();
            {{ExporterDeclarations}}
            """).WithReference<Uri>().AsExecutable())
            .WithAnalyzers([new Qyl0700MissingOtlpConfigurationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0700_ExporterWithoutEndpoint_IsReported()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source($$"""
            using System;
            {{ExporterDeclarations}}
            public static class Startup
            {
                public static void Configure(Builder builder) => builder.UseOtlpExporter();
            }
            """).WithReference<Uri>())
            .WithAnalyzers([new Qyl0700MissingOtlpConfigurationAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        var diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0700", diagnostic.Id);
    }

    private const string ExporterDeclarations = """
        public sealed class Builder { }
        public static class OtlpExtensions
        {
            public static Builder UseOtlpExporter(this Builder builder) => builder;
        }
        """;
}
