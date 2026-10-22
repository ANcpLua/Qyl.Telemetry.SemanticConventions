// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace Qyl.Telemetry.SemanticConventions.Analyzers;

/// <summary>
/// Reports semantic-convention migrations sourced from the supplemental
/// changelog catalog when live <c>[Obsolete]</c> metadata does not own the finding.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SupplementalSemconvMigrationAnalyzer : DiagnosticAnalyzer
{
    private const string OldNameProperty = "OldName";
    internal const string ReplacementNameProperty = "ReplacementName";
    internal const string MigrationKindProperty = "MigrationKind";
    private const string ItemKindProperty = "Kind";
    private const string SignalProperty = "Signal";
    private const string DomainProperty = "Domain";
    private const string ChangelogVersionProperty = "ChangelogVersion";

    private static readonly ImmutableHashSet<string> MetricInstrumentMethodNames = ImmutableHashSet.Create(
        "CreateCounter",
        "CreateHistogram",
        "CreateGauge",
        "CreateObservableCounter",
        "CreateObservableGauge",
        "CreateObservableUpDownCounter",
        "CreateUpDownCounter");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.SupplementalExactSemconvMigration,
        DiagnosticDescriptors.SupplementalManualSemconvMigration,
    ];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var liveObsoleteAttributeNames = BuildLiveObsoleteAttributeNames(context.Compilation);
        var liveObsoleteAttributeValues = BuildLiveObsoleteAttributeValues(context.Compilation);

        context.RegisterOperationBlockStartAction(blockContext =>
        {
            if (blockContext.OperationBlocks.IsDefaultOrEmpty
                || IsCatalogSource(blockContext.OperationBlocks[0]))
            {
                return;
            }

            var scope = new TelemetryPayloadFlowScope(blockContext.OperationBlocks);

            blockContext.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, scope, liveObsoleteAttributeNames, liveObsoleteAttributeValues),
                OperationKind.Invocation);
            blockContext.RegisterOperationAction(
                ctx => AnalyzeObjectCreation(ctx, scope, liveObsoleteAttributeNames, liveObsoleteAttributeValues),
                OperationKind.ObjectCreation);
            blockContext.RegisterOperationAction(
                ctx => AnalyzeVariableDeclarator(ctx, scope, liveObsoleteAttributeNames, liveObsoleteAttributeValues),
                OperationKind.VariableDeclarator);
            blockContext.RegisterOperationAction(
                ctx => AnalyzeAssignment(ctx, scope, liveObsoleteAttributeNames, liveObsoleteAttributeValues),
                OperationKind.SimpleAssignment);
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        TelemetryPayloadFlowScope scope,
        ImmutableHashSet<string> liveObsoleteAttributeNames,
        ImmutableHashSet<string> liveObsoleteAttributeValues)
    {
        var invocation = (IInvocationOperation)context.Operation;

        AnalyzeMetricInstrumentName(context, invocation);
        AnalyzeActivityOrEventName(context, invocation);
        TelemetryAttributePayloadDetection.AnalyzeInvocation(
            invocation,
            scope,
            payload => ReportPayloadIfCatalogOnly(
                context,
                payload,
                liveObsoleteAttributeNames,
                liveObsoleteAttributeValues));
    }

    private static void AnalyzeObjectCreation(
        OperationAnalysisContext context,
        TelemetryPayloadFlowScope scope,
        ImmutableHashSet<string> liveObsoleteAttributeNames,
        ImmutableHashSet<string> liveObsoleteAttributeValues)
    {
        var objectCreation = (IObjectCreationOperation)context.Operation;

        if (objectCreation.Type?.Name == "ActivityEvent"
            && TelemetryAttributePayloadDetection.TryGetArgumentByOrdinal(
                objectCreation.Arguments,
                extensionMethod: false,
                0,
                out var nameArgument)
            && TelemetryAttributePayloadDetection.TryGetBareStringLiteral(
                nameArgument.Value,
                out var eventName,
                out var eventNameSyntax))
        {
            ReportNameIfCatalogOnly(
                context,
                eventName,
                eventNameSyntax,
                liveObsoleteAttributeNames,
                isProductionEmission: true);
        }

        TelemetryAttributePayloadDetection.AnalyzeObjectCreation(
            objectCreation,
            scope,
            payload => ReportPayloadIfCatalogOnly(
                context,
                payload,
                liveObsoleteAttributeNames,
                liveObsoleteAttributeValues));
    }

    private static void AnalyzeVariableDeclarator(
        OperationAnalysisContext context,
        TelemetryPayloadFlowScope scope,
        ImmutableHashSet<string> liveObsoleteAttributeNames,
        ImmutableHashSet<string> liveObsoleteAttributeValues)
    {
        TelemetryAttributePayloadDetection.AnalyzeVariableDeclarator(
            (IVariableDeclaratorOperation)context.Operation,
            scope,
            payload => ReportPayloadIfCatalogOnly(
                context,
                payload,
                liveObsoleteAttributeNames,
                liveObsoleteAttributeValues));
    }

    private static void AnalyzeAssignment(
        OperationAnalysisContext context,
        TelemetryPayloadFlowScope scope,
        ImmutableHashSet<string> liveObsoleteAttributeNames,
        ImmutableHashSet<string> liveObsoleteAttributeValues)
    {
        TelemetryAttributePayloadDetection.AnalyzeAssignment(
            (ISimpleAssignmentOperation)context.Operation,
            scope,
            payload => ReportPayloadIfCatalogOnly(
                context,
                payload,
                liveObsoleteAttributeNames,
                liveObsoleteAttributeValues));
    }

    private static void AnalyzeMetricInstrumentName(
        OperationAnalysisContext context,
        IInvocationOperation invocation)
    {
        if (!MetricInstrumentMethodNames.Contains(invocation.TargetMethod.Name)
            || invocation.TargetMethod.ContainingType.Name != "Meter"
            || !TelemetryAttributePayloadDetection.TryGetArgumentByOrdinal(
                invocation.Arguments,
                invocation.TargetMethod.IsExtensionMethod,
                0,
                out var nameArgument)
            || !TelemetryAttributePayloadDetection.TryGetBareStringLiteral(
                nameArgument.Value,
                out var metricName,
                out var metricNameSyntax)
            || !SemconvMigrationCatalog.TryGetMigrationByName(metricName, out var entry)
            || entry.Kind != SemconvMigrationItemKind.MetricName
            || !SemconvMigrationCatalog.IsSupplementalDiagnosticEntry(entry))
        {
            return;
        }

        ReportCatalogDiagnostic(context, entry, metricName, metricNameSyntax, isProductionEmission: true);
    }

    private static void AnalyzeActivityOrEventName(
        OperationAnalysisContext context,
        IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name == "StartActivity"
            && invocation.TargetMethod.ContainingType.Name == "ActivitySource"
            && TelemetryAttributePayloadDetection.TryGetArgumentByOrdinal(
                invocation.Arguments,
                invocation.TargetMethod.IsExtensionMethod,
                0,
                out var spanArgument)
            && TelemetryAttributePayloadDetection.TryGetBareStringLiteral(
                spanArgument.Value,
                out var spanName,
                out var spanNameSyntax)
            && SemconvMigrationCatalog.TryGetMigrationByName(spanName, out var spanEntry)
            && spanEntry.Kind == SemconvMigrationItemKind.SpanName
            && SemconvMigrationCatalog.IsSupplementalDiagnosticEntry(spanEntry))
        {
            ReportCatalogDiagnostic(context, spanEntry, spanName, spanNameSyntax, isProductionEmission: true);
        }

        if (invocation.TargetMethod.Name == "AddEvent"
            && TelemetryAttributePayloadDetection.TryGetArgumentByOrdinal(
                invocation.Arguments,
                invocation.TargetMethod.IsExtensionMethod,
                0,
                out var eventArgument)
            && TelemetryAttributePayloadDetection.TryGetBareStringLiteral(
                eventArgument.Value,
                out var eventName,
                out var eventNameSyntax)
            && SemconvMigrationCatalog.TryGetMigrationByName(eventName, out var eventEntry)
            && eventEntry.Kind == SemconvMigrationItemKind.EventName
            && SemconvMigrationCatalog.IsSupplementalDiagnosticEntry(eventEntry))
        {
            ReportCatalogDiagnostic(context, eventEntry, eventName, eventNameSyntax, isProductionEmission: true);
        }
    }

    private static void ReportPayloadIfCatalogOnly(
        OperationAnalysisContext context,
        TelemetryAttributePayloadLiteral payload,
        ImmutableHashSet<string> liveObsoleteAttributeNames,
        ImmutableHashSet<string> liveObsoleteAttributeValues)
    {
        if (!payload.KeyIsBareLiteral)
        {
            return;
        }

        ReportNameIfCatalogOnly(
            context,
            payload.Key,
            payload.KeySyntax,
            liveObsoleteAttributeNames,
            payload.IsProductionEmission);

        if (payload is { ValueIsBareLiteral: true, Value: not null, ValueSyntax: not null })
        {
            ReportValueIfCatalogOnly(
                context,
                payload.Key,
                payload.Value,
                payload.ValueSyntax,
                liveObsoleteAttributeValues,
                payload.IsProductionEmission);
        }
    }

    private static void ReportNameIfCatalogOnly(
        OperationAnalysisContext context,
        string name,
        SyntaxNode syntax,
        ImmutableHashSet<string> liveObsoleteAttributeNames,
        bool isProductionEmission)
    {
        if (liveObsoleteAttributeNames.Contains(name)
            || !SemconvMigrationCatalog.TryGetMigrationByName(name, out var entry)
            || entry.Kind is SemconvMigrationItemKind.MetricName or SemconvMigrationItemKind.SpanName
            || !SemconvMigrationCatalog.IsSupplementalDiagnosticEntry(entry))
        {
            return;
        }

        ReportCatalogDiagnostic(context, entry, name, syntax, isProductionEmission);
    }

    private static void ReportValueIfCatalogOnly(
        OperationAnalysisContext context,
        string key,
        string value,
        SyntaxNode syntax,
        ImmutableHashSet<string> liveObsoleteAttributeValues,
        bool isProductionEmission)
    {
        if (liveObsoleteAttributeValues.Contains(key + "=" + value)
            || !SemconvMigrationCatalog.TryGetAttributeValueMigration(key, value, out var entry)
            || !SemconvMigrationCatalog.IsSupplementalDiagnosticEntry(entry))
        {
            return;
        }

        ReportCatalogDiagnostic(context, entry, entry.OldName, syntax, isProductionEmission);
    }

    private static void ReportCatalogDiagnostic(
        OperationAnalysisContext context,
        SemconvMigrationCatalogEntry entry,
        string name,
        SyntaxNode syntax,
        bool isProductionEmission)
    {
        var descriptor = entry.HasExactReplacement && isProductionEmission
            ? DiagnosticDescriptors.SupplementalExactSemconvMigration
            : DiagnosticDescriptors.SupplementalManualSemconvMigration;
        var replacement = GetTerminalReplacement(entry, name);
        var evidence = string.IsNullOrEmpty(entry.ChangelogEvidence)
            ? entry.MigrationKind.ToString()
            : entry.ChangelogEvidence;

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(OldNameProperty, entry.OldName)
            .Add(ReplacementNameProperty, replacement)
            .Add(MigrationKindProperty, entry.MigrationKind.ToString())
            .Add(ItemKindProperty, entry.Kind.ToString())
            .Add(SignalProperty, entry.Signal)
            .Add(DomainProperty, entry.Domain)
            .Add(ChangelogVersionProperty, entry.Version);

        var args = descriptor.Id == "QYL0009"
            ? new object[] { entry.OldName, replacement, evidence }
            : new object[] { entry.OldName, evidence };

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            syntax.GetLocation(),
            properties,
            args));
    }

    private static string GetTerminalReplacement(SemconvMigrationCatalogEntry entry, string name)
    {
        if (!entry.HasExactReplacement)
        {
            return string.Empty;
        }

        return entry.Kind == SemconvMigrationItemKind.AttributeValue
            ? entry.ReplacementNames[0]
            : SemconvMigrationCatalog.ResolveTerminalReplacement(name);
    }

    private static ImmutableHashSet<string> BuildLiveObsoleteAttributeNames(Compilation compilation)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var type in SemconvNamespace.EnumerateAttributesTypes(compilation))
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol
                    {
                        IsConst: true,
                        Type.SpecialType: SpecialType.System_String,
                        ConstantValue: string value,
                    } field
                    && !string.IsNullOrEmpty(value)
                    && field.HasAttribute("System.ObsoleteAttribute"))
                {
                    builder.Add(value);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> BuildLiveObsoleteAttributeValues(Compilation compilation)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var type in SemconvNamespace.EnumerateAttributesTypes(compilation))
        {
            foreach (var (attributeName, valueField) in SemconvNamespace.EnumerateAttributeValueConstants(type))
            {
                if (valueField.HasAttribute("System.ObsoleteAttribute"))
                {
                    builder.Add(attributeName + "=" + (string)valueField.ConstantValue!);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsCatalogSource(IOperation operation)
    {
        var path = operation.Syntax.SyntaxTree.FilePath.Replace('\\', '/');
        return path.EndsWith("/OpenTelemetryDeprecatedSemconvCatalog.cs", StringComparison.Ordinal)
            || path.EndsWith("/SemconvMigrationCatalogEntry.cs", StringComparison.Ordinal);
    }
}
