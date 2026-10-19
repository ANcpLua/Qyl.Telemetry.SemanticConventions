using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ANcpLua.Roslyn.Utilities.Testing.MSBuild;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;

[TestClass]
public sealed class SetterFidelityTests
{
    [TestMethod]
    public void Every_settable_wire_key_has_a_unique_setter_and_matching_analyzer_entry()
    {
        var assemblies = new[] { typeof(SpanRecord).Assembly, typeof(IncubatingSpans).Assembly };
        var methods = assemblies.SelectMany(static a => a.GetExportedTypes())
            .Where(static t => t.Name.EndsWith("ActivityExtensions", StringComparison.Ordinal))
            .SelectMany(static t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .ToArray();
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var signature = string.Join(",", parameters.Select(static p => p.ParameterType.FullName));
            Assert.IsTrue(SemconvTypedSurface.Setters.TryGetValue((method.DeclaringType!.FullName!, method.Name, signature), out var metadata), method.ToString());
            Assert.IsNotNull(metadata);
            using var activity = new Activity("setter-fidelity");
            var args = new object?[parameters.Length];
            args[0] = activity;
            for (var index = 1; index < parameters.Length; index++) args[index] = Value(parameters[index].ParameterType);
            method.Invoke(null, args);
            var written = Assert.ContainsSingle(activity.TagObjects);
            Assert.AreEqual(metadata.Key + (metadata.IsTemplate ? ".value" : ""), written.Key, method.Name);
            Assert.IsTrue(actual.Add(metadata.Key), $"Duplicate setter for {metadata.Key}");
        }

        var root = RepositoryRoot.LocateFrom(AppContext.BaseDirectory, "Qyl.Telemetry.SemanticConventions.slnx");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".build", "registry.json")));
        var sources = new[] { document.RootElement }.Concat(document.RootElement.GetProperty("dependencies").EnumerateObject().Select(static d => d.Value)).ToArray();
        var attributes = sources.SelectMany(static s => Rows(s.GetProperty("registry"), "attributes"))
            .Select(static a => a.GetProperty("key").GetString()!).ToHashSet(StringComparer.Ordinal);
        var entityKeys = new HashSet<string>(StringComparer.Ordinal);
        var signalKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        foreach (var section in new[] { "registry", "refinements" })
        {
            if (!source.TryGetProperty(section, out var groups)) continue;
            foreach (var entity in Rows(groups, "entities"))
            foreach (var attribute in Rows(entity, "identity").Concat(Rows(entity, "description")))
                entityKeys.Add(attribute.GetProperty("key").GetString()!);
            foreach (var kind in new[] { "spans", "events", "metrics" })
            foreach (var signal in Rows(groups, kind))
            foreach (var attribute in Rows(signal, "attributes"))
                signalKeys.Add(attribute.GetProperty("key").GetString()!);
        }
        attributes.ExceptWith(entityKeys.Except(signalKeys));
        Assert.IsTrue(attributes.SetEquals(actual), "Missing: " + string.Join(", ", attributes.Except(actual)) + "; extra: " + string.Join(", ", actual.Except(attributes)));
        Assert.HasCount(methods.Length, SemconvTypedSurface.Setters);
    }

    private static IEnumerable<JsonElement> Rows(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out var values)) yield break;
        foreach (var value in values.EnumerateArray()) yield return value;
    }

    private static object Value(Type type)
    {
        if (type == typeof(string)) return "value";
        if (type == typeof(long)) return 17L;
        if (type == typeof(double)) return 1.25;
        if (type == typeof(bool)) return true;
        if (type == typeof(object)) return 17;
        if (type.IsArray)
        {
            var array = Array.CreateInstance(type.GetElementType()!, 1);
            array.SetValue(Value(type.GetElementType()!), 0);
            return array;
        }
        var custom = type.GetMethod("Custom", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(custom, type.FullName);
        return custom.Invoke(null, [Value(custom.GetParameters()[0].ParameterType)])!;
    }
}
