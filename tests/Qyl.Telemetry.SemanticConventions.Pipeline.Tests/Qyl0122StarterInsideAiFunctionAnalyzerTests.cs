using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>QYL0122: FunctionInvokingChatClient already records the execute_tool span of an AIFunction (ADR D6).</summary>
[TestClass]
public sealed class Qyl0122StarterInsideAiFunctionAnalyzerTests(TestContext testContext)
{

    private static readonly (string, string) MicrosoftExtensionsAi = ("Microsoft.Extensions.AI", """
        namespace Microsoft.Extensions.AI
        {
            public static class AIFunctionFactory
            {
                public static object Create(System.Delegate method, string? name = null) => method;
            }
        }
        """);
    [TestMethod]
    public async Task Reports_a_starter_inside_the_function_delegate()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new Qyl0122StarterInsideAiFunctionAnalyzer(), "QYL0122", """
            using Microsoft.Extensions.AI;
            using Qyl;

            public static class Tools
            {
                public static object LookupStock() => AIFunctionFactory.Create((string sku) =>
                {
                    using var span = IncubatingSpans.GenAiExecuteTool("lookup_stock", agentName: null, conversationId: null, toolCallId: null);
                    using var work = Spans.Work("lookup stock");
                    return 3;
                }, "lookup_stock");
            }
            """, libraries: [MicrosoftExtensionsAi], cancellationToken: testContext.CancellationToken);
        Assert.HasCount(2, diagnostics);
    }

    [TestMethod]
    public async Task Is_silent_outside_the_function_delegate()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new Qyl0122StarterInsideAiFunctionAnalyzer(), "QYL0122", """
            using Microsoft.Extensions.AI;
            using Qyl;

            public static class Tools
            {
                public static object LookupStock()
                {
                    using var span = Spans.Work("register tools");
                    return AIFunctionFactory.Create((string sku) => 3, "lookup_stock");
                }
            }
            """, libraries: [MicrosoftExtensionsAi], cancellationToken: testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }
}
