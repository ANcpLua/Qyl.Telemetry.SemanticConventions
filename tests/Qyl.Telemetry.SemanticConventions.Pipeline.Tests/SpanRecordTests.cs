using Qyl.Telemetry.SemanticConventions.Incubating.Spans;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class SpanRecordTests
{
    private const string ThreadIdWireKey = "thread.id";
    private const string CodexConversationThread = "0199a213-81c0-7800-8aa1-bbab2a035a53";

    [TestMethod]
    [DataRow((byte)7, 7L)]
    [DataRow((short)-12, -12L)]
    [DataRow(42, 42L)]
    [DataRow(long.MaxValue, long.MaxValue)]
    public void Integer_attributes_preserve_their_value(object value, long expected)
    {
        var span = Record(value);
        Assert.AreEqual(expected, span.GetInt64("value"));
        Assert.IsNull(span.GetInt64("missing"));
    }

    [TestMethod]
    [DataRow(1.25, 1.25)]
    [DataRow(1.5f, 1.5)]
    [DataRow(42, 42.0)]
    [DataRow(9007199254740992L, 9007199254740992.0)]
    public void Numeric_attributes_project_to_double(object value, double expected)
    {
        var span = Record(value);
        Assert.AreEqual(expected, span.GetDouble("value"));
        Assert.IsNull(span.GetDouble("missing"));
    }

    [TestMethod]
    [DataRow((byte)7, 7.0)]
    [DataRow((short)-12, -12.0)]
    public void GetDouble_IntegerAcceptedByGetInt64_ProjectsToDouble(object value, double expected)
    {
        var span = Record(value);
        Assert.IsNotNull(span.GetInt64("value"));
        Assert.AreEqual(expected, span.GetDouble("value"));
    }

    [TestMethod]
    public void Typed_getters_reject_strings_that_only_look_like_numbers_or_booleans()
    {
        var span = Record("42");
        Assert.AreEqual("42", span.GetString("value"));
        Assert.IsNull(span.GetInt64("value"));
        Assert.IsNull(span.GetDouble("value"));
        Assert.IsNull(span.GetBoolean("value"));
        Assert.IsNull(span.GetString("missing"));
        Assert.IsNull(span.GetBoolean("missing"));
        Assert.AreEqual(true, Record(true).GetBoolean("value"));
        Assert.IsNull(Record(42).GetString("value"));
    }

    [TestMethod]
    public void String_lists_require_every_element_to_be_a_string()
    {
        Assert.AreSequenceEqual(["first", "second"], Record(new object[] { "first", "second" }).GetStrings("value")!);
        Assert.IsNull(Record(new object[] { "first", 42 }).GetStrings("value"));
        Assert.IsNull(Record("first").GetStrings("value"));
        Assert.IsNull(Record(42).GetStrings("value"));
        Assert.IsNull(Record(null).GetStrings("missing"));
    }

    [TestMethod]
    public void Nanosecond_timestamps_preserve_subtick_precision_in_the_raw_value()
    {
        const ulong start = 1_800_000_000_000_000_099;
        var span = new SpanRecord("4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7", "operation")
        {
            StartTimeUnixNano = start,
            EndTimeUnixNano = start + 201,
        };
        Assert.AreEqual(start, span.StartTimeUnixNano);
        Assert.AreEqual(start + 201, span.EndTimeUnixNano);
        Assert.AreEqual(DateTimeOffset.UnixEpoch.AddTicks((long)(start / 100)), span.StartTime);
        Assert.AreEqual(DateTimeOffset.UnixEpoch.AddTicks((long)((start + 201) / 100)), span.EndTime);
        Assert.AreEqual(TimeSpan.FromTicks(2), span.Duration);
    }

    [TestMethod]
    public void ThreadId_UuidUnderCodexContract_ReadsBackAsExactText()
    {
        var reference = OpenaiCodexIncubatingSpanDefinitions.OpenaiCodexTurnInternal.Attributes.Single(static a => a.Name == ThreadIdWireKey);
        var span = Record(ThreadIdWireKey, CodexConversationThread);

        Assert.AreEqual("string", reference.Type);
        Assert.AreEqual(CodexConversationThread, span.GetString(reference.Name));
        Assert.IsNull(span.GetInt64(reference.Name));
    }

    [TestMethod]
    public void ThreadId_NumberUnderCoreContract_StaysNumeric()
    {
        Assert.IsTrue(RegistryCatalog.TryGetAttribute(ThreadIdWireKey, out var definition));
        var span = Record(ThreadIdWireKey, 42L);

        Assert.AreEqual(RegistryAttributeValueKind.Integer, definition.ValueKind);
        Assert.AreEqual(42L, span.GetInt64(definition.Name));
        Assert.IsNull(span.GetString(definition.Name));
    }

    private static SpanRecord Record(object? value) => Record("value", value);

    private static SpanRecord Record(string key, object? value) => new("4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7", "operation")
    {
        Attributes = new Dictionary<string, object?> { [key] = value },
    };
}
