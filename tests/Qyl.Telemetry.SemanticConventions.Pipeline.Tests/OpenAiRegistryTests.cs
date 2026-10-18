using Qyl.Telemetry.SemanticConventions.Incubating.Entities;
using Qyl.Telemetry.SemanticConventions.Incubating.Activities;
using Qyl.Telemetry.SemanticConventions.Incubating.Events;
using Qyl.Telemetry.SemanticConventions.Incubating.Metrics;
using Qyl.Telemetry.SemanticConventions.Incubating.Spans;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class OpenAiRegistryTests
{
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
    public void Span_and_event_associations_preserve_the_entity_type()
    {
        var span = QylIncubatingSpanDefinitions.QylAgentOperationInternal;
        var observation = QylIncubatingEventDefinitions.QylAgentOperationStateChanged;
        Assert.AreSequenceEqual(["qyl.agent.operation"], span.Entities.Select(static e => e.Type).ToArray());
        Assert.AreSequenceEqual(["agent {qyl.agent.operation.name}"], span.SpanName);
        Assert.AreSequenceEqual(["qyl.agent.operation"], observation.Entities.Select(static e => e.Type).ToArray());
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
}
