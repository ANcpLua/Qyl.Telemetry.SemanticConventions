using System.Diagnostics;
using System.Text.Json;
using ANcpLua.Roslyn.Utilities.Testing;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class SemconvConstantNamespaceTests(TestContext testContext)
{
    [TestMethod]
    [DataRow("OpenTelemetry.SemanticConventions.Attributes", 1)]
    [DataRow("Qyl.Telemetry.SemanticConventions.Attributes", 1)]
    [DataRow("Qyl.Telemetry.SemanticConventions.Incubating.Attributes", 1)]
    [DataRow("NotQyl.Telemetry.SemanticConventions.Attributes", 0)]
    public async Task Namespace_roots_preserve_upstream_support_and_reject_lookalikes(string attributeNamespace, int expectedDiagnostics)
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source($$"""
            using System.Diagnostics;
            namespace {{attributeNamespace}}
            {
                public static class FixtureAttributes { public const string Key = "fixture.key"; }
            }
            namespace Consumer
            {
                internal static class Writer
                {
                    internal static void Write(Activity activity) => activity.SetTag("fixture.key", 1);
                }
            }
            """).WithReference<Activity>())
            .WithAnalyzers([new PreferSemconvConstantAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);

        Assert.HasCount(expectedDiagnostics, diagnostics);
        foreach (var diagnostic in diagnostics) Assert.AreEqual("QYL0004", diagnostic.Id);
    }

    [TestMethod]
    public async Task Qyl0004_ObsoletePackageConstant_IsNeverSuggested()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new PreferSemconvConstantAnalyzer(), "QYL0004", """
            using System.Diagnostics;
            internal static class Consumer
            {
                internal static void Write(Activity activity) => activity.SetTag("db.system", "postgresql");
            }
            """, cancellationToken: testContext.CancellationToken);

        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Actual_package_constants_apply_to_telemetry_keys_and_leave_protocol_fields_alone()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source("""
            using System.Diagnostics;
            using System.Text.Json;
            internal static class Consumer
            {
                internal static void Write(Activity activity) => activity.SetTag("service.name", "probe");
                internal static string Read(JsonElement value) => value.GetProperty("service.name").GetRawText();
            }
            """).WithReference<Activity>().WithReference<JsonElement>().WithReference<AttributeRef>())
            .WithAnalyzers([new PreferSemconvConstantAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);

        var diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0004", diagnostic.Id);
        var node = diagnostic.Location.SourceTree!.GetRoot(testContext.CancellationToken)
            .FindNode(diagnostic.Location.SourceSpan);
        var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().First();
        Assert.AreEqual("SetTag", ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText);
    }
}
