using System.Globalization;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>QYL0121: Custom is the escape hatch for a value the registry does not list, not a second spelling of a member.</summary>
[TestClass]
public sealed class Qyl0121CustomValueEqualsMemberAnalyzerTests(TestContext testContext)
{

    [TestMethod]
    public async Task Reports_Custom_with_a_value_a_member_carries()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new Qyl0121CustomValueEqualsMemberAnalyzer(), "QYL0121", """
            using Qyl.Telemetry.SemanticConventions.Activities;
            using Qyl.Telemetry.SemanticConventions.Incubating.Activities;

            public static class Values
            {
                public static readonly DbSystemName Postgres = DbSystemName.Custom("postgresql");
                public static readonly MessagingSystem Kafka = MessagingSystem.Custom("kafka");
                public static readonly QylAttributeNamespace NativeValue = QylAttributeNamespace.Custom("value");
            }
            """, cancellationToken: testContext.CancellationToken);
        Assert.HasCount(3, diagnostics);
        Assert.IsTrue(diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).Any(static m => m.Contains("DbSystemName.Postgresql", StringComparison.Ordinal)));
        Assert.IsTrue(diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).Any(static m => m.Contains("MessagingSystem.Kafka", StringComparison.Ordinal)));
        Assert.IsTrue(diagnostics.Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).Any(static m => m.Contains("QylAttributeNamespace.ValueMember", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Is_silent_for_a_value_the_registry_does_not_list()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new Qyl0121CustomValueEqualsMemberAnalyzer(), "QYL0121", """
            using Qyl.Telemetry.SemanticConventions.Activities;

            public static class Values
            {
                public static DbSystemName Memory => DbSystemName.Custom("memory");
                public static DbSystemName FromConfig(string value) => DbSystemName.Custom(value);
            }
            """, cancellationToken: testContext.CancellationToken);
        Assert.IsEmpty(diagnostics);
    }
}
