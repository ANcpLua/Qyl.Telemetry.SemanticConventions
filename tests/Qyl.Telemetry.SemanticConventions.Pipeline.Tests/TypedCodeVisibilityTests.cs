using System.Globalization;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>
/// The tag-setter detection reads a generated table of every typed setter, value-set member and
/// starter parameter (ADR D3), so the rules that read attribute writes see typed code, not only
/// <c>SetTag("key", value)</c>.
/// </summary>
[TestClass]
public sealed class TypedCodeVisibilityTests(TestContext testContext)
{
    [TestMethod]
    public async Task Native_error_type_is_not_analyzed_as_the_upstream_enum()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new Qyl0013IncorrectAttributeTypeAnalyzer(), "QYL0013", """
            using System.Diagnostics;
            using Qyl.Telemetry.SemanticConventions.Activities;
            using Qyl.Telemetry.SemanticConventions.Incubating.Activities;
            public static class Instrumentation
            {
                public static void Stamp(Activity activity)
                {
                    activity.SetErrorTypeNative(42);
                    activity.SetErrorType(ErrorType.Custom("Failure"));
                }
            }
            """, cancellationToken: testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    [DataRow("UnrelatedActivityExtensions")]
    [DataRow("ErrorActivityExtensions")]
    public async Task Same_named_methods_require_the_declaring_type_and_parameter_signature(string declaringType)
    {
        var source = $$"""
            using System.Diagnostics;
            namespace Qyl.Telemetry.SemanticConventions.Activities
            {
                public static class {{declaringType}}
                {
                    public static Activity SetErrorType(this Activity activity, int value) => activity;
                }
            }
            public static class Instrumentation
            {
                public static void Stamp(Activity activity) =>
                    Qyl.Telemetry.SemanticConventions.Activities.{{declaringType}}.SetErrorType(activity, 42);
            }
            """;
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new Qyl0013IncorrectAttributeTypeAnalyzer(), "QYL0013", source,
            cancellationToken: testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task Qyl0012_sees_a_custom_value_through_a_typed_setter()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new Qyl0012InvalidAttributeValueAnalyzer(), "QYL0012", """
            using System.Diagnostics;
            using Qyl.Telemetry.SemanticConventions.Incubating.Activities;

            public static class Instrumentation
            {
                public static void Stamp(Activity activity) =>
                    activity.SetQylInstrumentationDomain(QylInstrumentationDomain.Custom("DB.Client"));
            }
            """, cancellationToken: testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.Contains("db.client", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0401_sees_the_operation_set_through_a_typed_setter()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new Qyl0401GenAiMissingRequiredAttributesAnalyzer(), "QYL0401", """
            using System.Diagnostics;
            using Qyl.Telemetry.SemanticConventions.Incubating.Activities;

            public static class Instrumentation
            {
                private static readonly ActivitySource Source = new("Qyl.Telemetry.AutoInstrumentation");

                public static void Chat()
                {
                    using var activity = Source.StartActivity("chat", ActivityKind.Client);
                    activity?.SetGenAiOperationName(GenAiOperationName.Chat);
                }
            }
            """, cancellationToken: testContext.CancellationToken);
        Assert.IsNotEmpty(diagnostics);
        Assert.IsTrue(diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).Any(static m => m.Contains("gen_ai.provider.name", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Qyl0400_sees_the_attributes_a_starter_sets()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new GenAiExecuteToolNameAnalyzer(), "QYL0400", """
            using Qyl;

            public static class Tools
            {
                public static void Run()
                {
                    using var span = IncubatingSpans.GenAiExecuteTool("lookup_stock", agentName: null, conversationId: null, toolCallId: null);
                }
            }
            """, cancellationToken: testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, "the starter sets gen_ai.operation.name and gen_ai.tool.name");
    }
}
