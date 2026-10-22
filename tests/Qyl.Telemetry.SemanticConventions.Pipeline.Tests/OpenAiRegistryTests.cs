using System.Diagnostics;
using System.Text.Json;
using ANcpLua.Roslyn.Utilities.Testing.MSBuild;
using Qyl.Telemetry.SemanticConventions.Incubating.Entities;
using Qyl.Telemetry.SemanticConventions.Incubating.Activities;
using Qyl.Telemetry.SemanticConventions.Incubating.Events;
using Qyl.Telemetry.SemanticConventions.Incubating.Metrics;
using Qyl.Telemetry.SemanticConventions.Incubating.Spans;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class OpenAiRegistryTests
{
    private const string CodexExec = nameof(OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexCodexExecInternal);
    private const string DispatchToolCallWithCodeModeResult = nameof(OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexDispatchToolCallWithCodeModeResultInternal);
    private const string StartupPrewarm = nameof(OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexStartupPrewarmInternal);
    private const string Turn = nameof(OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexTurnInternal);
    private const string ThreadIdWireKey = "thread.id";
    private static readonly string[] SignalKinds = ["spans", "events", "metrics"];

    [TestMethod]
    [DataRow("codex.turn.cost_microusd", "counter", null)]
    [DataRow("codex.api_request.duration_ms", "histogram", "ms")]
    [DataRow("exec_server_request_duration_seconds", "histogram", "s")]
    [DataRow("exec_server_connections_active", "gauge", null)]
    public void Native_metric_units_and_instruments_are_not_invented(string name, string instrument, string? unit)
    {
        Assert.IsTrue(RegistryCatalog.TryGetMetric(name, out var definition));
        Assert.IsNotNull(definition);
        Assert.AreEqual(instrument, definition.Instrument);
        Assert.AreEqual(unit, definition.Unit);
    }

    [TestMethod]
    public void The_typed_cost_instrument_keeps_an_empty_unit_when_the_native_sdk_omits_it()
    {
        Assert.AreEqual(string.Empty, CodexIncubatingMetricDefinitions.CodexTurnCostMicrousd.Unit);
    }

    [TestMethod]
    public void A_native_value_namespace_does_not_collide_with_the_value_set_accessor()
    {
        Assert.AreEqual("value", QylAttributeNamespace.ValueMember.Value);
    }

    [TestMethod]
    public void Dotted_and_underscore_exit_code_keys_both_have_constants()
    {
        var type = typeof(Incubating.Attributes.Process.ProcessAttributes);
        var canonical = type.GetField("ExitCode");
        var native = type.GetField("ExitCodeNative");
        Assert.IsNotNull(canonical);
        Assert.IsNotNull(native);
        Assert.AreEqual("process.exit.code", canonical.GetRawConstantValue());
        Assert.AreEqual("process.exit_code", native.GetRawConstantValue());
    }

    [TestMethod]
    public void A_shared_native_span_name_keeps_client_and_server_contracts()
    {
        Assert.AreEqual("client", OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexCodexExecServerRequestClient.SpanKind);
        Assert.AreEqual("server", OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexCodexExecServerRequestServer.SpanKind);
        Assert.AreEqual("internal", OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexCodeModeGrpcExecuteInternal.SpanKind);
    }

    [TestMethod]
    public void Dynamic_exported_span_names_do_not_become_false_fixed_names()
    {
        Assert.IsEmpty(OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexAppServerRequestServer.SpanName);
        Assert.IsEmpty(OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexCodexExecServerRequestServer.SpanName);
        Assert.AreSequenceEqual(["code_mode.grpc.execute"],
            OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexCodeModeGrpcExecuteInternal.SpanName);
        Assert.IsFalse(RegistryCatalog.TryGetAttribute("otel.kind", out _));
        Assert.IsFalse(RegistryCatalog.TryGetAttribute("otel.name", out _));
        Assert.AreEqual(AttributePhase.End,
            OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexCodexExecServerRequestServer.Attributes
                .Single(static a => a.Name == "result").Phase);
    }

    [TestMethod]
    public void Entity_identity_is_separate_from_descriptive_state_and_requirement_levels()
    {
        var session = QylIncubatingEntityDefinitions.QylAgentSession;
        Assert.AreSequenceEqual(["gen_ai.conversation.id", "qyl.agent.product"],
            session.Identity.Select(static a => a.Name).ToArray());
        var operation = QylIncubatingEntityDefinitions.QylAgentOperation;
        Assert.AreSequenceEqual(["qyl.agent.operation.id"], operation.Identity.Select(static a => a.Name).ToArray());
        Assert.IsTrue(operation.Attributes.Any(static a => a.Name == "qyl.agent.operation.state"));
        Assert.IsFalse(operation.Identity.Any(static a => a.Name == "qyl.agent.operation.state"));
        Assert.IsNull(typeof(EntityIdentityAttributeRef).GetProperty("RequirementLevel"));
    }

    [TestMethod]
    public void Operation_identity_is_carried_by_signals_without_requiring_a_resource_per_operation()
    {
        var span = QylIncubatingSpanDefinitions.QylAgentOperationInternal;
        var observation = QylIncubatingEventDefinitions.QylAgentOperationStateChanged;
        Assert.IsTrue(span.Attributes.Any(static a => a.Name == "qyl.agent.operation.id" && a.RequirementLevel == RequirementLevel.Required));
        Assert.IsTrue(observation.Attributes.Any(static a => a.Name == "qyl.agent.operation.id" && a.RequirementLevel == RequirementLevel.Required));
        Assert.IsFalse(span.EntityAssociations.OfType<EntityRef>().Any(static e => e.Type == "qyl.agent.operation"));
        Assert.IsFalse(observation.EntityAssociations.OfType<EntityRef>().Any(static e => e.Type == "qyl.agent.operation"));
        Assert.AreSequenceEqual(["agent {qyl.agent.operation.name}"], span.SpanName);
        Assert.AreEqual(AttributePhase.End, span.Attributes.Single(static a => a.Name == "qyl.agent.operation.state").Phase);
        Assert.AreEqual(AttributePhase.Start, observation.Attributes.Single(static a => a.Name == "qyl.agent.operation.state").Phase);
    }

    [TestMethod]
    public void State_counts_are_additive_without_per_operation_metric_dimensions()
    {
        var status = QylIncubatingMetricDefinitions.QylAgentOperationStatus;
        Assert.AreEqual("updowncounter", status.Instrument);
        Assert.AreEqual("1", status.Unit);
        Assert.IsTrue(status.Attributes.Any(static a => a.Name == "qyl.agent.operation.state"));
        Assert.IsFalse(status.Attributes.Any(static a => a.Name == "qyl.agent.operation.id"));
        Assert.AreEqual("s", QylIncubatingMetricDefinitions.QylAgentOperationDuration.Unit);
    }

    [TestMethod]
    public void Source_payload_capture_remains_optional_and_off_metric_dimensions()
    {
        var payload = QylIncubatingEventDefinitions.QylAgentOperationStateChanged.Attributes
            .Single(static a => a.Name == "qyl.agent.record.payload");
        Assert.AreEqual(RequirementLevel.OptIn, payload.RequirementLevel);
        Assert.AreEqual("any", payload.Type);
        Assert.IsFalse(QylIncubatingMetricDefinitions.QylAgentOperationDuration.Attributes
            .Any(static a => a.Name == "qyl.agent.record.payload"));
    }

    [TestMethod]
    [DataRow(CodexExec, AttributePhase.End)]
    [DataRow(DispatchToolCallWithCodeModeResult, AttributePhase.Start)]
    [DataRow(StartupPrewarm, AttributePhase.Start)]
    [DataRow(Turn, AttributePhase.Start)]
    public void CodexSpanContract_ThreadIdReference_IsConversationThreadString(string definition, AttributePhase phase)
    {
        var span = (SpanDefinition<Internal>)typeof(OpenaiCodexIncubatingSpanDefinitions).GetField(definition)!.GetValue(null)!;

        var threadId = Assert.ContainsSingle(span.Attributes.Where(static a => a.Name == ThreadIdWireKey));

        Assert.AreEqual("string", threadId.Type);
        Assert.AreEqual(RequirementLevel.Recommended, threadId.RequirementLevel);
        Assert.AreEqual(phase, threadId.Phase);
    }

    [TestMethod]
    public void CodexRegistry_ThreadIdWireKey_IsReferencedByTheFourConversationSpans()
    {
        var signals = CodexThreadIdReferences().Select(static reference => reference.Signal).ToArray();

        Assert.AreSequenceEqual(
            ["openai_codex.codex.exec.internal", "openai_codex.dispatch_tool_call_with_code_mode_result.internal", "openai_codex.startup_prewarm.internal", "openai_codex.turn.internal"],
            signals,
            SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    [DataRow("openai_codex.codex.exec.internal")]
    [DataRow("openai_codex.dispatch_tool_call_with_code_mode_result.internal")]
    [DataRow("openai_codex.startup_prewarm.internal")]
    [DataRow("openai_codex.turn.internal")]
    public void CodexRegistry_ThreadIdReference_IsTheConversationThreadString(string span)
    {
        var reference = Assert.ContainsSingle(CodexThreadIdReferences().Where(candidate => candidate.Signal == span));

        Assert.AreEqual(
            new ThreadIdReference(span, "openai_codex.thread.id", "string", "recommended", false, "development", "ID of the Codex conversation thread (UUID)"),
            reference);
    }

    [TestMethod]
    public void CoreThreadId_RegistryDefinition_StaysIntegerWithLongSetter()
    {
        Assert.IsTrue(RegistryCatalog.TryGetAttribute(ThreadIdWireKey, out var attribute));

        var setter = typeof(ThreadIncubatingActivityExtensions).GetMethod(nameof(ThreadIncubatingActivityExtensions.SetThreadId));

        Assert.AreEqual(RegistryAttributeValueKind.Integer, attribute.ValueKind);
        Assert.IsNotNull(setter);
        Assert.AreSequenceEqual([typeof(Activity), typeof(long)], setter.GetParameters().Select(static p => p.ParameterType).ToArray());
    }

    private sealed record ThreadIdReference(string Signal, string Key, string Type, string RequirementLevel, bool SamplingRelevant, string Stability, string Brief);

    private static ThreadIdReference[] CodexThreadIdReferences()
    {
        var root = RepositoryRoot.LocateFrom(AppContext.BaseDirectory, "Qyl.Telemetry.SemanticConventions.slnx");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".build", "registry.json")));
        var refinements = document.RootElement.GetProperty("refinements");
        return [.. SignalKinds
            .SelectMany(kind => refinements.GetProperty(kind).EnumerateArray())
            .Where(static signal => signal.GetProperty("provenance").GetProperty("path").GetString() == "registry/vendor/openai-codex.yaml")
            .SelectMany(static signal => signal.TryGetProperty("attributes", out var attributes)
                ? attributes.EnumerateArray().Where(static attribute => WireKey(attribute) == ThreadIdWireKey).Select(attribute => new ThreadIdReference(
                    signal.GetProperty("id").GetString()!,
                    attribute.GetProperty("key").GetString()!,
                    attribute.GetProperty("type").ToString(),
                    attribute.GetProperty("requirement_level").ToString(),
                    attribute.TryGetProperty("sampling_relevant", out var samplingRelevant) && samplingRelevant.GetBoolean(),
                    attribute.GetProperty("stability").GetString()!,
                    attribute.GetProperty("brief").GetString()!))
                : [])];
    }

    private static string? WireKey(JsonElement attribute) =>
        attribute.TryGetProperty("annotations", out var annotations)
        && annotations.TryGetProperty("qyl", out var qyl)
        && qyl.TryGetProperty("openai", out var openai)
        && openai.TryGetProperty("literal_name", out var literalName)
            ? literalName.GetString()
            : attribute.GetProperty("key").GetString();
}
