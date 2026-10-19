using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using ANcpLua.Roslyn.Utilities.Testing;
using ANcpLua.Roslyn.Utilities.Testing.MSBuild;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class GeneratorFidelityTests(TestContext testContext)
{
    [TestMethod]
    public async Task Weaver_preserves_associations_reference_metadata_and_typed_projection()
    {
        var root = RepositoryRoot.LocateFrom(AppContext.BaseDirectory, "Qyl.Telemetry.SemanticConventions.slnx");
        var fixture = Path.Combine(root, "tests", "fixtures", "semantic-fidelity");
        var temporary = Directory.CreateTempSubdirectory("qyl-weaver-fidelity-");
        var loadContext = new AssemblyLoadContext("qyl-weaver-fidelity", isCollectible: true);
        try
        {
            var templates = Directory.CreateDirectory(Path.Combine(temporary.FullName, "templates", "fidelity"));
            var production = Path.Combine(root, "templates", "registry", "csharp");
            foreach (var file in Directory.EnumerateFiles(production, "*.j2"))
                File.Copy(file, Path.Combine(templates.FullName, Path.GetFileName(file)));
            Directory.CreateDirectory(Path.Combine(templates.FullName, "jq"));
            File.Copy(Path.Combine(production, "jq", "symbols.jq"), Path.Combine(templates.FullName, "jq", "symbols.jq"));
            File.Copy(Path.Combine(fixture, "fidelity.jq"), Path.Combine(templates.FullName, "jq", "fidelity.jq"));
            File.Copy(Path.Combine(fixture, "weaver.yaml"), Path.Combine(templates.FullName, "weaver.yaml"));
            var output = Path.Combine(temporary.FullName, "generated");
            await Generate(root, fixture, templates.Parent!.FullName, output);

            var compilation = AnalyzerRun.CompileSource(Compile.Source("global using System; global using System.Collections.Generic;")
                .WithReference<SpanRecord>().WithReference<Activity>().WithReference(typeof(Enumerable).Assembly));
            compilation = compilation.AddSyntaxTrees(Directory.EnumerateFiles(output, "*.cs")
                .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)));
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image, cancellationToken: testContext.CancellationToken);
            Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
            image.Position = 0;
            var assembly = loadContext.LoadFromStream(image);

            var span = Field<SpanDefinition<Internal>>(assembly, "Fidelity.FidelityIncubatingSpanDefinitions", "FidelityOperationInternal");
            var metric = Field<MetricDefinition<Counter>>(assembly, "Fidelity.FidelityIncubatingMetricDefinitions", "FidelityOperations");
            var @event = Field<EventDefinition>(assembly, "Fidelity.FidelityIncubatingEventDefinitions", "FidelityCompleted");
            var entity = Field<EntityDefinition>(assembly, "Fidelity.FidelityIncubatingEntityDefinitions", "FidelityOperation");
            var recommended = Field<EntityDefinition>(assembly, "Fidelity.FidelityIncubatingEntityDefinitions", "FidelityAlternate");
            Assert.AreEqual(SignalRequirementLevel.OptIn, span.RequirementLevel);
            Assert.AreEqual(SignalRequirementLevel.OptIn, metric.RequirementLevel);
            Assert.AreEqual(SignalRequirementLevel.OptIn, @event.RequirementLevel);
            Assert.AreEqual(SignalRequirementLevel.OptIn, entity.RequirementLevel);
            Assert.AreEqual(SignalRequirementLevel.Recommended, recommended.RequirementLevel);
            foreach (var associations in new[] { span.EntityAssociations, metric.EntityAssociations, @event.EntityAssociations })
                AssertAssociations(associations);

            var start = span.Attributes.Single(static a => a.Name == "fidelity.start");
            var finish = span.Attributes.Single(static a => a.Name == "fidelity.finish");
            Assert.AreEqual(AttributePhase.Start, start.Phase);
            Assert.IsTrue(start.SamplingRelevant);
            Assert.AreEqual(AttributePhase.End, finish.Phase);
            Assert.IsFalse(finish.SamplingRelevant);
            var count = span.Attributes.Single(static a => a.Name == "fidelity.count");
            Assert.AreEqual(AttributePhase.Start, count.Phase);
            Assert.IsFalse(count.SamplingRelevant, "availability at start does not imply sampling relevance");
            var starter = assembly.GetType("Qyl.IncubatingSpans", throwOnError: true)!.GetMethod("FidelityOperation");
            Assert.IsNotNull(starter);
            var parameters = starter.GetParameters().Select(static p => p.Name).ToArray();
            Assert.Contains("start", parameters);
            Assert.DoesNotContain("finish", parameters);
            var parameterMap = Field<Dictionary<string, Dictionary<string, string>>>(assembly,
                "Qyl.Telemetry.SemanticConventions.Analyzers.SemconvTypedSurface", "StarterParameters");
            Assert.AreEqual("fidelity.start", parameterMap["Qyl.IncubatingSpans.FidelityOperation"]["start"]);
            Assert.DoesNotContain("finish", parameterMap["Qyl.IncubatingSpans.FidelityOperation"].Keys);

            var view = assembly.GetType("Fidelity.FidelityOperationSpan", throwOnError: true)!;
            var matches = view.GetMethod("Matches")!;
            var values = new Dictionary<string, object?> { ["fidelity.id"] = "one", ["fidelity.count"] = 42L, ["fidelity.ratio"] = 1.5, ["fidelity.mode"] = 0L };
            var record = new SpanRecord("trace", "span", "operation") { Kind = SpanKind.Internal, Attributes = values };
            Assert.AreEqual(true, matches.Invoke(null, [record]));
            values["fidelity.ratio"] = 2;
            Assert.AreEqual(true, matches.Invoke(null, [record]), "an integer is a supported double representation");
            values["fidelity.count"] = "42";
            Assert.AreEqual(false, matches.Invoke(null, [record]));
            values["fidelity.count"] = 42;
            values["fidelity.ratio"] = "1.5";
            Assert.AreEqual(false, matches.Invoke(null, [record]));
            values["fidelity.ratio"] = 0.0;
            values["fidelity.count"] = 0L;
            Assert.AreEqual(true, matches.Invoke(null, [record]), "zero is a valid numeric value");
            values["fidelity.mode"] = null;
            Assert.AreEqual(false, matches.Invoke(null, [record]), "a missing enum value must not become zero");

            var constants = assembly.GetType("Fidelity.FidelityAttributes", throwOnError: true)!.GetFields();
            foreach (var wireKey in new[] { "fidelity.state.value", "fidelity.state_value", "fidelity.state.value.native" })
                Assert.ContainsSingle(constants.Where(f => Equals(f.GetRawConstantValue(), wireKey)));
            var extensions = assembly.GetType("Qyl.Telemetry.SemanticConventions.Incubating.Activities.FidelityIncubatingActivityExtensions", true)!;
            Assert.HasCount(constants.Length, extensions.GetMethods(BindingFlags.Public | BindingFlags.Static));
        }
        finally
        {
            loadContext.Unload();
            temporary.Delete(recursive: true);
        }
    }

    private static void AssertAssociations(IReadOnlyList<EntityAssociation> associations)
    {
        var all = Assert.IsInstanceOfType<EntityAssociation.AllOf>(Assert.ContainsSingle(associations));
        Assert.HasCount(2, all.Requirements);
        var local = Assert.IsInstanceOfType<EntityRef>(all.Requirements[0]);
        Assert.AreEqual("fidelity.operation", local.Type);
        Assert.AreEqual("https://fidelity.example/local/1", local.Provenance.SchemaUrl);
        var alternatives = Assert.IsInstanceOfType<EntityAssociation.OneOf>(all.Requirements[1]);
        Assert.HasCount(2, alternatives.Alternatives);
        var external = Assert.IsInstanceOfType<EntityRef>(alternatives.Alternatives[0]);
        Assert.AreEqual("external.service", external.Type);
        Assert.AreEqual("https://fidelity.example/external/1", external.Provenance.SchemaUrl);
        var alternative = Assert.IsInstanceOfType<EntityRef>(alternatives.Alternatives[1]);
        Assert.AreEqual("fidelity.alternate", alternative.Type);
        Assert.AreEqual("https://fidelity.example/local/1", alternative.Provenance.SchemaUrl);
    }

    private static T Field<T>(Assembly assembly, string type, string field) =>
        Assert.IsInstanceOfType<T>(assembly.GetType(type, throwOnError: true)!.GetField(field,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));

    private async Task Generate(string root, string fixture, string templates, string output)
    {
        var start = new ProcessStartInfo("mise")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in new[] { "exec", "--", "weaver", "registry", "generate", "-r", Path.Combine(fixture, "registry"), "--v2",
                     "-t", templates, "--params", Path.Combine(fixture, "params.yaml"), "fidelity", output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        Assert.IsNotNull(process);
        var stdout = process.StandardOutput.ReadToEndAsync(testContext.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(testContext.CancellationToken);
        try
        {
            await process.WaitForExitAsync(testContext.CancellationToken);
            Assert.AreEqual(0, process.ExitCode, (await stdout) + (await stderr));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
