// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// QYL0005: Flags telemetry attribute key literals whose values match a
/// semantic-convention attribute name that is marked <c>[Obsolete]</c> in the
/// consumer's resolved <c>OpenTelemetry.SemanticConventions</c> assembly.
/// </summary>
/// <remarks>
/// Distinct from <see cref="DeprecatedSemconvAnalyzer"/> (QYL0010), which fires
/// on direct typed-constant references. This rule catches the case where the
/// consumer hardcodes a literal, bypassing the typed constant entirely — a common
/// pattern in legacy code that the OTel SDK's <c>SetTag(string, …)</c> overloads encourage.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LiteralMatchesDeprecatedSemconvAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.LiteralMatchesDeprecatedSemconv];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var deprecationMap = BuildDeprecationMap(context.Compilation);
        if (deprecationMap.Count == 0)
        {
            return;
        }

        TelemetryAttributePayloadDetection.RegisterPayloadAnalysis(
            context,
            (ctx, payload) => ReportIfDeprecated(ctx, deprecationMap, payload));
    }

    private static Dictionary<string, string> BuildDeprecationMap(Compilation compilation)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, _, value, obsolete) in SemconvNamespace.EnumerateAttributeKeyConstants(compilation))
        {
            if (obsolete is not null && !map.ContainsKey(value))
            {
                map[value] = SemconvCodeFixHelpers.GetObsoleteMessage(obsolete);
            }
        }

        return map;
    }

    private static void ReportIfDeprecated(
        OperationAnalysisContext context,
        Dictionary<string, string> deprecationMap,
        TelemetryAttributePayloadLiteral payload)
    {
        // Only fire on bare literals; QYL0010 already handles typed-constant references.
        if (!payload.KeyIsBareLiteral
            || !deprecationMap.TryGetValue(payload.Key, out var deprecationMessage))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (SemconvCodeFixHelpers.TryExtractExactReplacement(deprecationMessage, out var replacement))
        {
            properties = properties.Add(SemconvCodeFixHelpers.ReplacementValueProperty, replacement);
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.LiteralMatchesDeprecatedSemconv,
            payload.KeySyntax.GetLocation(),
            properties,
            payload.Key,
            deprecationMessage));
    }
}
