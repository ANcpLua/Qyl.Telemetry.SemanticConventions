using ANcpLua.Roslyn.Utilities.Testing.MSBuild;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
/// <summary>
/// Required generated signal families remain available in both packages. Exact generated-file
/// drift belongs to scripts/check-generated.sh, which regenerates against the committed source.
/// </summary>
[TestClass]
public sealed class GeneratedSurfaceTests
{
    private static readonly string[] GeneratedRoots = ["src/Qyl.Telemetry.SemanticConventions/Generated", "src/Qyl.Telemetry.SemanticConventions.Incubating/Generated", "generated", "typescript/src", "typescript/dist", ];
    [TestMethod]
    public void The_Generated_Surface_Covers_Both_Packages_And_Every_Signal_Kind()
    {
        var paths = Enumerate().ToList();
        foreach (var kind in new[]
        {
            "Attributes",
            "Activities",
            "Metrics",
            "Spans",
            "Events",
            "Entities"
        })
        {
            Assert.IsTrue(paths.Any(path => path.Contains($"/Generated/{kind}/", StringComparison.Ordinal) && path.StartsWith("src/Qyl.Telemetry.SemanticConventions/", StringComparison.Ordinal)), $"the stable package ships {kind}");
            Assert.IsTrue(paths.Any(path => path.Contains($"/Generated/{kind}/", StringComparison.Ordinal) && path.StartsWith("src/Qyl.Telemetry.SemanticConventions.Incubating/", StringComparison.Ordinal)), $"the incubating package ships {kind}");
        }

        Assert.Contains("src/Qyl.Telemetry.SemanticConventions/Generated/SchemaUrl.g.cs", paths);
        Assert.Contains("src/Qyl.Telemetry.SemanticConventions/Generated/Names/QylTelemetryNames.g.cs", paths);
        Assert.Contains("src/Qyl.Telemetry.SemanticConventions.Incubating/Generated/Mapping/AttributeMapping.g.cs", paths);
        Assert.Contains("generated/pins.props", paths);
    }

    private static IEnumerable<string> Enumerate()
    {
        foreach (var root in GeneratedRoots)
        {
            var directory = RepoPath(root.Split('/'));
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                yield return Path.GetRelativePath(RepoPath(), file).Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    private static string RepoPath(params string[] parts)
    {
        string root = RepositoryRoot.LocateFrom(AppContext.BaseDirectory, "Qyl.Telemetry.SemanticConventions.slnx");
        return Path.Combine([root, ..parts]);
    }
}
