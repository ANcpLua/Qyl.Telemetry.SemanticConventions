using System.Diagnostics;
using ANcpLua.Roslyn.Utilities.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Qyl.Telemetry.SemanticConventions.Analyzers;

namespace Qyl.Telemetry.SemanticConventions.Pipeline.Tests;
[TestClass]
public sealed class SemconvCodeFixProviderTests(TestContext testContext)
{
    [TestMethod]
    public async Task Qyl0003Fix_ObsoleteAliasHoldsReplacement_UsesCurrentConstant()
    {
        var fixedUsage = await FixAsync(new DeprecatedSemconvAnalyzer(), new LiveSemconvMetadataCodeFixProvider(), """
            namespace OpenTelemetry.SemanticConventions
            {
                public static class DbAttributes
                {
                    [System.Obsolete("Replaced by <c>db.system.name</c>.")]
                    public const string AttributeDbSystem = "db.system";

                    [System.Obsolete("Use AttributeDbSystemName.")]
                    public const string AttributeDbSystemNameAlias = "db.system.name";

                    public const string AttributeDbSystemName = "db.system.name";
                }
            }
            """, """
            public static class Instrumentation
            {
                public static string Key => OpenTelemetry.SemanticConventions.DbAttributes.AttributeDbSystem;
            }
            """, testContext.CancellationToken);
        Assert.AreEqual("""
            public static class Instrumentation
            {
                public static string Key => OpenTelemetry.SemanticConventions.DbAttributes.AttributeDbSystemName;
            }
            """, fixedUsage);
    }

    [TestMethod]
    public async Task Qyl0003Fix_PrivateConstantHoldsReplacement_UsesPublicConstant()
    {
        var fixedUsage = await FixAsync(new DeprecatedSemconvAnalyzer(), new LiveSemconvMetadataCodeFixProvider(), """
            namespace OpenTelemetry.SemanticConventions
            {
                public static class DbAttributes
                {
                    [System.Obsolete("Replaced by <c>db.system.name</c>.")]
                    public const string AttributeDbSystem = "db.system";

                    private const string SystemNameKey = "db.system.name";

                    public const string AttributeDbSystemName = SystemNameKey;
                }
            }
            """, """
            public static class Instrumentation
            {
                public static string Key => OpenTelemetry.SemanticConventions.DbAttributes.AttributeDbSystem;
            }
            """, testContext.CancellationToken);
        Assert.AreEqual("""
            public static class Instrumentation
            {
                public static string Key => OpenTelemetry.SemanticConventions.DbAttributes.AttributeDbSystemName;
            }
            """, fixedUsage);
    }

    [TestMethod]
    public async Task Qyl0005Fix_DeprecatedKeyLiteral_RewritesToReplacement()
    {
        var fixedUsage = await FixAsync(new LiteralMatchesDeprecatedSemconvAnalyzer(), new LiveSemconvMetadataCodeFixProvider(), """
            namespace OpenTelemetry.SemanticConventions
            {
                public static class DbAttributes
                {
                    [System.Obsolete("Replaced by <c>db.system.name</c>.")]
                    public const string AttributeDbSystem = "db.system";
                }
            }
            """, """
            using System.Diagnostics;
            public static class Instrumentation
            {
                public static void Stamp(Activity activity) => activity.SetTag("db.system", "postgresql");
            }
            """, testContext.CancellationToken);
        Assert.AreEqual("""
            using System.Diagnostics;
            public static class Instrumentation
            {
                public static void Stamp(Activity activity) => activity.SetTag("db.system.name", "postgresql");
            }
            """, fixedUsage);
    }

    [TestMethod]
    public async Task Qyl0009Fix_ExactCatalogRename_RewritesLiteral()
    {
        var fixedUsage = await FixAsync(new SupplementalSemconvMigrationAnalyzer(), new SupplementalSemconvMigrationCodeFixProvider(), "", """
            using System.Diagnostics;
            public static class Instrumentation
            {
                public static void Stamp(Activity activity) => activity.SetTag("gen_ai.system", "openai");
            }
            """, testContext.CancellationToken);
        Assert.AreEqual("""
            using System.Diagnostics;
            public static class Instrumentation
            {
                public static void Stamp(Activity activity) => activity.SetTag("gen_ai.provider.name", "openai");
            }
            """, fixedUsage);
    }

    private static async Task<string> FixAsync(DiagnosticAnalyzer analyzer, CodeFixProvider provider, string declarations, string usage, CancellationToken cancellationToken)
    {
        var compilation = AnalyzerRun.CompileSource(Compile.Source(usage).WithSource(declarations).WithReference<Activity>());
        using var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution
            .AddProject("Fixture", "Fixture", LanguageNames.CSharp)
            .WithCompilationOptions(compilation.Options)
            .WithParseOptions(compilation.SyntaxTrees[0].Options)
            .WithMetadataReferences(compilation.References)
            .AddDocument("Declarations.cs", declarations).Project;
        var document = project.AddDocument("Usage.cs", usage);
        var documentCompilation = await document.Project.GetCompilationAsync(cancellationToken);
        var diagnostic = (await documentCompilation!.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync(cancellationToken)).Single();
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken));
        var operations = await actions.Single().GetOperationsAsync(cancellationToken);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        return (await changed.GetTextAsync(cancellationToken)).ToString();
    }
}
