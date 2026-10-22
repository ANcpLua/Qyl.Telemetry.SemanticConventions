using System.Diagnostics;
using System.Globalization;
using ANcpLua.Roslyn.Utilities.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
[TestClass]
public sealed class RegistryDerivedAnalyzerTests(TestContext testContext)
{

    [TestMethod]
    public async Task Qyl0013_preserves_integer_enum_types()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("""
                SetTag("cpython.gc.generation", 0L);
                SetTag("cpython.gc.generation", "0");
                SetTag("rpc.grpc.status_code", 0L);
                SetTag("rpc.grpc.status_code", "0");
                """))).WithAnalyzers([new Qyl0013IncorrectAttributeTypeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.HasCount(2, diagnostics);
        Assert.IsTrue(diagnostics.All(static diagnostic => diagnostic.Id == "QYL0013"));
    }

    [TestMethod]
    public async Task Qyl0013_uses_attribute_types_outside_the_old_handwritten_subset()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("SetTag(\"gen_ai.memory.record.id\", 42);"))).WithAnalyzers([new Qyl0013IncorrectAttributeTypeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0013", diagnostics[0].Id);
        Assert.Contains("string", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0013_accepts_registry_array_and_schema_governed_any_types()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("""
                SetTag("gen_ai.response.finish_reasons", new[] { "stop" });
                SetTag("gen_ai.input.messages", new object());
                """))).WithAnalyzers([new Qyl0013IncorrectAttributeTypeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0013_enforces_registry_integer_types()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("""
                SetTag("http.request.method", "GET");
                SetTag("http.request.resend_count", 1);
                SetTag("http.response.status_code", "200");
                """))).WithAnalyzers([new Qyl0013IncorrectAttributeTypeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0013", diagnostics[0].Id);
        Assert.Contains("integer", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0013_NullableValueOfRegistryType_IsAccepted()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("""
                int? port = 443;
                long? size = 512L;
                SetTag("server.port", port);
                SetTag("http.request.body.size", size);
                """))).WithAnalyzers([new Qyl0013IncorrectAttributeTypeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0013_ConversationThreadUuidUnderThreadId_IsAccepted()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("SetTag(\"thread.id\", \"0199a213-81c0-7800-8aa1-bbab2a035a53\");"))).WithAnalyzers([new Qyl0013IncorrectAttributeTypeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0013_BooleanUnderThreadId_IsReported()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("SetTag(\"thread.id\", true);"))).WithAnalyzers([new Qyl0013IncorrectAttributeTypeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.Contains("integer", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0012_enforces_qyl_owned_enum_tokens()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("""
                SetTag("qyl.instrumentation.domain", "HTTP.CLIENT");
                SetTag("qyl.instrumentation.domain", "Messaging.RabbitMq");
                """))).WithAnalyzers([new Qyl0012InvalidAttributeValueAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.HasCount(2, diagnostics);
        Assert.IsTrue(diagnostics.Select(static diagnostic => diagnostic.Id).All(static id => id == "QYL0012"));
        Assert.IsTrue(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)).Any(static message => message.Contains("http.client", StringComparison.Ordinal)));
        Assert.IsTrue(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)).Any(static message => message.Contains("messaging.rabbitmq", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Qyl0012_reports_only_noncanonical_spelling_of_known_enum_values()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("SetTag(\"http.request.method\", \"get\");"))).WithAnalyzers([new Qyl0012InvalidAttributeValueAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0012", diagnostics[0].Id);
        Assert.Contains("'GET'", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0012_allows_unknown_extension_enum_values()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("SetTag(\"http.request.method\", \"CUSTOM\");"))).WithAnalyzers([new Qyl0012InvalidAttributeValueAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0403_uses_the_generated_operation_enum()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("SetTag(\"gen_ai.operation.name\", \"Execute_Tool\");"))).WithAnalyzers([new Qyl0012InvalidAttributeValueAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0403", diagnostics[0].Id);
        Assert.Contains("execute_tool", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0403_accepts_new_memory_and_custom_operations()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("""
                SetTag("gen_ai.operation.name", "create_memory_store");
                SetTag("gen_ai.operation.name", "vendor_specific_operation");
                """))).WithAnalyzers([new Qyl0012InvalidAttributeValueAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    [DataRow("gen_ai.client.inference.operation.input_tokens")]
    [DataRow("gen_ai.client.inference.operation.output_tokens")]
    [DataRow("gen_ai.server.time_per_output_token")]
    public async Task Qyl0402_accepts_registry_defined_token_metrics(string metricName)
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(HistogramSource(metricName)).WithReference<Activity>()).WithAnalyzers([new Qyl0402UseTokenUsageHistogramAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0402_reports_unknown_genai_token_metrics()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(HistogramSource("gen_ai.custom.token.count")).WithReference<Activity>()).WithAnalyzers([new Qyl0402UseTokenUsageHistogramAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0402", diagnostics[0].Id);
        Assert.Contains("gen_ai.client.inference.operation.input_tokens", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0400_uses_generated_execute_tool_requirements()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("SetTag(\"gen_ai.operation.name\", \"execute_tool\");"))).WithAnalyzers([new GenAiExecuteToolNameAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0400", diagnostics[0].Id);
        Assert.Contains("gen_ai.tool.name", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0400_is_silent_when_generated_execute_tool_requirement_is_present()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(SinkSource("""
                SetTag("gen_ai.operation.name", "execute_tool");
                SetTag("gen_ai.tool.name", "weather");
                """))).WithAnalyzers([new GenAiExecuteToolNameAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0401_applies_the_internal_execute_tool_span_requirements()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(ActivitySource("ActivityKind.Internal", "SetTag(\"gen_ai.operation.name\", \"execute_tool\");")).WithReference<Activity>()).WithAnalyzers([new Qyl0401GenAiMissingRequiredAttributesAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0401", diagnostics[0].Id);
        Assert.Contains("gen_ai.tool.name", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0401_applies_the_client_inference_requirements()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(ActivitySource("ActivityKind.Client", "SetTag(\"gen_ai.operation.name\", \"chat\");")).WithReference<Activity>()).WithAnalyzers([new Qyl0401GenAiMissingRequiredAttributesAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0401", diagnostics[0].Id);
        Assert.Contains("gen_ai.provider.name", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0401_applies_provider_refinement_requirements()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(ActivitySource("ActivityKind.Client", """
                SetTag("gen_ai.operation.name", "chat");
                SetTag("gen_ai.provider.name", "openai");
                """)).WithReference<Activity>()).WithAnalyzers([new Qyl0401GenAiMissingRequiredAttributesAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0401", diagnostics[0].Id);
        Assert.Contains("gen_ai.request.model", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0401_does_not_promote_conditional_model_to_universally_required()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(ActivitySource("ActivityKind.Client", """
                SetTag("gen_ai.operation.name", "chat");
                SetTag("gen_ai.provider.name", "custom.provider");
                """)).WithReference<Activity>()).WithAnalyzers([new Qyl0401GenAiMissingRequiredAttributesAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0401_resolves_mcp_client_spans_separately()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(ActivitySource("ActivityKind.Client", "SetTag(\"mcp.method.name\", \"tools/call\");")).WithReference<Activity>()).WithAnalyzers([new Qyl0401GenAiMissingRequiredAttributesAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0401_accepts_memory_operations_added_by_the_pinned_registry()
    {
        var diagnostics = await AnalyzerRun.CompileSource(Compile.Source(ActivitySource("ActivityKind.Client", "SetTag(\"gen_ai.operation.name\", \"create_memory_store\");")).WithReference<Activity>()).WithAnalyzers([new Qyl0401GenAiMissingRequiredAttributesAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    private static string SinkSource(string statements) => SourceFixture.InMethod(statements, members: "private void SetTag(string key, object? value) { }");
    private static string ActivitySource(string kind, string statements) => SourceFixture.InMethod($$"""
            using var activity = Source.StartActivity("operation", {{kind}});
            void SetTag(string key, object? value) => activity?.SetTag(key, value);
            {{statements}}
            """, declarations: "using System.Diagnostics;", members: "private static readonly ActivitySource Source = new(\"tests\");", typeName: "Instrumentation");
    private static string HistogramSource(string metricName) => SourceFixture.InMethod($"using var meter = new System.Diagnostics.Metrics.Meter(\"application\"); meter.CreateHistogram<double>(\"{metricName}\");");
}
