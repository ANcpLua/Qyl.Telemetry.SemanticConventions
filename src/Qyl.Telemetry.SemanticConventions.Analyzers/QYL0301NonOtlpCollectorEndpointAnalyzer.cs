
namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
///     QYL0301: Detects collector endpoint configurations that don't use OTLP protocol.
/// </summary>
/// <remarks>
///     <para>
///         Collector endpoints should use the OTLP protocol for standardized telemetry export:
///         <list type="bullet">
///             <item>gRPC: grpc://host:4317</item>
///             <item>HTTP: http://host:4318/v1/traces (or /v1/metrics, /v1/logs)</item>
///         </list>
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class Qyl0301NonOtlpCollectorEndpointAnalyzer : AlAnalyzer {
    private static readonly string[] OtlpPatterns = [
        "4317", // gRPC default port
        "4318", // HTTP default port
        "/v1/traces",
        "/v1/metrics",
        "/v1/logs",
        "otlp"
    ];

    /// <summary>The diagnostic identifier for QYL0301.</summary>
    private const string DiagnosticId = "QYL0301";

    private static readonly DiagnosticDescriptor Rule = CreateRule(
        DiagnosticId,
        DiagnosticCategories.Configuration,
        DiagnosticSeverities.Suggestion);

    /// <summary>Gets the diagnostic descriptors for the supported diagnostics.</summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>Registers operation actions to analyze endpoint assignments.</summary>
    protected override void InitializeCore(AnalysisContext context) =>
        context.RegisterOperationAction(AnalyzeAssignment, OperationKind.SimpleAssignment);

    private static void AnalyzeAssignment(OperationAnalysisContext context) {
        var assignment = (ISimpleAssignmentOperation)context.Operation;

        if (assignment.Target is not IMemberReferenceOperation { Member.Name: var propertyName } ||
            !OtlpConfigurationNames.EndpointProperties.Contains(propertyName, StringComparer.OrdinalIgnoreCase) ||
            assignment.Value.ConstantValue is not { HasValue: true, Value: string endpoint } ||
            IsOtlpEndpoint(endpoint)) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.Syntax.GetLocation(), endpoint));
    }

    private static bool IsOtlpEndpoint(string endpoint) =>
        OtlpPatterns.Any(endpoint.ContainsIgnoreCase);
}
