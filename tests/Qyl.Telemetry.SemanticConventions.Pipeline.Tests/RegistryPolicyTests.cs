using System.Reflection;
using System.Text.RegularExpressions;
using ANcpLua.Roslyn.Utilities.Testing.MSBuild;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>
/// The collector's deny lists moved out of qyl's eng/config/collector-semantic-policy.json into the
/// registry (registry/qyl/policy.yaml); the generated <see cref = "RegistryPolicy"/> is the one table
/// the views and the collector catalog read. The expected lists are that policy file's
/// unrelated exact, prefix and token denials stay intact when the two exception fields
/// become eligible for persistence only after Collector value sanitization.
/// </summary>
[TestClass]
public sealed class RegistryPolicyTests
{
    private static readonly string[] CollectorDeniedExactKeys = ["code.file.path", "code.stacktrace", "db.query.text", "executor.input", "executor.output", "gen_ai.agent.description", "gen_ai.agent.id", "gen_ai.conversation.id", "gen_ai.data_source.id", "gen_ai.evaluation.explanation", "gen_ai.evaluation.name", "gen_ai.input.messages", "gen_ai.output.messages", "gen_ai.prompt.name", "gen_ai.response.id", "gen_ai.retrieval.documents", "gen_ai.retrieval.query.text", "gen_ai.system_instructions", "gen_ai.tool.call.arguments", "gen_ai.tool.call.id", "gen_ai.tool.call.result", "gen_ai.tool.definitions", "gen_ai.tool.description", "gen_ai.workflow.name", "host.id", "host.ip", "host.mac", "host.name", "mcp.resource.uri", "mcp.session.id", "message.content", "service.instance.id", "session.id", "session.previous_id", "url.full", "url.query", ];
    private static readonly string[] CollectorDeniedKeyTokens = ["access_token", "api-key", "api_key", "apikey", "authorization", "body", "completion", "cookie", "credential", "definition", "document", "fingerprint", "id_token", "instruction", "jwt", "message", "password", "private_key", "prompt", "query", "refresh_token", "result", "secret", "set-cookie", "token", ];
    private static readonly string[] CollectorTokenExemptKeys = ["db.query.summary", "gen_ai.token.type", "messaging.kafka.message.offset", "messaging.message.body.size", "messaging.message.id", ];
    [TestMethod]
    public void Every_unrelated_exact_and_prefix_denial_is_preserved()
    {
        // The collector's list plus the vendor keys whose content the registry denies elsewhere (10.0.1).
        foreach (var key in CollectorDeniedExactKeys.Concat(["build.error.message", "db.odp.user.statement", "nservicebus.originating_machine", "nservicebus.processing_machine"]))
            Assert.Contains(key, RegistryPolicy.NotPersistedKeys);
        Assert.AreSequenceEqual(RegistryPolicy.NotPersistedKeys.Order(), RegistryPolicy.NotPersistedKeys);
        Assert.Contains("enduser.", RegistryPolicy.NotPersistedPrefixes);
        Assert.Contains("user.", RegistryPolicy.NotPersistedPrefixes);
    }

    [TestMethod]
    public void Existing_token_denials_and_exemptions_are_preserved()
    {
        foreach (var token in CollectorDeniedKeyTokens)
            Assert.Contains(token, RegistryPolicy.DeniedKeyTokens);
        foreach (var key in CollectorTokenExemptKeys)
            Assert.Contains(key, RegistryPolicy.TokenExemptKeys);
        Assert.Contains("messaging.masstransit.message_id", RegistryPolicy.VendorKeys);
        Assert.Contains("nservicebus.message_intent", RegistryPolicy.VendorKeys);
    }

    [TestMethod]
    public void The_generated_table_equals_the_registry_annotations()
    {
        var policy = File.ReadAllText(RepoPath("registry", "qyl", "policy.yaml"));
        Assert.AreSequenceEqual(RegistryPolicy.NotPersistedKeys.Order(), Annotated(policy, "persisted: false").Order());
        Assert.AreSequenceEqual(RegistryPolicy.TokenExemptKeys.Order(), Annotated(policy, "token_exempt: true").Order());
        Assert.AreSequenceEqual(RegistryPolicy.ExceptionTextKeys.Order(), Annotated(policy, "sanitize: exception_text").Order());
    }

    [TestMethod]
    [DataRow("authorization", false)]
    [DataRow("gen_ai.agent.name", true)]
    [DataRow("db.query.summary", true)]
    [DataRow("messaging.message.id", true)]
    [DataRow("gen_ai.usage.input_tokens", true)]
    [DataRow("messaging.masstransit.message_id", true)]
    [DataRow("qyl.app.order.id", true)]
    [DataRow("exception.message", true)]
    [DataRow("exception.stacktrace", true)]
    [DataRow("exception.message.detail", false)]
    [DataRow("diagnostic.message", false)]
    [DataRow("custom.exception.message", false)]
    [DataRow("exception.stacktrace.secret", false)]
    [DataRow("exception.stacktrace.detail", false)]
    [DataRow("Exception.StackTrace", false)]
    [DataRow("EXCEPTION.STACKTRACE.detail", false)]
    [DataRow("http.request.header.exception.message", true)]
    [DataRow("user.exception.stacktrace", false)]
    [DataRow("Exception.Message", false)]
    [DataRow("http.request.header.authorization", true)]
    [DataRow("user.email", false)]
    [DataRow("enduser.id", false)]
    [DataRow("qyl.app.order.message", false)]
    [DataRow("gen_ai.request.max_tokens", true)]
    [DataRow("gen_ai.usage.text.input_tokens", true)]
    [DataRow("qyl.app.user.email", true)]
    [DataRow("qyl.app.url.full", true)]
    [DataRow("qyl.app.enduser.id", true)]
    [DataRow("db.odp.user.statement", false)]
    [DataRow("nservicebus.processing_machine", false)]
    [DataRow("qyl.app.API_KEY", false)]
    public void IsPersisted_applies_the_collectors_rule(string key, bool persisted)
    {
        Assert.AreEqual(persisted, RegistryPolicy.IsPersisted(key));
    }

    [TestMethod]
    public void Every_generated_attribute_reference_agrees_with_the_table()
    {
        var references = new[]
        {
            typeof(SpanDefinition<>).Assembly,
            typeof(Incubating.Spans.GenAiIncubatingSpanDefinitions).Assembly
        }.SelectMany(static assembly => assembly.GetTypes()).Where(static type => type.Namespace?.EndsWith(".Spans", StringComparison.Ordinal) == true).SelectMany(static type => type.GetFields(BindingFlags.Public | BindingFlags.Static)).Select(static field => field.GetValue(null)).SelectMany(static definition => (IReadOnlyList<AttributeRef>)definition!.GetType().GetProperty("Attributes")!.GetValue(definition)!).ToList();
        Assert.IsNotEmpty(references);
        Assert.IsTrue(references.All(static reference => reference.IsPersisted == RegistryPolicy.IsPersisted(reference.Name)));
        Assert.IsTrue(references.Any(static reference => !reference.IsPersisted));
    }

    [TestMethod]
    public void Only_the_two_exact_exception_keys_require_value_sanitization()
    {
        Assert.AreSequenceEqual(["exception.message", "exception.stacktrace"], RegistryPolicy.ExceptionTextKeys);
        Assert.IsTrue(RegistryPolicy.ContainsDeniedToken("exception.message"), "The general message-token denial must remain.");
        Assert.Contains("exception.message", RegistryPolicy.TokenExemptKeys);
        Assert.DoesNotContain("exception.stacktrace", RegistryPolicy.TokenExemptKeys);
        Assert.IsTrue(RegistryPolicy.ExceptionTextKeys.All(RegistryPolicy.IsPersisted));
    }

    private static IEnumerable<string> Annotated(string policy, string annotation) =>
        Regex.Matches(policy, @"- ref: (?<key>\S+)\s+annotations: \{qyl: \{(?<annotations>[^}]+)\}\}")
            .Where(match => Regex.IsMatch(match.Groups["annotations"].Value,
                @"(?:^|,)\s*" + Regex.Escape(annotation) + @"\s*(?:,|$)"))
            .Select(static match => match.Groups["key"].Value);
    private static string RepoPath(params string[] parts)
    {
        string root = RepositoryRoot.LocateFrom(AppContext.BaseDirectory, "Qyl.Telemetry.SemanticConventions.slnx");
        return Path.Combine([root, ..parts]);
    }
}
