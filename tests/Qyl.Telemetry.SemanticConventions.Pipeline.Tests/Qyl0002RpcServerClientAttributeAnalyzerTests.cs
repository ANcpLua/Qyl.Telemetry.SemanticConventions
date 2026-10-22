using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>
///   QYL0002: a <c>client.*</c> key in a method that writes an RPC context key, the current
///   <c>rpc.system.name</c> included, is reported.
/// </summary>
[TestClass]
public sealed class Qyl0002RpcServerClientAttributeAnalyzerTests(TestContext testContext)
{
    private const string RpcSystemName = "rpc.system.name";
    private const string RpcMethod = "rpc.method";

    [TestMethod]
    [DataRow(RpcSystemName)]
    [DataRow(RpcMethod)]
    public async Task Qyl0002_ClientAddressBesideRpcContextKey_IsReported(string contextKey)
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new RpcServerClientAttributeAnalyzer(), "QYL0002", $$"""
            using System.Diagnostics;
            public static class Instrumentation
            {
                public static void Stamp(Activity activity)
                {
                    activity.SetTag("{{contextKey}}", "grpc");
                    activity.SetTag("client.address", "10.0.0.1");
                }
            }
            """, cancellationToken: testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
    }
}
