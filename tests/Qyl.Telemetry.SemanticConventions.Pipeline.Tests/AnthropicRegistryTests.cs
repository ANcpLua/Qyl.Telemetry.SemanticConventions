using System.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Incubating.Activities;
using Qyl.Telemetry.SemanticConventions.Incubating.Mapping;
using Qyl.Telemetry.SemanticConventions.Incubating.Spans;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class AnthropicRegistryTests
{
    [TestMethod]
    [DataRow("prompt.id", RegistryAttributeValueKind.String)]
    [DataRow("prompt_text", RegistryAttributeValueKind.String)]
    [DataRow("workspace.host_paths", RegistryAttributeValueKind.StringArray)]
    [DataRow("anthropic.toolset.name", RegistryAttributeValueKind.String)]
    [DataRow("sampling_trace_id", RegistryAttributeValueKind.String)]
    [DataRow("start_type", RegistryAttributeValueKind.String)]
    [DataRow("wsl.version", RegistryAttributeValueKind.String)]
    [DataRow("success", RegistryAttributeValueKind.Any)]
    public void Native_export_fields_are_recognized_without_rewriting_their_names(
        string key, RegistryAttributeValueKind expectedKind)
    {
        Assert.IsTrue(RegistryCatalog.TryGetAttribute(key, out var definition));
        Assert.IsNotNull(definition);
        Assert.AreEqual(key, definition.Name);
        Assert.AreEqual(expectedKind, definition.ValueKind);
        Assert.IsTrue(AttributeMapping.IsVendorPassThrough(key));
        Assert.IsFalse(AttributeMapping.TryGetRename(key, out _));
    }

    [TestMethod]
    public void Host_attribute_templates_accept_only_the_declared_prefix_with_a_suffix()
    {
        Assert.IsTrue(RegistryCatalog.TryGetAttribute("sdk_host.connector", out var definition));
        Assert.IsNotNull(definition);
        Assert.IsTrue(definition.IsTemplate);
        Assert.AreEqual("sdk_host", definition.Name);
        Assert.AreEqual(RegistryAttributeValueKind.String, definition.ValueKind);
        Assert.IsFalse(RegistryCatalog.TryGetAttribute("sdk_host", out _));
        Assert.IsFalse(RegistryCatalog.TryGetAttribute("sdk_host.", out _));
        Assert.IsFalse(RegistryCatalog.TryGetAttribute("sdk_hostile.connector", out _));
        Assert.IsFalse(RegistryCatalog.TryGetAttribute("anthropic.undocumented_field", out _));
    }

    [TestMethod]
    public void The_still_emitted_legacy_provider_key_is_preserved_as_vendor_data()
    {
        Assert.IsTrue(RegistryCatalog.TryGetAttribute("gen_ai.system", out var definition));
        Assert.IsNotNull(definition);
        Assert.AreEqual(RegistryAttributeValueKind.String, definition.ValueKind);
        Assert.IsTrue(AttributeMapping.IsVendorPassThrough("gen_ai.system"));
        Assert.IsFalse(AttributeMapping.TryGetRename("gen_ai.system", out _));
        Assert.IsFalse(AttributeMapping.IsObsoleted("gen_ai.system"));
    }

    [TestMethod]
    [DataRow("claude_code.cost.usage", "USD")]
    [DataRow("claude_code.token.usage", "tokens")]
    [DataRow("claude_code.active_time.total", "s")]
    public void Native_counters_keep_their_instrument_and_units(string name, string unit)
    {
        Assert.IsTrue(RegistryCatalog.TryGetMetric(name, out var definition));
        Assert.IsNotNull(definition);
        Assert.AreEqual("counter", definition.Instrument);
        Assert.AreEqual(unit, definition.Unit);
        Assert.IsFalse(RegistryCatalog.TryGetMetric("claude_code.unpublished_metric", out _));
    }

    [TestMethod]
    public void Browser_span_names_keep_the_native_action_suffix_fallback()
    {
        Assert.AreSequenceEqual(
            ["tool_execution_{tool_name}_{action}", "tool_execution_{tool_name}"],
            AnthropicIncubatingSpanDefinitions.AnthropicChromeToolExecutionInternal.SpanName);
        Assert.AreSequenceEqual(
            ["message_streaming"],
            AnthropicIncubatingSpanDefinitions.AnthropicChromeMessageStreamingInternal.SpanName);
    }

    [TestMethod]
    public void Api_and_sdk_spans_share_a_wire_name_but_keep_their_distinct_kinds()
    {
        var client = AnthropicIncubatingSpanDefinitions.AnthropicMessagesCreateClient;
        var server = AnthropicIncubatingSpanDefinitions.AnthropicMessagesCreateServer;
        Assert.AreEqual("client", client.SpanKind);
        Assert.AreEqual("server", server.SpanKind);
        Assert.AreSequenceEqual(["anthropic.messages.create"], client.SpanName);
        Assert.AreSequenceEqual(client.SpanName, server.SpanName);
    }

    [TestMethod]
    public void Count_tokens_remains_sdk_only_and_retains_its_non_usage_count()
    {
        var client = AnthropicIncubatingSpanDefinitions.AnthropicMessagesCountTokensClient;
        Assert.AreEqual("client", client.SpanKind);
        Assert.IsTrue(client.Attributes.Any(static a => a.Name == "anthropic.message.count_tokens.input_tokens"));
        Assert.IsFalse(client.Attributes.Any(static a => a.Name == "gen_ai.usage.input_tokens"));
        Assert.IsNull(typeof(AnthropicIncubatingSpanDefinitions).GetField("AnthropicMessagesCountTokensServer"));
    }

    [TestMethod]
    public void Messages_sdk_spans_keep_request_configuration_and_retry_attribution()
    {
        var attributes = AnthropicIncubatingSpanDefinitions.AnthropicMessagesCreateClient.Attributes;
        Assert.IsTrue(attributes.Any(static a => a.Name == "gen_ai.request.model"));
        Assert.IsTrue(attributes.Any(static a => a.Name == "gen_ai.request.max_tokens"));
        Assert.IsTrue(attributes.Any(static a => a.Name == "http.request.resend_count"));
        Assert.IsTrue(attributes.Any(static a => a.Name == "anthropic.content.truncated"));
    }

    [TestMethod]
    public void Native_aliases_do_not_make_the_existing_session_setter_ambiguous()
    {
        Assert.IsTrue(RegistryCatalog.TryGetAttribute("sessionId", out _));
        Assert.IsTrue(RegistryCatalog.TryGetAttribute("session_id", out _));
        using var activity = new Activity("anthropic-registry-test");
        activity.SetSessionId("session-123");
        Assert.AreEqual("session-123", activity.GetTagItem("session.id"));
        Assert.IsNull(activity.GetTagItem("sessionId"));
        Assert.IsNull(activity.GetTagItem("session_id"));
    }

    [TestMethod]
    [DataRow("click", null, "tool_execution_click")]
    [DataRow("click", "left", "tool_execution_click_left")]
    [DataRow("click", 2.5, "tool_execution_click_2.5")]
    public void Mixed_wire_values_can_name_browser_spans(object toolName, object? action, string expected)
    {
        Assert.AreEqual(expected, Qyl.SpanNames.AnthropicChromeToolExecution(toolName, action));
    }
}
