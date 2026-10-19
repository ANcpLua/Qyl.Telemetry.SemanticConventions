using Qyl.Telemetry.SemanticConventions.Incubating.Views;
using Qyl.Telemetry.SemanticConventions.Views;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class TypedProjectionTests
{
    [TestMethod]
    [DataRow(42)]
    [DataRow(true)]
    [DataRow(null)]
    public void Required_strings_reject_wrong_wire_types(object? value)
    {
        var record = Tool(value);
        Assert.IsFalse(GenAiExecuteToolSpan.Matches(record));
        Assert.IsFalse(GenAiExecuteToolSpan.TryCreate(record, out var view));
        Assert.IsNull(view);
        Assert.ThrowsExactly<ArgumentException>(() => new GenAiExecuteToolSpan(record));
        Assert.IsEmpty(new[] { record }.GenAiExecuteTool());
    }

    [TestMethod]
    public void Required_strings_are_returned_without_coercion()
    {
        var record = Tool("lookup_stock");
        Assert.IsTrue(GenAiExecuteToolSpan.TryCreate(record, out var projection));
        var view = Assert.IsInstanceOfType<GenAiExecuteToolSpan>(projection);
        Assert.AreEqual("lookup_stock", view.ToolName);
        Assert.AreSame(record, view.Record);
    }

    [TestMethod]
    public void Required_enum_strings_do_not_become_an_empty_custom_value()
    {
        var record = new SpanRecord("trace", "span", "query")
        {
            Kind = SpanKind.Client,
            Attributes = new Dictionary<string, object?> { ["db.system.name"] = 42 }
        };
        Assert.IsFalse(DbClientSpan.Matches(record));
        Assert.IsFalse(DbClientSpan.TryCreate(record, out var view));
        Assert.IsNull(view);
        Assert.ThrowsExactly<ArgumentException>(() => new DbClientSpan(record));
    }

    [TestMethod]
    public void Mutation_after_projection_cannot_fabricate_a_required_string()
    {
        var attributes = new Dictionary<string, object?>
        {
            ["gen_ai.operation.name"] = "execute_tool", ["gen_ai.tool.name"] = "lookup_stock"
        };
        var record = new SpanRecord("trace", "span", "tool") { Kind = SpanKind.Internal, Attributes = attributes };
        var view = new GenAiExecuteToolSpan(record);
        attributes["gen_ai.tool.name"] = 42;
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = view.ToolName);
    }

    private static SpanRecord Tool(object? value) => new("trace", "span", "tool")
    {
        Kind = SpanKind.Internal,
        Attributes = new Dictionary<string, object?>
        {
            ["gen_ai.operation.name"] = "execute_tool", ["gen_ai.tool.name"] = value
        }
    };
}
