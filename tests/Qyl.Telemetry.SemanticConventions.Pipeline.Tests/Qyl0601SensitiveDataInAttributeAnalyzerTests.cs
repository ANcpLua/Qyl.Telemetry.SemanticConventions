// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using ANcpLua.Roslyn.Utilities.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>
///   QYL0601 fires on a sensitive-looking key only where the key provably reaches telemetry,
///   matches sensitive words on segment boundaries, and exempts keys the pinned registry
///   defines outright.
/// </summary>
[TestClass]
public sealed class Qyl0601SensitiveDataInAttributeAnalyzerTests(TestContext testContext)
{

    [TestMethod]
    [DataRow("activity.SetTag(\"user.password\", secret);", "user.password")]
    [DataRow("activity.SetBaggage(\"api_key\", secret);", "api_key")]
    [DataRow("activity.SetTag(\"x-auth-token\", secret);", "x-auth-token")]
    [DataRow("activity.SetTag(\"apiKey\", secret);", "apiKey")]
    [DataRow("counter.Add(1, new KeyValuePair<string, object?>(\"credit_card\", secret));", "credit_card")]
    [DataRow("var tags = new TagList(); tags.Add(\"connection_string\", secret);", "connection_string")]
    public async Task Fires_where_a_sensitive_key_reaches_telemetry(string statement, string expectedKey)
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture(statement))).WithAnalyzers([new Qyl0601SensitiveDataInAttributeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0601", diagnostics[0].Id);
        Assert.Contains(expectedKey, diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task A_dictionary_reaches_telemetry_through_the_local_it_is_built_in()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("""
                var attributes = new Dictionary<string, object?>();
                attributes["password"] = secret;
                source.StartActivity("operation", ActivityKind.Internal, default(ActivityContext), tags: attributes);
                """))).WithAnalyzers([new Qyl0601SensitiveDataInAttributeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0601", diagnostics[0].Id);
        Assert.Contains("password", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task A_dictionary_that_never_reaches_telemetry_is_silent_whatever_it_is_called()
    {
        // The previous detection fired here because the receiver was named "attributes".
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("""
                var attributes = new Dictionary<string, object?>();
                attributes["password"] = secret;
                """))).WithAnalyzers([new Qyl0601SensitiveDataInAttributeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    [DataRow("activity.SetTag(\"gen_ai.usage.input_tokens\", 42);")]
    [DataRow("activity.SetTag(\"app.pipeline.stage\", \"build\");")]
    [DataRow("activity.SetTag(\"app.author.name\", \"x\");")]
    public async Task Sensitive_words_match_whole_segments_not_substrings(string statement)
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture(statement))).WithAnalyzers([new Qyl0601SensitiveDataInAttributeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    [DataRow("activity.SetTag(\"aspnetcore.authorization.policy\", \"admin\");")]
    [DataRow("activity.SetTag(\"aws.secretsmanager.secret.arn\", \"arn:aws:secretsmanager:eu-west-1:123456789012:secret:x\");")]
    public async Task A_key_the_registry_defines_outright_is_exempt(string statement)
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture(statement))).WithAnalyzers([new Qyl0601SensitiveDataInAttributeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task A_key_that_only_extends_a_registry_template_is_not_exempt()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("activity.SetTag(\"http.request.header.authorization\", secret);"))).WithAnalyzers([new Qyl0601SensitiveDataInAttributeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0601", diagnostics[0].Id);
        Assert.Contains("http.request.header.authorization", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task Qyl0601_SensitiveKeyOnGaugeRecord_IsReported()
    {
        var diagnostics = await CompileTelemetry(Compile.Source(Fixture("meter.CreateGauge<int>(\"queue.depth\").Record(1, new KeyValuePair<string, object?>(\"password\", secret));"))).WithAnalyzers([new Qyl0601SensitiveDataInAttributeAnalyzer()]).GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        var diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("QYL0601", diagnostic.Id);
        Assert.Contains("password", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation CompileTelemetry(Compile source) =>
        AnalyzerRun.CompileSource(source.WithReference<System.Diagnostics.Activity>().WithReference<System.Diagnostics.Metrics.Meter>());

    private static string Fixture(string statement) => SourceFixture.InMethod(statement, declarations: """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Diagnostics.Metrics;
        """, members: """
        private static readonly ActivitySource source = new("test.telemetry");
        private static readonly Meter meter = new("test.telemetry");
        private static readonly Counter<int> counter = meter.CreateCounter<int>("requests");
        private readonly Activity activity = new("operation");
        """, parameters: "string secret");
}
