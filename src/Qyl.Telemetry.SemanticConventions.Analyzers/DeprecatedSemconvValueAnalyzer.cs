// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// QYL0007: Flags telemetry attribute payloads where the value is a constant
/// string that matches a deprecated member of the corresponding
/// <c>*Values</c> nested class on the SemConv attribute type.
/// </summary>
/// <remarks>
/// Map is built from compiled symbols at compilation start. For each <c>*Attributes</c>
/// class, every nested <c>*Values</c> class is matched against its sibling
/// <c>Attribute*</c> constant by name (e.g. <c>HttpRequestMethodValues</c> ↔
/// <c>AttributeHttpRequestMethod</c>). <c>[Obsolete]</c>-marked value-class members
/// then become entries in a <c>(attrName, value) → message</c> map.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeprecatedSemconvValueAnalyzer : DiagnosticAnalyzer
{

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.DeprecatedSemconvValue];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var map = BuildValueDeprecationMap(context.Compilation);
        if (map.Count == 0)
        {
            return;
        }

        TelemetryAttributePayloadDetection.RegisterPayloadAnalysis(
            context,
            (ctx, payload) => ReportIfDeprecated(ctx, map, payload));
    }

    private static Dictionary<(string AttrName, string Value), string> BuildValueDeprecationMap(Compilation compilation)
    {
        var map = new Dictionary<(string, string), string>();
        foreach (var (attributeName, _, value, obsolete) in SemconvNamespace.EnumerateAttributeValueConstants(compilation))
        {
            if (obsolete is not null && !map.ContainsKey((attributeName, value)))
            {
                map[(attributeName, value)] = SemconvCodeFixHelpers.GetObsoleteMessage(obsolete);
            }
        }

        return map;
    }

    private static void ReportIfDeprecated(
        OperationAnalysisContext context,
        Dictionary<(string AttrName, string Value), string> map,
        TelemetryAttributePayloadLiteral payload)
    {
        if (payload.Value is null
            || payload.ValueSyntax is null
            || !map.TryGetValue((payload.Key, payload.Value), out var message))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (SemconvCodeFixHelpers.TryExtractExactReplacement(message, out var replacement))
        {
            properties = properties.Add(SemconvCodeFixHelpers.ReplacementValueProperty, replacement);
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.DeprecatedSemconvValue,
            payload.ValueSyntax.GetLocation(),
            properties,
            payload.Value,
            payload.Key,
            message));
    }
}
