// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// QYL0001: Flags <c>SetTag/AddTag/SetAttribute("graphql.document", …)</c> and the generated
/// typed setter <c>SetGraphqlDocument(…)</c>. v1.41.0
/// demoted <c>graphql.document</c> from <c>recommended</c> to <c>opt_in</c> due to
/// its high-cardinality and PII risk. Diagnostic surfaces as a hint, not a hard
/// failure — capture is still legal when explicitly enabled with sanitization.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GraphqlDocumentOptInAnalyzer : DiagnosticAnalyzer
{
    private const string GraphqlDocumentKey = "graphql.document";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.GraphqlDocumentIsOptIn];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!TagSetterDetection.TryGetTagWrite(invocation, out var key, out _)
            || !string.Equals(key, GraphqlDocumentKey, StringComparison.Ordinal))
        {
            return;
        }

        var location = TagSetterDetection.IsTagSetterInvocation(invocation)
            && TagSetterDetection.TryGetTagSetterKeyArgument(invocation, out var keyArgument)
                ? keyArgument.Syntax
                : invocation.Syntax;
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.GraphqlDocumentIsOptIn,
            location.GetLocation()));
    }
}
