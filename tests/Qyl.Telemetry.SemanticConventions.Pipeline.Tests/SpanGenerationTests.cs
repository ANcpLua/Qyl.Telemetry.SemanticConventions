using System.Diagnostics;
using System.Reflection;
using Qyl.Telemetry.SemanticConventions.Incubating.Spans;
using Qyl.Telemetry.SemanticConventions.Spans;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>
/// Span rows are keyed by refinement id and grouped by type. Before, the spans filter deduplicated
/// by type, so the three gen_ai skill spans, which share <c>gen_ai.execute_tool.internal</c> with
/// the generic execute_tool span, collapsed into one row and lost their span-name rules and
/// <c>gen_ai.skill.*</c> requirement levels.
/// </summary>
[ResourceLock(ApplicationSourceListeners.Name)]
[TestClass]
public sealed class SpanGenerationTests
{
    private const string ExecuteTool = "gen_ai.execute_tool.internal";
    [TestMethod]
    public void The_three_skill_spans_are_rows_of_their_own_that_specialize_execute_tool()
    {
        var skills = new[]
        {
            GenAiIncubatingSpanDefinitions.GenAiExecuteToolLoadSkillInternal,
            GenAiIncubatingSpanDefinitions.GenAiExecuteToolReadSkillResourceInternal,
            GenAiIncubatingSpanDefinitions.GenAiExecuteToolCommandInternal,
        };
        Assert.AreSequenceEqual(["gen_ai.execute_tool.load_skill.internal", "gen_ai.execute_tool.read_skill_resource.internal", "gen_ai.execute_tool.command.internal"], skills.Select(static s => s.Id));
        Assert.IsTrue(skills.All(static s => s is { Type: ExecuteTool, IsSpecialization: true, SpanKind: "internal" }));
        Assert.IsTrue(skills.All(static s => s.Attributes.Any(a => a is { Name: "gen_ai.skill.name", RequirementLevel: RequirementLevel.ConditionallyRequired })));
        var generic = GenAiIncubatingSpanDefinitions.GenAiExecuteToolInternal;
        Assert.AreEqual(ExecuteTool, generic.Id);
        Assert.IsFalse(generic.IsSpecialization);
        Assert.IsFalse(generic.Attributes.Any(static a => a.Name.StartsWith("gen_ai.skill.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Each_skill_span_carries_its_own_span_name_rule()
    {
        Assert.AreSequenceEqual(["execute_tool {gen_ai.tool.name}"], GenAiIncubatingSpanDefinitions.GenAiExecuteToolInternal.SpanName);
        Assert.AreSequenceEqual(["execute_tool {gen_ai.tool.name} {gen_ai.skill.name}", "execute_tool {gen_ai.tool.name}"], GenAiIncubatingSpanDefinitions.GenAiExecuteToolLoadSkillInternal.SpanName);
        Assert.AreSequenceEqual(["execute_tool {gen_ai.tool.name} {gen_ai.skill.name} {gen_ai.skill.resource.name}", "execute_tool {gen_ai.tool.name} {gen_ai.skill.resource.name}", "execute_tool {gen_ai.tool.name}"], GenAiIncubatingSpanDefinitions.GenAiExecuteToolReadSkillResourceInternal.SpanName);
        Assert.AreSequenceEqual(["execute_tool {gen_ai.tool.name} {gen_ai.skill.name} {gen_ai.skill.resource.name}", "execute_tool {gen_ai.tool.name} {gen_ai.skill.resource.name}", "execute_tool {gen_ai.tool.name} {process.executable.name}", "execute_tool {gen_ai.tool.name}"], GenAiIncubatingSpanDefinitions.GenAiExecuteToolCommandInternal.SpanName);
    }

    [TestMethod]
    public void A_specialization_without_a_rule_of_its_own_uses_its_types()
    {
        Assert.AreEqual("db.sql.client", DbSpanDefinitions.DbPostgresqlClient.Type);
        Assert.AreSequenceEqual(DbSpanDefinitions.DbSqlClient.SpanName, DbSpanDefinitions.DbPostgresqlClient.SpanName);
        Assert.AreEqual("{db.query.summary}", DbSpanDefinitions.DbClient.SpanName[0]);
        Assert.AreEqual("{db.system.name}", DbSpanDefinitions.DbClient.SpanName[DbSpanDefinitions.DbClient.SpanName.Count - 1]);
        Assert.AreSequenceEqual(MessagingIncubatingSpanDefinitions.MessagingProcessConsumer.SpanName, MessagingIncubatingSpanDefinitions.MessagingKafkaProcessConsumer.SpanName);
    }

    [TestMethod]
    public void The_genai_provider_refinements_stay_excluded()
    {
        var fields = typeof(GenAiIncubatingSpanDefinitions).GetFields(BindingFlags.Public | BindingFlags.Static).Select(static f => f.Name).ToList();
        Assert.IsFalse(fields.Any(static name => name.StartsWith("Openai", StringComparison.Ordinal) || name.StartsWith("Anthropic", StringComparison.Ordinal) || name.StartsWith("AzureAi", StringComparison.Ordinal) || name.StartsWith("AwsBedrock", StringComparison.Ordinal)));
        Assert.Contains("GenAiExecuteToolInternal", fields);
        Assert.Contains("GenAiInvokeAgentInternal", fields);
    }

    [TestMethod]
    public void The_skill_spans_have_starters_that_specialize_the_execute_tool_starter()
    {
        using var listener = new ApplicationSourceListener();
        using var generic = IncubatingSpans.GenAiExecuteTool("lookup_stock", agentName: null, conversationId: null, toolCallId: "provider-call-123");
        using var load = IncubatingSpans.GenAiExecuteToolLoadSkill("load_skill", agentName: null, conversationId: null, skillName: "pdf", toolCallId: null);
        using var loadUnnamed = IncubatingSpans.GenAiExecuteToolLoadSkill("load_skill", agentName: null, conversationId: null, skillName: null, toolCallId: null);
        using var read = IncubatingSpans.GenAiExecuteToolReadSkillResource("read_skill_resource", agentName: null, conversationId: null, skillName: "pdf", skillResourceName: "forms.md", toolCallId: null);
        using var readResourceOnly = IncubatingSpans.GenAiExecuteToolReadSkillResource("read_skill_resource", agentName: null, conversationId: null, skillName: "pdf", skillResourceName: null, toolCallId: null);
        using var command = IncubatingSpans.GenAiExecuteToolCommand("bash", agentName: null, conversationId: null, processExecutableName: "git", skillName: null, skillResourceName: null, toolCallId: null);
        Assert.AreSequenceEqual(["execute_tool lookup_stock", "execute_tool load_skill pdf", "execute_tool load_skill", "execute_tool read_skill_resource pdf forms.md", "execute_tool read_skill_resource", "execute_tool bash git"], new[] { generic, load, loadUnnamed, read, readResourceOnly, command }.Select(static s => s!.DisplayName));
        foreach (var activity in new[] { generic, load, loadUnnamed, read, readResourceOnly, command })
        {
            Assert.IsNotNull(activity);
            Assert.AreNotEqual(default(ActivityTraceId), activity.TraceId);
            Assert.AreNotEqual(default(ActivitySpanId), activity.SpanId);
            Assert.AreEqual(ActivityKind.Internal, activity.Kind);
            Assert.AreEqual(ActivityStatusCode.Unset, activity.Status);
            Assert.AreEqual("Qyl.Telemetry.Application", activity.Source.Name);
            Assert.AreEqual(SchemaUrl.QylVersion, activity.Source.Version);
            Assert.AreEqual(SchemaUrl.Qyl, activity.Source.TelemetrySchemaUrl);
            Assert.AreEqual("execute_tool", activity.GetTagItem("gen_ai.operation.name"));
            Assert.AreEqual("gen_ai.execute_tool", activity.GetTagItem("qyl.instrumentation.domain"));
        }
        Assert.AreEqual("provider-call-123", generic!.GetTagItem("gen_ai.tool.call.id"));
        foreach (var activity in new[] { load, loadUnnamed, read, readResourceOnly, command })
        {
            Assert.IsFalse(activity!.TagObjects.Any(static tag => tag.Key == "gen_ai.tool.call.id"));
        }
        Assert.AreEqual("pdf", load!.GetTagItem("gen_ai.skill.name"));
        Assert.AreEqual("forms.md", read!.GetTagItem("gen_ai.skill.resource.name"));
        Assert.AreEqual("git", command!.GetTagItem("process.executable.name"));
    }
}
