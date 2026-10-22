
namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
///     QYL0700: Detects OTLP exporter calls without explicit endpoint configuration.
/// </summary>
/// <remarks>
///     <para>
///         When using UseOtlpExporter() or AddOtlpExporter() without an explicit endpoint,
///         telemetry will be sent to the default localhost endpoint or fail to export
///         if no collector is running locally.
///     </para>
///     <para>
///         Methods that trigger this diagnostic (when called without endpoint configuration):
///         <list type="bullet">
///             <item>UseOtlpExporter</item>
///             <item>AddOtlpExporter</item>
///         </list>
///     </para>
///     <para>
///         Ways to satisfy the endpoint configuration requirement:
///         <list type="bullet">
///             <item>Pass options delegate with Endpoint property set</item>
///             <item>Pass explicit endpoint URI parameter</item>
///             <item>Call after Environment.SetEnvironmentVariable for OTEL_EXPORTER_OTLP_ENDPOINT</item>
///         </list>
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class Qyl0700MissingOtlpConfigurationAnalyzer : AlAnalyzer {
    /// <summary>The diagnostic identifier for QYL0700.</summary>
    private const string DiagnosticId = "QYL0700";

    private static readonly string[] OtlpExporterMethods = [
        "UseOtlpExporter",
        "AddOtlpExporter"
    ];

    private static readonly DiagnosticDescriptor Rule = CreateRule(
        DiagnosticId,
        DiagnosticCategories.OpenTelemetry,
        DiagnosticSeverities.Suggestion);

    /// <summary>Gets the diagnostic descriptors for the supported diagnostics.</summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>Registers syntax tree actions to analyze OTLP exporter configuration.</summary>
    protected override void InitializeCore(AnalysisContext context) =>
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context) {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || !OtlpExporterMethods.Contains(memberAccess.Name.Identifier.Text)
            || HasExplicitEndpointConfiguration(invocation, context.SemanticModel, context.CancellationToken)
            || HasEnvironmentVariableSet(invocation)) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }

    private static bool HasExplicitEndpointConfiguration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var arguments = invocation.ArgumentList.Arguments;

        // UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri("...")) or a target-typed new("...")
        if (arguments is [_, { Expression: BaseObjectCreationExpressionSyntax uriCreation }, ..]
            && semanticModel.GetTypeInfo(uriCreation, cancellationToken).Type is { Name: "Uri", ContainingNamespace.Name: "System" }) {
            return true;
        }

        foreach (var arg in arguments) {
            if (arg.Expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax
                && LambdaSetsEndpoint(arg.Expression)) {
                return true;
            }

            if (arg.NameColon?.Name.Identifier.Text.EqualsIgnoreCase("endpoint") == true) {
                return true;
            }

            if (arg.Expression is BaseObjectCreationExpressionSyntax creation
                && creation.Initializer?.Expressions.Any(static e =>
                    e is AssignmentExpressionSyntax assignment
                    && assignment.Left.ToString().EqualsIgnoreCase("Endpoint")) == true) {
                return true;
            }
        }

        return false;
    }

    private static bool LambdaSetsEndpoint(ExpressionSyntax lambda) {
        if (lambda switch {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            AnonymousMethodExpressionSyntax anon => anon.Body,
            _ => null
        } is not { } body) {
            return false;
        }

        foreach (var assignment in body.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>()) {
            var leftText = assignment.Left.ToString();
            if (leftText.EndsWithIgnoreCase(".Endpoint") || leftText.EqualsOrdinal("Endpoint")) {
                return true;
            }
        }

        return false;
    }

    private static bool HasEnvironmentVariableSet(InvocationExpressionSyntax invocation) {
        if (GetEnclosingScope(invocation) is not { } scope) {
            return false;
        }

        // Only check invocations that appear before this one in the member or the top-level statements
        foreach (var inv in scope.DescendantNodes().OfType<InvocationExpressionSyntax>().TakeWhile(inv => inv != invocation)) {
            if (inv.GetMethodName() is "SetEnvironmentVariable" or "Add"
                && inv.ArgumentList.Arguments is [{ Expression: LiteralExpressionSyntax literal }, ..]
                && literal.Token.ValueText.ContainsIgnoreCase("OTEL_EXPORTER_OTLP_ENDPOINT")) {
                return true;
            }
        }

        return false;
    }

    private static SyntaxNode? GetEnclosingScope(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(static ancestor => ancestor is BaseMethodDeclarationSyntax
            or AccessorDeclarationSyntax
            or LocalFunctionStatementSyntax
            or GlobalStatementSyntax) switch {
            GlobalStatementSyntax global => global.Parent,
            var scope => scope
        };
}
