
namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
///     QYL0701: Detects OTLP exporter configurations using HTTP protocol without compression.
/// </summary>
/// <remarks>
///     <para>
///         OTLP supports two transports: gRPC and HTTP/protobuf. Neither enables
///         compression by default — the OTLP specification mandates no default, and the
///         .NET exporter leaves compression off on both transports unless configured.
///         gRPC is not compressed automatically.
///     </para>
///     <para>
///         For services emitting large telemetry payloads (especially those using
///         gen_ai.content attributes with full request/response text), enabling
///         gzip compression can substantially reduce bandwidth usage and decrease
///         export latency.
///     </para>
///     <para>
///         The analyzer flags OTLP exporter configurations that set Protocol to
///         HttpProtobuf without enabling compression. The gRPC path is not currently
///         inspected — a known scope limitation, not an implication that gRPC compresses
///         on its own.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class Qyl0701UncompressedExportAnalyzer : AlAnalyzer {
    /// <summary>The diagnostic identifier for QYL0701.</summary>
    private const string DiagnosticId = "QYL0701";

    private static readonly DiagnosticDescriptor Rule = CreateRule(
        DiagnosticId,
        DiagnosticCategories.OpenTelemetry,
        DiagnosticSeverity.Warning);

    /// <summary>Type names for OTLP exporter options.</summary>
    private static readonly string[] OtlpOptionsTypeNames = [
        "OpenTelemetry.Exporter.OtlpExporterOptions",
        "OpenTelemetry.Exporter.OtlpExporterOptionsBase"
    ];

    /// <summary>Gets the diagnostic descriptors for the supported diagnostics.</summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>Registers compilation start action to analyze OTLP exporter configurations.</summary>
    protected override void InitializeCore(AnalysisContext context) =>
        context.RegisterCompilationStartAction(OnCompilationStart);

    private static void OnCompilationStart(CompilationStartAnalysisContext context) {
        var otlpOptionsTypes = OtlpOptionsTypeNames
            .Select(context.Compilation.GetTypeByMetadataName)
            .WhereNotNull()
            .ToImmutableArray();

        if (otlpOptionsTypes.IsEmpty) {
            return;
        }

        var httpProtobufType = context.Compilation.GetTypeByMetadataName(
            "OpenTelemetry.Exporter.OtlpExportProtocol");

        context.RegisterSyntaxNodeAction(
            ctx => AnalyzeInvocation(ctx, otlpOptionsTypes, httpProtobufType),
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        ImmutableArray<INamedTypeSymbol> otlpOptionsTypes,
        INamedTypeSymbol? httpProtobufType) {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.GetMethodName() is not { } methodName || !OtlpConfigurationNames.ExporterMethods.Contains(methodName)) {
            return;
        }

        var lambdaArg = invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .OfType<SimpleLambdaExpressionSyntax>()
            .FirstOrDefault();

        if (lambdaArg is not null) {
            if (!HasCompressionConfiguration(lambdaArg)
                && HasHttpProtobufConfiguration(lambdaArg, httpProtobufType, context.SemanticModel, context.CancellationToken)) {
                context.ReportDiagnostic(Rule, invocation.GetMethodLocation());
            }

            return;
        }

        // Delegate arguments (AddOtlpExporter(ConfigureOtlp)) cannot be traced statically
        if (invocation.ArgumentList.Arguments.Any(a => a.Expression is IdentifierNameSyntax)) {
            return;
        }

        foreach (var arg in invocation.ArgumentList.Arguments) {
            if (ModelExtensions.GetTypeInfo(context.SemanticModel, arg.Expression, context.CancellationToken).Type is not { } argType) {
                continue;
            }

            if (otlpOptionsTypes.Any(optionsType => argType.InheritsFrom(optionsType) || argType.IsEqualTo(optionsType))
                && IsHttpProtobufOptionsWithoutCompression(arg.Expression)) {
                context.ReportDiagnostic(Rule, invocation.GetMethodLocation());
            }
        }
    }

    private static bool HasCompressionConfiguration(SimpleLambdaExpressionSyntax lambda) {
        foreach (var node in lambda.DescendantNodes()) {
            var name = node switch {
                AssignmentExpressionSyntax assignment => assignment.Left.GetIdentifierName(),
                InvocationExpressionSyntax invocation => invocation.GetMethodName(),
                _ => null
            };

            if (name is not null && (name.ContainsIgnoreCase("compression") || name.ContainsIgnoreCase("gzip"))) {
                return true;
            }
        }

        return false;
    }

    private static bool HasHttpProtobufConfiguration(
        SimpleLambdaExpressionSyntax lambda,
        INamedTypeSymbol? httpProtobufType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        foreach (var node in lambda.DescendantNodes()) {
            if (node is not AssignmentExpressionSyntax { Right: MemberAccessExpressionSyntax memberAccess } assignment
                || assignment.Left.GetIdentifierName() is not "Protocol") {
                continue;
            }

            if (memberAccess.Name.Identifier.Text.EqualsOrdinal("HttpProtobuf")) {
                return true;
            }

            // Fall back to semantic model for aliased/renamed references
            if (httpProtobufType is not null
                && ModelExtensions.GetSymbolInfo(semanticModel, memberAccess, cancellationToken).Symbol
                    is IFieldSymbol { Name: "HttpProtobuf" } fieldSymbol
                && fieldSymbol.ContainingType.IsEqualTo(httpProtobufType)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsHttpProtobufOptionsWithoutCompression(
        ExpressionSyntax expression) {
        if (expression is not ObjectCreationExpressionSyntax { Initializer: { } initializer }) {
            return false;
        }

        var hasHttpProtobuf = false;
        var hasCompression = false;

        foreach (var expr in initializer.Expressions) {
            if (expr is not AssignmentExpressionSyntax assignment
                || assignment.Left.GetIdentifierName() is not { } leftText) {
                continue;
            }

            if (leftText.EqualsOrdinal("Protocol")
                && assignment.Right is MemberAccessExpressionSyntax { Name.Identifier.Text: "HttpProtobuf" }) {
                hasHttpProtobuf = true;
            }

            if (leftText.ContainsIgnoreCase("compression") || leftText.ContainsIgnoreCase("gzip")) {
                hasCompression = true;
            }
        }

        return hasHttpProtobuf && !hasCompression;
    }
}
